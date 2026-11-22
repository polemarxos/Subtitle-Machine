Imports System.Threading

''' <summary>
''' Shows every external dependency (FFmpeg, whisper.cpp, whisper model, llama.cpp, TranslateGemma),
''' its installed and latest version, and installs / updates them from their official download sites.
''' </summary>
Public Class ComponentsForm
    Inherits Form

    Private ReadOnly _settings As AppSettings
    Private ReadOnly _autoInstall As Boolean
    Private _components As List(Of ToolComponent)
    Private ReadOnly _latest As New Dictionary(Of String, RemoteVersion)
    Private ReadOnly _errors As New Dictionary(Of String, String)
    Private _cts As CancellationTokenSource
    Private _loading As Boolean

    Private ReadOnly lblHardware As New Label()
    Private ReadOnly lv As New ListView()
    Private ReadOnly cmbWhisper As New ComboBox()
    Private ReadOnly cmbSize As New ComboBox()
    Private ReadOnly cmbGpu As New ComboBox()
    Private ReadOnly chkStartup As New CheckBox()
    Private ReadOnly chkAutoUpdate As New CheckBox()
    Private ReadOnly chkVad As New CheckBox()
    Private ReadOnly chkOcr As New CheckBox()
    Private ReadOnly chkContext As New CheckBox()
    Private ReadOnly chkFormat As New CheckBox()
    Private ReadOnly numLineLen As New NumericUpDown()
    Private ReadOnly chkKeepSrt As New CheckBox()
    Private ReadOnly chkNotify As New CheckBox()
    Private ReadOnly cmbTheme As New ComboBox()
    Private ReadOnly btnGlossary As New Button()
    Private ReadOnly btnCheck As New Button()
    Private ReadOnly btnInstall As New Button()
    Private ReadOnly btnCancel As New Button()
    Private ReadOnly btnClose As New Button()
    Private ReadOnly progress As New ProgressBar()
    Private ReadOnly lblStatus As New Label()

    Private Shared ReadOnly BgDark As Color = Color.FromArgb(30, 30, 30)
    Private Shared ReadOnly BgControl As Color = Color.FromArgb(45, 45, 48)
    Private Shared ReadOnly Accent As Color = Color.FromArgb(0, 122, 204)

    Public Sub New(settings As AppSettings, Optional autoInstallMissing As Boolean = False)
        _settings = settings
        _autoInstall = autoInstallMissing
        BuildUi()
    End Sub

    Private Sub BuildUi()
        SuspendLayout()
        Text = "Components & settings"
        Font = New Font("Segoe UI", 9.0F)
        BackColor = BgDark
        ForeColor = Color.White
        ClientSize = New Size(780, 712)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = True       ' downloads can take a while – allow minimising
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = True     ' visible in the taskbar (also on first start, before the main window)

        lblHardware.SetBounds(12, 10, 756, 36)
        lblHardware.Text = $"Detected GPU: {HardwareInfo.Gpu}" & vbCrLf &
                           $"Recommended: TranslateGemma {HardwareInfo.RecommendedTranslateSize().ToUpperInvariant()} with {HardwareInfo.RecommendedGpuBackend().ToUpperInvariant()} acceleration"

        lv.SetBounds(12, 52, 756, 170)
        lv.View = View.Details
        lv.CheckBoxes = True
        lv.FullRowSelect = True
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable
        lv.BackColor = BgDark
        lv.ForeColor = Color.White
        lv.BorderStyle = BorderStyle.FixedSingle
        lv.Columns.Add("Component", 255)
        lv.Columns.Add("Installed", 175)
        lv.Columns.Add("Latest", 175)
        lv.Columns.Add("Status", 140)
        AddHandler lv.ItemChecked, Sub() UpdateSelectionSize()

        Dim grp As New GroupBox With {.Text = "Settings", .ForeColor = Color.White}
        grp.SetBounds(12, 230, 756, 362)

        Dim y = 24
        AddRow(grp, "Whisper model:", cmbWhisper, y)
        For Each m In WhisperModelComponent.Models
            cmbWhisper.Items.Add(New ComboItem(m.Name, m.Description))
        Next
        y += 30
        AddRow(grp, "Translation model:", cmbSize, y)
        cmbSize.Items.Add(New ComboItem("auto", $"Automatic – by GPU memory (→ {HardwareInfo.RecommendedTranslateSize().ToUpperInvariant()})"))
        For Each m In TranslateModelComponent.Sizes
            cmbSize.Items.Add(New ComboItem(m.Tag, $"TranslateGemma {m.Tag.ToUpperInvariant()} – {m.Download}, {m.Vram}"))
        Next
        y += 30
        AddRow(grp, "GPU acceleration:", cmbGpu, y)
        cmbGpu.Items.Add(New ComboItem("auto", $"Automatic (→ {HardwareInfo.RecommendedGpuBackend().ToUpperInvariant()})"))
        cmbGpu.Items.Add(New ComboItem("cuda", "CUDA – NVIDIA (fastest)"))
        cmbGpu.Items.Add(New ComboItem("vulkan", "Vulkan – AMD / Intel / NVIDIA (Whisper uses CPU)"))
        cmbGpu.Items.Add(New ComboItem("cpu", "CPU only (slow)"))
        y += 36
        AddCheck(grp, chkVad, "Voice detection: Whisper skips silence (fewer invented lines)", 12, y)
        AddCheck(grp, chkOcr, "Read image subtitles (PGS / VobSub) with Windows OCR", 390, y)
        y += 28
        AddCheck(grp, chkContext, "Translate lines in context (consistent names, gender, tone)", 12, y)
        AddCheck(grp, chkFormat, "Fix line length && timing – max characters per line:", 390, y)
        chkFormat.Width = 300
        numLineLen.SetBounds(692, y, 50, 23)
        numLineLen.Minimum = 30 : numLineLen.Maximum = 70
        numLineLen.BackColor = BgControl : numLineLen.ForeColor = Color.White
        grp.Controls.Add(numLineLen)
        y += 28
        AddCheck(grp, chkAutoUpdate, "Update Subtitle Machine automatically", 12, y)
        AddCheck(grp, chkStartup, "Check for component updates on startup", 390, y)
        y += 28
        AddCheck(grp, chkKeepSrt, "Keep the .srt files after adding them into the MKV", 12, y)
        AddCheck(grp, chkNotify, "Notification and sound when a batch is finished", 390, y)
        y += 32
        AddRow(grp, "Theme:", cmbTheme, y)
        cmbTheme.Width = 220
        cmbTheme.Items.Add(New ComboItem("system", "Same as Windows"))
        cmbTheme.Items.Add(New ComboItem("dark", "Dark"))
        cmbTheme.Items.Add(New ComboItem("light", "Light"))
        btnGlossary.Text = "Edit glossary (names && terms)…"
        btnGlossary.SetBounds(390, y - 2, 250, 28)
        grp.Controls.Add(btnGlossary)
        AddHandler btnGlossary.Click, Sub()
                                          Using g As New GlossaryForm()
                                              g.ShowDialog(Me)
                                          End Using
                                      End Sub

        StyleButton(btnCheck, "Check for updates", 12, Accent)
        StyleButton(btnInstall, "Install / update selected", 152, Accent)
        btnInstall.Width = 170
        StyleButton(btnCancel, "Cancel", 330, Color.FromArgb(217, 83, 79))
        btnCancel.Enabled = False
        StyleButton(btnClose, "Close", 638, BgControl)

        progress.SetBounds(12, 644, 756, 22)
        lblStatus.SetBounds(12, 672, 756, 34)
        lblStatus.Text = "Ready"

        Controls.AddRange(New Control() {lblHardware, lv, grp, btnCheck, btnInstall, btnCancel, btnClose, progress, lblStatus})

        btnCheck.Tag = "accent" : btnInstall.Tag = "accent" : btnCancel.Tag = "danger"
        lblStatus.Tag = "muted"
        Theme.Apply(Me)

        ' Dark/light column header instead of the white Windows header.
        lv.OwnerDraw = True
        AddHandler lv.DrawColumnHeader, Sub(s, e)
                                            Using b As New SolidBrush(Theme.Current.Panel)
                                                e.Graphics.FillRectangle(b, e.Bounds)
                                            End Using
                                            Dim r = e.Bounds : r.Inflate(-6, 0)
                                            TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, r, Theme.Current.Muted, TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
                                        End Sub
        AddHandler lv.DrawItem, Sub(s, e) e.DrawDefault = True
        AddHandler lv.DrawSubItem, Sub(s, e) e.DrawDefault = True
        ' While downloading the list stays readable but the ticks cannot be changed.
        AddHandler lv.ItemCheck, Sub(s, e)
                                     If _cts IsNot Nothing Then e.NewValue = e.CurrentValue
                                 End Sub

        ' Scale positions with Windows display scaling (125 %, 150 % …).
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ResumeLayout(False)

        AddHandler btnCheck.Click, Async Sub() Await CheckUpdatesAsync()
        AddHandler btnInstall.Click, Async Sub() Await InstallSelectedAsync()
        AddHandler btnCancel.Click, Sub() _cts?.Cancel()
        AddHandler btnClose.Click, Sub() Close()
        AddHandler cmbWhisper.SelectedIndexChanged, AddressOf OptionsChanged
        AddHandler cmbSize.SelectedIndexChanged, AddressOf OptionsChanged
        AddHandler cmbGpu.SelectedIndexChanged, AddressOf OptionsChanged
        AddHandler cmbTheme.SelectedIndexChanged, AddressOf OptionsChanged
        For Each c In New CheckBox() {chkStartup, chkAutoUpdate, chkVad, chkOcr, chkContext, chkFormat, chkKeepSrt, chkNotify}
            AddHandler c.CheckedChanged, AddressOf OptionsChanged
        Next
        AddHandler numLineLen.ValueChanged, AddressOf OptionsChanged
    End Sub

    Private Sub AddRow(parent As Control, caption As String, combo As ComboBox, y As Integer)
        Dim l As New Label With {.Text = caption, .ForeColor = Color.White, .AutoSize = False}
        l.SetBounds(12, y + 3, 135, 20)
        combo.SetBounds(150, y, 480, 23)
        combo.DropDownStyle = ComboBoxStyle.DropDownList
        combo.FlatStyle = FlatStyle.Flat
        combo.BackColor = BgControl
        combo.ForeColor = Color.White
        parent.Controls.Add(l)
        parent.Controls.Add(combo)
    End Sub

    Private Sub AddCheck(parent As Control, c As CheckBox, caption As String, x As Integer, y As Integer)
        c.Text = caption
        c.SetBounds(x, y, 350, 22)
        c.ForeColor = Color.White
        parent.Controls.Add(c)
    End Sub

    Private Sub StyleButton(b As Button, caption As String, x As Integer, color As Color)
        b.Text = caption
        b.SetBounds(x, 604, 130, 30)
        b.FlatStyle = FlatStyle.Flat
        b.BackColor = color
        b.FlatAppearance.BorderColor = color
        b.ForeColor = Color.White
    End Sub

    Private Class ComboItem
        Public ReadOnly Property Value As String
        Public ReadOnly Property Caption As String
        Public Sub New(value As String, caption As String)
            Me.Value = value
            Me.Caption = caption
        End Sub
        Public Overrides Function ToString() As String
            Return Caption
        End Function
    End Class

    Private Shared Sub SelectValue(combo As ComboBox, value As String)
        For i = 0 To combo.Items.Count - 1
            If String.Equals(DirectCast(combo.Items(i), ComboItem).Value, value, StringComparison.OrdinalIgnoreCase) Then
                combo.SelectedIndex = i
                Return
            End If
        Next
        If combo.Items.Count > 0 Then combo.SelectedIndex = 0
    End Sub

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _loading = True
        SelectValue(cmbWhisper, _settings.WhisperModel)
        SelectValue(cmbSize, _settings.TranslateModelSize)
        SelectValue(cmbGpu, _settings.GpuBackend)
        chkStartup.Checked = _settings.CheckComponentUpdates
        chkAutoUpdate.Checked = _settings.AutoUpdateApp
        chkVad.Checked = _settings.UseVad
        chkOcr.Checked = _settings.OcrImageSubtitles
        chkContext.Checked = _settings.ContextTranslation
        chkFormat.Checked = _settings.FixFormatting
        numLineLen.Value = _settings.MaxLineLength
        chkKeepSrt.Checked = _settings.KeepSrtFiles
        chkNotify.Checked = _settings.NotifyWhenDone
        SelectValue(cmbTheme, _settings.Theme)
        _loading = False
        RebuildList()
        Await CheckUpdatesAsync()
        If _autoInstall AndAlso _components.Any(Function(c) c.IsRequired() AndAlso Not c.IsInstalled()) Then
            Await InstallSelectedAsync()
            ' First-time setup finished → close by itself and go straight to the main window.
            If Not IsDisposed AndAlso Not _components.Any(Function(c) c.IsRequired() AndAlso Not c.IsInstalled()) Then
                lblStatus.Text = "All components are installed – ready to go!"
                Await Task.Delay(1500)
                If Not IsDisposed Then
                    DialogResult = DialogResult.OK
                    Close()
                End If
            End If
        End If
    End Sub

    Private Sub OptionsChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Dim oldGpu = _settings.EffectiveGpuBackend()
        _settings.WhisperModel = DirectCast(cmbWhisper.SelectedItem, ComboItem).Value
        _settings.TranslateModelSize = DirectCast(cmbSize.SelectedItem, ComboItem).Value
        _settings.GpuBackend = DirectCast(cmbGpu.SelectedItem, ComboItem).Value
        _settings.CheckComponentUpdates = chkStartup.Checked
        _settings.AutoUpdateApp = chkAutoUpdate.Checked
        _settings.UseVad = chkVad.Checked
        _settings.OcrImageSubtitles = chkOcr.Checked
        _settings.ContextTranslation = chkContext.Checked
        _settings.FixFormatting = chkFormat.Checked
        _settings.MaxLineLength = CInt(numLineLen.Value)
        _settings.KeepSrtFiles = chkKeepSrt.Checked
        _settings.NotifyWhenDone = chkNotify.Checked
        Dim newTheme = DirectCast(cmbTheme.SelectedItem, ComboItem).Value
        If newTheme <> _settings.Theme Then
            _settings.Theme = newTheme
            Theme.SetMode(newTheme)
            Theme.Apply(Me)
            If Owner IsNot Nothing Then Theme.Apply(Owner)
        End If
        _settings.Save()
        ' Changing the GPU backend changes which engine builds are needed.
        If oldGpu <> _settings.EffectiveGpuBackend() Then
            _latest.Remove("whisper")
            _latest.Remove("llama")
        End If
        RebuildList()
        If TypeOf sender Is ComboBox OrElse sender Is chkVad Then
            Dim unused = CheckUpdatesAsync(onlyMissing:=True)
        End If
    End Sub

    Private Sub RebuildList()
        _components = DependencyManager.GetComponents(_settings)
        lv.BeginUpdate()
        lv.Items.Clear()
        For Each c In _components
            Dim latest As RemoteVersion = Nothing
            _latest.TryGetValue(c.Id, latest)
            Dim err As String = Nothing
            _errors.TryGetValue(c.Id, err)

            Dim status As String
            Dim check As Boolean
            If Not c.IsInstalled() Then
                status = If(c.IsRequired(), "Missing (required)", "Not installed (optional)")
                check = c.IsRequired()
            ElseIf latest IsNot Nothing AndAlso c.NeedsInstallOrUpdate(latest) Then
                status = "Update available"
                check = True
            ElseIf latest IsNot Nothing Then
                status = "Up to date"
            Else
                status = "Installed"
            End If
            If err IsNot Nothing Then status = "Error: " & err

            Dim latestText = If(latest IsNot Nothing, latest.Version, If(err IsNot Nothing, "—", "?"))
            If latest IsNot Nothing AndAlso latest.TotalSize > 0 Then latestText &= $"  [{WebHelper.FormatBytes(latest.TotalSize)}]"
            Dim item As New ListViewItem(c.DisplayName) With {.Tag = c, .Checked = check}
            item.SubItems.Add(If(c.InstalledVersion(), "—"))
            item.SubItems.Add(latestText)
            item.SubItems.Add(status)
            item.ToolTipText = c.Source
            If status.StartsWith("Missing") OrElse status.StartsWith("Error") Then item.ForeColor = Theme.Current.Err
            If status = "Update available" Then item.ForeColor = Theme.Current.Warn
            If status = "Up to date" Then item.ForeColor = Theme.Current.Ok
            lv.Items.Add(item)
        Next
        lv.ShowItemToolTips = True
        lv.EndUpdate()
        UpdateSelectionSize()
    End Sub

    Private Sub UpdateSelectionSize()
        If _cts IsNot Nothing Then Return
        Dim total As Long = 0
        For Each it As ListViewItem In lv.Items
            If Not it.Checked Then Continue For
            Dim rv As RemoteVersion = Nothing
            If _latest.TryGetValue(DirectCast(it.Tag, ToolComponent).Id, rv) Then total += rv.TotalSize
        Next
        If total > 0 Then lblStatus.Text = $"Selected download size: {WebHelper.FormatBytes(total)}"
    End Sub

    Private Sub SetBusy(busy As Boolean)
        btnCheck.Enabled = Not busy
        btnInstall.Enabled = Not busy
        btnCancel.Enabled = busy
        cmbWhisper.Enabled = Not busy
        cmbSize.Enabled = Not busy
        cmbGpu.Enabled = Not busy
    End Sub

    Private Async Function CheckUpdatesAsync(Optional onlyMissing As Boolean = False) As Task
        If _cts IsNot Nothing Then Return
        _cts = New CancellationTokenSource()
        SetBusy(True)
        progress.Style = ProgressBarStyle.Marquee
        lblStatus.Text = "Checking latest versions…"
        Try
            Dim targets = _components.Where(Function(c) Not onlyMissing OrElse Not _latest.ContainsKey(c.Id)).ToList()
            Dim checks = targets.Select(Async Function(c)
                                            Try
                                                Dim rv = Await c.GetLatestAsync(_cts.Token)
                                                Return (c.Id, rv, CStr(Nothing))
                                            Catch ex As Exception
                                                Logger.Log($"Version check {c.Id} failed: {ex.Message}")
                                                Return (c.Id, CType(Nothing, RemoteVersion), ex.Message)
                                            End Try
                                        End Function).ToList()
            For Each r In Await Task.WhenAll(checks)
                If r.Item2 IsNot Nothing Then
                    _latest(r.Item1) = r.Item2
                    _errors.Remove(r.Item1)
                Else
                    _errors(r.Item1) = r.Item3
                End If
            Next
            lblStatus.Text = If(_errors.Count = 0, "Version check complete.", "Some checks failed – are you online? (details in the log)")
        Finally
            _cts.Dispose()
            _cts = Nothing
            If Not IsDisposed Then
                progress.Style = ProgressBarStyle.Continuous
                SetBusy(False)
                RebuildList()
            End If
        End Try
    End Function

    Private Async Function InstallSelectedAsync() As Task
        If _cts IsNot Nothing Then Return
        Dim selected = lv.Items.Cast(Of ListViewItem)().Where(Function(i) i.Checked).Select(Function(i) DirectCast(i.Tag, ToolComponent)).ToList()
        If selected.Count = 0 Then
            lblStatus.Text = "Nothing selected."
            Return
        End If

        _cts = New CancellationTokenSource()
        SetBusy(True)
        Dim failures As New List(Of String)
        Dim wasCancelled As Boolean
        Dim reporter As New Progress(Of StatusUpdate)(Sub(s)
                                                          lblStatus.Text = s.Text
                                                          If s.Percent >= 0 Then
                                                              progress.Style = ProgressBarStyle.Continuous
                                                              progress.Value = Math.Min(100, Math.Max(0, s.Percent))
                                                          Else
                                                              progress.Style = ProgressBarStyle.Marquee
                                                          End If
                                                      End Sub)
        Try
            For Each c In selected
                Try
                    Dim latest As RemoteVersion = Nothing
                    If Not _latest.TryGetValue(c.Id, latest) Then
                        CType(reporter, IProgress(Of StatusUpdate)).Report(New StatusUpdate($"{c.DisplayName}: checking latest version…"))
                        latest = Await c.GetLatestAsync(_cts.Token)
                        _latest(c.Id) = latest
                    End If
                    Await c.InstallAsync(latest, reporter, _cts.Token)
                    _errors.Remove(c.Id)
                Catch ex As OperationCanceledException
                    lblStatus.Text = "Cancelled. Partially downloaded files will resume next time."
                    Exit For
                Catch ex As Exception
                    Logger.Log($"Install {c.Id} failed: {ex}")
                    _errors(c.Id) = ex.Message
                    failures.Add($"{c.DisplayName}: {ex.Message}")
                End Try
            Next
        Finally
            wasCancelled = _cts.IsCancellationRequested
            _cts.Dispose()
            _cts = Nothing
        End Try
        If IsDisposed Then Return
        SetBusy(False)
        progress.Style = ProgressBarStyle.Continuous
        progress.Value = 0
        RebuildList()
        If Not wasCancelled Then lblStatus.Text = If(failures.Count = 0, "All selected components are installed.", "Finished with errors.")
        If failures.Count > 0 Then
            MessageBox.Show(Me, String.Join(vbCrLf & vbCrLf, failures), "Some components failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If _cts IsNot Nothing Then
            If MessageBox.Show(Me, "A download is in progress. Cancel it and close?", "Components", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.No Then
                e.Cancel = True
                Return
            End If
            _cts.Cancel()
        End If
        MyBase.OnFormClosing(e)
    End Sub

End Class


''' <summary>Simple editor for glossary.txt.</summary>
Public Class GlossaryForm
    Inherits Form

    Private ReadOnly txt As New TextBox()

    Public Sub New()
        SuspendLayout()
        Text = "Glossary – names and terms"
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(640, 520)
        MinimumSize = New Size(480, 360)
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False

        Dim help As New Label With {
            .Dock = DockStyle.Top, .Height = 70, .Padding = New Padding(10, 8, 10, 0), .Tag = "muted",
            .Text = "One entry per line:  original = translation   (e.g.  Nick = Νίκος)" & vbCrLf &
                    "Leave the translation empty to keep a word as it is:  Winterfell =" & vbCrLf &
                    "Entries under [el], [de]… apply only to that language. The AI is told to use these translations."}
        txt.Multiline = True
        txt.ScrollBars = ScrollBars.Both
        txt.WordWrap = False
        txt.AcceptsReturn = True
        txt.Font = New Font("Consolas", 10.5F)
        txt.Dock = DockStyle.Fill
        txt.Text = If(IO.File.Exists(Glossary.FilePath), IO.File.ReadAllText(Glossary.FilePath, System.Text.Encoding.UTF8), Glossary.Template)

        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 52, .Tag = "panel"}
        Dim btnSave As New Button With {.Text = "Save", .Tag = "accent", .Anchor = AnchorStyles.Right Or AnchorStyles.Top}
        btnSave.SetBounds(ClientSize.Width - 222, 10, 100, 32)
        Dim btnCancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .Anchor = AnchorStyles.Right Or AnchorStyles.Top}
        btnCancel.SetBounds(ClientSize.Width - 114, 10, 100, 32)
        bottom.Controls.AddRange(New Control() {btnSave, btnCancel})
        CancelButton = btnCancel

        Controls.Add(txt)
        Controls.Add(bottom)
        Controls.Add(help)
        AddHandler btnSave.Click, Sub()
                                      IO.File.WriteAllText(Glossary.FilePath, txt.Text, New System.Text.UTF8Encoding(False))
                                      Dim n = Glossary.Parse(txt.Text).Entries.Count
                                      Logger.Log($"Glossary saved ({n} entries)")
                                      DialogResult = DialogResult.OK
                                  End Sub
        Theme.Apply(Me)
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ResumeLayout(False)
    End Sub
End Class
