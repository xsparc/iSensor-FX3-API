'File:          FX3IPinFcns.vb
'Author:        Alex Nolan (alex.nolan@analog.com), Juan Chong (juan.chong@analog.com)
'Date:          8/1/2018
'Description:   Extension of the FX3Connection class. Has all the functions needed to 
'               implement the IPinFcns interface defined in the AdisApi.

Imports AdisApi

Partial Class FX3Connection

#Region "IPinFunctions Implementation"

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

        'Validate that the pin isn't acting as a PWM pin
        If isPWMPin(pin) Then
            Throw New FX3ConfigurationException("ERROR: The selected pin is currently configured to drive a PWM signal. Please call StopPWM(pin) before interfacing with the pin further")
        End If

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
        If Not XferControlData(buf, 7, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer failed before pulse drive.")
        End If

        'Function should block until end of pin drive
        System.Threading.Thread.Sleep(pperiod + 100)

        'Wait for the status to be returned over BULK-In
        If Not DataInEndPt.XferData(buf, 4) Then
            Throw New FX3CommunicationException("ERROR: Transfer from FX3 after pulse drive failed")
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
            Throw New FX3BadStatusException("ERROR: Pin Drive Failed, Status: " + status.ToString("X4"))
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
        Dim buf(15) As Byte
        Dim totalTime As Long
        Dim transferStatus As Boolean
        Dim timeoutTimer As New Stopwatch
        Dim convertedTime As Double
        Dim status, currentTime, rollOverCount, conversionFactor As UInteger
        Dim totalTicks, rollOverCountULong As ULong
        Dim delayOverflow As Boolean
        Dim delayScaled As UInteger

        'Validate that the pin isn't acting as a PWM pin
        If isPWMPin(pin) Then
            Throw New FX3ConfigurationException("ERROR: The selected pin is currently configured to drive a PWM signal. Please call StopPWM(pin) before interfacing with the pin further")
        End If

        'Set the total time for the operation
        totalTime = delayInMs + timeoutInMs

        'Set the pin
        buf(0) = pin.pinConfig And &HFF
        buf(1) = 0

        'Set the polarity
        buf(2) = polarity

        'Sleep on PC side if delay is too large (approx. 7 minutes)
        delayOverflow = False
        delayScaled = delayInMs
        If delayInMs > (UInt32.MaxValue / 10000) Then
            delayOverflow = True
            Threading.Thread.Sleep(delayInMs)
            delayScaled = 0
            totalTime = timeoutInMs
        End If

        buf(3) = delayScaled And &HFF
        buf(4) = (delayScaled And &HFF00) >> 8
        buf(5) = (delayScaled And &HFF0000) >> 16
        buf(6) = (delayScaled And &HFF000000) >> 24

        'set the timeout to 0 (no timeout in firmware side) if timeout + delay is greater than a single period 
        If (timeoutInMs + delayInMs) > (UInt32.MaxValue / 10000) Then
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
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer timed out")
        End If

        'Start bulk transfer
        transferStatus = False
        If totalTime = 0 Then
            transferStatus = DataInEndPt.XferData(buf, 16)
        Else
            While ((Not transferStatus) And (timeoutTimer.ElapsedMilliseconds() < totalTime))
                transferStatus = DataInEndPt.XferData(buf, 16)
            End While
        End If

        'stop stopwatch
        timeoutTimer.Stop()

        'If the transfer failed return the timeout value
        If Not transferStatus Then
            If delayOverflow Then
                'The delay was inserted as a sleep on the PC side
                Return Convert.ToDouble(timeoutTimer.ElapsedMilliseconds() + delayInMs)
            Else
                Return Convert.ToDouble(timeoutTimer.ElapsedMilliseconds())
            End If

        End If

        'Read status from the buffer and throw exception for bad status
        status = BitConverter.ToUInt32(buf, 0)
        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Failed to configure PulseWait pin as input, error code: " + status.ToString("X4"))
        End If

        'Read current time
        currentTime = BitConverter.ToUInt32(buf, 4)

        'Read roll over counter
        rollOverCount = BitConverter.ToUInt32(buf, 8)
        rollOverCountULong = CULng(rollOverCount)

        'Read conversion factor
        conversionFactor = BitConverter.ToUInt32(buf, 12)

        'Calculate the total time, in timer ticks
        totalTicks = rollOverCountULong * UInt32.MaxValue
        totalTicks += currentTime

        'Scale the time waited to MS
        convertedTime = Convert.ToDouble(totalTicks)
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

        'Validate that the pin isn't acting as a PWM pin
        If isPWMPin(pin) Then
            Throw New FX3ConfigurationException("ERROR: The selected pin is currently configured to drive a PWM signal. Please call StopPWM(pin) before interfacing with the pin further")
        End If

        'Configure control endpoint for pin read
        ConfigureControlEndpoint(USBCommands.ADI_READ_PIN, False)
        FX3ControlEndPt.Index = pin.pinConfig And &HFF

        'Transfer data
        If Not XferControlData(buf, 5, 1000) Then
            'Throw an exception if the transaction times out
            Throw New FX3CommunicationException("ERROR: Pin read timed out")
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
            Throw New FX3BadStatusException("ERROR: Pin read failed, status - " + status.ToString("X4"))
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

        'Validate input
        If pins.Count > 32 Then
            Throw New FX3ConfigurationException("ERROR: Cannot read more than 32 pins in one call to ReadPins")
        End If

        Dim shiftValue As Integer = 0
        Dim pinResult As UInteger = 0
        'Build the pin values into a UInteger
        For Each pin In pins
            pinResult = pinResult And (ReadPin(pin) << shiftValue)
            shiftValue = shiftValue + 1
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

        'Validate that the pin isn't acting as a PWM pin
        If isPWMPin(pin) Then
            Throw New FX3ConfigurationException("ERROR: The selected pin is currently configured to drive a PWM signal. Please call StopPWM(pin) before interfacing with the pin further")
        End If

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
            Throw New FX3CommunicationException("ERROR: Pin set operation timed out")
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
            Throw New FX3BadStatusException("ERROR: Pin set failed, status - " + status.ToString("X4"))
        End If

    End Sub

#End Region

#Region "Other Pin Functions"

    Public Function MeasureBusyPulse(TriggerPin As IPinObject, TriggerDriveTime As UInteger, TriggerDrivePolarity As UInteger, BusyPin As IPinObject, BusyPolarity As UInteger, Timeout As UInteger) As Double
        'Declare variables needed for transfer
        Dim buf(15) As Byte
        Dim transferStatus As Boolean
        Dim timeoutTimer As New Stopwatch
        Dim convertedTime As Double
        Dim status, currentTime, rollOverCount, conversionFactor As UInteger
        Dim totalTicks, rollOverCountULong As ULong
        Dim FX3Timeout As UInteger

        'Validate that the pin isn't acting as a PWM pin
        If isPWMPin(BusyPin) Then
            Throw New FX3ConfigurationException("ERROR: The selected pin is currently configured to drive a PWM signal. Please call StopPWM(pin) before interfacing with the pin further")
        End If

        'Validate that the trigger drive time isn't too long (less than one timer period)
        If TriggerDriveTime > (UInt32.MaxValue / 10000) Then
            Throw New FX3ConfigurationException("ERROR: Invalid trigger pin drive time of " + TriggerDriveTime.ToString() + "ms. Max allowed is " + (UInt32.MaxValue / 10000).ToString() + "ms")
        End If

        'Buffer will contain the following, in order:
        'BusyPin(2), BusyPinPolarity(1), Timeout(4), TriggerMode(1), TriggerPin(2), TriggerDrivePolarity(1), TriggerDriveTime(4) ---> Total of 15 bytes

        'Set the pin
        buf(0) = BusyPin.pinConfig And &HFF
        buf(1) = (BusyPin.pinConfig And &HFF00) >> 8

        'Set the polarity
        buf(2) = BusyPolarity

        'If the timeout is too large (greater than one timer period) set to 0 -> no timeout on firmware
        FX3Timeout = Timeout
        If Timeout > (UInt32.MaxValue / 10000) Then
            FX3Timeout = 0
        End If

        'Load timeout into the buffer
        buf(3) = FX3Timeout And &HFF
        buf(4) = (FX3Timeout And &HFF00) >> 8
        buf(5) = (FX3Timeout And &HFF0000) >> 16
        buf(6) = (FX3Timeout And &HFF000000) >> 24

        'Put mode in buffer (1 for trigger on SPI word, 10for trigger on pin drive)
        buf(7) = 0

        'Put trigger pin in buffer
        buf(8) = TriggerPin.pinConfig And &HFF
        buf(9) = (TriggerPin.pinConfig And &HFF00) >> 8

        'Put trigger drive polarity in buffer
        buf(10) = TriggerDrivePolarity

        'Put trigger drive time (in MS) in buffer
        buf(11) = TriggerDriveTime And &HFF
        buf(12) = (TriggerDriveTime And &HFF00) >> 8
        buf(13) = (TriggerDriveTime And &HFF0000) >> 16
        buf(14) = (TriggerDriveTime And &HFF000000) >> 24

        'Start stopwatch
        timeoutTimer.Start()

        'Send a vendor command to start a pulse wait operation (returns immediatly)
        ConfigureControlEndpoint(USBCommands.ADI_BUSY_MEASURE, True)
        If Not XferControlData(buf, 15, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer timed out")
        End If

        'Start bulk transfer
        transferStatus = False
        If Timeout = 0 Then
            transferStatus = DataInEndPt.XferData(buf, 16)
        Else
            While ((Not transferStatus) And (timeoutTimer.ElapsedMilliseconds() < Timeout))
                transferStatus = DataInEndPt.XferData(buf, 16)
            End While
        End If

        'stop stopwatch
        timeoutTimer.Stop()

        'If the transfer failed return the timeout value
        If Not transferStatus Then
            Return Convert.ToDouble(timeoutTimer.ElapsedMilliseconds())
        End If

        'Read status from the buffer and throw exception for bad status
        status = BitConverter.ToUInt32(buf, 0)
        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Failed to configure pin as input, error code: " + status.ToString("X4"))
        End If

        'Read current time
        currentTime = BitConverter.ToUInt32(buf, 4)

        'Read roll over counter
        rollOverCount = BitConverter.ToUInt32(buf, 8)
        rollOverCountULong = CULng(rollOverCount)

        'Read conversion factor
        conversionFactor = BitConverter.ToUInt32(buf, 12)

        'Calculate the total time, in timer ticks
        totalTicks = rollOverCountULong * UInt32.MaxValue
        totalTicks += currentTime

        'Scale the time waited to MS
        convertedTime = Convert.ToDouble(totalTicks)
        convertedTime = Math.Round(convertedTime / conversionFactor, 3)

        'Return the actual time waited
        Return convertedTime
    End Function

    Public Function MeasureBusyPulse(TriggerRegAddr As UShort, TriggerRegValue As UShort, BusyPin As IPinObject, BusyPolarity As UInteger, Timeout As UInteger) As Double
        'Declare variables needed for transfer
        Dim buf(15) As Byte
        Dim transferStatus As Boolean
        Dim timeoutTimer As New Stopwatch
        Dim convertedTime As Double
        Dim status, currentTime, rollOverCount, conversionFactor As UInteger
        Dim totalTicks, rollOverCountULong As ULong
        Dim FX3Timeout As UInteger

        'Validate that the pin isn't acting as a PWM pin
        If isPWMPin(BusyPin) Then
            Throw New FX3ConfigurationException("ERROR: The selected pin is currently configured to drive a PWM signal. Please call StopPWM(pin) before interfacing with the pin further")
        End If

        'Buffer will contain the following, in order:
        'BusyPin(2), BusyPinPolarity(1), Timeout(4), TriggerMode(1), SPI Trigger Addr(2), SPI Trigger Value(2), Zeros (3) ---> Total of 15 bytes

        'Set the pin
        buf(0) = BusyPin.pinConfig And &HFF
        buf(1) = (BusyPin.pinConfig And &HFF00) >> 8

        'Set the polarity
        If Not BusyPolarity = 0 Then
            BusyPolarity = 1
        End If
        buf(2) = BusyPolarity

        'If the timeout is too large (greater than one timer period) set to 0 -> no timeout on firmware
        FX3Timeout = Timeout
        If Timeout > (UInt32.MaxValue / 10000) Then
            FX3Timeout = 0
        End If

        'Load timeout into the buffer
        buf(3) = FX3Timeout And &HFF
        buf(4) = (FX3Timeout And &HFF00) >> 8
        buf(5) = (FX3Timeout And &HFF0000) >> 16
        buf(6) = (FX3Timeout And &HFF000000) >> 24

        'Put mode in buffer (1 for trigger on SPI word, 0 for trigger on pin drive)
        buf(7) = 1

        'Put SPI command word in buffer
        buf(8) = TriggerRegAddr And &HFF
        buf(9) = (TriggerRegAddr And &HFF00) >> 8

        buf(10) = TriggerRegValue And &HFF
        buf(11) = (TriggerRegValue And &HFF00) >> 8

        'Start stopwatch
        timeoutTimer.Start()

        'Send a vendor command to start a pulse wait operation (returns immediatly)
        ConfigureControlEndpoint(USBCommands.ADI_BUSY_MEASURE, True)
        If Not XferControlData(buf, 15, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer timed out")
        End If

        'Start bulk transfer
        transferStatus = False
        If Timeout = 0 Then
            transferStatus = DataInEndPt.XferData(buf, 16)
        Else
            While ((Not transferStatus) And (timeoutTimer.ElapsedMilliseconds() < Timeout))
                transferStatus = DataInEndPt.XferData(buf, 16)
            End While
        End If

        'stop stopwatch
        timeoutTimer.Stop()

        'If the transfer failed return the timeout value
        If Not transferStatus Then
            Return Convert.ToDouble(timeoutTimer.ElapsedMilliseconds())
        End If

        'Read status from the buffer and throw exception for bad status
        status = BitConverter.ToUInt32(buf, 0)
        If Not status = 0 Then
            Throw New FX3BadStatusException("ERROR: Failed to configure pin as input, error code: " + status.ToString("X4"))
        End If

        'Read current time
        currentTime = BitConverter.ToUInt32(buf, 4)

        'Read roll over counter
        rollOverCount = BitConverter.ToUInt32(buf, 8)
        rollOverCountULong = CULng(rollOverCount)

        'Read conversion factor
        conversionFactor = BitConverter.ToUInt32(buf, 12)

        'Calculate the total time, in timer ticks
        totalTicks = rollOverCountULong * UInt32.MaxValue
        totalTicks += currentTime

        'Scale the time waited to MS
        convertedTime = Convert.ToDouble(totalTicks)
        convertedTime = Math.Round(convertedTime / conversionFactor, 3)

        'Return the actual time waited
        Return convertedTime

    End Function

    ''' <summary>
    ''' This function configures the selected pin to drive a pulse width modulated output.
    ''' </summary>
    ''' <param name="Frequency">The desired PWM frequency, in Hz. Valid values are in the range of 0.1Hz (0.1) - 1MHz (1000000.0)</param>
    ''' <param name="DutyCycle">The PWM duty cycle. Valid values are in the range 0.0 - 1.0. To achieve a "clock" signal set the duty cycle to 0.5</param>
    ''' <param name="Pin">The pin to configure as a PWM signal.</param>
    Public Sub StartPWM(ByVal Frequency As Double, ByVal DutyCycle As Double, ByVal Pin As IPinObject)

        'Check that pin is an fx3pin
        If Not IsFX3Pin(Pin) Then
            Throw New FX3GeneralException("ERROR: All pin objects used with the FX3 Api must be of type FX3PinObject")
        End If

        'Check that the timer complex GPIO isnt being used
        Dim pinTimerBlock As Integer = Pin.pinConfig Mod 8
        For Each PWMPin In m_PwmPinList
            If Not (PWMPin.pinConfig = Pin.pinConfig) And (pinTimerBlock = PWMPin.pinConfig Mod 8) Then
                Throw New FX3ConfigurationException("ERROR: The PWM hardware for the pin selected is currently being used by pin number " + PWMPin.pinConfig.ToString())
            End If
        Next

        'Validate frequency
        If Frequency < 0.1 Or Frequency > 1000000 Then
            Throw New FX3ConfigurationException("ERROR: Invalid PWM frequency: " + Frequency.ToString() + "Hz")
        End If

        'Validate duty cycle
        If DutyCycle < 0 Or DutyCycle > 1 Then
            Throw New FX3ConfigurationException("ERROR: Invalid duty cycle: " + (100 * DutyCycle).ToString() + "%")
        End If

        'Validate that the complex GPIO 0 block is not being used. This block drives the timer subsystem and is unavailable for use as a PWM.
        If pinTimerBlock = 0 Then
            Throw New FX3ConfigurationException("ERROR: The selected " + Pin.ToString() + " pin cannot be used as a PWM")
        End If

        'Calculate the needed period and threshold value for the given setting
        Dim period, threshold As UInt32

        'The base clock is 10.08MHz (403.2MHz / 40)
        Dim baseClock As Double = 10080000

        period = Convert.ToUInt32(baseClock / Frequency) - 1
        threshold = Convert.ToUInt32((baseClock / Frequency) * DutyCycle)
        'Decrement threshold, but clamp at 0
        If Not threshold = 0 Then
            threshold = threshold - 1
        End If

        'If the threshold is 0 throw an exception, this particular setting is not achievable by the board (min 1)
        If threshold < 1 Then
            Throw New FX3ConfigurationException("ERROR: The selected PWM setting (Freq: " + Frequency.ToString() + "Hz, Duty Cycle: " + (DutyCycle * 100).ToString() + "%) is not achievable using a 10MHz clock")
        End If

        'Create transfer buffer
        Dim buf(9) As Byte

        'Place PWM settings in the buffer
        buf(0) = Pin.pinConfig And &HFF
        buf(1) = 0
        buf(2) = period And &HFF
        buf(3) = (period And &HFF00) >> 8
        buf(4) = (period And &HFF0000) >> 16
        buf(5) = (period And &HFF000000) >> 24
        buf(6) = threshold And &HFF
        buf(7) = (threshold And &HFF00) >> 8
        buf(8) = (threshold And &HFF0000) >> 16
        buf(9) = (threshold And &HFF000000) >> 24

        ConfigureControlEndpoint(USBCommands.ADI_PWM_CMD, True)
        FX3ControlEndPt.Index = 1

        'Transfer control data
        If Not XferControlData(buf, 10, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer timed out while setting up a PWM signal")
        End If

        'Add the selected pin to the list of active PWM pins
        m_PwmPinList.Add(Pin)

    End Sub

    ''' <summary>
    ''' This function call disables the PWM output from the FX3 and returns the pin to a tristated mode.
    ''' </summary>
    Public Sub StopPWM(ByVal Pin As IPinObject)

        'Exit if the pin isnt acting as a PWM
        If Not isPWMPin(Pin) Then
            Exit Sub
        End If

        'Create buffer
        Dim buf(1) As Byte

        'Place pin settings in the buffer
        buf(0) = Pin.pinConfig And &HFF
        buf(1) = 0

        'Configure control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_PWM_CMD, True)
        FX3ControlEndPt.Index = 0

        'Send stop command
        If Not XferControlData(buf, 2, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer timed out while stopping a PWM signal")
        End If

        'Remove pin from the PWM pin list
        For Each PWMPin In m_PwmPinList
            If PWMPin.pinConfig = Pin.pinConfig Then
                m_PwmPinList.Remove(PWMPin)
                Exit For
            End If
        Next

    End Sub

    ''' <summary>
    ''' This function checks to see if the selected pin has already been configured to act as a PWM output pin.
    ''' </summary>
    ''' <param name="Pin">The pin to check. Must be an FX3PinObject pin</param>
    ''' <returns>True if the pin is configured as a PWM pin, false otherwise</returns>
    Public Function isPWMPin(ByVal Pin As IPinObject) As Boolean

        'Check that its an FX3 pin
        If Not IsFX3Pin(Pin) Then
            Return False
        End If

        'Check that the pin is in PWM mode
        Dim pinFound As Boolean = False
        For Each activePWMPin In m_PwmPinList
            If Pin.pinConfig = activePWMPin.pinConfig Then
                pinFound = True
                Exit For
            End If
        Next
        Return pinFound
    End Function

    ''' <summary>
    ''' This function determines if the pin object being passed is an FX3 version of the IPinObject (as opposed to a blackfin pin for the SDP).
    ''' </summary>
    ''' <param name="Pin">The pin to check</param>
    ''' <returns>True if Pin is an FX3 pin, false if not</returns>
    Private Function IsFX3Pin(ByVal Pin As IPinObject) As Boolean
        Dim validPin As Boolean = False
        'Check the tostring overload and type
        If Pin.ToString().Substring(0, 3) = "FX3" And (Pin.GetType() = GetType(FX3PinObject)) Then
            validPin = True
        End If
        Return validPin

    End Function

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

    ''' <summary>
    ''' Readonly property to get the FX3_GPIO0 pin (mapped to GPIO 5 on FX3). This pin does not map to the standard iSensor breakout,
    ''' and should be used for other general purpose interfacing.
    ''' </summary>
    ''' <returns>Returns the GPIO pin, as an IPinObject</returns>
    Public ReadOnly Property FX3_GPIO1 As IPinObject
        Get
            Return New FX3PinObject(5)
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the FX3_GPIO1 pin (mapped to GPIO 6 on FX3). This pin does not map to the standard iSensor breakout,
    ''' and should be used for other general purpose interfacing.
    ''' </summary>
    ''' <returns>Returns the GPIO pin, as an IPinObject</returns>
    Public ReadOnly Property FX3_GPIO2 As IPinObject
        Get
            Return New FX3PinObject(6)
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the FX3_GPIO2 pin (mapped to GPIO 7 on FX3). This pin does not map to the standard iSensor breakout,
    ''' and should be used for other general purpose interfacing.
    ''' </summary>
    ''' <returns>Returns the GPIO pin, as an IPinObject</returns>
    Public ReadOnly Property FX3_GPIO3 As IPinObject
        Get
            Return New FX3PinObject(7)
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the FX3_GPIO3 pin (mapped to GPIO 12 on FX3). This pin does not map to the standard iSensor breakout,
    ''' and should be used for other general purpose interfacing. This pin shares a complex GPIO block with DIO1. If DIO1 is being used
    ''' as a clock source, via the StartPWM function, then this pin cannot be used as a clock source.
    ''' </summary>
    ''' <returns>Returns the GPIO pin, as an IPinObject</returns>
    Public ReadOnly Property FX3_GPIO4 As IPinObject
        Get
            Return New FX3PinObject(12)
        End Get
    End Property


#End Region

End Class