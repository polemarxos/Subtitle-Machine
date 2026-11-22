Imports System.Globalization

''' <summary>
''' Interface language. Texts are written in English in the code and looked up in the Greek
''' dictionary (Core\LocGreek.vb) when the interface is Greek. Missing entries fall back to English.
''' Usage:  Tr("Saved {0}.", fileName)
''' </summary>
Public Module Loc

    ''' <summary>"en" or "el".</summary>
    Public Property Language As String = "en"

    ''' <summary>"auto" = Windows display language (Greek if Windows is Greek), otherwise "en" / "el".</summary>
    Public Sub Init(setting As String)
        Select Case If(setting, "auto").ToLowerInvariant()
            Case "el" : Language = "el"
            Case "en" : Language = "en"
            Case Else : Language = If(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName = "el", "el", "en")
        End Select
    End Sub

    Public Function Tr(text As String, ParamArray args As Object()) As String
        If text Is Nothing Then Return Nothing
        Dim s = text
        If Language = "el" Then
            Dim g As String = Nothing
            If GreekStrings.Map.TryGetValue(text, g) Then s = g
        End If
        If args IsNot Nothing AndAlso args.Length > 0 Then
            Try
                s = String.Format(CultureInfo.CurrentCulture, s, args)
            Catch ex As FormatException
                s = String.Format(CultureInfo.CurrentCulture, text, args)
            End Try
        End If
        Return s
    End Function

    ''' <summary>True when a text has a translation (used to translate designer texts).</summary>
    Public Function HasTranslation(text As String) As Boolean
        Return text IsNot Nothing AndAlso GreekStrings.Map.ContainsKey(text)
    End Function

End Module
