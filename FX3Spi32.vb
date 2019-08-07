'File:          FX3Spi32.vb
'Author:        Alex Nolan (alex.nolan@analog.com), Juan Chong (juan.chong@analog.com)
'Date:          07/15/2019
'Description:   This file contains all the implementation functions for the ISpi32Interface, which allows for a protocol agnostic interface to the SPI bus.

Imports System.Collections.Concurrent
Imports System.ComponentModel
Imports System.Threading
Imports AdisApi

Partial Class FX3Connection

    ''' <summary>
    ''' This function performs a single bi-directional 32 bit SPI transaction. If DrActive is set to false the transfer is performed asynchronously. If DrActive is set to true, 
    ''' the transfer should wait until a data ready condition (determined by DrPin and DrPolarity) is true.
    ''' </summary>
    ''' <param name="WriteData">The 32 bit data to be send to the slave on the MOSI line</param>
    ''' <returns>The 32 bit data sent to the master over the MISO line during the SPI transaction</returns>
    Public Function Transfer(WriteData As UInteger) As UInteger Implements ISpi32Interface.Transfer
        Dim readValue As UInt32
        Dim buf(7) As Byte
        Dim numBytes As Integer
        Dim status As UInteger

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_TRANSFER_BYTES, False)

        'Set the write value
        FX3ControlEndPt.Index = ((WriteData And &HFFFF0000) >> 16)
        FX3ControlEndPt.Value = WriteData And &HFFFF

        'Send the vendor command
        If Not XferControlData(buf, 8, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout during control endpoint transfer for SPI byte transfer")
        End If

        'Calculate how many bytes to read back
        numBytes = m_FX3SPIConfig.WordLength / 8

        'Read back data from buffer
        readValue = BitConverter.ToUInt32(buf, 4)

        'Read back the operation status from the return buffer
        status = BitConverter.ToUInt32(buf, 0)

        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Bad read command - " + status.ToString("X4"))
        End If

        Return readValue
    End Function

    ''' <summary>
    ''' This function performs an array bi-directional SPI transfer. WriteData.Count() total SPI transfers are performed. If DrActive is set to true, the transfer should wait
    ''' until a data ready condition (determined by DrPin and DrPolarity) is true, and then perform all SPI transfers. If DrActive is false it is performed asynchronously.
    ''' </summary>
    ''' <param name="WriteData">The data to be written to the slave on the MOSI line in each SPI transaction. The total number of transfers performed is determined by the size of WriteData.</param>
    ''' <returns>The data received from the slave device on the MISO line, as an array</returns>
    Public Function TransferArray(WriteData As IEnumerable(Of UInteger)) As UInteger() Implements ISpi32Interface.TransferArray
        Return TransferArray(WriteData, 1, 1)
    End Function

    ''' <summary>
    ''' This function performs an array bi-directional SPI transfer. This overload transfers all the data in WriteData numCaptures times. The total
    ''' number of SPI words transfered is WriteData.Count() * numCaptures.
    ''' If DrActive is set to true, the transfer should wait until a data ready condition (determined by DrPin and DrPolarity) is true, and
    ''' then perform all SPI transfers. If DrActive is false it is performed asynchronously.
    ''' </summary>
    ''' <param name="WriteData">The data to be written to the slave on the MOSI line in each SPI transaction.</param>
    ''' <param name="numCaptures">The number of transfers of the WriteData array performed.</param>
    ''' <returns>The data received from the slave device on the MISO line, as an array. The total size is WriteData.Count() * numCaptures</returns>
    Public Function TransferArray(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger) As UInteger() Implements ISpi32Interface.TransferArray
        Return TransferArray(WriteData, numCaptures, 1)
    End Function

    ''' <summary>
    ''' This function performs an array bi-directional SPI transfer. If DrActive is set to true, this overload transfers all the data in WriteData
    ''' numCaptures times per data ready condition being met. It captures data from numBuffers data ready signals. If DrActive is set to false, all the
    ''' transfers are performed asynchronously. The total number of SPI transfers is WriteData.Count()*numCaptures*numBuffers.
    ''' 
    ''' The following code snippet would perform 400 total SPI transfers, accross 100 data ready conditions. 
    ''' 
    ''' MOSI = (&H1234, &H5678)
    ''' myISpi32.DrActive = True
    ''' MISO = myISpi32.TransferArray(MOSI, 2, 100)
    ''' 
    ''' During the transfers, the SPI bus would look like the following:
    ''' 
    ''' MOSI: ---(0x1234)---(0x5678)---(0x1234)---(0x5678)-----------------(0x1234)---(0x5678)---(0x1234)---(0x5678)--...-----(0x1234)-----(0x5678)-----(0x1234)-----(0x5678)--
    ''' MISO:----MISO(0)----MISO(1)----MISO(2)----MISO(3)------------------MISO(4)----MISO(5)----MISO(6)----MISO(7)---...-----MISO(196)----MISO(197)----MISO(198)----MISO(199)-
    ''' DR:   ___|¯¯¯|_____________________________________________________|¯¯¯|______________________________________..._____|¯¯¯|____________________________________________
    ''' </summary>
    ''' <param name="WriteData">The data to be written to the slave over the MOSI line in each SPI transaction</param>
    ''' <param name="numCaptures">The number of transfers of the WriteData array performed on each data ready (if enabled).</param>
    ''' <param name="numBuffers">The total number of data readys to capture.</param>
    ''' <returns></returns>
    Public Function TransferArray(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger, numBuffers As UInteger) As UInteger() Implements ISpi32Interface.TransferArray
        Dim MISOData As New List(Of UInteger)
        Dim BytesPerUsbBuffer As UInteger
        Dim numTransfers As UInteger
        Dim validTransfer As Boolean

        'Sends the transfer start command to the FX3
        BytesPerUsbBuffer = ISpi32TransferStreamSetup(WriteData, numCaptures, numBuffers)

        'Calculate number of transfers needed
        numTransfers = Math.Ceiling(CDbl(WriteData.Count() * 4 * numCaptures * numBuffers) / BytesPerUsbBuffer)

        'Find transfer size and create data buffer
        Dim transferSize As Integer
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3GeneralException("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        For transferCount As Integer = 0 To numTransfers - 1
            validTransfer = StreamingEndPt.XferData(buf, transferSize)
            If validTransfer Then
                For byteCount As Integer = 0 To BytesPerUsbBuffer - 4 Step 4
                    MISOData.Add(BitConverter.ToUInt32(buf, byteCount))
                    If MISOData.Count >= WriteData.Count() * numBuffers * numCaptures Then
                        Exit For
                    End If
                Next
            End If
        Next

        ISpi32TransferStreamDone()

        Return MISOData.ToArray()

    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    Private Sub ISpi32Interface_StopStream() Implements ISpi32Interface.StopStream
        'Buffer to hold command data
        Dim buf(3) As Byte
        Dim status As UInteger

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_TRANSFER_STREAM, False)
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_STOP_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured when stopping a transfer stream thread on the FX3")
        End If

        'Read status from the buffer and throw exception for bad status
        status = BitConverter.ToUInt32(buf, 0)
        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Failed to set stream stop event, status: " + status.ToString("X4"))
        End If
    End Sub

    ''' <summary>
    ''' This is similar to the most general streaming function used by the IRegInterface, and all other buffered streaming functions can be derived from 
    ''' it with a little glue logic. When a stream is started, a second thread should be started to pull buffers from the interfacing board asynchronously.
    ''' Each buffer will consist of WriteData.count() * numCaptures 32-bit words, which are just the raw data read back from the DUT over the MISO line. 
    ''' The stream is expected to produce numBuffers total buffers.
    ''' </summary>
    ''' <param name="WriteData">The data to send over the MOSI line</param>
    ''' <param name="numCaptures">The number of interations of the WriteData array to perform in a single buffer</param>
    ''' <param name="numBuffers">The total number of buffers to capture</param>
    ''' <param name="timeoutSeconds">The time to wait on the interfacing board before stopping the stream</param>
    ''' <param name="worker">A background worker used to notify the caller of progress made in the stream. You MUST check that this parameter has been initialized</param>
    Private Sub ISpi32Interface_StartBufferedStream(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger, numBuffers As UInteger, timeoutSeconds As Integer, worker As BackgroundWorker) Implements ISpi32Interface.StartBufferedStream

        Dim BytesPerUsbBuffer As UInteger

        'Set the total number of buffers to read
        m_TotalBuffersToRead = numBuffers

        'Setup the stream
        BytesPerUsbBuffer = ISpi32TransferStreamSetup(WriteData, numCaptures, numBuffers)

        'Start the streaming thread
        m_StreamThread = New Thread(AddressOf ISpi32InterfaceStreamWorker)
        m_StreamThread.Start(WriteData.Count() * 4 * numCaptures)

        'If there is a background worker, monitor and generate the Report Progress and Cancel events as needed
        If IsNothing(worker) Then
            Exit Sub
        End If

        'Flag for when stream is done
        Dim streamDone As Boolean = False

        'Tracking the progress (in the range of 0 - 100)
        Dim progress, oldProgress As Integer

        'Init the progress tracking
        progress = 0
        oldProgress = 0
        While Not streamDone
            If worker.WorkerSupportsCancellation Then
                'If the worker supports cancellation then monitor the flag
                streamDone = ((GetNumBuffersRead >= numBuffers) Or worker.CancellationPending)
            Else
                streamDone = (GetNumBuffersRead >= numBuffers)
            End If

            If worker.WorkerReportsProgress Then
                progress = (GetNumBuffersRead / numBuffers) * 100
                If progress > oldProgress Then
                    worker.ReportProgress(progress)
                    oldProgress = progress
                End If
            End If

            'Sleep to avoid using too much CPU time
            System.Threading.Thread.Sleep(20)

        End While

        If worker.WorkerSupportsCancellation And worker.CancellationPending Then
            m_StreamThreadRunning = False
        End If

    End Sub

    Private Sub ISpi32TransferStreamDone()
        'Buffer to hold command data
        Dim buf(3) As Byte
        Dim status As UInteger

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_TRANSFER_STREAM, False)
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_DONE_CMD

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured when cleaning up a transfer stream thread on the FX3")
        End If

        'Read status from the buffer and throw exception for bad status
        status = BitConverter.ToUInt32(buf, 0)
        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Failed to set stream done event, status: " + status.ToString("X4"))
        End If
    End Sub

    Private Function ISpi32TransferStreamSetup(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger, numBuffers As UInteger) As UInteger
        'Validate parameters
        If WriteData.Count() = 0 Then
            Throw New FX3ConfigurationException("ERROR: WriteData must contain at least one element")
        End If

        'Buffer to send config data to the FX3
        Dim buf As New List(Of Byte)
        Dim bytesPerUsbBuffer As UInteger
        Dim bytesPerDrTransfer As UInteger
        Dim transferSize As UInteger
        Dim bytesMOSIData As UShort

        'Add numCaptures
        buf.Add(numCaptures And &HFF)
        buf.Add((numCaptures And &HFF00) >> 8)
        buf.Add((numCaptures And &HFF0000) >> 16)
        buf.Add((numCaptures And &HFF000000) >> 24)

        'Add numBuffers
        buf.Add(numBuffers And &HFF)
        buf.Add((numBuffers And &HFF00) >> 8)
        buf.Add((numBuffers And &HFF0000) >> 16)
        buf.Add((numBuffers And &HFF000000) >> 24)

        'Calculate the number of bytes per "register" buffer (iterating through writedata numcapture times)
        bytesPerDrTransfer = WriteData.Count() * 4 * numCaptures

        'Get the USB transfer size
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3GeneralException("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Clamp the bytes per USB buffer at the transfer size of a single packet
        If bytesPerDrTransfer > transferSize Then
            bytesPerUsbBuffer = transferSize
        Else
            bytesPerUsbBuffer = Math.Floor(transferSize / bytesPerDrTransfer) * bytesPerDrTransfer
        End If

        'Add bytes per buffer
        buf.Add(bytesPerUsbBuffer And &HFF)
        buf.Add((bytesPerUsbBuffer And &HFF00) >> 8)
        buf.Add((bytesPerUsbBuffer And &HFF0000) >> 16)
        buf.Add((bytesPerUsbBuffer And &HFF000000) >> 24)

        'Add Number of bytes of write (MOSI) data
        bytesMOSIData = WriteData.Count() * 4
        buf.Add(bytesMOSIData And &HFF)
        buf.Add((bytesMOSIData And &HFF00) >> 8)

        'Add the write (MOSI) data
        For Each MOSIWord In WriteData
            buf.Add(MOSIWord And &HFF)
            buf.Add((MOSIWord And &HFF00) >> 8)
            buf.Add((MOSIWord And &HFF0000) >> 16)
            buf.Add((MOSIWord And &HFF000000) >> 24)
        Next

        'Check that the transmit buffer isn't too large (>512)
        If buf.Count() > 512 Then
            Throw New FX3ConfigurationException("ERROR: Invalid WriteData provided to StartTransferStream. Transfer count of " + buf.Count().ToString() + "bytes exceeds allowed amount of 512 bytes")
        End If

        'Configure control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_TRANSFER_STREAM, True)
        m_ActiveFX3.ControlEndPt.Index = StreamCommands.ADI_STREAM_START_CMD

        'Send stream start command
        If Not XferControlData(buf.ToArray(), buf.Count(), 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured during control endpoint transfer for SPI transfer stream")
        End If

        'Return the bytes per buffer so it doesnt have to be calculated again
        Return bytesPerUsbBuffer

    End Function

    Private Sub ISpi32InterfaceStreamWorker(BytesPerBuffer As UInteger)

        'Bool to track if transfer from FX3 board is successful
        Dim validTransfer As Boolean = True
        'Variable to track number of buffers read
        Dim numBuffersRead As Integer = 0
        'List to build output buffer in UInteger format
        Dim bufferBuilder As New List(Of UInteger)
        'Int to track buffer index
        Dim bufIndex As Integer = 0

        'Find transfer size and create data buffer
        Dim transferSize As Integer
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3GeneralException("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Create the 32 bit data queue
        m_TransferStreamData = New ConcurrentQueue(Of UInteger())

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        'Wait for previous stream thread to exit, if any
        m_StreamThreadRunning = False

        'Wait until a lock can be acquired on the streaming end point
        m_StreamMutex.WaitOne()

        'Reset frames read counter to 0
        m_FramesRead = 0

        'Set the thread state flags
        m_StreamThreadRunning = True

        While m_StreamThreadRunning
            'Read data from FX3
            validTransfer = StreamingEndPt.XferData(buf, transferSize)
            'Check that the data was read correctly
            If validTransfer Then
                For bufIndex = 0 To (transferSize - 4) Step 4
                    'Add the value at the current index position
                    bufferBuilder.Add(BitConverter.ToUInt32(buf, bufIndex))
                    If bufferBuilder.Count() * 4 >= BytesPerBuffer Then
                        m_TransferStreamData.Enqueue(bufferBuilder.ToArray())
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
                            ISpi32TransferStreamDone()
                            'Exit the while loop
                            Exit While
                        End If
                    End If
                Next
            Else
                'Exit for a failed data transfer
                Console.WriteLine("Transfer failed during transfer stream. Error code: " + StreamingEndPt.LastError.ToString() + " (0x" + StreamingEndPt.LastError.ToString("X4") + ")")
                ISpi32Interface_StopStream()
                Exit While
            End If
        End While

        'Set thread state flags
        m_StreamThreadRunning = False

        'Release the mutex
        m_StreamMutex.ReleaseMutex()

    End Sub

    ''' <summary>
    ''' Gets a buffer from the TransferStreamData thread safe queue. Same implementation as GetBuffer.
    ''' </summary>
    ''' <returns></returns>
    Private Function ISpi32Interface_GetBufferedStreamDataPacket() As UInteger() Implements ISpi32Interface.GetBufferedStreamDataPacket
        'Ensure that the queue has been initialized
        If IsNothing(m_TransferStreamData) Then
            Return Nothing
        End If

        'Return nothing if there is no data in the queue and the producer thread is idle
        If (m_TransferStreamData.Count = 0) And (Not m_StreamThreadRunning) Then
            Return Nothing
        End If

        'Set up variables for return buffer
        Dim buffer() As UInteger = Nothing
        Dim validData As Boolean = False
        m_streamTimeoutTimer.Restart()

        'Wait for a buffer to be avialable and dequeue
        While (Not validData) And (m_streamTimeoutTimer.ElapsedMilliseconds < m_StreamTimeout * 1000)
            validData = m_TransferStreamData.TryDequeue(buffer)
        End While
        Return buffer

    End Function

    ''' <summary>
    ''' Set the timeout period used for dequeuing a buffer from the thread safe queue.
    ''' </summary>
    ''' <returns></returns>
    Private Property ISpi32Interface_StreamTimeoutSeconds As Integer Implements ISpi32Interface.StreamTimeoutSeconds
        Get
            Return Me.StreamTimeoutSeconds
        End Get
        Set(value As Integer)
            Me.StreamTimeoutSeconds = value
        End Set
    End Property

    ''' <summary>
    ''' Sets of data ready triggering is used for the ISpi32Interface
    ''' </summary>
    ''' <returns></returns>
    Private Property ISpi32Interface_DrActive As Boolean Implements ISpi32Interface.DrActive
        Get
            Return Me.DrActive
        End Get
        Set(value As Boolean)
            Me.DrActive = value
        End Set
    End Property

    ''' <summary>
    ''' This property is used to get or set the data ready pin. Is tied to the ReadyPin property
    ''' </summary>
    ''' <returns></returns>
    Public Property DrPin As IPinObject Implements ISpi32Interface.DrPin
        Get
            Return ReadyPin
        End Get
        Set(value As IPinObject)
            ReadyPin = value
        End Set
    End Property

    ''' <summary>
    ''' Sets/Gets the data ready polarity for the ISpi32Interface.
    ''' </summary>
    ''' <returns></returns>
    Private Property ISpi32Interface_DrPolarity As Boolean Implements ISpi32Interface.DrPolarity
        Get
            Return DrPolarity
        End Get
        Set(value As Boolean)
            DrPolarity = value
        End Set
    End Property

End Class
