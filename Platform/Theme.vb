Imports System.Runtime.InteropServices
Imports Microsoft.Win32

''' <summary>
''' Light / dark colours for all windows. Controls can be tagged:
'''   "accent" (blue button), "danger" (red button), "muted" (grey text),
'''   "panel" (header/footer background), "keep" (leave this control and its children alone).
''' </summary>
Public Class Palette
    Public Property IsDark As Boolean
    Public Property Back As Color
    Public Property Panel As Color
    Public Property Control As Color
    Public Property Border As Color
    Public Property Text As Color
    Public Property Muted As Color
    Public Property Accent As Color = Color.FromArgb(0, 122, 204)
    Public Property Danger As Color = Color.FromArgb(196, 64, 58)
    Public Property Selected As Color
    Public Property AltRow As Color
    Public Property Ok As Color
    Public Property Warn As Color
    Public Property Err As Color
    Public Property Busy As Color
End Class

Public Module Theme

    Public ReadOnly Dark As New Palette With {
        .IsDark = True,
        .Back = Color.FromArgb(30, 30, 30), .Panel = Color.FromArgb(37, 37, 38), .Control = Color.FromArgb(48, 48, 52),
        .Border = Color.FromArgb(70, 70, 74), .Text = Color.Gainsboro, .Muted = Color.FromArgb(140, 140, 140),
        .Selected = Color.FromArgb(0, 84, 140), .AltRow = Color.FromArgb(35, 35, 37),
        .Ok = Color.FromArgb(130, 210, 130), .Warn = Color.FromArgb(240, 200, 90), .Err = Color.FromArgb(240, 120, 110), .Busy = Color.FromArgb(100, 180, 255)}

    Public ReadOnly Light As New Palette With {
        .IsDark = False,
        .Back = Color.FromArgb(250, 250, 250), .Panel = Color.FromArgb(238, 238, 240), .Control = Color.White,
        .Border = Color.FromArgb(200, 200, 204), .Text = Color.FromArgb(30, 30, 30), .Muted = Color.FromArgb(110, 110, 110),
        .Selected = Color.FromArgb(204, 228, 247), .AltRow = Color.FromArgb(243, 243, 245),
        .Ok = Color.FromArgb(20, 130, 40), .Warn = Color.FromArgb(170, 110, 0), .Err = Color.FromArgb(190, 40, 30), .Busy = Color.FromArgb(0, 100, 190)}

    Public Property Current As Palette = Dark

    ''' <summary>"system" follows the Windows app mode (Settings › Personalisation › Colours).</summary>
    Public Sub SetMode(mode As String)
        Select Case mode
            Case "light" : Current = Light
            Case "dark" : Current = Dark
            Case Else : Current = If(WindowsUsesLightTheme(), Light, Dark)
        End Select
    End Sub

    Private Function WindowsUsesLightTheme() As Boolean
        Try
            Using k = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                Dim v = k?.GetValue("AppsUseLightTheme")
                If TypeOf v Is Integer Then Return CInt(v) <> 0
            End Using
        Catch
        End Try
        Return False
    End Function

    Public Sub Apply(root As Control)
        Dim p = Current
        ApplyTo(root, p)
        Dim f = TryCast(root, Form)
        If f IsNot Nothing Then
            If f.IsHandleCreated Then SetDarkTitleBar(f.Handle, p.IsDark) Else AddHandler f.HandleCreated, Sub() SetDarkTitleBar(f.Handle, Current.IsDark)
        End If
        root.Invalidate(True)
    End Sub

    Private Sub ApplyTo(c As Control, p As Palette)
        Dim tag = TryCast(c.Tag, String)
        If tag = "keep" Then Return

        Select Case True
            Case TypeOf c Is Form, TypeOf c Is UserControl
                c.BackColor = p.Back : c.ForeColor = p.Text
            Case TypeOf c Is Button
                Dim b = DirectCast(c, Button)
                b.FlatStyle = FlatStyle.Flat
                Dim bg = If(tag = "accent", p.Accent, If(tag = "danger", p.Danger, p.Control))
                b.BackColor = bg
                b.ForeColor = If(tag = "accent" OrElse tag = "danger", Color.White, p.Text)
                b.FlatAppearance.BorderColor = If(tag = "accent" OrElse tag = "danger", bg, p.Border)
                b.FlatAppearance.BorderSize = 1
            Case TypeOf c Is ComboBox, TypeOf c Is TextBoxBase, TypeOf c Is NumericUpDown, TypeOf c Is ListBox
                c.BackColor = p.Control : c.ForeColor = p.Text
            Case TypeOf c Is ListView
                c.BackColor = p.Back : c.ForeColor = p.Text
            Case TypeOf c Is DataGridView
                StyleGrid(DirectCast(c, DataGridView), p)
            Case TypeOf c Is Label
                c.ForeColor = If(tag = "muted", p.Muted, p.Text)
                If c.BackColor <> Color.Transparent Then c.BackColor = If(c.Parent IsNot Nothing, c.Parent.BackColor, p.Back)
            Case TypeOf c Is CheckBox, TypeOf c Is RadioButton, TypeOf c Is GroupBox
                c.ForeColor = p.Text
            Case TypeOf c Is Panel, TypeOf c Is TableLayoutPanel, TypeOf c Is FlowLayoutPanel
                c.BackColor = If(tag = "panel", p.Panel, If(c.Parent IsNot Nothing AndAlso TypeOf c.Parent IsNot Form, c.Parent.BackColor, p.Back))
                c.ForeColor = p.Text
        End Select
        For Each child As Control In c.Controls
            ApplyTo(child, p)
        Next
    End Sub

    Public Sub StyleGrid(g As DataGridView, p As Palette)
        g.BackgroundColor = p.Back
        g.GridColor = p.Border
        g.BorderStyle = BorderStyle.FixedSingle
        g.EnableHeadersVisualStyles = False
        g.ColumnHeadersDefaultCellStyle.BackColor = p.Panel
        g.ColumnHeadersDefaultCellStyle.ForeColor = p.Text
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Panel
        g.RowHeadersDefaultCellStyle.BackColor = p.Panel
        g.DefaultCellStyle.BackColor = p.Back
        g.DefaultCellStyle.ForeColor = p.Text
        g.DefaultCellStyle.SelectionBackColor = p.Selected
        g.DefaultCellStyle.SelectionForeColor = If(p.IsDark, Color.White, p.Text)
        g.AlternatingRowsDefaultCellStyle.BackColor = p.AltRow
    End Sub

    <DllImport("dwmapi.dll")>
    Private Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    ''' <summary>Dark window title bar on Windows 10 (1809+) and 11.</summary>
    Public Sub SetDarkTitleBar(hwnd As IntPtr, dark As Boolean)
        Try
            Dim v = If(dark, 1, 0)
            If DwmSetWindowAttribute(hwnd, 20, v, 4) <> 0 Then DwmSetWindowAttribute(hwnd, 19, v, 4)
        Catch
        End Try
    End Sub

End Module
