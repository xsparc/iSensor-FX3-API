'File: FX3Programming.vb
'Author: Alex Nolan
'Date: 6/10/2019
'This file is an extension of the primary FX3 Connection class. It contains all the functions
'related to board enumeration, connection, and programming.

Imports CyUSB
Imports System.IO
Imports System.Threading

Partial Class FX3Connection

#Region "Connection Functions"

    ''' <summary>
    ''' Attempts to program the selected FX3 board with the application firmware.
    ''' </summary>
    ''' <param name="deviceSn">Serial number of the device being connected to.</param>
    Public Sub Connect(ByVal deviceSn As String)

        Dim tempHandle As CyUSBDevice = Nothing
        Dim timeoutTimer As New Stopwatch

        'Exit sub if we're already connected to a device
        If m_FX3Connected = True Then
            Exit Sub
        End If

        'Find the device handle using the selected serial number
        For Each item As CyFX3Device In m_usbList
            'Look for the selected serial number and get its handle
            If String.Equals(item.SerialNumber, deviceSn) Then
                tempHandle = item
            End If
        Next

        'Exit if we can't find the correct board
        If tempHandle Is Nothing Then
            'Set default values for the interface
            SetDefaultValues(m_sensorType)
            Throw New Exception("ERROR: Could not find the board selected to connect to. Was it removed?")
        End If

        'Program the FX3 board with the application firmware
        'Check the active FX3 firmware and compare against the requested serial number
        If String.Equals(tempHandle.SerialNumber, deviceSn) Then
            'If the board is already programmed and in streamer mode, then don't re-program
            If String.Equals(tempHandle.FriendlyName, ApplicationName) Then
                'Set flag indicating that the FX3 successfully connected
                m_FX3Connected = True
            Else
                ProgramAppFirmware(tempHandle)
            End If
        End If

        RefreshDeviceList()

        'Update the handle for the newly programmed board
        For Each item As CyUSBDevice In m_usbList
            'Look for the selected serial number, get its handle, and set it as the active device
            If String.Equals(item.SerialNumber, deviceSn) Then
                m_ActiveFX3 = CType(item, CyFX3Device)
                m_ActiveFX3SN = deviceSn
                'Set flag indicating that the FX3 successfully connected
                m_FX3Connected = True
                'exit the loop early
                Exit For
            End If
        Next

        'Exit if we can't find the correct board
        If m_ActiveFX3SN Is Nothing Then
            'Set default values for the interface
            SetDefaultValues(m_sensorType)
            Throw New Exception("ERROR: Could not find the board selected to connect to. Was it removed?")
        End If

        'Check that we're talking to the target board and it's running the application firmware
        If Not FX3CodeRunningOnTarget() Then
            m_FX3Connected = False
            Throw New Exception("ERROR: FX3 Board not successfully connected")
        End If

        'Check that the connection speed is adequate
        CheckConnectionSpeedOnTarget()

        'Set up endpoints
        EnumerateEndpointsOnTarget()

        'Check that endpoints are properly enumerated
        If Not CheckEndpointStatus() Then
            m_status = "ERROR: Unable to enumerate endpoints"
            'Set default values for the interface
            SetDefaultValues(m_sensorType)
            Throw New Exception("ERROR: Unable to configure endpoints")
        End If

        'Make sure that the board SPI parameters match current setting
        WriteBoardSpiParameters()

    End Sub

    ''' <summary>
    ''' This function sends a reset command to the specified FX3 board, or does nothing if no board is connected
    ''' </summary>
    Public Sub Disconnect()

        'Exit sub if we're not connected to an FX3 board
        If m_FX3Connected = False Then
            Exit Sub
        End If

        'Clear the connected state to treat the FX3 as a new board once it reboots
        m_FX3Connected = False
        'Reset the FX3 currently in use
        ResetFX3Firmware(m_ActiveFX3)
        'Small delay to let Windows catch up
        Thread.Sleep(1000)
        'Set default values for the interface
        SetDefaultValues(m_sensorType)
        'Force a refresh of the board list
        RefreshDeviceList()

    End Sub

    ''' <summary>
    ''' Property which returns the active FX3 board.
    ''' </summary>
    ''' <returns>Returns active FX3 device ID if enumeration has been completed. Returns nothing otherwise.</returns>
    Public ReadOnly Property ActiveFX3 As CyFX3Device
        Get
            Return m_ActiveFX3
        End Get
    End Property

    ''' <summary>
    ''' Property which returns the serial number of the active FX3 board. 
    ''' </summary>
    ''' <returns>Returns the serial number of the active FX3 device.</returns>
    Public Property ActiveFX3SerialNumber As String
        Get
            Return m_ActiveFX3SN
        End Get
        Set(value As String)
            m_ActiveFX3SN = value
        End Set
    End Property

    ''' <summary>
    ''' Property which returns all FX3 boards detected on the system.
    ''' </summary>
    ''' <returns>All detected FX3 boards.</returns>
    Public ReadOnly Property DetectedFX3s As USBDeviceList
        Get
            RefreshDeviceList()
            Return m_usbList
        End Get
    End Property

    ''' <summary>
    ''' Property which reads the firmware version from the FX3
    ''' </summary>
    ''' <returns>The firmware version, as a string</returns>
    Public ReadOnly Property GetVersion As String
        Get
            Return GetFirmwareID()
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the serial number of the active FX3 board
    ''' </summary>
    ''' <returns>THe current serial number, as a string</returns>
    Public ReadOnly Property GetTargetSerialNumber As String
        Get
            Return GetSerialNumber()
        End Get
    End Property

#End Region

#Region "FX3 Board Management"

    ''' <summary>
    ''' Initializes the interrupt handlers for connecting/disconnecting boards and forces an FX3 list refresh
    ''' </summary>
    Private Sub InitBoardList()

        'Set up device list
        Dim m_usbList = New USBDeviceList(CyConst.DEVICES_CYUSB)

        'Register all event handlers
        AddHandler m_usbList.DeviceRemoved, New EventHandler(AddressOf usbDevices_DeviceRemoved)
        AddHandler m_usbList.DeviceAttached, New EventHandler(AddressOf usbDevices_DeviceAttached)

        'Refresh device list to program any boards in cypress bootloader mode
        RefreshDeviceList()

    End Sub

    ''' <summary>
    ''' Handles connect events generated by the Cypress USB library
    ''' </summary>
    ''' <param name="sender"></param>
    ''' <param name="e"></param>
    Private Sub usbDevices_DeviceAttached(ByVal sender As Object, ByVal e As EventArgs)

        'Parse the event data
        Dim usbEvent As USBEventArgs = TryCast(e, USBEventArgs)

        'Update the FX3Interface device list, programming new boards as needed
        RefreshDeviceList()

    End Sub

    ''' <summary>
    ''' Handles disconnect events generated by the cypress USB library
    ''' </summary>
    ''' <param name="sender"></param>
    ''' <param name="e"></param>
    Private Sub usbDevices_DeviceRemoved(ByVal sender As Object, ByVal e As EventArgs)

        'Parse event data and handle
        Dim usbEvent As USBEventArgs = TryCast(e, USBEventArgs)
        CheckDisconnectEvent(usbEvent)

    End Sub

    ''' <summary>
    ''' This function checks the event arguments when a USB disconnect occurs. If the FX3 which was
    ''' disconnected is marked as the active device, this function attepmpts to gracefully update the 
    ''' interface state to prevent application lockup from accessing a disconnected board.
    ''' </summary>
    ''' <param name="usbEvent">The event to handle</param>
    Private Sub CheckDisconnectEvent(ByVal usbEvent As USBEventArgs)

        If IsNothing(m_ActiveFX3) Then
            'If the active board is set to nothing then this was an "expected" disconnect event
            Exit Sub
        End If

        'Determine if disconnect event observed is for the active board
        If usbEvent.FriendlyName = ApplicationName And usbEvent.SerialNum = m_ActiveFX3SN Then
            'This is an unexpected disconnect of the active board

            'Set default values for the interface
            SetDefaultValues(m_sensorType)

            'Set status message
            m_status = "ERROR: Unexpected FX3 Disconnect"

            'Update the FX3Interface device list
            RefreshDeviceList()

            'Raise event so programs up the stack can handle
            RaiseEvent UnexpectedDisconnect(usbEvent.SerialNum)
        End If

    End Sub

    ''' <summary>
    ''' Refreshes the list of FX3 boards connected to the PC and indicates to bootloader programmer thread if any need to be programmed
    ''' </summary>
    Private Sub RefreshDeviceList()
        m_usbList = New USBDeviceList(CyConst.DEVICES_CYUSB)
        For Each item As USBDevice In m_usbList
            'Program any device that enumerates as a stock FX3
            If String.Equals(item.FriendlyName, CypressBootloaderName) Then
                BootloaderQueue.Add(item)
            End If
        Next
    End Sub

    ''' <summary>
    ''' This function runs in a seperate thread from the main application. When a new, unprogrammed board
    ''' is connected to the system, the device identifier is placed in a queue, indicating to this thread
    ''' that a new board needs to be programmed with the ADI bootloader.
    ''' </summary>
    Private Sub ProgramBootloader_Thread()

        'Message from the queue
        Dim selectedBoard As CyFX3Device

        While True
            'This function blocks until a new board is available to be programmed
            selectedBoard = BootloaderQueue.Take()
            'Program the indicated board (in cypress bootloader mode)
            Try
                ProgramBootloader(selectedBoard)
            Catch ex As Exception
                If ex.Message = "ERROR: Selected FX3 is not in bootloader mode. Please reset the FX3." Then
                    'Don't need to do anything, this is to catch exceptions caused by a concurrent program of the FX3 by multiple instances of the FX3Connection
                End If
            End Try

        End While

    End Sub

    ''' <summary>
    ''' This function programs the bootloader of a single board
    ''' </summary>
    ''' <param name="selectedBoard">The handle for the FX3 board to be programmed with the ADI bootloader firmware</param>
    Private Sub ProgramBootloader(ByVal selectedBoard As CyFX3Device)

        'Programming status
        Dim flashStatus As FX3_FWDWNLOAD_ERROR_CODE = FX3_FWDWNLOAD_ERROR_CODE.SUCCESS

        'Check that the cypress bootloader is currently running
        If Not selectedBoard.IsBootLoaderRunning Then
            Throw New Exception("ERROR: Selected FX3 is not in bootloader mode. Please reset the FX3.")
        End If

        'Attempt to program the board
        flashStatus = selectedBoard.DownloadFw(BlinkFirmwarePath, FX3_FWDWNLOAD_MEDIA_TYPE.RAM)

        If flashStatus = FX3_FWDWNLOAD_ERROR_CODE.FAILED Then
            Throw New Exception("ERROR: Bootloader download failed.")
        End If

    End Sub

    ''' <summary>
    ''' This function programs a single board running the ADI bootloader with the ADI application firmware.
    ''' </summary>
    ''' <param name="selectedBoard">The handle for the board to be programmed with the ADI application firmware</param>
    ''' <returns></returns>
    Private Function ProgramAppFirmware(ByVal selectedBoard As CyFX3Device) As Boolean

        'If the board programmed successfully
        Dim boardProgrammed As Boolean = False
        'Timer for checking if board programmed successfully
        Dim timeoutTimer As New Stopwatch
        'Status code from the cypress driver
        Dim flashStatus As FX3_FWDWNLOAD_ERROR_CODE = FX3_FWDWNLOAD_ERROR_CODE.SUCCESS
        'Board serial number
        Dim serialNumber As String = selectedBoard.SerialNumber

        'Attempt to program the board
        flashStatus = selectedBoard.DownloadFw(FirmwarePath, FX3_FWDWNLOAD_MEDIA_TYPE.RAM)

        'If the cypress driver level programming fails return false
        If flashStatus = FX3_FWDWNLOAD_ERROR_CODE.FAILED Then
            Return False
        End If

        'Check that the board appropriately re-enumerates
        timeoutTimer.Start()
        While ((timeoutTimer.ElapsedMilliseconds < ProgrammingTimeout) And boardProgrammed = False)
            Thread.Sleep(DeviceListDelay)
            RefreshDeviceList()
            For Each item As CyUSBDevice In m_usbList
                'Look for the device we just programmed running the ADI Application firmware
                If String.Equals(item.FriendlyName, ApplicationName) And String.Equals(item.SerialNumber, serialNumber) Then
                    boardProgrammed = True
                End If
            Next
        End While
        timeoutTimer.Stop()

        Return boardProgrammed

    End Function

    ''' <summary>
    ''' Function which checks if the FX3 is connected and programmed
    ''' </summary>
    ''' <returns>A boolean indicating if the board is programmed</returns>
    Public Function FX3CodeRunningOnTarget() As Boolean

        'Return false if the board hasn't been connected yet
        If Not m_FX3Connected Then
            Return False
        End If

        'Make sure the selected board identifies as a "streamer" device
        If Not String.Equals(m_ActiveFX3.FriendlyName, ApplicationName) Then
            Throw New Exception("ERROR: The target board is not running the application firmware")
            Return False
        End If

        'Make sure the selected board is reporting back the correct serial (using the control endpoint, not the USB descriptor)
        If Not String.Equals(m_ActiveFX3SN, GetSerialNumber()) Then
            Throw New Exception("ERROR: The target board reported a different serial number. You're probably talking to the wrong board!")
            Return False
        End If

        'Get the firmware ID from the board and check whether it contains "FX3"
        If GetFirmwareID().IndexOf("FX3") = -1 Then
            Throw New Exception("ERROR: Board not responding to requests")
            Return False
        End If

        'If we make it past all the checks, return true
        Return True

    End Function

    ''' <summary>
    ''' The path to the firmware .img file. Needs to be set before the FX3 can be programmed
    ''' </summary>
    ''' <returns>A string, represeting the path</returns>
    Public Property FirmwarePath As String
        Get
            Return m_FirmwarePath
        End Get
        Set(value As String)
            'Setter checks that the path is valid before setting
            If isFirmwarePathValid(value) Then
                m_FirmwarePath = value
            End If
        End Set
    End Property

    ''' <summary>
    ''' Set/get the blink firmware .img file used for multi-board identification
    ''' </summary>
    ''' <returns>A string representing the path to the firmware on the user machine</returns>
    Public Property BlinkFirmwarePath As String
        Get
            Return m_BlinkFirmwarePath
        End Get
        Set(value As String)
            'Setter checks that the path is valid before setting
            If isFirmwarePathValid(value) Then
                m_BlinkFirmwarePath = value
            End If
        End Set
    End Property

    ''' <summary>
    ''' Checks the boot status of the FX3 board by sending a vendor request
    ''' </summary>
    ''' <returns>The current connection status</returns>
    Public ReadOnly Property GetBootStatus As String
        Get
            'Check if the board is running
            FX3CodeRunningOnTarget()
            'Return the status
            Return m_status
        End Get
    End Property

    ''' <summary>
    ''' Checks if there is a Cypress FX3 USB device connected to the system
    ''' </summary>
    ''' <returns>A boolean indicating if there is an FX3 attached</returns>
    Public ReadOnly Property FX3BoardAttached As Boolean
        Get
            RefreshDeviceList()
            'Return false if none
            If m_usbList.Count = 0 Then
                Return False
            End If
            'Check if one is an FX3
            For Each item In m_usbList
                'Return true if we found an FX3
                If CType(item, CyFX3Device).VendorID = &H4B4 Then
                    Return True
                End If
            Next
            'Return false if no FX3 found
            Return False
        End Get
    End Property

    ''' <summary>
    ''' Send a reset command to the FX3 firmware. This command works for either the application or bootloader firmware.
    ''' </summary>
    ''' <param name="boardHandle">Handle of the board to be reset.</param>
    Private Sub ResetFX3Firmware(ByVal boardHandle As CyFX3Device)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte

        'Set board handle
        FX3ControlEndPt = boardHandle.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = &HB1
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_ENDPT
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE
        FX3ControlEndPt.XferData(buf, 4)

    End Sub

    ''' <summary>
    ''' Looks for and resets boards in application mode. Should only be called at program start, after InitBoardList()
    ''' Note: Should not be used if running multiple instances of the GUI.
    ''' </summary>
    ''' <returns>The number of boards running the application firmware which were reset</returns>
    Private Function ResetAllFX3s() As Integer

        'track number of boards reset
        Dim numBoardsReset As Integer = 0

        'Refresh the connected board list
        RefreshDeviceList()

        'Loop through current device list and reporgram all boards running the ADI Application firmware
        For Each item As CyFX3Device In m_usbList
            If String.Equals(item.FriendlyName, ApplicationName) Then
                ResetFX3Firmware(item)
                numBoardsReset = numBoardsReset + 1
            End If
        Next

        Return numBoardsReset
    End Function

    ''' <summary>
    ''' Wait for a newly-programmed FX3 to enumerate as a streamer (application) device
    ''' </summary>
    ''' <param name="timeout">Optional timeout to wait for a board to re-enumerate.</param>
    ''' <param name="sn">Serial number of device we're waiting for.</param>
    Private Sub WaitForStreamer(ByVal sn As String, Optional ByVal timeout As Integer = 3000)
        Dim timer As New Stopwatch
        Dim tempList As New USBDeviceList(CyConst.DEVICES_CYUSB)
        Dim streamerDetected As Boolean = False

        timer.Start()
        While (timer.ElapsedMilliseconds < timeout And Not streamerDetected)
            tempList = New USBDeviceList(CyConst.DEVICES_CYUSB)
            'Look for the device with a stock serial number
            For Each item In tempList
                'Look for the selected serial number, get its handle, and set it as the active device
                If String.Equals(CType(item, CyFX3Device).FriendlyName, ApplicationName) Then
                    If String.Equals(CType(item, CyFX3Device).SerialNumber, sn) Then
                        m_ActiveFX3 = CType(item, CyFX3Device)
                        m_ActiveFX3SN = sn
                        streamerDetected = True
                    Else
                        Thread.Sleep(DeviceListDelay)
                    End If
                Else
                    Thread.Sleep(DeviceListDelay)
                End If
            Next

        End While

        If Not streamerDetected Then
            Throw New Exception("ERROR: Could not find the FX3 board after programming the application firmware")
        End If

        timer.Reset()

    End Sub

    ''' <summary>
    ''' Checks to see if a provided firmware path is valid. Throws exception if it is not.
    ''' </summary>
    ''' <param name="path">The firmware path to check</param>
    ''' <returns>A boolean indicating if the firmware path is valid</returns>
    Private Function isFirmwarePathValid(ByRef path As String) As Boolean
        Dim validPath As Boolean = True
        Try
            'Check file path length
            If Not path.Length > 4 Then
                Throw New Exception("ERROR: Firmware path too short")
            End If
            'Check that it is a .img file
            If Not path.Substring(path.Length - 4, 4) = ".img" Then
                Throw New Exception("ERROR: Firmware must be a .img file")
            End If
            'Check that the file exists
            If Not File.Exists(path) Then
                Throw New Exception("ERROR: Firmware file does not exist")
            End If
        Catch ex As Exception
            validPath = False
            'Pass the exception up
        End Try

        Return validPath

    End Function

    ''' <summary>
    ''' Performs a data transfer on the control endpoint with a check to see if the transaction times out
    ''' </summary>
    ''' <param name="buf">The buffer to transfer</param>
    ''' <param name="numBytes">The number of bytes to transfer</param>
    ''' <param name="timeout">The timeout time (in milliseconds)</param>
    ''' <returns>Returns a boolean indicating if the transfer timed out or not</returns>
    Private Function XferControlData(ByRef buf As Byte(), ByVal numBytes As Integer, ByVal timeout As Integer) As Boolean

        Dim startTime As New Stopwatch
        Dim validTransfer As Boolean = True

        'Point the API to the target FX3
        FX3ControlEndPt = m_ActiveFX3.ControlEndPt

        'Block control endpoint transfers while streaming (except cancel)
        If StreamThreadRunning And Not (FX3ControlEndPt.ReqCode = &HD0) Then
            Return False
        End If

        'Perform transfer
        startTime.Start()
        validTransfer = FX3ControlEndPt.XferData(buf, numBytes)
        startTime.Stop()

        'Check transfer status
        If Not validTransfer Then
            Return False
        End If

        'Check and see if timeout expired
        If startTime.ElapsedMilliseconds() > timeout Then
            Return False
        Else
            Return True
        End If

    End Function

    ''' <summary>
    ''' Validates that the control endpoint is enumerated and configures it with some default values
    ''' </summary>
    ''' <param name="Reqcode">The vendor command reqcode to provide</param>
    ''' <param name="toDevice">Whether the transaction is DIR_TO_DEVICE (true) or DIR_FROM_DEVICE(false)</param>
    ''' <returns>A boolean indicating the success of the operation</returns>
    Private Function ConfigureControlEndpoint(ByVal Reqcode As UInt16, ByVal toDevice As Boolean) As Boolean

        'Point the API to the target FX3
        FX3ControlEndPt = m_ActiveFX3.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = Reqcode
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_DEVICE
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        If toDevice Then
            FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE
        Else
            FX3ControlEndPt.Direction = CyConst.DIR_FROM_DEVICE
        End If
        Return True

    End Function

    ''' <summary>
    ''' Gets the current firmware ID from the FX3
    ''' </summary>
    ''' <returns>Returns the firmware ID, as a string</returns>
    Private Function GetFirmwareID() As String
        Dim firmwareID As String
        Dim buf(31) As Byte
        ConfigureControlEndpoint(&HB0, False)
        XferControlData(buf, 32, 2000)
        firmwareID = System.Text.Encoding.UTF8.GetString(buf)
        Return firmwareID
    End Function

    ''' <summary>
    ''' Gets the serial number of the target FX3 using the control endpoint
    ''' </summary>
    ''' <returns>The unique FX3 serial number</returns>
    Private Function GetSerialNumber() As String
        Dim serialNumber As String
        Dim buf(31) As Byte
        ConfigureControlEndpoint(&HB5, False)
        XferControlData(buf, 32, 2000)
        serialNumber = System.Text.Encoding.Unicode.GetString(buf)
        Return serialNumber
    End Function

    ''' <summary>
    ''' Checks that all the endpoints are properly enumerated
    ''' </summary>
    ''' <returns>A boolean indicating if the endpoints are properly enumerated</returns>
    Private Function CheckEndpointStatus() As Boolean
        Dim returnValue As Boolean = True

        'Check if control endpoint is set
        If FX3ControlEndPt Is Nothing Then
            returnValue = False
            Throw New Exception("ERROR: Control Endpoint not configured")
        End If

        'Check if streaming endpoint is set
        If StreamingEndPt Is Nothing Then
            returnValue = False
            Throw New Exception("ERROR: Streaming Endpoint not configured")
        End If

        'Check if bulk data in endpoint is set
        If DataInEndPt Is Nothing Then
            returnValue = False
            Throw New Exception("ERROR: Data In Endpoint not configured")
        End If

        'Check if bulk data out endpoint is set
        If DataOutEndPt Is Nothing Then
            returnValue = False
            Throw New Exception("ERROR: Data Out Endpoint not configured")
        End If

        Return returnValue
    End Function

    ''' <summary>
    ''' Resets all the currently configured endpoints on the FX3.
    ''' </summary>
    Private Sub ResetEndpoints()

        For Each endpoint In m_ActiveFX3.EndPoints
            endpoint.Reset()
        Next

    End Sub

    ''' <summary>
    ''' Enumerates all the FX3 endpoints used
    ''' </summary>
    Private Sub EnumerateEndpointsOnTarget()

        'Enumerate the bulk endpoints
        For Each endpoint In m_ActiveFX3.EndPoints
            If endpoint.Address = 1 Then
                DataOutEndPt = endpoint
            ElseIf endpoint.Address = 129 Then
                StreamingEndPt = endpoint
            ElseIf endpoint.Address = 130 Then
                DataInEndPt = endpoint
            End If
        Next

        'Enumerate the control endpoint
        FX3ControlEndPt = m_ActiveFX3.ControlEndPt

    End Sub

    ''' <summary>
    ''' Checks that the board is enumerated and connected via USB 2.0 or 3.0
    ''' </summary>
    Private Sub CheckConnectionSpeedOnTarget()

        If IsNothing(m_ActiveFX3) Then
            'Clear the active FX3 device handle
            m_ActiveFX3 = Nothing
            Throw New Exception("ERROR: FX3 Board not enumerated")
        End If

        If Not (m_ActiveFX3.bHighSpeed Or m_ActiveFX3.bSuperSpeed) Then
            'Clear the active FX3 device handle
            m_ActiveFX3 = Nothing
            Throw New Exception("ERROR: FX3 must be connected with USB 2.0 or better")
        End If

    End Sub

#End Region

#Region "FX3 Bootloader Functions"

    ''' <summary>
    ''' BOOTLOADER FW: Blink the onboard LED
    ''' </summary>
    ''' <param name="sn">Serial number of the selected board</param>
    Public Sub BootloaderBlinkLED(ByVal sn As String)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte
        Dim tempHandle As CyFX3Device = Nothing
        Dim boardOk As Boolean = False

        'Find the device handle using the selected serial number
        For Each item In m_usbList
            'Look for the selected serial number, get its handle, and set it as the active device
            If String.Equals(CType(item, CyFX3Device).SerialNumber, sn) Then
                tempHandle = CType(item, CyFX3Device)
                boardOk = True
            End If
        Next

        If Not String.Equals(tempHandle.FriendlyName, ADIBootloaderName) Then
            Throw New Exception("ERROR: The selected board is not in bootloader mode")
        End If

        If Not boardOk Then
            Throw New Exception("ERROR: Could not find the board ID matching the serial number specified")
        End If

        'Set board handle
        FX3ControlEndPt = tempHandle.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = &HEF
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_ENDPT
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE
        FX3ControlEndPt.XferData(buf, 4)

    End Sub

    ''' <summary>
    ''' BOOTLOADER FW: Turn off the LED
    ''' </summary>
    ''' <param name="sn">Serial number of the selected board</param>
    Public Sub BootloaderTurnOffLED(ByVal sn As String)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte
        Dim tempHandle As CyFX3Device = Nothing
        Dim boardOk As Boolean = False

        'Find the device handle using the selected serial number
        For Each item In m_usbList
            'Look for the selected serial number, get its handle, and set it as the active device
            If String.Equals(CType(item, CyFX3Device).SerialNumber, sn) Then
                tempHandle = CType(item, CyFX3Device)
                boardOk = True
            End If
        Next

        If Not String.Equals(tempHandle.FriendlyName, ADIBootloaderName) Then
            Throw New Exception("ERROR: The selected board is not in bootloader mode")
        End If

        If Not boardOk Then
            Throw New Exception("ERROR: Could not find the board ID matching the serial number specified")
        End If

        'Set board handle
        FX3ControlEndPt = tempHandle.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = &HEE
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_ENDPT
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE
        FX3ControlEndPt.XferData(buf, 4)

    End Sub

    ''' <summary>
    ''' BOOTLOADER FW: Turn on the LED
    ''' </summary>
    ''' <param name="sn">Serial number of the selected board</param>
    Public Sub BootloaderTurnOnLED(ByVal sn As String)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte
        Dim tempHandle As CyFX3Device = Nothing
        Dim boardOk As Boolean = False

        'Find the device handle using the selected serial number
        For Each item In m_usbList
            'Look for the selected serial number, get its handle, and set it as the active device
            If String.Equals(CType(item, CyFX3Device).SerialNumber, sn) Then
                tempHandle = CType(item, CyFX3Device)
                boardOk = True
            End If
        Next

        If Not String.Equals(tempHandle.FriendlyName, ADIBootloaderName) Then
            Throw New Exception("ERROR: The selected board is not in bootloader mode")
        End If

        If Not boardOk Then
            Throw New Exception("ERROR: Could not find the board ID matching the serial number specified")
        End If

        'Set board handle
        FX3ControlEndPt = tempHandle.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = &HEC
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_ENDPT
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE
        FX3ControlEndPt.XferData(buf, 4)

    End Sub

#End Region

End Class
