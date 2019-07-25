'File:          FX3IRegInterface.vb
'Author:        Alex Nolan (alex.nolan@analog.com), Juan Chong (juan.chong@analog.com)
'Date:          8/1/2018
'Description:   Extension of the FX3Connection class. Has all the functions needed to 
'               implement the IRegInterface interface defined in the AdisApi.

Imports System.ComponentModel
Imports AdisApi

Partial Class FX3Connection

#Region "IRegInterface Implementation"

    ''' <summary>
    ''' This function is not currently implemented. Calling it will throw a NotImplementedException.
    ''' </summary>
    ''' <param name="addr"></param>
    ''' <param name="numCaptures"></param>
    Public Sub StartStream(addr As IEnumerable(Of UInteger), numCaptures As UInteger) Implements IRegInterface.StartStream
        Throw New NotImplementedException()
    End Sub

    ''' <summary>
    ''' This function is not currently implemented. Calling it will throw a NotImplementedException.
    ''' </summary>
    ''' <returns></returns>
    Public Function GetStreamDataPacketU16() As UShort() Implements IRegInterface.GetStreamDataPacketU16
        Throw New NotImplementedException()
    End Function

    ''' <summary>
    ''' This function is not currently implemented. Calling it will throw a NotImplementedException.
    ''' </summary>
    ''' <param name="addrData"></param>
    ''' <param name="numCaptures"></param>
    ''' <param name="numBuffers"></param>
    ''' <returns></returns>
    Public Function ReadRegArrayStream(addrData As IEnumerable(Of AddrDataPair), numCaptures As UInteger, numBuffers As UInteger) As UShort() Implements IRegInterface.ReadRegArrayStream
        Throw New NotImplementedException()
    End Function

    ''' <summary>
    ''' This function is not currently implemented. Calling it will throw a NotImplementedException.
    ''' </summary>
    ''' <param name="addr"></param>
    ''' <param name="data"></param>
    Public Sub WriteRegWord(addr As UInteger, data As UInteger) Implements IRegInterface.WriteRegWord
        Throw New NotImplementedException()
    End Sub

    ''' <summary>
    ''' This function is not currently implemented. Calling it will throw a NotImplementedException.
    ''' </summary>
    Public Sub Start() Implements IRegInterface.Start
        Throw New NotImplementedException()
    End Sub

    ''' <summary>
    ''' If the data ready is used for register reads
    ''' </summary>
    ''' <returns>The current data ready usage setting</returns>
    Public Property DrActive As Boolean Implements AdisApi.IRegInterface.DrActive
        Get
            Return m_FX3SPIConfig.DrActive
        End Get
        Set(value As Boolean)
            m_FX3SPIConfig.DrActive = value
            If m_FX3Connected Then
                m_ActiveFX3.ControlEndPt.Index = 12
                m_ActiveFX3.ControlEndPt.Value = m_FX3SPIConfig.DrActive
                ConfigureSPI()
            End If
        End Set
    End Property

    ''' <summary>
    ''' Switches burstMode on and off. Set burstMode to the number of burst read registers. 
    ''' </summary>
    ''' <returns>The number of burst read registers.</returns>
    Public Property burstMode() As UShort Implements IRegInterface.BurstMode
        Get
            Return m_burstMode
        End Get
        Set(ByVal value As UShort)
            m_burstMode = value
        End Set
    End Property

    ''' <summary>
    ''' Sets the timeout for the Bulk Endpoint used in real time streaming modes.
    ''' </summary>
    ''' <returns>The timeout time, in seconds</returns>
    Public Property StreamTimeoutSeconds As Integer Implements IRegInterface.StreamTimeoutSeconds
        Get
            'Initialize if not set
            If IsNothing(m_StreamTimeout) Then
                m_StreamTimeout = 3
            End If
            Return m_StreamTimeout
        End Get
        Set(value As Integer)
            If value < 1 Then
                Throw New FX3ConfigurationException("ERROR: Stream timeout invalid")
            End If
            m_StreamTimeout = value
        End Set
    End Property

    ''' <summary>
    ''' Drives the Reset pin low for 500ms and then sleeps for another 500ms
    ''' </summary>
    Public Sub Reset() Implements IRegInterface.Reset

        'Drives a low pulse on the reset pin for 250ms
        PulseDrive(ResetPin, 0, 250, 1)
        'Sleep for 100 ms
        System.Threading.Thread.Sleep(100)
        'Wait for ready pin to be high
        PulseWait(ReadyPin, 1, 0, 2000)

    End Sub

    ''' <summary>
    ''' Starts a buffered stream operation. The registers listed in addr are read numCaptures times per register buffer. This process is repeated numBuffers times. 
    ''' </summary>
    ''' <param name="addr">List of register addresses to read</param>
    ''' <param name="numCaptures">Number of times to read the register list per buffer.</param>
    ''' <param name="numBuffers">Number of total register buffers to read.</param>
    ''' <param name="timeoutSeconds">Stream timeout, in seconds</param>
    ''' <param name="worker">Background worker to handle progress updates</param>
    Public Sub StartBufferedStream(addr As IEnumerable(Of UInteger), numCaptures As UInteger, numBuffers As UInteger, timeoutSeconds As Integer, worker As BackgroundWorker) Implements IRegInterface.StartBufferedStream

        'Insert "Nothing" to make StartBufferedStream() happy
        Dim addrDataList As New List(Of AddrDataPair)
        For Each item In addr
            addrDataList.Add(New AddrDataPair(item, Nothing))
        Next

        StartBufferedStream(addrDataList, numCaptures, numBuffers, timeoutSeconds, worker)

    End Sub

    ''' <summary>
    ''' Starts a buffered stream operation. This is usually called from the TextFileStreamManager. DUTType must be set before executing. 
    ''' </summary>
    ''' <param name="addrData">The list of register addresses to read from, when PartType is not ADcmXLx021</param>
    ''' <param name="numCaptures">The number of reads to perform on each register listed in addr</param>
    ''' <param name="numBuffers">The total number of buffers to read. One buffer is either a frame or a set of register reads</param>
    ''' <param name="timeoutSeconds">The bulk endpoint timeout time</param>
    ''' <param name="worker">A Background worker object which can be used by a GUI to track the current stream status and send cancellation requests</param>
    Public Sub StartBufferedStream(ByVal addrData As IEnumerable(Of AddrDataPair), ByVal numCaptures As UInteger, ByVal numBuffers As UInteger, ByVal timeoutSeconds As Integer, ByVal worker As BackgroundWorker) Implements IRegInterface.StartBufferedStream

        'Check the worker status
        Dim reportProgress As Boolean = Not IsNothing(worker)
        If reportProgress Then
            reportProgress = reportProgress And worker.WorkerReportsProgress
        End If
        Dim supportsCancellation As Boolean = (Not IsNothing(worker))
        If supportsCancellation Then
            supportsCancellation = supportsCancellation And worker.WorkerSupportsCancellation
        End If

        'Set the write bit for each address that has data (likely a page write)
        For Each item In addrData
            If Not item.data Is Nothing Then
                item.addr = item.addr Or &H80
            End If
        Next

        'Track the progress in the current buffered stream
        Dim progress, oldProgress As Integer

        'Update the timeout property of the interface
        StreamTimeoutSeconds = timeoutSeconds

        'Throw exception if the number of buffers (reads) is invalid
        If numBuffers < 1 Then
            Throw New FX3ConfigurationException("ERROR: numBuffers must be at least one")
        End If

        'Initialize progress counters
        progress = 0
        oldProgress = 0

        'Spin up the streaming threads

        'If the DUT is set as a ADcmXLx021
        If PartType = DUTType.ADcmXL1021 Or PartType = DUTType.ADcmXL2021 Or PartType = DUTType.ADcmXL3021 Then
            'Start the streaming threads
            StartRealTimeStreaming(numBuffers)
        Else
            If burstMode = 0 Then
                'Generic stream manager implementation for IMU, etc
                StartGenericStream(addrData, numCaptures, numBuffers)
            Else
                'Burst stream manager implementation
                StartBurstStream(numBuffers)
            End If
        End If

        'While loop for worker events
        While (GetNumBuffersRead < numBuffers) And m_StreamThreadRunning
            'Check for cancellations
            If supportsCancellation Then
                If worker.CancellationPending Then
                    'When a cancel is received stop stream and exit
                    StopStream()
                    Exit Sub
                End If
            End If
            'Update progress every percent
            If reportProgress Then
                progress = (GetNumBuffersRead / numBuffers) * 100
                If progress > oldProgress Then
                    worker.ReportProgress(progress)
                    oldProgress = progress
                End If
            End If
            'Sleep so as to not rail the processor
            Threading.Thread.Sleep(10)
        End While

    End Sub

    ''' <summary>
    ''' Stops the currently running data stream, if any.
    ''' </summary>
    Public Sub StopStream() Implements IRegInterface.StopStream

        'If the DUT is set to ADcmXLx021 and it is streaming then stop
        If m_StreamThreadRunning Then
            If PartType = DUTType.ADcmXL1021 Or PartType = DUTType.ADcmXL2021 Or PartType = DUTType.ADcmXL3021 Then
                StopRealTimeStreaming()
            Else
                'If streaming for other device is running then stop the stream
                If burstMode = 0 Then
                    StopGenericStream()
                Else
                    StopBurstStream()
                End If
            End If
        End If

    End Sub

    ''' <summary>
    ''' This function returns a single buffered stream packet. Needed for IBufferedStreamProducer
    ''' </summary>
    ''' <returns>The stream data packet, as a short</returns>
    Public Function GetBufferedStreamDataPacket() As UShort() Implements IRegInterface.GetBufferedStreamDataPacket

        'Wait for the frame (or buffer in case of IMU) and then return it
        Return GetBuffer()

    End Function

    ''' <summary>
    ''' This function reads a set of registers using a streaming endpoint from the FX3.
    ''' </summary>
    ''' <param name="addr"></param>
    ''' <param name="numCaptures"></param>
    ''' <param name="numBuffers"></param>
    ''' <returns></returns>
    Public Function ReadRegArrayStream(addr As IEnumerable(Of UInteger), numCaptures As UInteger, numBuffers As UInteger) As UShort() Implements IRegInterface.ReadRegArrayStream

        Dim addrData As New List(Of AddrDataPair)

        'Build a list of address data pairs from addr
        For Each address In addr
            addrData.Add(New AddrDataPair(address, Nothing))
        Next

        'Call the overload which takes an addrDataPair
        Return ReadRegArrayStream(addrData, numCaptures, numBuffers)

    End Function

    ''' <summary>
    ''' This function writes a single register byte, given as an Address / Data pair
    ''' </summary>
    ''' <param name="addrData">The AddrDataPair to be written</param>
    Public Sub WriteRegByte(addrData As AddrDataPair) Implements IRegInterface.WriteRegByte

        'Make a call to the hardware level WriteRegByte function with the given data
        WriteRegByte(addrData.addr, addrData.data)

    End Sub

    ''' <summary>
    ''' This function writes an enumerable list of data to the DUT as AddrDataPairs
    ''' </summary>
    ''' <param name="addrData">The list of AddrDataPair to be written to DUT</param>
    Public Sub WriteRegByte(addrData As IEnumerable(Of AddrDataPair)) Implements IRegInterface.WriteRegByte

        'Iterate through the IEnumerable list, performing writes as needed
        For Each value In addrData
            WriteRegByte(value.addr, value.data)
        Next

    End Sub

    ''' <summary>
    ''' This is the most general WriteRegByte, which the others are based on
    ''' </summary>
    ''' <param name="addr">The address to write to</param>
    ''' <param name="data">The byte of data to write</param>
    Public Sub WriteRegByte(addr As UInteger, data As UInteger) Implements IRegInterface.WriteRegByte

        'Transfer buffer
        Dim buf(3) As Byte

        'status message
        Dim status As UInt32

        'Configure control endpoint for a single byte register write
        ConfigureControlEndpoint(USBCommands.ADI_WRITE_BYTE, False)
        FX3ControlEndPt.Value = data And &HFFFF
        FX3ControlEndPt.Index = addr And &HFFFF

        'Transfer data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: WriteRegByte timed out - Check board connection")
        End If

        'Read back the operation status from the return buffer
        status = BitConverter.ToUInt32(buf, 0)
        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Bad write command - " + status.ToString("X4"))
        End If

    End Sub

    ''' <summary>
    ''' Overload of WriteRegByte which allows for multiple registers to be specified to write to, as an IEnumerable list of register addresses.
    ''' </summary>
    ''' <param name="addr">The list of register addresses to write to.</param>
    ''' <param name="data">The data to write to each register in the address list.</param>
    Public Sub WriteRegByte(addr As IEnumerable(Of UInteger), data As IEnumerable(Of UInteger)) Implements IRegInterface.WriteRegByte

        'Index in the IEnumerable register list
        Dim index As Integer = 0

        'For each address / data pair, call writeRegByte
        While index < addr.Count And index < data.Count
            WriteRegByte(addr(index), data(index))
            index = index + 1
        End While

    End Sub

    ''' <summary>
    ''' Reads an array of 16 bit register values using the bulk in endpoint
    ''' </summary>
    ''' <param name="addr">The list of registers to read</param>
    ''' <returns>The register values, as a UShort array</returns>
    Public Function ReadRegArray(addr As IEnumerable(Of UInteger)) As UShort() Implements IRegInterface.ReadRegArray

        'Allocate variables needed for transfer

        'FX3 Buffer size (in bytes)
        Dim bufSize As Integer = 12288
        Dim tempBufSize As Integer
        'Total number of bytes to be transfered
        Dim numBytes As Integer = addr.Count * 2
        'Number of 12KB transfers needed
        Dim numTransfers As Integer
        'Variable for tracking position in addr
        Dim addrIndex As Integer
        'Byte array for transfering data
        Dim buf(bufSize - 1) As Byte
        'List to store return values
        Dim returnList As New List(Of UShort)
        'Variable for tracking index in the transfer buffer
        Dim bufIndex As Integer = 0
        'Varaible for converting byte values to short
        Dim regValue As UShort

        'Split into multiple transactions if needed
        If numBytes > bufSize Then
            numTransfers = Math.Ceiling(numBytes / bufSize)
        Else
            numTransfers = 1
        End If

        'Loop though, performing transfers as needed
        addrIndex = 0
        For transferCount As Integer = 1 To numTransfers
            bufIndex = 0

            'Build bulk transfer buffer for when DR is enabled
            If DrActive Then
                While (bufIndex <= (bufSize - m_BytesPerBulkRead)) And (addrIndex < addr.Count)
                    For i As Integer = 0 To m_BytesPerBulkRead / 2
                        buf(bufIndex) = addr(addrIndex)
                        buf(bufIndex + 1) = 0
                        bufIndex = bufIndex + 2
                        addrIndex = addrIndex + 1
                    Next
                End While
            Else
                'Build transfer buffer otherwise
                While (addrIndex < addr.Count) And (bufIndex < bufSize)
                    buf(bufIndex) = addr(addrIndex)
                    buf(bufIndex + 1) = 0
                    bufIndex = bufIndex + 2
                    addrIndex = addrIndex + 1
                End While
            End If

            'Send control transfer
            ConfigureControlEndpoint(USBCommands.ADI_BULK_REGISTER_TRANSFER, True)
            'Set the index equal to the number of bytes to read
            FX3ControlEndPt.Index = bufIndex
            'Set the value to the number of bytes per data ready
            If DrActive Then
                FX3ControlEndPt.Value = m_BytesPerBulkRead
            Else
                FX3ControlEndPt.Value = 0
            End If
            If Not XferControlData(buf, 4, 2000) Then
                Throw New FX3CommunicationException("ERROR: Control Endpoint transfer timed out")
            End If

            'Send bulk transfer
            tempBufSize = bufIndex
            If Not DataOutEndPt.XferData(buf, tempBufSize) Then
                Throw New FX3CommunicationException("ERROR: Bulk endpoint data transfer out failed")
            End If

            'Receieve data back
            tempBufSize = bufIndex
            If Not DataInEndPt.XferData(buf, tempBufSize) Then
                Throw New FX3CommunicationException("ERROR: Bulk endpoint data transfer in failed")
            End If

            'Build the return array
            For i As Integer = 0 To bufIndex - 2 Step 2
                regValue = buf(i)
                regValue = regValue << 8
                regValue = regValue + buf(i + 1)
                returnList.Add(regValue)
            Next

        Next

        'Convert list to array and return
        Return returnList.ToArray()

    End Function

    ''' <summary>
    ''' ReadRegArray overload which includes register writes. Breaks the call into multiple calls of readRegByte and writeRegByte
    ''' </summary>
    ''' <param name="addrData">The data to read/write</param>
    ''' <param name="numCaptures">The number of times to perform the read/write operation</param>
    ''' <returns>The output data, as a UShort array</returns>
    Public Function ReadRegArray(addrData As IEnumerable(Of AddrDataPair), numCaptures As UInteger) As UShort() Implements IRegInterface.ReadRegArray

        'If data is null (nothing) perform a read of the address
        'If data is not null write the data to address

        'List to store output in
        Dim outputValues As New List(Of UShort)
        Dim loopCounter As Integer = 0

        For loopCounter = 0 To numCaptures - 1
            For Each pair In addrData
                If IsNothing(pair.data) Then
                    'Read operation
                    outputValues.Add(ReadRegWord(pair.addr))
                Else
                    'Write operation (lower byte)
                    WriteRegByte(pair.addr, pair.data And &HFF)
                    'Write operation (upper byte)
                    WriteRegByte(pair.addr + 1, (pair.data And &HFF00) >> 8)
                    'Add a 0 to output to make adbfInterface happy
                    outputValues.Add(0)
                End If
            Next
        Next

        'Return output list as array
        Return outputValues.ToArray()

    End Function

    ''' <summary>
    ''' Overload of ReadRegArray which builds a new IEnumerable of addr and call the overload which takes an enumerable of addr
    ''' </summary>
    ''' <param name="addr">List of register address's to read</param>
    ''' <param name="numCaptures">Number of captures to perform on the register list</param>
    ''' <returns>The register values, as a short array</returns>
    Public Function ReadRegArray(addr As IEnumerable(Of UInteger), numCaptures As UInteger) As UShort() Implements IRegInterface.ReadRegArray

        Dim newAddr((addr.Count) * numCaptures - 1) As UInteger
        Dim addrIndex As Integer = 0

        'Set the bytes per data ready
        If DrActive Then
            m_BytesPerBulkRead = addr.Count * 2
        Else
            m_BytesPerBulkRead = 0
        End If

        'Build address list
        For Each address In addr
            For i As Integer = 0 To numCaptures - 1
                newAddr(addrIndex) = address
                addrIndex = addrIndex + 1
            Next
        Next

        'Call overload which only takes addresses
        Return ReadRegArray(newAddr)

    End Function

    ''' <summary>
    ''' This is the most general ReadRegByte. Other implementations are based on this.
    ''' </summary>
    ''' <param name="addr">The address to read</param>
    ''' <returns>Returns the value read in over SPI as a short</returns>
    Public Function ReadRegByte(addr As UInteger) As UShort Implements IRegInterface.ReadRegByte

        Dim value As UShort = ReadRegWord(addr)

        'Return upper byte if even address, lower if odd
        If addr Mod 2 = 0 Then
            'Even case
            Return value And &HFF
        Else
            'Odd case
            Return (value << 8)
        End If

    End Function

    ''' <summary>
    ''' Reads a single 16 bit register on the DUT
    ''' </summary>
    ''' <param name="addr">The address of the register to read</param>
    ''' <returns>The 16 bit register value, as a UShort</returns>
    Public Function ReadRegWord(addr As UInteger) As UShort Implements IRegInterface.ReadRegWord

        'Transfer buffer
        Dim buf(5) As Byte

        'Status word
        Dim status As UInt32

        'Variables to parse value from the buffer
        Dim returnValue, shiftValue As UShort

        'Configure the control endpoint for a single register read
        ConfigureControlEndpoint(USBCommands.ADI_READ_BYTES, False)

        'Set the value to 0
        FX3ControlEndPt.Value = 0

        'Set the index to the register address (lower byte)
        FX3ControlEndPt.Index = addr

        'Transfer the data
        If Not XferControlData(buf, 6, 2000) Then
            Throw New FX3CommunicationException("ERROR: FX3 is not responding to transfer request")
        End If

        'Calculate reg value
        shiftValue = buf(4)
        shiftValue = shiftValue << 8
        returnValue = shiftValue + buf(5)

        'Read back the operation status from the return buffer
        status = BitConverter.ToUInt32(buf, 0)

        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Bad read command - " + status.ToString("X4"))
        End If

        Return returnValue

    End Function

#End Region

End Class