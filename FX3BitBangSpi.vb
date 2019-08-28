'File:          FX3BitBangSpi.vb
'Author:        Alex Nolan (alex.nolan@analog.com)
'Date:          08/27/2019
'Description:   This file contains all the interfacing functions for the FX3 SPI bitbang capabilities.

Partial Class FX3Connection

    Public Property BitBangSpiConfig As BitBangSpiConfig
        Get
            Return m_BitBangSpi
        End Get
        Set(value As BitBangSpiConfig)
            m_BitBangSpi = value
        End Set
    End Property

    Public Function BitBangSpi(BitsPerTransfer As UInteger, NumTransfers As UInteger, MOSIData As Byte(), TimeoutInMs As UInteger) As Byte()
        Dim buf As New List(Of Byte)
        Dim timeoutTimer As New Stopwatch()
        Dim transferStatus As Boolean
        Dim bytesPerTransfer As UInteger
        Dim len As Integer

        'Build the buffer
        buf.AddRange(m_BitBangSpi.GetParameterArray())
        buf.Add(BitsPerTransfer And &HFF)
        buf.Add((BitsPerTransfer And &HFF00) >> 8)
        buf.Add((BitsPerTransfer And &HFF0000) >> 16)
        buf.Add((BitsPerTransfer And &HFF000000) >> 24)
        buf.Add(NumTransfers And &HFF)
        buf.Add((NumTransfers And &HFF00) >> 8)
        buf.Add((NumTransfers And &HFF0000) >> 16)
        buf.Add((NumTransfers And &HFF000000) >> 24)
        buf.AddRange(MOSIData)

        'Find bytes per transfer
        bytesPerTransfer = BitsPerTransfer >> 3
        If BitsPerTransfer And &H7 Then bytesPerTransfer += 1

        'Check size
        If buf.Count() > 4096 Then
            Throw New FX3ConfigurationException("ERROR: Too much data (" + buf.Count() + " bytes) in a single bit banged SPI transaction.")
        End If

        'Send the start command
        ConfigureControlEndpoint(USBCommands.ADI_BITBANG_SPI, True)
        If Not XferControlData(buf.ToArray(), buf.Count(), 2000) Then
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer failed for SPI bit bang setup")
        End If

        'Read data back from part
        transferStatus = False
        bytesPerTransfer = BitsPerTransfer / 8
        timeoutTimer.Start()
        Dim resultBuf(bytesPerTransfer * NumTransfers - 1) As Byte
        While ((Not transferStatus) And (timeoutTimer.ElapsedMilliseconds() < TimeoutInMs))
            len = bytesPerTransfer * NumTransfers
            transferStatus = DataInEndPt.XferData(resultBuf, len)
        End While
        timeoutTimer.Stop()

        If Not transferStatus Then
            Console.WriteLine("ERROR: Bit bang SPI transfer timed out")
        End If

        Return resultBuf
    End Function

    Public Function BitBangReadReg16(addr As UInteger) As UShort
        Dim MOSI As New List(Of Byte)
        Dim buf() As Byte
        Dim retValue, shift As UShort
        addr = addr And &HFF
        MOSI.Add(addr)
        MOSI.Add(0)
        MOSI.Add(0)
        MOSI.Add(0)
        buf = BitBangSpi(16, 2, MOSI.ToArray(), m_StreamTimeout)
        shift = buf(2)
        shift = shift << 8
        retValue = shift + buf(3)
        Return retValue
    End Function

    Public Sub BitBangWriteRegByte(addr As Byte, data As Byte)
        Dim MOSI As New List(Of Byte)
        'Addr first (with write bit)
        addr = addr And &HFF
        addr = addr Or &H80
        MOSI.Add(addr)
        MOSI.Add(data)
        'Call bitbang SPI implementation
        BitBangSpi(16, 1, MOSI.ToArray(), m_StreamTimeout)
    End Sub

    Public Sub RestoreHardwareSpi()

    End Sub

End Class
