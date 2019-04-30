Imports RegMapClasses

Partial Class FX3Connection

    Private m_CrcFirstIndex As Integer
    ''' <summary>
    ''' Gets or sets the index of the first burst data word used in CRC calculations.
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Property CrcFirstIndex() As Integer
        Get
            Return m_CrcFirstIndex
        End Get
        Set(ByVal value As Integer)
            m_CrcFirstIndex = value
        End Set
    End Property

    Private m_CrcLastIndex As Integer
    ''' <summary>
    ''' Gets or sets the index of the last burst data word used in CRC calculations.
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Property CrcLastIndex() As Integer
        Get
            Return m_CrcLastIndex
        End Get
        Set(ByVal value As Integer)
            m_CrcLastIndex = value
        End Set
    End Property

    Private m_CrcResultIndex As Integer
    ''' <summary>
    ''' Gets or sets the index of the word that contains the CRC result.
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Property CrcResultIndex() As Integer
        Get
            Return m_CrcResultIndex
        End Get
        Set(ByVal value As Integer)
            m_CrcResultIndex = value
        End Set
    End Property

    Private m_WordCount As Integer
    ''' <summary>
    ''' Gets or sets the number of 16 bit words that are transferred during the burst.
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Property WordCount() As Integer
        Get
            Return m_WordCount
        End Get
        Set(ByVal value As Integer)
            ' Validate and that we have a valid UShort value, as we must convert to ushort for spi object.  
            If value < 1 Or value > UShort.MaxValue Then
                Throw New ArgumentException("WordCount must be between 1 and " & UShort.MaxValue.ToString() & ".")
            End If
            m_WordCount = value
        End Set
    End Property

    Private m_TriggerReg As RegClass
    ''' <summary>
    ''' Gets or sets register that is used to trigger burst operation.
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Property TriggerReg() As RegClass
        Get
            Return m_TriggerReg
        End Get
        Set(ByVal value As RegClass)
            m_TriggerReg = value
        End Set
    End Property

    ''' <summary>
    ''' Takes interface out of burst mode by setting BurstMode to zero.
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub ClearBurstMode()
        burstMode = 0
    End Sub

    ''' <summary>
    ''' Puts interface into burst mode by setting burstMode to match word count.
    ''' </summary>
    ''' <remarks></remarks>
    ''' <exception cref="System.InvalidOperationException">Thrown if word count has not been set.</exception>
    Public Sub SetupBurstMode()
        If Me.WordCount = 0 Then
            Throw New InvalidOperationException("WordCount must be set before performing a burst read Operaton.")
        End If
        If Me.TriggerReg Is Nothing Then
            Throw New InvalidOperationException("Trigger register must be set before performing a burst read Operaton.")
        End If
        burstMode = CUShort(Me.WordCount)
    End Sub

End Class
