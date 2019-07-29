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
    ''' Performs a single 32 bit SPI data transfer.
    ''' </summary>
    ''' <param name="WriteData">Data to send over the MOSI line</param>
    ''' <returns>The 32 bit data read back on the MISO line</returns>
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
    ''' 
    ''' </summary>
    ''' <param name="WriteData"></param>
    ''' <returns></returns>
    Public Function TransferArray(WriteData As IEnumerable(Of UInteger)) As UInteger() Implements ISpi32Interface.TransferArray
        Dim MISOData As New List(Of UInteger)
        For Each value In WriteData
            MISOData.Add(Transfer(value))
        Next
        Return MISOData.ToArray()
    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="WriteData"></param>
    ''' <param name="numCaptures"></param>
    ''' <returns></returns>
    Public Function TransferArray(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger) As UInteger() Implements ISpi32Interface.TransferArray
        Dim MISOData As New List(Of UInteger)

        For capCount As Integer = 0 To numCaptures - 1
            For Each value In WriteData
                MISOData.Add(Transfer(value))
            Next
        Next

        Return MISOData.ToArray()
    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="WriteData"></param>
    ''' <param name="numCaptures"></param>
    ''' <param name="numBuffers"></param>
    ''' <returns></returns>
    Public Function TransferArray(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger, numBuffers As UInteger) As UInteger() Implements ISpi32Interface.TransferArray
        Dim MISOData As New List(Of UInteger)

        For bufCount As Integer = 0 To numBuffers - 1
            For capCount As Integer = 0 To numCaptures - 1
                For Each value In WriteData
                    MISOData.Add(Transfer(value))
                Next
            Next
        Next

        Return MISOData.ToArray()

    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    Private Sub ISpi32Interface_StopStream() Implements ISpi32Interface.StopStream
        Throw New NotImplementedException()
    End Sub

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="WriteData"></param>
    ''' <param name="numCaptures"></param>
    ''' <param name="numBuffers"></param>
    ''' <param name="timeoutSeconds"></param>
    ''' <param name="worker"></param>
    Private Sub ISpi32Interface_StartBufferedStream(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger, numBuffers As UInteger, timeoutSeconds As Integer, worker As BackgroundWorker) Implements ISpi32Interface.StartBufferedStream

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
            Throw New FX3ConfigurationException("ERROR: Invalid WriteData provided to StartBufferedStream. Transfer count of " + buf.Count().ToString() + "bytes exceeds allowed amount of 512 bytes")
        End If

        'Configure control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_TRANSFER_STREAM, True)

        'Send stream start command
        If Not XferControlData(buf.ToArray(), buf.Count(), 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured during control endpoint transfer for SPI transfer stream")
        End If

        'Start the streaming thread
        m_StreamThread = New Thread(AddressOf ISpi32InterfaceStreamWorker)
        m_StreamThread.Start()

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

    Private Sub ISpi32InterfaceStreamWorker()

        'Set the stream running flags
        m_StreamThreadRunning = True
        m_FramesRead = 0

        'Create the 32 bit data queue
        m_TransferStreamData = New ConcurrentQueue(Of UInteger())

        m_StreamThreadRunning = True
        While m_StreamThreadRunning

        End While

        'Set exit flag

    End Sub

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <returns></returns>
    Private Function ISpi32Interface_GetBufferedStreamDataPacket() As UInteger() Implements ISpi32Interface.GetBufferedStreamDataPacket
        Throw New NotImplementedException()
    End Function

    ''' <summary>
    ''' 
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
    ''' 
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

    Public Property DrPin As IPinObject Implements ISpi32Interface.DrPin
        Get
            Return ReadyPin
        End Get
        Set(value As IPinObject)
            ReadyPin = value
        End Set
    End Property

    Private Property ISpi32Interface_DrPolarity As Boolean Implements ISpi32Interface.DrPolarity
        Get
            Return DrPolarity
        End Get
        Set(value As Boolean)
            DrPolarity = value
        End Set
    End Property

End Class
