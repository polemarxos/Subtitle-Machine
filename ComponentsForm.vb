Imports System.Threading

''' <summary>
''' Shows every external dependency (FFmpeg, whisper.cpp, whisper model, llama.cpp, TranslateGemma),
''' its installed and latest version, and installs / updates them from their official download sites.
''' </summary>
Public Class ComponentsForm
    Inherits Form

    Private ReadOnly _settings As AppSettings
    Private ReadOnly _autoInstall As Boolean
    Private _ignoreCheck As Boolean
    ''' <summary>Opened from the "Update now" banner: install the updates found right away.</summary>
    Private ReadOnly _installUpdates As Boolean
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
    Private ReadOnly chkHearing As New CheckBox()
    Private ReadOnly chkSync As New CheckBox()
    Private ReadOnly chkLog As New CheckBox()
    Private ReadOnly cmbUiLang As New ComboBox()
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

    Public Sub New(settings As AppSettings, Optional autoInstallMissing As Boolean = False, Optional installUpdates As Boolean = False)
        _settings = settings
        _autoInstall = autoInstallMissing
        _installUpdates = installUpdates
        BuildUi()
    End Sub

    Private Sub BuildUi()
        SuspendLayout()
        Text = Loc.Tr("Components & settings")
        Font = New Font("Segoe UI", 9.0F)
        BackColor = BgDark
        ForeColor = Color.White
        ClientSize = New Size(780, 828)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = True       ' downloads can take a while – allow minimising
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = True     ' visible in the taskbar (also on first start, before the main window)

        lblHardware.SetBounds(12, 10, 756, 36)
        lblHardware.Text = Loc.Tr("Detected GPU: {0}", HardwareInfo.Gpu) & vbCrLf &
                           Loc.Tr("Recommended: TranslateGemma {0} with {1} acceleration", HardwareInfo.RecommendedTranslateSize().ToUpperInvariant(), HardwareInfo.RecommendedGpuBackend().ToUpperInvariant())

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

        Dim grp As New GroupBox With {.Text = Loc.Tr("Settings"), .ForeColor = Color.White}
        grp.SetBounds(12, 230, 756, 478)

        Dim y = 24
        AddRow(grp, Loc.Tr("Whisper model:"), cmbWhisper, y)
        For Each m In WhisperModelComponent.Models
            cmbWhisper.Items.Add(New ComboItem(m.Name, m.Description))
        Next
        y += 30
        AddRow(grp, Loc.Tr("Translation model:"), cmbSize, y)
        cmbSize.Items.Add(New ComboItem("auto", Loc.Tr("Automatic – by GPU memory (→ {0})", HardwareInfo.RecommendedTranslateSize().ToUpperInvariant())))
        For Each m In TranslateModelComponent.Sizes
            cmbSize.Items.Add(New ComboItem(m.Tag, Loc.Tr("TranslateGemma {0} – {1}, {2}", m.Tag.ToUpperInvariant(), m.Download, m.Vram)))
        Next
        y += 30
        AddRow(grp, Loc.Tr("GPU acceleration:"), cmbGpu, y)
        cmbGpu.Items.Add(New ComboItem("auto", Loc.Tr("Automatic (→ {0})", HardwareInfo.RecommendedGpuBackend().ToUpperInvariant())))
        cmbGpu.Items.Add(New ComboItem("cuda", Loc.Tr("CUDA – NVIDIA (fastest)")))
        cmbGpu.Items.Add(New ComboItem("vulkan", Loc.Tr("Vulkan – AMD / Intel / NVIDIA (Whisper uses CPU)")))
        cmbGpu.Items.Add(New ComboItem("cpu", Loc.Tr("CPU only (slow)")))
        y += 36
        AddCheck(grp, chkVad, Loc.Tr("Voice detection: Whisper skips silence (fewer invented lines)"), 12, y)
        AddCheck(grp, chkOcr, Loc.Tr("Read image subtitles (PGS / VobSub) with Windows OCR"), 390, y)
        y += 28
        AddCheck(grp, chkContext, Loc.Tr("Translate lines in context (consistent names, gender, tone)"), 12, y)
        AddCheck(grp, chkFormat, Loc.Tr("Fix line length && timing – max characters per line:"), 390, y)
        chkFormat.Width = 300
        numLineLen.SetBounds(692, y, 50, 23)
        numLineLen.Minimum = 30 : numLineLen.Maximum = 70
        numLineLen.BackColor = BgControl : numLineLen.ForeColor = Color.White
        grp.Controls.Add(numLineLen)
        y += 28
        AddCheck(grp, chkAutoUpdate, Loc.Tr("Update Subtitle Machine automatically"), 12, y)
        AddCheck(grp, chkStartup, Loc.Tr("Check for component updates on startup"), 390, y)
        y += 28
        AddCheck(grp, chkKeepSrt, Loc.Tr("Keep the .srt files after adding them into the MKV"), 12, y)
        AddCheck(grp, chkNotify, Loc.Tr("Notification and sound when a batch is finished"), 390, y)
        y += 28
        AddCheck(grp, chkHearing, Loc.Tr("Remove hearing-impaired texts: [music], (laughs), JOHN:, ♪ lyrics ♪"), 12, y)
        chkHearing.Width = 700
        y += 28
        AddCheck(grp, chkSync, Loc.Tr("Auto-sync downloaded subtitles to the speech of the video next to them"), 12, y)
        chkSync.Width = 700
        y += 28
        AddCheck(grp, chkLog, Loc.Tr("Write a log file (for troubleshooting; at most about 10 MB)"), 12, y)
        chkLog.Width = 700
        y += 32
        AddRow(grp, Loc.Tr("Theme:"), cmbTheme, y)
        cmbTheme.Width = 220
        cmbTheme.Items.Add(New ComboItem("system", Loc.Tr("Same as Windows")))
        cmbTheme.Items.Add(New ComboItem("dark", Loc.Tr("Dark")))
        cmbTheme.Items.Add(New ComboItem("light", Loc.Tr("Light")))
        btnGlossary.Text = Loc.Tr("Edit glossary (names && terms)…")
        btnGlossary.SetBounds(390, y - 2, 250, 28)
        y += 32
        AddRow(grp, Loc.Tr("Interface language:"), cmbUiLang, y)
        cmbUiLang.Width = 220
        cmbUiLang.Items.Add(New ComboItem("auto", Loc.Tr("Same as Windows")))
        cmbUiLang.Items.Add(New ComboItem("en", "English"))
        cmbUiLang.Items.Add(New ComboItem("el", "Ελληνικά"))
        grp.Controls.Add(btnGlossary)
        AddHandler btnGlossary.Click, Sub()
                                          Using g As New GlossaryForm()
                                              g.ShowDialog(Me)
                                          End Using
                                      End Sub

        StyleButton(btnCheck, Loc.Tr("Check for updates"), 12, Accent)
        StyleButton(btnInstall, Loc.Tr("Install / update selected"), 152, Accent)
        btnInstall.Width = 170
        StyleButton(btnCancel, Loc.Tr("Cancel"), 330, Color.FromArgb(217, 83, 79))
        btnCancel.Enabled = False
        StyleButton(btnClose, Loc.Tr("Close"), 638, BgControl)

        progress.SetBounds(12, 760, 756, 22)
        lblStatus.SetBounds(12, 788, 756, 34)
        lblStatus.Text = Loc.Tr("Ready")

        Controls.AddRange(New Control() {lblHardware, lv, grp, btnCheck, btnInstall, btnCancel, btnClose, progress, lblStatus})

        btnCheck.Tag = "accent" : btnInstall.Tag = "accent" : btnCancel.Tag = "danger"
        lblStatus.Tag = "muted"
        lblStatus.UseMnemonic = False
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
        ' Rows are drawn here too: the Windows default drawing of an owner-drawn list can show a tick
        ' on a row that is not ticked. The box is drawn from the row's real Checked state.
        AddHandler lv.DrawItem, Sub(s, e) e.DrawDefault = False
        AddHandler lv.DrawSubItem, AddressOf DrawComponentCell
        AddHandler lv.ItemChecked, Sub(s, e) lv.Invalidate(e.Item.Bounds)
        ' While downloading the list stays readable but the ticks cannot be changed.
        ' Windows toggles a row's tick on double-click, which ticked components by accident.
        ' Only a click on the box itself (or the space bar) changes it.
        AddHandler lv.MouseDown, Sub(s, e) _ignoreCheck = e.Clicks > 1
        AddHandler lv.MouseUp, Sub(s, e) _ignoreCheck = False
        AddHandler lv.ItemCheck, Sub(s, e)
                                     If _cts IsNot Nothing OrElse _ignoreCheck Then e.NewValue = e.CurrentValue
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
        AddHandler cmbUiLang.SelectedIndexChanged, AddressOf OptionsChanged
        For Each c In New CheckBox() {chkStartup, chkAutoUpdate, chkVad, chkOcr, chkContext, chkFormat, chkKeepSrt, chkNotify, chkHearing, chkSync, chkLog}
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
        b.SetBounds(x, 720, 130, 30)
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
        chkHearing.Checked = _settings.RemoveHearingImpaired
        chkSync.Checked = _settings.AutoSyncSubtitles
        chkLog.Checked = _settings.WriteLog
        SelectValue(cmbTheme, _settings.Theme)
        SelectValue(cmbUiLang, _settings.UiLanguage)
        _loading = False
        RebuildList()
        Await CheckUpdatesAsync()
        If _installUpdates AndAlso Not IsDisposed AndAlso lv.Items.Cast(Of ListViewItem)().Any(Function(i) i.Checked) Then
            Await InstallSelectedAsync()
        End If
        If _autoInstall AndAlso _components.Any(Function(c) c.IsRequired() AndAlso Not c.IsInstalled()) Then
            Await InstallSelectedAsync()
            ' First-time setup finished → close by itself and go straight to the main window.
            If Not IsDisposed AndAlso Not _components.Any(Function(c) c.IsRequired() AndAlso Not c.IsInstalled()) Then
                lblStatus.Text = Loc.Tr("All components are installed – ready to go!")
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
        _settings.RemoveHearingImpaired = chkHearing.Checked
        _settings.AutoSyncSubtitles = chkSync.Checked
        _settings.WriteLog = chkLog.Checked
        Dim newLang = DirectCast(cmbUiLang.SelectedItem, ComboItem).Value
        If newLang <> _settings.UiLanguage Then
            _settings.UiLanguage = newLang
            _settings.Save()
            Dim previous = Loc.Language
            Loc.Init(newLang)
            Dim msg = Loc.Tr("Restart Subtitle Machine to change the interface language.")
            Loc.Language = previous
            MessageBox.Show(Me, msg, "Subtitle Machine", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
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
                status = If(c.IsRequired(), Loc.Tr("Missing (required)"), Loc.Tr("Not installed (optional)"))
                check = c.IsRequired()
            ElseIf latest IsNot Nothing AndAlso c.NeedsInstallOrUpdate(latest) Then
                status = Loc.Tr("Update available")
                check = True
            ElseIf latest IsNot Nothing Then
                status = Loc.Tr("Up to date")
            Else
                status = Loc.Tr("Installed")
            End If
            If err IsNot Nothing Then status = Loc.Tr("Error: ") & err

            Dim latestText = If(latest IsNot Nothing, latest.Version, If(err IsNot Nothing, "—", "?"))
            If latest IsNot Nothing AndAlso latest.TotalSize > 0 Then latestText &= $"  [{WebHelper.FormatBytes(latest.TotalSize)}]"
            Dim item As New ListViewItem(c.DisplayName) With {.Tag = c}
            item.SubItems.Add(If(c.InstalledVersion(), "—"))
            item.SubItems.Add(latestText)
            item.SubItems.Add(status)
            If (Not c.IsInstalled() AndAlso c.IsRequired()) OrElse err IsNot Nothing Then item.ForeColor = Theme.Current.Err
            If status = Loc.Tr("Update available") Then item.ForeColor = Theme.Current.Warn
            If status = Loc.Tr("Up to date") Then item.ForeColor = Theme.Current.Ok
            lv.Items.Add(item)
            item.Checked = check ' set after adding, so Windows keeps it on this row
        Next
        lv.EndUpdate()
        lv.Invalidate()
        UpdateSelectionSize()
    End Sub

    Private Sub DrawComponentCell(sender As Object, e As DrawListViewSubItemEventArgs)
        Dim pal = Theme.Current
        Dim g = e.Graphics
        Using b As New SolidBrush(If(e.Item.Selected, pal.Selected, lv.BackColor))
            g.FillRectangle(b, e.Bounds)
        End Using
        Dim r = e.Bounds
        If e.ColumnIndex = 0 Then
            Dim state = If(e.Item.Checked, VisualStyles.CheckBoxState.CheckedNormal, VisualStyles.CheckBoxState.UncheckedNormal)
            Dim glyph = CheckBoxRenderer.GetGlyphSize(g, state)
            CheckBoxRenderer.DrawCheckBox(g, New Point(r.X + 4, r.Y + (r.Height - glyph.Height) \ 2), state)
            r = New Rectangle(r.X + glyph.Width + 8, r.Y, r.Width - glyph.Width - 8, r.Height)
        Else
            r.Inflate(-4, 0)
        End If
        Dim fore = If(e.Item.Selected AndAlso pal.IsDark, Color.White, e.Item.ForeColor)
        TextRenderer.DrawText(g, If(e.ColumnIndex = 0, e.Item.Text, e.SubItem.Text), lv.Font, r, fore,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
    End Sub

    Private Sub UpdateSelectionSize()
        If _cts IsNot Nothing Then Return
        Dim total As Long = 0
        For Each it As ListViewItem In lv.Items
            If Not it.Checked Then Continue For
            Dim rv As RemoteVersion = Nothing
            If _latest.TryGetValue(DirectCast(it.Tag, ToolComponent).Id, rv) Then total += rv.TotalSize
        Next
        If total > 0 Then lblStatus.Text = Loc.Tr("Selected download size: {0}", WebHelper.FormatBytes(total))
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
        lblStatus.Text = Loc.Tr("Checking latest versions…")
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
            lblStatus.Text = If(_errors.Count = 0, Loc.Tr("Version check complete."), Loc.Tr("Some checks failed – are you online? (details in the log)"))
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
        ' A ticked component that is already up to date would be downloaded again (gigabytes for a model): ask first.
        Dim current = selected.Where(Function(c)
                                         Dim rv As RemoteVersion = Nothing
                                         Return c.IsInstalled() AndAlso _latest.TryGetValue(c.Id, rv) AndAlso rv IsNot Nothing AndAlso Not c.NeedsInstallOrUpdate(rv)
                                     End Function).ToList()
        If current.Count > 0 Then
            Dim answer = MessageBox.Show(Me, Loc.Tr("These are already up to date:") & vbCrLf & String.Join(vbCrLf, current.Select(Function(c) "  • " & c.DisplayName)) & vbCrLf & vbCrLf &
                                         Loc.Tr("Download them again anyway (only needed to repair a broken installation)?"),
                                         Loc.Tr("Components & settings"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
            If answer = DialogResult.Cancel Then Return
            If answer = DialogResult.No Then
                selected = selected.Except(current).ToList()
                For Each it As ListViewItem In lv.Items
                    If current.Contains(DirectCast(it.Tag, ToolComponent)) Then it.Checked = False
                Next
            End If
        End If
        If selected.Count = 0 Then
            lblStatus.Text = Loc.Tr("Nothing selected.")
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
                        CType(reporter, IProgress(Of StatusUpdate)).Report(New StatusUpdate(Loc.Tr("{0}: checking latest version…", c.DisplayName)))
                        latest = Await c.GetLatestAsync(_cts.Token)
                        _latest(c.Id) = latest
                    End If
                    Await c.InstallAsync(latest, reporter, _cts.Token)
                    _errors.Remove(c.Id)
                Catch ex As OperationCanceledException
                    lblStatus.Text = Loc.Tr("Cancelled. Partially downloaded files will resume next time.")
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
        If Not wasCancelled Then lblStatus.Text = If(failures.Count = 0, Loc.Tr("All selected components are installed."), Loc.Tr("Finished with errors."))
        If failures.Count > 0 Then
            MessageBox.Show(Me, String.Join(vbCrLf & vbCrLf, failures), Loc.Tr("Some components failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If _cts IsNot Nothing Then
            If MessageBox.Show(Me, Loc.Tr("A download is in progress. Cancel it and close?"), Loc.Tr("Components & settings"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.No Then
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
        Text = Loc.Tr("Glossary – names and terms")
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(640, 520)
        MinimumSize = New Size(480, 360)
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False

        Dim help As New Label With {
            .Dock = DockStyle.Top, .Height = 70, .Padding = New Padding(10, 8, 10, 0), .Tag = "muted",
            .Text = Loc.Tr("One entry per line:  original = translation   (e.g.  Nick = Νίκος)") & vbCrLf &
                    Loc.Tr("Leave the translation empty to keep a word as it is:  Winterfell =") & vbCrLf &
                    Loc.Tr("Entries under [el], [de]… apply only to that language. The AI is told to use these translations.")}
        txt.Multiline = True
        txt.ScrollBars = ScrollBars.Both
        txt.WordWrap = False
        txt.AcceptsReturn = True
        txt.Font = New Font("Consolas", 10.5F)
        txt.Dock = DockStyle.Fill
        txt.Text = If(IO.File.Exists(Glossary.FilePath), IO.File.ReadAllText(Glossary.FilePath, System.Text.Encoding.UTF8), Glossary.Template)

        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Width = ClientSize.Width, .Height = 52, .Tag = "panel"}
        Dim btnSave As New Button With {.Text = Loc.Tr("Save"), .Tag = "accent", .Anchor = AnchorStyles.Right Or AnchorStyles.Top}
        btnSave.SetBounds(ClientSize.Width - 222, 10, 100, 32)
        Dim btnCancel As New Button With {.Text = Loc.Tr("Cancel"), .DialogResult = DialogResult.Cancel, .Anchor = AnchorStyles.Right Or AnchorStyles.Top}
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
