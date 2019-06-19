'Author:       Alex Nolan
'Date:         7/31/2018     
'Description:  Collection of helper data structures used in the FX3Connection 

#Region "Helper Data Structures"

''' <summary>
''' This enum lists all supported vendor commands for the application firmware.
''' </summary>
Public Enum USBCommands

    'Return FX3 firmware ID
    ADI_FIRMWARE_ID_CHECK = &HB0

    'Hard-reset the FX3 firmware (return to bootloader mode)
    ADI_HARD_RESET = &HB1

    'Set FX3 SPI configuration
    ADI_SET_SPI_CONFIG = &HB2

    'Return FX3 SPI configuration
    ADI_READ_SPI_CONFIG = &HB3

    'Return the current status of the FX3 firmware
    ADI_GET_STATUS = &HB4

    'Return the FX3 unique serial number
    ADI_SERIAL_NUMBER_CHECK = &HB5

    ' Soft-reset the FX3 firmware (don't return to bootloader mode)
    ADI_WARM_RESET = &HB6

    'Start/stop a generic data stream
    ADI_STREAM_GENERIC_DATA = &HC0

    'Start/stop a burst data stream
    ADI_STREAM_BURST_DATA = &HC1

    'Read the value of a user-specified GPIO
    ADI_READ_PIN = &HC3

    'Read the current FX3 timer register value
    ADI_READ_TIMER_VALUE = &HC4

    'Drive a user-specified GPIO for a user-specified time
    ADI_PULSE_DRIVE = &HC5

    'Wait for a user-specified pin to reach a user-specified level (with timeout)
    ADI_PULSE_WAIT = &HC6

    'Drive a user-specified GPIO
    ADI_SET_PIN = &HC7

    'Return the pulse frequency (data ready on a user-specified pin
    ADI_MEASURE_DR = &HC8

    'Start/stop a real-time stream
    ADI_STREAM_REALTIME = &HD0

    'Do nothing (default case
    ADI_NULL_COMMAND = &HD1

    'Read a word at a specified address and return the data over the control endpoint
    ADI_READ_BYTES = &HF0

    'Write one byte of data to a user-specified address
    ADI_WRITE_BYTE = &HF1

    'Return data over a bulk endpoint before a bulk read/write operation
    ADI_BULK_REGISTER_TRANSFER = &HF2

    'The following commands are for the ADI bootloader only

    ' Turn on APP_LED_GPIO solid 
    ADI_LED_ON = &HEC

    ' Turn off APP_LED_GPIO 
    ADI_LED_OFF = &HED

    ' Turn off APP_LED_GPIO blinking 
    ADI_LED_BLINKING_OFF = &HEE

    ' Turn on APP_LED_GPIO blinking 
    ADI_LED_BLINKING_ON = &HEF

End Enum

''' <summary>
''' Class for all the programmable SPI configuration options on the FX3 when using ADcmXL devices
''' </summary>
Public Class SPIConfig

    'Public Variables
    Public ClockFrequency As Int32
    Public WordLength As Byte
    Public Cpol As Boolean
    Public ChipSelectPolarity As Boolean
    Public Cpha As Boolean
    Public IsLSBFirst As Boolean
    Public ChipSelectControl As SpiChipselectControl
    Public ChipSelectLeadTime As SpiLagLeadTime
    Public ChipSelectLagTime As SpiLagLeadTime
    Public DUTType As DUTType
    Public DrActive As Boolean
    Public DrPolarity As Boolean
    Public TimerTickScaleFactor As UInt32

    'Private variables for general use
    Private SclkPeriod As Double
    Private StallSeconds As Double

    ''' <summary>
    ''' Property to store the current SPI clock. Updates the StallTime when set.
    ''' </summary>
    ''' <returns>The current SPI clock frequency</returns>
    Public Property SCLKFrequency As Int32
        Get
            Return ClockFrequency
        End Get
        Set(value As Int32)
            ClockFrequency = value
            SclkPeriod = (1 / ClockFrequency)
            'Update the member variable to avoid changing the StallCycles
            m_StallTime = Convert.ToInt16(m_StallCycles * SclkPeriod * 1000000)
        End Set
    End Property

    ''' <summary>
    ''' Property to get/set the stall time (in microseconds)
    ''' </summary>
    ''' <returns>The current stall time setting, in microseconds</returns>
    Public Property StallTime As UInt16
        Get
            Return m_StallTime
        End Get
        Set(value As UInt16)
            m_StallTime = value
            'Calculate the new stall cycles value based on spi clock and update private variable
            SclkPeriod = 1 / ClockFrequency
            StallSeconds = m_StallTime / 1000000
            m_StallCycles = Convert.ToInt16(StallSeconds / SclkPeriod)
        End Set
    End Property
    'Private member variable to store the stall time
    Private m_StallTime As UInt16 = 50

    ''' <summary>
    ''' Property to set the stall time, in terms of SPI clock cycles
    ''' </summary>
    ''' <returns>The current stall cycles</returns>
    Public Property StallCycles As UInt16
        Get
            Return m_StallCycles
        End Get
        Set(value As UInt16)
            m_StallCycles = value
            'Set the stall time
            SclkPeriod = 1 / ClockFrequency
            StallSeconds = SclkPeriod * m_StallCycles
            m_StallTime = Convert.ToInt16(StallSeconds * 1000000)
        End Set
    End Property
    'Private member variable to store the stall cycles
    Private m_StallCycles As UInt16 = 100

    ''' <summary>
    ''' Property to get/set the data ready pin
    ''' </summary>
    ''' <returns>The ready pin, as an FX3PinObject</returns>
    Public Property DataReadyPin As FX3PinObject
        Get
            Return m_ReadyPin
        End Get
        Set(value As FX3PinObject)
            m_ReadyPin = value
            m_ReadyPinGPIO = m_ReadyPin.PinNumber
        End Set
    End Property
    'Private member variable to store the current data ready pin
    Private m_ReadyPin As FX3PinObject

    ''' <summary>
    ''' Property to get/set the data ready FX3 GPIO number
    ''' </summary>
    ''' <returns>The GPIO number, as a UINT16</returns>
    Public Property DataReadyPinFX3GPIO As UInt16
        Get
            Return m_ReadyPinGPIO
        End Get
        Set(value As UInt16)
            m_ReadyPinGPIO = value
            m_ReadyPin = New FX3PinObject(m_ReadyPinGPIO)
        End Set
    End Property
    'Private member variable to store the current data ready pin GPIO number
    Private m_ReadyPinGPIO As UInt16

    ''' <summary>
    ''' Class Constructor, sets reasonable default values for IMU and ADcmXL devices
    ''' </summary>
    ''' <param name="SensorType">Optional parameter to specify default device SPI settings. Valid options are IMU and ADcmXL</param>
    Public Sub New(Optional ByVal SensorType As DeviceType = DeviceType.IMU)
        If SensorType = DeviceType.ADcmXL Then
            ClockFrequency = 14000000
            WordLength = 8
            Cpol = True
            Cpha = True
            ChipSelectPolarity = False
            ChipSelectControl = SpiChipselectControl.SPI_SSN_CTRL_HW_END_OF_XFER
            ChipSelectLagTime = SpiLagLeadTime.SPI_SSN_LAG_LEAD_ONE_CLK
            ChipSelectLeadTime = SpiLagLeadTime.SPI_SSN_LAG_LEAD_ONE_CLK
            StallTime = 25
            StallCycles = 350
            DUTType = DUTType.ADcmXL3021
            IsLSBFirst = False
            DrActive = False
            DrPolarity = True
            DataReadyPinFX3GPIO = 3
            TimerTickScaleFactor = 1000
        Else
            ClockFrequency = 2000000
            WordLength = 8
            Cpol = True
            Cpha = True
            ChipSelectPolarity = False
            ChipSelectControl = SpiChipselectControl.SPI_SSN_CTRL_HW_END_OF_XFER
            ChipSelectLagTime = SpiLagLeadTime.SPI_SSN_LAG_LEAD_ONE_CLK
            ChipSelectLeadTime = SpiLagLeadTime.SPI_SSN_LAG_LEAD_ONE_CLK
            StallTime = 25
            StallCycles = 50
            DUTType = DUTType.IMU
            IsLSBFirst = False
            DrActive = True
            DrPolarity = True
            DataReadyPinFX3GPIO = 4
            TimerTickScaleFactor = 1000
        End If

    End Sub

End Class

''' <summary>
''' Enum for the possible chip select modes
''' </summary>
Public Enum SpiChipselectControl
    'SSN Is controlled by API And Is Not at clock boundaries. 
    SPI_SSN_CTRL_FW = 0
    'SSN Is controlled by hardware And Is done In sync With clock.
    'The SSN Is asserted at the beginning Of a transfer, And de-asserted
    'at the End Of a transfer Or When no data Is available To transmit. 
    SPI_SSN_CTRL_HW_END_OF_XFER
    'SSN Is controlled by the hardware And Is done In sync With clock.
    'The SSN Is asserted at the beginning Of transfer Of every word, And
    'de-asserted at the End Of the transfer Of that word. 
    SPI_SSN_CTRL_HW_EACH_WORD
    'If CPHA Is 0, the SSN control Is per word; And If CPHA Is 1, Then the
    'SSN control Is per transfer. 
    SPI_SSN_CTRL_HW_CPHA_BASED
    'SSN control Is done externally by the firmware application.
    SPI_SSN_CTRL_NONE
End Enum

''' <summary>
''' Enum for determining the default device settings to be initialized
''' </summary>
Public Enum DeviceType
    'IMU device
    IMU = 0
    'ADcmXL device
    ADcmXL
End Enum

''' <summary>
''' Enum for the possible chip select lag/lead times, in SPI clock cycles
''' </summary>
Public Enum SpiLagLeadTime
    SPI_SSN_LAG_LEAD_ZERO_CLK = 0       ' SSN Is in sync with SCK.
    SPI_SSN_LAG_LEAD_HALF_CLK           ' SSN leads / lags SCK by a half clock cycle.
    SPI_SSN_LAG_LEAD_ONE_CLK            ' SSN leads / lags SCK by one clock cycle.
    SPI_SSN_LAG_LEAD_ONE_HALF_CLK       ' SSN leads / lags SCK by one And half clock cycles.
End Enum

''' <summary>
''' Enum of the possible DUT types for the ADcmXLx021
''' </summary>
Public Enum DUTType
    ADcmXL1021 = 0
    ADcmXL2021
    ADcmXL3021
    IMU
End Enum

#End Region
