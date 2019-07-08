'File:          FX3Programming.vb
'Author:        Alex Nolan (alex.nolan@analog.com), Juan Chong (juan.chong@analog.com)
'Date:          6/10/2019
'Description:   This file is an extension of the primary FX3 Connection class. It contains all the functions
'               related to board enumeration, connection, and programming.

Imports CyUSB
Imports System.IO
Imports System.Threading
Imports System.Windows.Threading

Partial Class FX3Connection

#Region "Connection Functions"

    ''' <summary>
    ''' Attempts to program the selected FX3 board with the application firmware. The FX3 board should be programmed
    ''' with the ADI bootloader.
    ''' </summary>
    ''' <param name="FX3SerialNumber">Serial number of the device being connected to.</param>
    Public Sub Connect(ByVal FX3SerialNumber As String)

        Dim tempHandle As CyUSBDevice = Nothing
        Dim boardProgrammed As Boolean = False

        'Exit sub if we're already connected to a device
        If m_FX3Connected = True Then
            Exit Sub
        End If

        'Ensure that the connect flag isn't erroniously set
        m_NewBoardHandler.Reset()

        'Find the device handle using the selected serial number
        For Each item As CyFX3Device In m_usbList
            'Look for the selected serial number and get its handle
            If String.Equals(item.SerialNumber, FX3SerialNumber) Then
                tempHandle = item
            End If
        Next

        'Exit if we can't find the correct board
        If tempHandle Is Nothing Then
            'Set default values for the interface
            SetDefaultValues(m_sensorType)
            Throw New FX3ProgrammingException("ERROR: Could not find the board selected to connect to. Was it removed?")
        End If

        'Program the FX3 board with the application firmware
        'Check the active FX3 firmware and compare against the requested serial number
        If String.Equals(tempHandle.SerialNumber, FX3SerialNumber) Then
            'If the board is already programmed and in streamer mode, then don't re-program
            If String.Equals(tempHandle.FriendlyName, ApplicationName) Then
                'Set flag indicating that the FX3 successfully connected
                m_FX3Connected = True
            Else
                ProgramAppFirmware(tempHandle)
            End If
        End If

        m_ActiveFX3SN = FX3SerialNumber

        'Create a new windows dispatcher frame
        Dim originalFrame As DispatcherFrame = New DispatcherFrame()
        'Create a new thread which waits for the event
        Dim tempThread As Thread = New Thread(Sub()
                                                  'Wait until the board connected event is triggered
                                                  boardProgrammed = m_NewBoardHandler.WaitOne(TimeSpan.FromMilliseconds(Convert.ToDouble(ProgrammingTimeout)))
                                                  'Stops the execution of the connect function
                                                  originalFrame.Continue = False
                                              End Sub)

        tempThread.Start()
        'Resume execution of the connect function
        Dispatcher.PushFrame(originalFrame)

        'Throw exception if no board connected within timeout period
        If Not boardProgrammed Then
            Throw New FX3ProgrammingException("ERROR: Timeout occured during the FX3 re-enumeration process")
        End If

        'Check that the board appropriately re-enumerates
        boardProgrammed = False
        m_ActiveFX3SN = Nothing
        For Each item As CyUSBDevice In m_usbList
            'Look for the device we just programmed running the ADI Application firmware
            If String.Equals(item.FriendlyName, ApplicationName) And String.Equals(item.SerialNumber, FX3SerialNumber) Then
                boardProgrammed = True
                m_ActiveFX3 = CType(item, CyFX3Device)
                m_ActiveFX3SN = FX3SerialNumber
                'Set flag indicating that the FX3 successfully connected
                m_FX3Connected = True
                'leave loop early
                Exit For
            End If
        Next

        If Not boardProgrammed Then
            Throw New FX3ProgrammingException("ERROR: No application firmware found with the correct serial number")
        End If

        'Exit if we can't find the correct board
        If m_ActiveFX3SN Is Nothing Then
            'Set default values for the interface
            SetDefaultValues(m_sensorType)
            'Throw exception
            Throw New FX3ProgrammingException("ERROR: Could not find the board selected to connect to. Was it removed?")
        End If

        'Check that we're talking to the target board and it's running the application firmware
        If Not FX3CodeRunningOnTarget() Then
            'Set default values for the interface
            SetDefaultValues(m_sensorType)
            'Throw exception
            Throw New FX3ProgrammingException("ERROR: FX3 Board not successfully connected")
        End If

        'Check that the connection speed is adequate
        CheckConnectionSpeedOnTarget()

        'Set up endpoints
        EnumerateEndpointsOnTarget()

        'Check that endpoints are properly enumerated
        If Not CheckEndpointStatus() Then
            'Set default values for the interface
            SetDefaultValues(m_sensorType)
            'Throw exception
            Throw New FX3GeneralException("ERROR: Unable to configure endpoints")
        End If

        'Make sure that the board SPI parameters match current setting
        WriteBoardSpiParameters()

        'Set the board info
        m_ActiveFX3Info = New FX3Board(FX3SerialNumber, DateTime.Now)
        m_ActiveFX3Info.SetFirmwareVersion(GetFirmwareID())

    End Sub

    ''' <summary>
    ''' This function sends a reset command to the specified FX3 board, or does nothing if no board is connected
    ''' </summary>
    Public Sub Disconnect()

        'Exit sub if we're not connected to an FX3 board
        If m_FX3Connected = False Then
            Exit Sub
        End If

        'Start global timer. Used for managing disconnect events
        m_disconnectTimer = New Stopwatch()
        m_disconnectTimer.Start()

        'Save the current active board serial number
        m_disconnectedFX3SN = m_ActiveFX3SN

        'Reset the FX3 currently in use
        ResetFX3Firmware(m_ActiveFX3)
        'Set default values for the interface
        SetDefaultValues(m_sensorType)

    End Sub

    ''' <summary>
    ''' Property which returns the active FX3 board. Returns nothing if there is not a board connected.
    ''' </summary>
    ''' <returns>Returns active FX3 board if enumeration has been completed. Returns nothing otherwise.</returns>
    Public ReadOnly Property ActiveFX3 As FX3Board
        Get
            If m_FX3Connected Then
                Return m_ActiveFX3Info
            Else
                Return Nothing
            End If
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
    ''' Property which returns a list of the serial numbers of all FX3 boards running the ADI bootloader
    ''' </summary>
    ''' <returns>All detected FX3 boards.</returns>
    Public ReadOnly Property AvailableFX3s As List(Of String)
        Get
            Dim parsedList As New List(Of String)
            RefreshDeviceList()

            'Run through list looking for boards in bootloader mode
            For Each item As CyFX3Device In m_usbList
                If item.FriendlyName = ADIBootloaderName Then
                    parsedList.Add(item.SerialNumber)
                End If
            Next
            Return parsedList

        End Get
    End Property

    ''' <summary>
    ''' Property which returns a list of the serial numbers of all FX3 boards currently in use, running the application firmware.
    ''' </summary>
    ''' <returns>The list of board serial numbers</returns>
    Public ReadOnly Property BusyFX3s As List(Of String)
        Get
            Dim parsedList As New List(Of String)
            RefreshDeviceList()

            'Run through list looking for boards in bootloader mode
            For Each item As CyFX3Device In m_usbList
                If item.FriendlyName = ApplicationName Then
                    parsedList.Add(item.SerialNumber)
                End If
            Next
            Return parsedList

        End Get
    End Property

    ''' <summary>
    ''' Property which reads the firmware version from the FX3
    ''' </summary>
    ''' <returns>The firmware version, as a string</returns>
    Public ReadOnly Property GetFirmwareVersion As String
        Get
            Return GetFirmwareID()
        End Get
    End Property

    ''' <summary>
    ''' Readonly property to get the serial number of the active FX3 board
    ''' </summary>
    ''' <returns>The current serial number, as a string</returns>
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
        CheckConnectEvent(usbEvent)

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

            'Update the FX3Interface device list
            RefreshDeviceList()

            'Raise event so programs up the stack can handle
            RaiseEvent UnexpectedDisconnect(usbEvent.SerialNum)
        End If

    End Sub

    ''' <summary>
    ''' This function parses connect events. If the board connecting is running the ADI bootloader,
    ''' and has a serial number which matches that of the most recently disconnected FX3, a disconnect
    ''' finished event is raised. This allows GUIs or applications up the stack to better manage their
    ''' event flow (rather than blocking in a disconnect call).
    ''' </summary>
    ''' <param name="usbEvent">The event to handle</param>
    Private Sub CheckConnectEvent(ByVal usbEvent As USBEventArgs)

        'Check if the board which reconnected is the one being programmed
        If usbEvent.SerialNum = m_ActiveFX3SN Then
            m_NewBoardHandler.Set()
            Exit Sub
        End If

        'Handle the case where the event args aren't properly generated (lower level driver issue)
        If usbEvent.SerialNum = "" Then
            m_NewBoardHandler.Set()
            Exit Sub
        End If

        'exit if the board wasn't disconnected
        If IsNothing(m_disconnectedFX3SN) Then
            Exit Sub
        End If

        'Check board name and SN
        If usbEvent.FriendlyName = ADIBootloaderName And usbEvent.SerialNum = m_disconnectedFX3SN Then
            'Raise event
            RaiseEvent DisconnectFinished(m_disconnectedFX3SN, m_disconnectTimer.ElapsedMilliseconds())
            'Reset the timer
            m_disconnectTimer.Reset()
            'Reset the disconnected serial number
            m_disconnectedFX3SN = Nothing
            'return
            Exit Sub
        End If

        'Sometimes when there are multiple boards connected the event handling doesn't work as expected
        'To verify, will manually parse the device list
        For Each item As CyFX3Device In m_usbList
            If item.FriendlyName = ADIBootloaderName And item.SerialNumber = m_disconnectedFX3SN Then
                'Raise event
                RaiseEvent DisconnectFinished(m_disconnectedFX3SN, m_disconnectTimer.ElapsedMilliseconds())
                'Reset the timer
                m_disconnectTimer.Reset()
                'Reset the disconnected serial number
                m_disconnectedFX3SN = Nothing
                'return
                Exit Sub
            End If
        Next

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
    Private Sub ProgramBootloaderThread()

        'Message from the queue
        Dim selectedBoard As CyFX3Device

        While True
            'This function blocks until a new board is available to be programmed
            selectedBoard = BootloaderQueue.Take()
            'Program the indicated board (in cypress bootloader mode)
            Try
                ProgramBootloader(selectedBoard)
            Catch ex As FX3ProgrammingException
                'Don't need to do anything
                'This can be caused when a second instance of the FX3Connection generates a race condition
                System.Threading.Thread.Sleep(1000)
                RefreshDeviceList()
            End Try

        End While

    End Sub

    ''' <summary>
    ''' This function programs the bootloader of a single board
    ''' </summary>
    ''' <param name="selectedBoard">The handle for the FX3 board to be programmed with the ADI bootloader firmware</param>
    Private Sub ProgramBootloader(ByVal SelectedBoard As CyFX3Device)

        'Programming status
        Dim flashStatus As FX3_FWDWNLOAD_ERROR_CODE = FX3_FWDWNLOAD_ERROR_CODE.SUCCESS

        'Check that the cypress bootloader is currently running
        If Not SelectedBoard.IsBootLoaderRunning Then
            Throw New FX3ProgrammingException("ERROR: Selected FX3 is not in bootloader mode. Please reset the FX3.")
        End If

        'Attempt to program the board
        flashStatus = SelectedBoard.DownloadFw(BlinkFirmwarePath, FX3_FWDWNLOAD_MEDIA_TYPE.RAM)

        'Validate the flash status
        If Not flashStatus = FX3_FWDWNLOAD_ERROR_CODE.SUCCESS Then
            Throw New FX3ProgrammingException("ERROR: Bootloader download failed with code " + flashStatus.ToString())
        End If

    End Sub

    ''' <summary>
    ''' This function programs a single board running the ADI bootloader with the ADI application firmware.
    ''' </summary>
    ''' <param name="selectedBoard">The handle for the board to be programmed with the ADI application firmware</param>
    Private Sub ProgramAppFirmware(ByVal selectedBoard As CyFX3Device)

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
        If Not flashStatus = FX3_FWDWNLOAD_ERROR_CODE.SUCCESS Then
            Throw New FX3ProgrammingException("ERROR: Application firmware download failed with code " + flashStatus.ToString())
        End If

    End Sub

    ''' <summary>
    ''' Function which checks if the FX3 is connected and programmed
    ''' </summary>
    ''' <returns>A boolean indicating if the board is programmed</returns>
    Public Function FX3CodeRunningOnTarget() As Boolean

        'Return false if the board hasn't been connected yet
        If Not m_FX3Connected Then
            Return False
        End If

        'Make sure the selected board identifies as a "streamer" device running the application firmware
        If Not String.Equals(m_ActiveFX3.FriendlyName, ApplicationName) Then
            Return False
        End If

        'Make sure the selected board is reporting back the correct serial (using the control endpoint, not the USB descriptor)
        Dim boardSerialNumber As String = GetSerialNumber()
        If Not String.Equals(m_ActiveFX3SN, boardSerialNumber) Then
            Return False
        End If

        'Get the firmware ID from the board and check whether it contains "FX3"
        If GetFirmwareID().IndexOf("FX3") = -1 Then
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
            Else
                Throw New FX3ConfigurationException("ERROR: Invalid application firmware path provided: " + value)
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
            Else
                Throw New FX3ConfigurationException("ERROR: Invalid bootloader path provided: " + value)
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
            If FX3CodeRunningOnTarget() Then
                Return "Application firmware running on FX3"
            Else
                Return "Application firmware Not running on FX3"
            End If
        End Get
    End Property

    ''' <summary>
    ''' Checks if there is a Cypress FX3 USB device connected to the system
    ''' </summary>
    ''' <returns>A boolean indicating if there is an FX3 attached</returns>
    Public ReadOnly Property FX3BoardAttached As Boolean
        Get
            'Force an update of the device list
            RefreshDeviceList()
            'Return true if count is not 0
            Return (Not m_usbList.Count = 0)
        End Get
    End Property

    ''' <summary>
    ''' Send a reset command to the FX3 firmware. This command works for either the application or bootloader firmware.
    ''' </summary>
    ''' <param name="BoardHandle">Handle of the board to be reset.</param>
    Private Sub ResetFX3Firmware(ByVal BoardHandle As CyFX3Device)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte

        'Configure the control endpoint
        BoardHandle.ControlEndPt.ReqCode = USBCommands.ADI_HARD_RESET
        BoardHandle.ControlEndPt.ReqType = CyConst.REQ_VENDOR
        BoardHandle.ControlEndPt.Target = CyConst.TGT_ENDPT
        BoardHandle.ControlEndPt.Value = 0
        BoardHandle.ControlEndPt.Index = 0
        BoardHandle.ControlEndPt.Direction = CyConst.DIR_TO_DEVICE
        'Not throwing an exception here, its possible to send to a board which isn't configured with the application firmware.
        BoardHandle.ControlEndPt.XferData(buf, 4)

    End Sub

    ''' <summary>
    ''' Looks for and resets boards in application mode. Should only be called at program start, after InitBoardList()
    ''' Note: Should not be used if running multiple instances of the GUI.
    ''' </summary>
    ''' <returns>The number of boards running the application firmware which were reset</returns>
    Public Function ResetAllFX3s() As Integer

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
    ''' Checks to see if a provided firmware path is valid. Throws exception if it is not.
    ''' </summary>
    ''' <param name="Path">The firmware path to check</param>
    ''' <returns>A boolean indicating if the firmware path is valid</returns>
    Private Function isFirmwarePathValid(ByRef Path As String) As Boolean

        'Check file path length
        If Not Path.Length > 4 Then
            Return False
        End If

        'Check that it is a .img file
        If Not Path.Substring(Path.Length - 4, 4) = ".img" Then
            Return False
        End If

        'Check that the file exists
        If Not File.Exists(Path) Then
            Return False
        End If

        'Return true if all tests pass
        Return True

    End Function

    ''' <summary>
    ''' Performs a data transfer on the control endpoint with a check to see if the transaction times out
    ''' </summary>
    ''' <param name="Buf">The buffer to transfer</param>
    ''' <param name="NumBytes">The number of bytes to transfer</param>
    ''' <param name="Timeout">The timeout time (in milliseconds)</param>
    ''' <returns>Returns a boolean indicating if the transfer timed out or not</returns>
    Private Function XferControlData(ByRef Buf As Byte(), ByVal NumBytes As Integer, ByVal Timeout As Integer) As Boolean

        Dim startTime As New Stopwatch
        Dim validTransfer As Boolean = True

        'Point the API to the target FX3
        FX3ControlEndPt = m_ActiveFX3.ControlEndPt

        'Block control endpoint transfers while streaming (except cancel)
        If m_StreamThreadRunning And Not (FX3ControlEndPt.ReqCode = USBCommands.ADI_STREAM_REALTIME) Then
            Return False
        End If

        'Perform transfer
        startTime.Start()
        validTransfer = FX3ControlEndPt.XferData(Buf, NumBytes)
        startTime.Stop()

        'Check transfer status
        If Not validTransfer Then
            Return False
        End If

        'Check and see if timeout expired
        If startTime.ElapsedMilliseconds() > Timeout Then
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
    Private Sub ConfigureControlEndpoint(ByVal ReqCode As UInt16, ByVal ToDevice As Boolean)

        'Validate inputs
        If IsNothing(m_ActiveFX3) Then
            Throw New FX3GeneralException("ERROR: Attempted to configure control endpoint without FX3 being enumerated.")
        End If

        If Not m_FX3Connected Then
            Throw New FX3GeneralException("ERROR: Attempted to configure control endpoint without FX3 connected.")
        End If

        'Point the API to the target FX3
        FX3ControlEndPt = m_ActiveFX3.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = ReqCode
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_DEVICE
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        If ToDevice Then
            FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE
        Else
            FX3ControlEndPt.Direction = CyConst.DIR_FROM_DEVICE
        End If

    End Sub

    ''' <summary>
    ''' Gets the current firmware ID from the FX3
    ''' </summary>
    ''' <returns>Returns the firmware ID, as a string</returns>
    Private Function GetFirmwareID() As String

        'The firmware ID to return
        Dim firmwareID As String

        'Transfer buffer
        Dim buf(31) As Byte

        'Set up control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_FIRMWARE_ID_CHECK, False)

        If Not XferControlData(buf, 32, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer timed out while reading firmware ID")
        End If

        'Parse result
        Try
            firmwareID = System.Text.Encoding.UTF8.GetString(buf)
            firmwareID = firmwareID.Substring(0, Math.Max(0, firmwareID.IndexOf(vbNullChar)))
        Catch ex As Exception
            'Throw the exception up
            Throw New FX3GeneralException("ERROR: Parsing firmware ID failed", ex)
        End Try

        Return firmwareID

    End Function

    ''' <summary>
    ''' Gets the serial number of the target FX3 using the control endpoint
    ''' </summary>
    ''' <returns>The unique FX3 serial number</returns>
    Private Function GetSerialNumber() As String

        'The serial number to return
        Dim serialNumber As String

        'Transfer buffer
        Dim buf(31) As Byte

        'Set up the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_SERIAL_NUMBER_CHECK, False)

        If Not XferControlData(buf, 32, 2000) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer timed out while reading FX3 serial number")
        End If

        'Parse result
        Try
            serialNumber = System.Text.Encoding.Unicode.GetString(buf)
        Catch ex As Exception
            'Throw the exception up
            Throw New FX3GeneralException("ERROR: Parsing FX3 serial number failed", ex)
        End Try
        Return serialNumber
    End Function

    ''' <summary>
    ''' Checks that all the endpoints are properly enumerated
    ''' </summary>
    ''' <returns>A boolean indicating if the endpoints are properly enumerated</returns>
    Private Function CheckEndpointStatus() As Boolean

        'Check if control endpoint is set
        If FX3ControlEndPt Is Nothing Then
            Return False
        End If

        'Check if streaming endpoint is set
        If StreamingEndPt Is Nothing Then
            Return False
        End If

        'Check if bulk data in endpoint is set
        If DataInEndPt Is Nothing Then
            Return False
        End If

        'Check if bulk data out endpoint is set
        If DataOutEndPt Is Nothing Then
            Return False
        End If

        'Return true if all tests pass
        Return True

    End Function

    ''' <summary>
    ''' Resets all the currently configured endpoints on the FX3.
    ''' </summary>
    Private Sub ResetEndpoints()

        'Exit if the board is not enumerated yet
        If m_ActiveFX3 Is Nothing Then
            Exit Sub
        End If

        'Reset each listed endpoint
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
            If endpoint.Address = EndpointAddresses.ADI_FROM_PC_ENDPOINT Then
                DataOutEndPt = endpoint
            ElseIf endpoint.Address = EndpointAddresses.ADI_STREAMING_ENDPOINT Then
                StreamingEndPt = endpoint
            ElseIf endpoint.Address = EndpointAddresses.ADI_TO_PC_ENDPOINT Then
                DataInEndPt = endpoint
            End If
        Next

        'Enumerate the control endpoint
        FX3ControlEndPt = m_ActiveFX3.ControlEndPt

    End Sub

    ''' <summary>
    ''' Checks that the board is enumerated and connected via USB 2.0 or 3.0. Throws general exceptions for an invalid speed.
    ''' </summary>
    Private Sub CheckConnectionSpeedOnTarget()

        If IsNothing(m_ActiveFX3) Then
            Throw New FX3GeneralException("ERROR: FX3 Board not enumerated")
        End If

        If Not (m_ActiveFX3.bHighSpeed Or m_ActiveFX3.bSuperSpeed) Then
            'Clear the active FX3 device handle
            m_ActiveFX3 = Nothing
            Throw New FX3GeneralException("ERROR: FX3 must be connected with USB 2.0 or better")
        End If

    End Sub

#End Region

#Region "FX3 Bootloader Functions"

    ''' <summary>
    ''' BOOTLOADER FW: Blink the onboard LED
    ''' </summary>
    ''' <param name="SerialNumber">Serial number of the selected board</param>
    Public Sub BootloaderBlinkLED(ByVal SerialNumber As String)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte
        Dim tempHandle As CyFX3Device = Nothing
        Dim boardOk As Boolean = False

        'Find the device handle using the selected serial number
        For Each item In m_usbList
            'Look for the selected serial number, get its handle, and set it as the active device
            If String.Equals(CType(item, CyFX3Device).SerialNumber, SerialNumber) Then
                tempHandle = CType(item, CyFX3Device)
                boardOk = True
            End If
        Next

        If Not String.Equals(tempHandle.FriendlyName, ADIBootloaderName) Then
            Throw New FX3GeneralException("ERROR: The selected board is not in bootloader mode")
        End If

        If Not boardOk Then
            Throw New FX3GeneralException("ERROR: Could not find the board ID matching the serial number specified")
        End If

        'Set board handle
        FX3ControlEndPt = tempHandle.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = USBCommands.ADI_LED_BLINKING_ON
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_ENDPT
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE

        'Transfer command to bootloader
        If Not FX3ControlEndPt.XferData(buf, 4) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer failed when sending LED blink command to bootloader.")
        End If

    End Sub

    ''' <summary>
    ''' BOOTLOADER FW: Turn off the LED
    ''' </summary>
    ''' <param name="SerialNumber">Serial number of the selected board</param>
    Public Sub BootloaderTurnOffLED(ByVal SerialNumber As String)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte
        Dim tempHandle As CyFX3Device = Nothing
        Dim boardOk As Boolean = False

        'Find the device handle using the selected serial number
        For Each item In m_usbList
            'Look for the selected serial number, get its handle, and set it as the active device
            If String.Equals(CType(item, CyFX3Device).SerialNumber, SerialNumber) Then
                tempHandle = CType(item, CyFX3Device)
                boardOk = True
            End If
        Next

        If Not String.Equals(tempHandle.FriendlyName, ADIBootloaderName) Then
            Throw New FX3GeneralException("ERROR: The selected board is not in bootloader mode")
        End If

        If Not boardOk Then
            Throw New FX3GeneralException("ERROR: Could not find the board ID matching the serial number specified")
        End If

        'Set board handle
        FX3ControlEndPt = tempHandle.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = USBCommands.ADI_LED_OFF
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_ENDPT
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE

        'Transfer control data
        If Not FX3ControlEndPt.XferData(buf, 4) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer failed when sending LED off command to bootloader.")
        End If

    End Sub

    ''' <summary>
    ''' BOOTLOADER FW: Turn on the LED
    ''' </summary>
    ''' <param name="SerialNumber">Serial number of the selected board</param>
    Public Sub BootloaderTurnOnLED(ByVal SerialNumber As String)

        'Sub assumes the board has firmware loaded on it that will respond to reset commands
        Dim buf(3) As Byte
        Dim tempHandle As CyFX3Device = Nothing
        Dim boardOk As Boolean = False

        'Find the device handle using the selected serial number
        For Each item In m_usbList
            'Look for the selected serial number, get its handle, and set it as the active device
            If String.Equals(CType(item, CyFX3Device).SerialNumber, SerialNumber) Then
                tempHandle = CType(item, CyFX3Device)
                boardOk = True
            End If
        Next

        If Not String.Equals(tempHandle.FriendlyName, ADIBootloaderName) Then
            Throw New FX3GeneralException("ERROR: The selected board is not in bootloader mode")
        End If

        If Not boardOk Then
            Throw New FX3GeneralException("ERROR: Could not find the board ID matching the serial number specified")
        End If

        'Set board handle
        FX3ControlEndPt = tempHandle.ControlEndPt

        'Configure the control endpoint
        FX3ControlEndPt.ReqCode = USBCommands.ADI_LED_ON
        FX3ControlEndPt.ReqType = CyConst.REQ_VENDOR
        FX3ControlEndPt.Target = CyConst.TGT_ENDPT
        FX3ControlEndPt.Value = 0
        FX3ControlEndPt.Index = 0
        FX3ControlEndPt.Direction = CyConst.DIR_TO_DEVICE

        'Transfer control data
        If Not FX3ControlEndPt.XferData(buf, 4) Then
            Throw New FX3CommunicationException("ERROR: Control endpoint transfer failed when sending LED on command to bootloader.")
        End If

    End Sub

#End Region

End Class
