'File:         FX3ErrorLog.vb
'Author:       Alex Nolan (alex.nolan@analog.com)
'Date:         5/1/2020     
'Description:  Error logging for FX3 firmware

#Region "FX3 Error Log Class"

''' <summary>
''' 
''' </summary>
Public Class FX3ErrorLog

    ''' <summary>
    ''' 
    ''' </summary>
    Public Line As UInteger

    ''' <summary>
    ''' 
    ''' </summary>
    Public FileIdentifier As UInteger

    ''' <summary>
    ''' 
    ''' </summary>
    Public BootTimeStamp As UInteger

    ''' <summary>
    ''' 
    ''' </summary>
    Public ErrorCode As UInteger

    ''' <summary>
    ''' 
    ''' </summary>
    Public FirmwareRev As String

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="FlashData"></param>
    Public Sub New(FlashData As Byte())
        If FlashData.Count < 32 Then
            Throw New FX3ConfigurationException("ERROR: Flash log must be instantiated from a 32 byte array")
        End If

        'parse array
        Line = BitConverter.ToUInt32(FlashData, 4)
        ErrorCode = BitConverter.ToUInt32(FlashData, 8)
        BootTimeStamp = BitConverter.ToUInt32(FlashData, 12)
        FileIdentifier = BitConverter.ToUInt32(FlashData, 16)
        FirmwareRev = System.Text.Encoding.UTF8.GetString(FlashData.ToList().GetRange(20, 12).ToArray())

    End Sub

End Class

#End Region

#Region "FX3 Error Log Functions"

Partial Class FX3Connection

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <param name="ByteAddress"></param>
    ''' <param name="ReadLength"></param>
    ''' <returns></returns>
    Public Function ReadFlash(ByteAddress As UInteger, ReadLength As UShort) As Byte()

        'transfer buffer
        Dim buf(ReadLength - 1) As Byte

        'configure for flash read command
        ConfigureControlEndpoint(USBCommands.ADI_READ_FLASH, False)

        'address is passed in value/index
        FX3ControlEndPt.Value = ByteAddress And &HFFFF
        FX3ControlEndPt.Index = ByteAddress >> 16

        'send command
        If Not XferControlData(buf, ReadLength, 5000) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer failed for flash read")
        End If

        'return data placed in buffer
        Return buf

    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    Public Sub ClearErrorLog()

        'transfer buffer
        Dim buf(3) As Byte

        'configure for clear flash command
        ConfigureControlEndpoint(USBCommands.ADI_CLEAR_FLASH_LOG, True)

        'send command
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer failed for flash log clear!")
        End If

    End Sub

    Public Function GetErrorLogCount() As UInteger

        'log count address
        Const LOG_COUNT_ADDRESS As UInteger = &H34000

        Return BitConverter.ToUInt32(ReadFlash(LOG_COUNT_ADDRESS, 4), 0)

    End Function

    ''' <summary>
    ''' 
    ''' </summary>
    ''' <returns></returns>
    Public Function GetErrorLog() As List(Of FX3ErrorLog)

        'log count address
        Const LOG_COUNT_ADDRESS As UInteger = &H34000

        'base address in flash for the log
        Const LOG_BASE_ADDR As UInteger = &H34040

        'log storage capacity
        Const LOG_CAPACITY As UInteger = 1500

        'log to build
        Dim log As New List(Of FX3ErrorLog)

        'log raw byte data
        Dim rawData As New List(Of Byte)

        'log count
        Dim logCount As UInteger

        'bytes to read
        Dim bytesToRead As Integer

        'specific read length
        Dim readLen As UInteger

        'read address
        Dim readAddress As UInteger

        'get the count from flash
        logCount = BitConverter.ToUInt32(ReadFlash(LOG_COUNT_ADDRESS, 4), 0)

        'return for empty log
        If logCount = 0 Then
            Return log
        End If

        'cap at capacity
        If logCount > LOG_CAPACITY Then
            logCount = LOG_CAPACITY
        End If

        bytesToRead = 32 * logCount
        readAddress = LOG_BASE_ADDR
        While bytesToRead > 0
            readLen = Math.Min(4096, bytesToRead)
            rawData.AddRange(ReadFlash(readAddress, readLen))
            readAddress += readLen
            bytesToRead -= readLen
        End While

        'convert raw byte array to error log object array
        readAddress = 0
        For i As Integer = 1 To logCount
            log.Add(New FX3ErrorLog(rawData.GetRange(readAddress, 32).ToArray()))
            readAddress += 32
        Next

        Return log
    End Function

End Class

#End Region
