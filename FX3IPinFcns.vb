'Author:        Alex Nolan
'Date:          8/1/2018
'Description:   Extension of the FX3Connection class. Has all the functions needed to 
'               implement the IPinFcns interface defined in the AdisApi.

Imports AdisApi

Partial Class FX3Connection

#Region "Pin Functions Implementation"

    ''' <summary>
    ''' This function drives a pin to the specified level for a given time interval in ms
    ''' </summary>
    ''' <param name="pin">The FX3PinObject for the pin to drive</param>
    ''' <param name="polarity">The level to drive the pin to. 1 - high, 0 - low</param>
    ''' <param name="pperiod">The time to drive the pin for, in ms</param>
    ''' <param name="mode">Not implemented</param>
    Public Sub PulseDrive(pin As IPinObject, polarity As UInteger, pperiod As Double, mode As UInteger) Implements IPinFcns.PulseDrive

        'Send a vendor command to drive pin (returns immediatly)
        ConfigureControlEndpoint(USBCommands.ADI_PULSE_DRIVE, True)
        Dim buf(6) As Byte
        Dim intPeriod As UInteger = Convert.ToUInt32(pperiod)
        Dim status, shiftedValue As UInteger
        Dim goodTransfer As Boolean

        'Set the GPIO pin number (only 1 byte in FX3PinObject)
        buf(0) = pin.pinConfig And &HFF
        buf(1) = 0
        'Set the polarity (1 for high, 0 for low)
        buf(2) = polarity And &H1
        'Set the drive time
        buf(3) = intPeriod And &HFF
        buf(4) = (intPeriod And &HFF00) >> 8
        buf(5) = (intPeriod And &HFF0000) >> 16
        buf(6) = (intPeriod And &HFF000000) >> 24

        'Start data transfer
        XferControlData(buf, 7, 2000)

        'Function should block until end of pin drive
        System.Threading.Thread.Sleep(pperiod + 100)

        'Wait for the status to be returned over BULK-In
        goodTransfer = DataInEndPt.XferData(buf, 4)

        If Not goodTransfer Then
            Throw New Exception("ERROR: Transfer from FX3 after pin drive failed")
        End If

        'Get the status from the buffer
        status = buf(0)
        shiftedValue = buf(1)
        shiftedValue = shiftedValue << 8
        status = status + shiftedValue
        shiftedValue = buf(2)
        shiftedValue = shiftedValue << 16
        status = status + shiftedValue
        shiftedValue = buf(3)
        shiftedValue = shiftedValue << 24
        status = status + shiftedValue

        'Throw exception if the operation failed
        If Not status = 0 Then
            Throw New Exception("ERROR: Pin Drive Failed, Status: " + status.ToString("X4"))
        End If

    End Sub

    ''' <summary>
    ''' This function waits for a pin to reach a specified level
    ''' </summary>
    ''' <param name="pin">The pin to poll</param>
    ''' <param name="polarity">The level to wait for. 1 - high, 0 - low</param>
    ''' <param name="delayInMs">The delay from the start of the function call to when the pin polling starts</param>
    ''' <param name="timeoutInMs">The timeout from when the pin polling starts to when the function returns, if the desired level is never reached</param>
    ''' <returns>The total time waited (including delay) in ms</returns>
    Public Function PulseWait(pin As IPinObject, polarity As UInteger, delayInMs As UInteger, timeoutInMs As UInteger) As Double Implements IPinFcns.PulseWait

        'Waits for a pin level and returns the time waited
        'In the FX3 implementation, the maximum time allowed for timeoutInMs + delayInMs is 4.497 * 10^6 ms
        'Any values set over that amount will have no timeout (blocks forever until level is detected)

        'Declare variables needed for transfer
        Dim buf(10) As Byte
        Dim waitTime As UInteger
        Dim conversionFactor, shiftedConversionFactor As UInteger
        Dim shiftedValue As UInteger
        Dim totalTime As Double
        Dim transferStatus As Boolean
        Dim timeoutTimer As New Stopwatch
        Dim convertedTime As Double

        'Set the total time
        totalTime = delayInMs + timeoutInMs

        'Set the pin
        buf(0) = pin.pinConfig And &HFF
        buf(1) = 0

        'Set the polarity
        buf(2) = polarity

        'set the delay to 0 if it is greater than what can be stored in an int
        If delayInMs > UInt32.MaxValue / 1000 Then
            delayInMs = 0
        End If

        buf(3) = delayInMs And &HFF
        buf(4) = (delayInMs And &HFF00) >> 8
        buf(5) = (delayInMs And &HFF0000) >> 16
        buf(6) = (delayInMs And &HFF000000) >> 24

        'set the timeout to 0 if its greater than the allowable timer value
        If timeoutInMs > UInt32.MaxValue / 1000 Then
            timeoutInMs = 0
        End If

        buf(7) = timeoutInMs And &HFF
        buf(8) = (timeoutInMs And &HFF00) >> 8
        buf(9) = (timeoutInMs And &HFF0000) >> 16
        buf(10) = (timeoutInMs And &HFF000000) >> 24

        'Start stopwatch
        timeoutTimer.Start()

        'Send a vendor command to start a pulse wait operation (returns immediatly)
        ConfigureControlEndpoint(USBCommands.ADI_PULSE_WAIT, True)
        If Not XferControlData(buf, 11, 2000) Then
            Throw New Exception("ERROR: Control Endpoint transfer timed out")
        End If

        'Start bulk transfer
        transferStatus = False
        If totalTime = 0 Then
            transferStatus = DataInEndPt.XferData(buf, 8)
        Else
            While ((Not transferStatus) And (timeoutTimer.ElapsedMilliseconds() < totalTime))
                transferStatus = DataInEndPt.XferData(buf, 8)
            End While
        End If

        'stop stopwatch
        timeoutTimer.Stop()

        'Read the time value from the buffer
        waitTime = buf(0)
        shiftedValue = buf(1)
        shiftedValue = shiftedValue << 8
        waitTime = waitTime + shiftedValue
        shiftedValue = buf(2)
        shiftedValue = shiftedValue << 16
        waitTime = waitTime + shiftedValue
        shiftedValue = buf(3)
        shiftedValue = shiftedValue << 24
        waitTime = waitTime + shiftedValue

        'Read the scale factor (MS to ticks)
        conversionFactor = buf(4)
        shiftedConversionFactor = buf(5)
        shiftedConversionFactor = shiftedConversionFactor << 8
        conversionFactor = conversionFactor + shiftedConversionFactor
        shiftedConversionFactor = buf(6)
        shiftedConversionFactor = shiftedConversionFactor << 16
        conversionFactor = conversionFactor + shiftedConversionFactor
        shiftedConversionFactor = buf(7)
        shiftedConversionFactor = shiftedConversionFactor << 24
        conversionFactor = conversionFactor + shiftedConversionFactor

        'If the transfer failed return the timeout value
        If Not transferStatus Then
            Return timeoutTimer.ElapsedMilliseconds()
        End If

        'If operation failed on FX3 throw an exception
        If waitTime = &HFFFFFFFF Then
            Throw New Exception("ERROR: Pin read on FX3 failed")
        End If

        'Scale the time waited to MS
        convertedTime = Convert.ToDouble(waitTime)
        convertedTime = Math.Round(convertedTime / conversionFactor, 3)

        'Return the actual time waited
        Return convertedTime

    End Function

    ''' <summary>
    ''' Reads the value of a GPIO pin on the FX3
    ''' </summary>
    ''' <param name="pin">The pin to read, as a FX3PinObject</param>
    ''' <returns>The pin value - 1 is high, 0 is low</returns>
    Public Function ReadPin(pin As IPinObject) As UInteger Implements IPinFcns.ReadPin

        Dim buf(4) As Byte
        Dim status, shiftedValue As UInteger

        'Configure control endpoint for pin read
        ConfigureControlEndpoint(USBCommands.ADI_READ_PIN, False)
        FX3ControlEndPt.Index = pin.pinConfig And &HFF

        'Transfer data
        If Not XferControlData(buf, 5, 1000) Then
            'Throw an exception if the transaction times out
            Throw New Exception("ERROR: Pin read timed out")
        End If

        'Get the status from the buffer
        status = buf(1)
        shiftedValue = buf(2)
        shiftedValue = shiftedValue << 8
        status = status + shiftedValue
        shiftedValue = buf(3)
        shiftedValue = shiftedValue << 16
        status = status + shiftedValue
        shiftedValue = buf(4)
        shiftedValue = shiftedValue << 24
        status = status + shiftedValue

        'If the status is not success throw an exception
        If Not status = 0 Then
            Throw New Exception("ERROR: Pin read failed, status - " + status.ToString("X4"))
        End If

        'Return the input bit of the pin register
        Return buf(0)

    End Function

    ''' <summary>
    ''' Reads a list of FX3 GPIO pins. This function calls the overload which takes an IEnumerable
    ''' </summary>
    ''' <param name="pins">An array of FX3PinObjects to read</param>
    ''' <returns>The pin values, as a UInteger. The first pin is in bit 0, second is in bit 1, and so on</returns>
    Public Function ReadPins(ParamArray pins() As IPinObject) As UInteger Implements IPinFcns.ReadPins

        'Call the overload which takes IEnumerable
        Dim enumerablePins As IEnumerable(Of IPinObject)
        enumerablePins = pins
        Return ReadPins(enumerablePins)

    End Function

    ''' <summary>
    ''' Reads a list of FX3 GPIO pins
    ''' </summary>
    ''' <param name="pins">An enumerable list of FX3PinObjects to read (maximum of 32)</param>
    ''' <returns>The pin values, as a UInteger. The first pin is in bit 0, second is in bit 1, and so on</returns>
    Public Function ReadPins(pins As IEnumerable(Of IPinObject)) As UInteger Implements IPinFcns.ReadPins

        Dim shiftValue As Integer = 0
        Dim pinResult As UInteger = 0
        For Each pin In pins
            pinResult = pinResult And (ReadPin(pin) << shiftValue)
            shiftValue = shiftValue + 1
            If shiftValue > 31 Then
                Throw New Exception("ERROR: Cannot read more than 32 pins in one call to ReadPins")
            End If
        Next

        Return pinResult

    End Function

    Public Function ReadTime(start_pin As UInteger, start_polarity As UInteger, stop_pin As UInteger, stop_polarity As UInteger, delay As UInteger) As UShort() Implements IPinFcns.ReadTime
        Throw New NotImplementedException()
    End Function

    ''' <summary>
    ''' Sets the value of a FX3 GPIO pin. This value will perist until the pin is set to a different value, or read from
    ''' </summary>
    ''' <param name="pin">The FX3PinObject pin to read</param>
    ''' <param name="value">The polarity to set the pin to, 1 - high, 0 - low</param>
    Public Sub SetPin(pin As IPinObject, value As UInteger) Implements IPinFcns.SetPin

        Dim buf(3) As Byte
        Dim status, shiftedValue As UInteger

        'Valid pin values are 1 and 0
        If Not value = 0 Then
            value = 1
        End If

        'Setup a set pin command
        ConfigureControlEndpoint(USBCommands.ADI_SET_PIN, False)
        FX3ControlEndPt.Index = pin.pinConfig And &HFF
        FX3ControlEndPt.Value = value

        'Transfer data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New Exception("ERROR: Pin set operation timed out")
        End If

        'Get the status from the buffer
        status = buf(0)
        shiftedValue = buf(1)
        shiftedValue = shiftedValue << 8
        status = status + shiftedValue
        shiftedValue = buf(2)
        shiftedValue = shiftedValue << 16
        status = status + shiftedValue
        shiftedValue = buf(3)
        shiftedValue = shiftedValue << 24
        status = status + shiftedValue

        'If the status is not success throw an exception
        If Not status = 0 Then
            Throw New Exception("ERROR: Pin set failed, status - " + status.ToString("X4"))
        End If

    End Sub

#End Region

#Region "Pin Properties"

    ''' <summary>
    ''' Readonly property to get the reset pin (mapped to GPIO 0 on FX3)
    ''' </summary>
    ''' <returns>The reset pin, as an IPinObject</returns>
    Public ReadOnly Property ResetPin As IPinObject
        Get
            Return New FX3PinObject(0)
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the DIO1 pin (mapped to GPIO 4 on FX3)
    ''' </summary>
    ''' <returns>Returns the DIO1 pin, as an IPinObject</returns>
    Public ReadOnly Property DIO1 As IPinObject
        Get
            Return New FX3PinObject(4)
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the DIO2 pin (mapped to GPIO 3 on FX3)
    ''' </summary>
    ''' <returns>Returns the DIO2 pin, as an IPinObject</returns>
    Public ReadOnly Property DIO2 As IPinObject
        Get
            Return New FX3PinObject(3)
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the DIO3 pin (mapped to GPIO 2 on FX3)
    ''' </summary>
    ''' <returns>Returns the DIO3 pin, as an IPinObject</returns>
    Public ReadOnly Property DIO3 As IPinObject
        Get
            Return New FX3PinObject(2)
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the DIO4 pin (mapped to GPIO 1 on FX3)
    ''' </summary>
    ''' <returns>Returns the DIO4 pin, as an IPinObject</returns>
    Public ReadOnly Property DIO4 As IPinObject
        Get
            Return New FX3PinObject(1)
        End Get
    End Property

#End Region

End Class