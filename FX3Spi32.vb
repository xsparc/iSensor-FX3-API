'File:          FX3Spi32.vb
'Author:        Alex Nolan (alex.nolan@analog.com), Juan Chong (juan.chong@analog.com)
'Date:          07/15/2019
'Description:   This file contains all the implementation functions for the ISpi32Interface, which allows for a protocol agnostic interface to the SPI bus.

Imports System.ComponentModel
Imports AdisApi

Partial Class FX3Connection

    ''' <summary>
    ''' Performs a single 32 bit SPI data transfer.
    ''' </summary>
    ''' <param name="WriteData"></param>
    ''' <returns></returns>
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

    Public Function TransferArray(WriteData As IEnumerable(Of UInteger)) As UInteger() Implements ISpi32Interface.TransferArray
        Dim MISOData As New List(Of UInteger)
        For Each value In WriteData
            MISOData.Add(Transfer(value))
        Next
        Return MISOData.ToArray()
    End Function

    Public Function TransferArray(WriteData As IEnumerable(Of UInteger), numCaptures As UInteger) As UInteger() Implements ISpi32Interface.TransferArray
        Dim MISOData As New List(Of UInteger)

        For capCount As Integer = 0 To numCaptures - 1
            For Each value In WriteData
                MISOData.Add(Transfer(value))
            Next
        Next

        Return MISOData.ToArray()
    End Function

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
        Throw New NotImplementedException()
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

End Class
