'File:          FX3DataStreaming.vb
'Author:        Alex Nolan (alex.nolan@analog.com), Juan Chong (juan.chong@analog.com)
'Date:          06/26/2019
'Description:   All streaming functions (except ISpi32 streams) which run in the m_StreamingThread are implemented here

Imports System.Collections.Concurrent
Imports System.Threading
Imports AdisApi
Imports FX3USB
Imports StreamDataLogger

Partial Class FX3Connection

#Region "Stream Synchronization"
    ''' <summary>
    ''' Overload of WaitForStreamCompletion which blocks forever
    ''' </summary>
    Public Function WaitForStreamCompletion() As Boolean
        Return WaitForStreamCompletion(Nothing)
    End Function

    ''' <summary>
    ''' Blocks until the streaming endpoint mutex can be acquired. Allows a user to synchronize external application
    ''' the completion of a stream. Returns false if there is not a stream running, or if the timeout is reached without
    ''' the stream mutex being acquired.
    ''' </summary>
    ''' <param name="MillisecondsTimeout">The time to wait trying to acquire the stream mutex</param>
    ''' <returns>If the stream wait was successful</returns>
    Public Function WaitForStreamCompletion(MillisecondsTimeout As Integer) As Boolean

        'Track if lock is acquired
        Dim lockAquired As Boolean
        Dim timer As New Stopwatch

        'Check if stream thread is running
        If Not m_StreamThreadRunning Then
            timer.Start()
            While timer.ElapsedMilliseconds < MillisecondsTimeout And Not m_StreamThreadRunning
                System.Threading.Thread.Sleep(1)
            End While
            timer.Stop()
            If Not m_StreamThreadRunning Then
                Return False
            End If
        End If

        'Ensure that the total timeout remains consistent
        MillisecondsTimeout -= timer.ElapsedMilliseconds()

        'Acquire the mutex
        If IsNothing(MillisecondsTimeout) Or MillisecondsTimeout <= 0 Then
            'Perform wait with no timeout
            lockAquired = True
            m_StreamMutex.WaitOne()
        Else
            'Use mutex wait overload with timeout
            lockAquired = m_StreamMutex.WaitOne(MillisecondsTimeout)
        End If

        If lockAquired Then
            'Release the mutex
            m_StreamMutex.ReleaseMutex()
        End If

        'Return if the lock was acquired
        Return lockAquired

    End Function

    ''' <summary>
    ''' Place data in the thread safe queue and raise a buffer available event
    ''' </summary>
    ''' <param name="buf"></param>
    Private Sub EnqueueStreamData(ByRef buf As UShort())
        m_StreamData.Enqueue(buf)
        RaiseEvent NewBufferAvailable(m_StreamData.Count())
    End Sub

    ''' <summary>
    ''' Cancel a any running stream
    ''' </summary>
    Public Sub CancelStreamAsync() Implements IStreamEventProducer.CancelStreamAsync
        StopStream()
    End Sub

    Private Sub ExitStreamThread()
        'Set thread state flags
        m_StreamThreadRunning = False

        'Reset stream type to none
        m_StreamType = StreamType.None

        'Release the mutex
        m_StreamMutex.ReleaseMutex()

        'Raise stream done event
        RaiseEvent StreamFinished()
    End Sub

#End Region

#Region "Burst Stream Functions"

    ''' <summary>
    ''' Function to start a burst read using the BurstStreamManager
    ''' </summary>
    ''' <param name="numBuffers">The number of buffers to read in the stream operation</param>
    Private Sub StartBurstStream(ByVal numBuffers As UInteger)

        'Buffer to store command data
        Dim buf(8) As Byte

        'Send number of buffers to read
        buf(0) = numBuffers And &HFF
        buf(1) = (numBuffers And &HFF00) >> 8
        buf(2) = (numBuffers And &HFF0000) >> 16
        buf(3) = (numBuffers And &HFF000000) >> 24

        'Send word to trigger burst
        buf(4) = (m_TriggerReg.Address And &HFF)
        buf(5) = (m_TriggerReg.Address And &HFF00) >> 8

        'Send number of words to capture
        buf(6) = (m_WordCount And &HFF)
        buf(7) = (m_WordCount And &HFF00) >> 8

        'Reinitialize the thread safe queue
        m_StreamData = New ConcurrentQueue(Of UShort())

        ConfigureControlEndpoint(USBCommands.ADI_STREAM_BURST_DATA, True)
        m_ActiveFX3.ControlEndPt.Value = 0 'DNC
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_START_CMD 'Start stream

        'Send start stream command to the DUT
        If Not XferControlData(buf, 8, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred while starting burst stream")
        End If

        'Reset number of frames read
        m_FramesRead = 0

        'Set the total number of frames to read
        m_TotalBuffersToRead = numBuffers

        'Set the stream type
        m_StreamType = StreamType.BurstStream

        'Spin up a BurstStreamManager thread
        m_StreamThread = New Thread(AddressOf BurstStreamManager)
        m_StreamThread.Start()

    End Sub

    ''' <summary>
    ''' Stops a burst stream by setting the stream state variables
    ''' </summary>
    Public Sub StopBurstStream()

        'Buffer to store command data
        Dim buf(3) As Byte

        'Stop the stream manager thread
        m_StreamThreadRunning = False

        'Configure the endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_BURST_DATA, False)
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_STOP_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred while stopping burst stream")
        End If

    End Sub

    Private Sub BurstStreamDone()
        'Buffer to store command data
        Dim buf(3) As Byte

        'Configure the endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_BURST_DATA, True)
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_DONE_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred when cleaning up a burst stream thread on the FX3")
        End If

    End Sub

    ''' <summary>
    ''' Property to choose if the readback from the 16 bit trigger word at the start of each burst is discarded or not
    ''' </summary>
    ''' <returns></returns>
    Public Property StripBurstTriggerWord As Boolean
        Get
            Return m_StripBurstTriggerWord
        End Get
        Set(value As Boolean)
            m_StripBurstTriggerWord = value
        End Set
    End Property

    ''' <summary>
    ''' This function reads burst stream data from the DUT over the streaming endpoint. It is intended to operate in its own thread, and should not be called directly.
    ''' </summary>
    Private Sub BurstStreamManager()

        'The length of one frame, in bytes
        Dim frameLength As Integer
        'The index in the current raw buffer
        Dim index As Integer
        'The index in the current frame
        Dim frameIndex As Integer = 0
        'Temporary value for converting two bytes to a UShort
        Dim shortValue As UShort
        'The USB transfer size (from the FX3)
        Dim transferSize As Integer
        'List used to construct frames out of the output buffer
        Dim frameBuilder As New List(Of UShort)
        'Bool to track the transfer status
        Dim transferStatus As Boolean
        'Int to track number of frames read
        Dim framesCounter As Integer

        'Validate the transfer size
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3Exception("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        'Set total frames (infinite if less than 1)
        If m_TotalBuffersToRead < 1 Then
            m_TotalBuffersToRead = Int32.MaxValue
        End If

        'Determine the frame length (in bytes) based on configured word count plus trigger word
        frameLength = (m_WordCount * 2) + 2

        'Wait for previous stream thread to exit, if any
        m_StreamThreadRunning = False

        'Wait until a lock can be acquired on the streaming end point
        m_StreamMutex.WaitOne()

        'Set the stream thread running state variable
        m_StreamThreadRunning = True
        framesCounter = 0

        While m_StreamThreadRunning
            'Configured transfer size bytes from the FX3
            transferStatus = USB.XferData(buf, transferSize, StreamingEndPt)
            'Parse bytes into frames and add to m_StreamData if transaction was successful
            If transferStatus Then
                For index = 0 To transferSize - 2 Step 2
                    'Append every two bytes into words
                    shortValue = buf(index)
                    shortValue = shortValue << 8
                    shortValue = shortValue + buf(index + 1)
                    frameBuilder.Add(shortValue)
                    frameIndex = frameIndex + 2
                    'Once the end of each frame is reached add it to the queue
                    If frameIndex >= frameLength Then
                        'Remove trigger word entry
                        If m_StripBurstTriggerWord Then
                            frameBuilder.RemoveAt(0)
                        End If
                        'Enqueue data into thread-safe queue
                        EnqueueStreamData(frameBuilder.ToArray())
                        'Increment the shared frame counter
                        Interlocked.Increment(m_FramesRead)
                        'Increment the local frame counter
                        framesCounter = framesCounter + 1
                        'Reset frame builder list and counter
                        frameIndex = 0
                        frameBuilder.Clear()
                        'Exit if the total number of buffers has been read
                        If framesCounter >= m_TotalBuffersToRead Then
                            'Stop streaming
                            BurstStreamDone()
                            Exit While
                        End If
                    End If
                Next
            ElseIf m_StreamThreadRunning Then
                Console.WriteLine("Transfer failed during burst stream. Error code: " + StreamingEndPt.LastError.ToString() + " (0x" + StreamingEndPt.LastError.ToString("X4") + ")")
                StopBurstStream()
                'Exit streaming mode if the transfer fails
                Exit While
            Else
                'exiting due to cancel
                Exit While
            End If
        End While

        ExitStreamThread()

    End Sub

    Private Sub ValidateBurstStreamConfig()

        'Chip select control mode
        If m_FX3SPIConfig.ChipSelectControl <> SpiChipselectControl.SPI_SSN_CTRL_HW_END_OF_XFER Then
            Throw New FX3ConfigurationException("ERROR: Chip select hardware control must be enabled for real time streaming")
        End If

    End Sub

#End Region

#Region "Generic Stream Functions"

    ''' <summary>
    ''' Stops a generic stream by setting the stream state variables
    ''' </summary>
    Public Sub StopGenericStream()

        'Buffer to hold command data
        Dim buf(3) As Byte

        'Stop the stream manager thread
        m_StreamThreadRunning = False

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_GENERIC_DATA, False)
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_STOP_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred when stopping a generic stream")
        End If

    End Sub

    Private Sub GenericStreamDone()
        'Buffer to hold command data
        Dim buf(3) As Byte

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_GENERIC_DATA, True)
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_DONE_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred when cleaning up a generic stream thread on the FX3")
        End If

    End Sub

    ''' <summary>
    ''' Starts a generic data stream. This allows you to read/write a set of registers on the DUT, triggering off the data ready if needed.
    ''' The data read is placed in the thread-safe queue and can be retrieved with a call to GetBuffer. Each "buffer" is the result of
    ''' reading the addr list of registers numCaptures times. For example, if addr is set to [0, 2, 4] and numCaptures is set to 10, each
    ''' buffer will contain the 30 register values. The total number of register reads performed is numCaptures * numBuffers
    ''' </summary>
    ''' <param name="addr">The list of registers to </param>
    ''' <param name="numCaptures">The number of captures of the register list per data ready</param>
    ''' <param name="numBuffers">The total number of capture sequences to perform</param>
    Public Sub StartGenericStream(addr As IEnumerable(Of AddrDataPair), numCaptures As UInteger, numBuffers As UInteger)

        Dim BytesPerBuffer As Integer
        BytesPerBuffer = (addr.Count() * numCaptures) * 2

        'Validate buffer size
        If addr.Count() * 2 > MaxRegListSize Then
            Throw New FX3ConfigurationException("ERROR: Generic stream capture size too large- " + (addr.Count() * 2).ToString() + " bytes per register list exceeds maximum size of " + MaxRegListSize.ToString() + " bytes.")
        End If

        'Perform generic stream setup (sends start command to control endpoint)
        GenericStreamSetup(addr, numCaptures, numBuffers)

        'Reset frame counter
        m_FramesRead = 0
        'Set the total number of frames to read
        m_TotalBuffersToRead = numBuffers

        'Reinitialize the data queue
        m_StreamData = New ConcurrentQueue(Of UShort())

        'Set the stream type
        m_StreamType = StreamType.GenericStream

        'Start the Generic Stream Thread
        m_StreamThread = New Thread(AddressOf GenericStreamManager)
        m_StreamThread.Start(BytesPerBuffer)

    End Sub

    Private Sub GenericStreamSetup(addrData As IEnumerable(Of AddrDataPair), numCaptures As UInteger, numBuffers As UInteger)
        'Buffer to store control data
        Dim buf As New List(Of Byte)

        'Validate number of buffers
        If IsNothing(numBuffers) Or numBuffers < 1 Then
            Throw New FX3ConfigurationException("ERROR: Invalid number of buffers for a generic register stream: " + numBuffers.ToString())
        End If

        'Validate address list
        If IsNothing(addrData) Or addrData.Count = 0 Then
            Throw New FX3ConfigurationException("ERROR: Invalid address list for generic stream")
        End If

        'Validate number of captures
        If IsNothing(numCaptures) Or numCaptures < 1 Then
            Throw New FX3ConfigurationException("ERROR: Invalid number of captures for a generic register stream: " + numBuffers.ToString())
        End If

        'Add numBuffers
        buf.Add(numBuffers And &HFF)
        buf.Add((numBuffers And &HFF00) >> 8)
        buf.Add((numBuffers And &HFF0000) >> 16)
        buf.Add((numBuffers And &HFF000000) >> 24)

        'Add numCaptures
        buf.Add(numCaptures And &HFF)
        buf.Add((numCaptures And &HFF00) >> 8)
        buf.Add((numCaptures And &HFF0000) >> 16)
        buf.Add((numCaptures And &HFF000000) >> 24)

        'Add address list
        For Each item In addrData
            If item.data Is Nothing Then
                'Read case
                buf.Add(&H0)
                buf.Add(item.addr And &H7F)
            Else
                'Write case
                buf.Add(item.data And &HFF)
                buf.Add(item.addr Or &H80)
            End If
        Next

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_GENERIC_DATA, True)

        'Configure settings to enable/disable streaming
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_START_CMD

        'Send start command to the FX3
        If Not XferControlData(buf.ToArray(), buf.Count, 5000) Then
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer timed out when starting generic stream")
        End If

    End Sub


    ''' <summary>
    ''' This function pulls generic stream data from the FX3 over a bulk endpoint (DataIn). It is intended to run in its own thread,
    ''' and should not be called by itself.
    ''' </summary>
    ''' <param name="BytesPerBuffer">Number of bytes per generic stream buffer</param>
    Private Sub GenericStreamManager(BytesPerBuffer As Integer)

        'Bool to track if transfer from FX3 board is successful
        Dim validTransfer As Boolean = True
        'Variable to track number of buffers read
        Dim numBuffersRead As Integer = 0
        'List to build output buffer in USHORT format
        Dim bufferBuilder As New List(Of UShort)
        'Int to track buffer index
        Dim bufIndex As Integer = 0

        'Find transfer size and create data buffer
        Dim transferSize As Integer
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3Exception("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        'Wait for previous stream thread to exit, if any
        m_StreamThreadRunning = False

        'Wait until a lock can be acquired on the streaming end point
        m_StreamMutex.WaitOne()

        'Set the thread state flags
        m_StreamThreadRunning = True

        While m_StreamThreadRunning
            'Read data from FX3
            validTransfer = USB.XferData(buf, transferSize, StreamingEndPt)
            'Check that the data was read correctly
            If validTransfer Then
                'Build the output buffer
                For bufIndex = 0 To (transferSize - 2) Step 2
                    bufferBuilder.Add(BitConverter.ToUInt16(buf, bufIndex))
                    If bufferBuilder.Count() * 2 >= BytesPerBuffer Then
                        EnqueueStreamData(bufferBuilder.ToArray())
                        Interlocked.Increment(m_FramesRead)
                        bufferBuilder.Clear()
                        numBuffersRead = numBuffersRead + 1
                        'Exit for loop if total integer number of buffers for the USB packet have been read (ignore for case where its more than one packet per buffer)
                        If (transferSize - bufIndex) < BytesPerBuffer And Not (BytesPerBuffer > transferSize) Then
                            Exit For
                        End If
                        'Finish the stream if the total number of buffers has been read
                        If numBuffersRead >= m_TotalBuffersToRead Then
                            'Stop the stream
                            GenericStreamDone()
                            'Exit the while loop
                            Exit While
                        End If
                    End If
                Next
            ElseIf m_StreamThreadRunning Then
                'Exit for a failed data transfer
                Console.WriteLine("Transfer failed during generic stream. Error code: " + StreamingEndPt.LastError.ToString() + " (0x" + StreamingEndPt.LastError.ToString("X4") + ")")
                StopGenericStream()
                Exit While
            Else
                'Exit for a cancel
                Exit While
            End If
        End While

        ExitStreamThread()

    End Sub

    ''' <summary>
    ''' Validate the SPI configuration for a generic stream
    ''' </summary>
    Private Sub ValidateGenericStreamConfig()

        'Check the word length (must be 16)
        If m_FX3SPIConfig.WordLength <> 16 Then
            Throw New FX3ConfigurationException("ERROR: Generic stream only supported for a word length of 16 bits")
        End If

    End Sub

#End Region

#Region "Real-Time Stream Functions"

    ''' <summary>
    ''' This function starts real time streaming on the ADcmXLx021 (interface and FX3). Specifying pin exit is optional and must be 0 (disabled) or 1 (enabled)
    ''' </summary>
    Public Sub StartRealTimeStreaming(ByVal numFrames As UInteger)

        'Buffer to store command data
        Dim buf(4) As Byte

        'Validate the current FX3 settings
        ValidateRealTimeStreamConfig()

        buf(0) = numFrames And &HFF
        buf(1) = (numFrames And &HFF00) >> 8
        buf(2) = (numFrames And &HFF0000) >> 16
        buf(3) = (numFrames And &HFF000000) >> 24
        buf(4) = (m_pinStart)

        'Reinitialize the thread safe queue
        m_StreamData = New ConcurrentQueue(Of UShort())

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_REALTIME, True)
        m_ActiveFX3.ControlEndPt.Value = m_pinExit
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_START_CMD

        'Send start stream command to the DUT
        If Not XferControlData(buf, 5, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred while starting real time streaming")
        End If

        'Reset number of frames read
        m_FramesRead = 0
        m_numBadFrames = 0

        'Set the total number of frames to read
        m_TotalBuffersToRead = numFrames

        'Set the stream type
        m_StreamType = StreamType.RealTimeStream

        'Spin up a RealTimeStreamManager thread
        m_StreamThread = New Thread(AddressOf RealTimeStreamManager)
        m_StreamThread.Start()

    End Sub

    ''' <summary>
    ''' This function stops real time streaming on the ADcmXLx021 (interface and FX3)
    ''' </summary>
    Public Sub StopRealTimeStreaming()

        'Buffer to hold command data
        Dim buf(3) As Byte

        'Stop the stream manager thread
        m_StreamThreadRunning = False

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_REALTIME, False)
        m_ActiveFX3.ControlEndPt.Value = m_pinExit
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_STOP_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred while stopping a real time stream")
        End If
    End Sub

    Private Sub RealTimeStreamingDone()
        'Buffer to hold command data
        Dim buf(3) As Byte
        Dim status As UInteger

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_REALTIME, False)
        m_ActiveFX3.ControlEndPt.Value = m_pinExit
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_DONE_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occurred when cleaning up a real time stream thread on the FX3")
        End If

        'Read status from the buffer and throw exception for bad status
        status = BitConverter.ToUInt32(buf, 0)
        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Failed to set stream done event, status: " + status.ToString("X4"))
        End If

    End Sub

    ''' <summary>
    ''' This function pulls real time data from the DUT over the streaming endpoint. It is intended to operate in its own thread, and should not be called directly
    ''' </summary>
    Private Sub RealTimeStreamManager()

        'The length of one frame, in bytes
        Dim frameLength As Integer
        'The index in the current 1KB buffer
        Dim index As Integer
        'The index in the current frame
        Dim frameIndex As Integer = 0
        'The total number of frames read so far
        Dim totalFrames As Integer = 0
        'Temporary value for converting two bytes to a UShort
        Dim shortValue As UShort
        'The USB transfer size (from the FX3)
        Dim transferSize As Integer
        'List used to construct frames out of the output buffer
        Dim frameBuilder As New List(Of UShort)
        'Bool to track the transfer status
        Dim TransferStatus As Boolean
        'Int to track number of frames read
        Dim framesCounter As Integer

        'Check endpoint speed
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3Exception("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        'Set total frames (infinite if less than 1)
        If m_TotalBuffersToRead < 1 Then
            m_TotalBuffersToRead = Int32.MaxValue
        End If

        'Determine the frame length based on DUTType
        If m_FX3SPIConfig.DUTType = DUTType.ADcmXL1021 Then
            'Single Axis
            frameLength = 64 * 1 + 16 + 8 '88
        ElseIf m_FX3SPIConfig.DUTType = DUTType.ADcmXL2021 Then
            'Two Axis
            frameLength = 64 * 2 + 16 + 8 '152
        Else
            'Three Axis (Default)
            frameLength = 64 * 3 + 8 '200
        End If

        'Wait for previous stream thread to exit, if any
        m_StreamThreadRunning = False

        'Wait until a lock can be acquired on the streaming end point
        m_StreamMutex.WaitOne()

        'Set the stream thread running state variable
        m_StreamThreadRunning = True
        framesCounter = 0

        While m_StreamThreadRunning
            'Pull 1024 bytes from the DUT
            TransferStatus = USB.XferData(buf, transferSize, StreamingEndPt)
            'Parse the 1024 bytes into frames and add to m_StreamData if transaction was successful
            If TransferStatus Then
                For index = 0 To transferSize - 2 Step 2
                    'Flip bytes
                    shortValue = buf(index)
                    shortValue = shortValue << 8
                    shortValue = shortValue + buf(index + 1)
                    frameBuilder.Add(shortValue)
                    frameIndex = frameIndex + 2
                    'Once the end of each frame is reached add it to the queue
                    If frameIndex >= frameLength Then
                        EnqueueStreamData(frameBuilder.ToArray())
                        'Increment shared frame counter
                        Interlocked.Increment(m_FramesRead)
                        framesCounter = framesCounter + 1
                        'Reset frame builder list
                        frameIndex = 0
                        frameBuilder.Clear()
                        'Check that the total number of specified frames hasn't been read
                        If framesCounter >= m_TotalBuffersToRead Then
                            'Stop streaming
                            RealTimeStreamingDone()
                            Exit While
                        End If
                    End If
                Next
            ElseIf m_StreamThreadRunning Then
                'Exit streaming mode if the transfer fails
                Console.WriteLine("Transfer failed during AdCMXL real time stream. Error code: " + StreamingEndPt.LastError.ToString() + " (0x" + StreamingEndPt.LastError.ToString("X4") + ")")
                StopRealTimeStreaming()
                Exit While
            Else
                'exiting due to cancel
                Exit While
            End If
        End While

        ExitStreamThread()

    End Sub


    ''' <summary>
    ''' This function checks the CRC of each frame stored in the Stream Data Queue, and purges the bad ones
    ''' </summary>
    ''' <returns>The success of the data purge operation</returns>
    Public Function PurgeBadFrameData() As Boolean

        'Initialize variables
        Dim purgeSuccess As Boolean = True
        Dim frameDequeued As Boolean = False
        Dim tempQueue As New ConcurrentQueue(Of UShort())
        Dim frame() As UShort = Nothing
        Dim expectedFrameNum, frameNumber As UShort
        Dim firstFrame As Boolean

        'Only works for ADcmXLx021
        If Not (PartType = DUTType.ADcmXL1021 Or PartType = DUTType.ADcmXL2021 Or PartType = DUTType.ADcmXL3021) Then
            purgeSuccess = False
            Return purgeSuccess
        End If

        'Cannot run while streaming data
        If m_StreamThreadRunning Then
            purgeSuccess = False
            Return purgeSuccess
        End If

        'Pull data from queue
        m_numBadFrames = 0
        m_numFrameSkips = 0
        frameNumber = 0
        firstFrame = True
        While Not m_StreamData.Count = 0
            'Dequeue the frame
            frameDequeued = False
            While Not frameDequeued And m_StreamData.Count > 0
                frameDequeued = m_StreamData.TryDequeue(frame)
            End While
            'Check the CRC
            If CheckDUTCRC(frame) Then
                tempQueue.Enqueue(frame)
            Else
                m_numBadFrames = m_numBadFrames + 1
            End If
            'Parse the frame number
            expectedFrameNum = (frameNumber + 1) Mod 256
            If PartType = DUTType.ADcmXL1021 Then
                frameNumber = (frame(8) And &HFF00) >> 8
            Else
                frameNumber = (frame(0) And &HFF00) >> 8
            End If
            'Check against expected (except on first frame)
            If Not frameNumber = expectedFrameNum And Not firstFrame Then
                m_numFrameSkips += 1
            End If
            firstFrame = False
        End While

        'Set the output queue equal to the temp queue
        m_StreamData = tempQueue
        m_FramesRead = m_StreamData.Count

        Return purgeSuccess

    End Function

    ''' <summary>
    ''' This function validates the current SPI settings to ensure that they are compatible with the machine health
    ''' real time streaming mode. If the settings are not compatible, a FX3ConfigException is thrown.
    ''' </summary>
    Private Sub ValidateRealTimeStreamConfig()

        'SCLK
        If m_FX3SPIConfig.SCLKFrequency < 5000000 Then
            'Throw New FX3ConfigurationException("ERROR: Invalid SPI frequency for real time streaming")
        End If

        'Chip select control mode
        If m_FX3SPIConfig.ChipSelectControl <> SpiChipselectControl.SPI_SSN_CTRL_HW_END_OF_XFER Then
            Throw New FX3ConfigurationException("ERROR: Chip select hardware control must be enabled for real time streaming")
        End If

        'CPHA and CPOL must be true
        If m_FX3SPIConfig.Cpha = False Or m_FX3SPIConfig.Cpol = False Then
            Throw New FX3ConfigurationException("ERROR: Cpol and Cpha must both be set to true for real time streaming")
        End If

        'CS polarity
        If m_FX3SPIConfig.ChipSelectPolarity = True Then
            Throw New FX3ConfigurationException("ERROR: Chip select polarity must be false (active low) for real time streaming")
        End If

    End Sub

#End Region

End Class