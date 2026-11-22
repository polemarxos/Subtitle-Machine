Imports System.IO
Imports System.Threading

Public Class Form1

    Private Class ModelChoice
        Public Property Size As String
        Public Property Caption As String
        Public Overrides Function ToString() As String
            Return Caption
        End Function
    End Class

    Private ReadOnly Tasks As New List(Of TaskItem)
    Private _settings As AppSettings
    Private _cts As CancellationTokenSource
    Private _translator As ITranslator
    Private _loadingUi As Boolean

    Private WithEvents _notify As New NotifyIcon()

    ' ----- Constructor & Debug Console -----
    Public Sub New()
        If Environment.GetCommandLineArgs().Any(Function(a) a.Equals("-debug", StringComparison.OrdinalIgnoreCase)) Then
            Logger.DebugConsole = True
            Try
                NativeMethods.AllocConsole()
                NativeMethods.SetConsoleTitle("Subtitle Machine - Debug Console")
                Console.SetOut(New StreamWriter(Console.OpenStandardOutput()) With {.AutoFlush = True})
            Catch
            End Try
        End If
        InitializeComponent()
    End Sub

    ' =====================================================================
    '  Startup
    ' =====================================================================
    Private Async Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Dim ver = SelfUpdater.CurrentVersion().ToString(3)
        Text = $"Subtitle Machine {ver}"
        lblVersion.Text = "v" & ver
        Logger.Log($"=== Subtitle Machine {ver} started. Data folder: {AppPaths.DataDir} ===")
        AppPaths.MigrateLegacyLayout()
        SelfUpdater.CleanupLeftovers()
        _settings = AppSettings.Load()
        SubtitlePipeline.OcrFactory = AddressOf WindowsOcr.Create

        _loadingUi = True
        cmbSource.Items.Add("Auto-detect")
        For Each l In Languages.All
            cmbSource.Items.Add(l)
        Next
        BuildTargetPicker()
        cmbSubSource.Items.AddRange(New Object() {"Embedded subtitles, otherwise audio", "Embedded subtitles only", "Audio only (Whisper speech-to-text)"})
        cmbSubSource.SelectedIndex = Array.IndexOf({"auto", "embedded", "audio"}, _settings.SubtitleSource)
        Dim src = Languages.Find(_settings.TranscribeLanguage)
        If src IsNot Nothing Then cmbSource.SelectedItem = src Else cmbSource.SelectedIndex = 0
        chkReplaceOriginal.Checked = _settings.ReplaceOriginal
        chkOverwrite.Checked = _settings.OverwriteExisting
        lblGpu.Text = "GPU: " & HardwareInfo.Gpu.ToString()
        _loadingUi = False
        PopulateModelCombo()
        UpdateTargetsText()
        UpdateUiState()
        toolTip.SetToolTip(chkReplaceOriginal, "On: the translation is written into the original .srt (e.g. Movie.srt)." & vbCrLf & "Off: a new file is created (e.g. Movie.el.srt) and the original is kept.")
        toolTip.SetToolTip(cmbModel, "Google TranslateGemma, running locally with llama.cpp." & vbCrLf & "Automatic chooses the size that fits your graphics card.")
        toolTip.SetToolTip(cmbSubSource, "Embedded: use subtitles inside the video (text, or image subtitles read with OCR)." & vbCrLf &
                                         "Audio: always create the subtitles from the speech with Whisper.")
        toolTip.SetToolTip(btnTargets, "Choose one or more languages – one subtitle file is created per language.")
        toolTip.SetToolTip(cmbVideoOut, "MKV: the new subtitles are added to a copy of the video as extra tracks (fast, no quality loss)." & vbCrLf &
                                        "Burned-in: an MP4 copy with the subtitles drawn on the picture (first language; takes longer).")

        _loadingUi = True
        cmbVideoOut.Items.AddRange(New Object() {"Subtitle files only", "Also add subtitles into the video (MKV)", "Also burn subtitles into the picture (MP4)", "Both MKV and burned-in MP4"})
        cmbVideoOut.SelectedIndex = Array.IndexOf({"none", "mkv", "burn", "both"}, _settings.VideoOutput)
        cmbWhenDone.Items.AddRange(New Object() {"Do nothing", "Sleep", "Hibernate", "Shut down"})
        cmbWhenDone.SelectedIndex = 0
        _loadingUi = False

        ' Theme
        btnAddFiles.Tag = "accent" : btnStart.Tag = "accent" : btnStop.Tag = "danger"
        lblGpu.Tag = "muted" : lblVersion.Tag = "muted" : lblDropHint.Tag = "muted"
        pnlHeader.Tag = "panel" : pnlFooter.Tag = "panel" : pnlUpdate.Tag = "keep"
        ApplyTheme()

        _notify.Icon = Icon
        _notify.Text = "Subtitle Machine"

        ' Missing tools → offer to download everything.
        Dim missing = DependencyManager.MissingRequired(_settings)
        If missing.Count > 0 Then
            If MessageBox.Show(Me, "Subtitle Machine needs these components. They will be downloaded from their official sites:" & vbCrLf & vbCrLf &
                               String.Join(vbCrLf, missing.Select(Function(c) "  • " & c.DisplayName)) & vbCrLf & vbCrLf & "Download them now?",
                               "Subtitle Machine – setup", MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
                ShowComponents(autoInstall:=True)
            End If
        End If

        If _settings.AutoUpdateApp Then Await AutoUpdateAppAsync(interactive:=False)
        If _settings.CheckComponentUpdates AndAlso DependencyManager.MissingRequired(_settings).Count = 0 Then Await CheckComponentUpdatesAsync()
    End Sub

    Private Sub PopulateModelCombo()
        _loadingUi = True
        cmbModel.Items.Clear()
        Dim rec = HardwareInfo.RecommendedTranslateSize()
        Dim recInfo = TranslateModelComponent.Sizes.First(Function(x) x.Tag = rec)
        cmbModel.Items.Add(New ModelChoice With {.Size = "auto", .Caption = $"Automatic – TranslateGemma {rec.ToUpperInvariant()} for your GPU ({recInfo.Download}){Downloaded(rec)}"})
        For Each m In TranslateModelComponent.Sizes
            cmbModel.Items.Add(New ModelChoice With {.Size = m.Tag, .Caption = $"TranslateGemma {m.Tag.ToUpperInvariant()} – {m.Download}, {m.Vram}{Downloaded(m.Tag)}"})
        Next
        cmbModel.SelectedItem = If(cmbModel.Items.Cast(Of ModelChoice)().FirstOrDefault(Function(c) c.Size = _settings.TranslateModelSize), cmbModel.Items(0))
        _loadingUi = False
    End Sub

    Private Function Downloaded(size As String) As String
        Return If(File.Exists(TranslateModelComponent.ModelPath(size)), "  ✓ downloaded", "")
    End Function

    ' ----- Target languages (multi-select) -----
    Private ReadOnly _targetList As New CheckedListBox()
    Private ReadOnly _targetDrop As New ToolStripDropDown()

    Private Sub BuildTargetPicker()
        _targetList.CheckOnClick = True
        _targetList.BorderStyle = BorderStyle.None
        _targetList.BackColor = Theme.Current.Control
        _targetList.ForeColor = Theme.Current.Text
        _targetList.Size = New Size(240, 320)
        For Each l In Languages.All
            _targetList.Items.Add(l, _settings.TargetLanguages.Contains(l.Code))
        Next
        AddHandler _targetList.ItemCheck, Sub(s, e) BeginInvoke(Sub() TargetsChanged())
        Dim host As New ToolStripControlHost(_targetList) With {.Margin = Padding.Empty, .Padding = Padding.Empty, .AutoSize = False, .Size = _targetList.Size}
        _targetDrop.Items.Add(host)
        _targetDrop.Padding = Padding.Empty
    End Sub

    Private Sub btnTargets_Click(sender As Object, e As EventArgs) Handles btnTargets.Click
        _targetDrop.Show(btnTargets, New Point(0, btnTargets.Height))
    End Sub

    Private Sub TargetsChanged()
        Dim codes = _targetList.CheckedItems.Cast(Of LanguageInfo)().Select(Function(l) l.Code).ToList()
        If codes.Count = 0 Then
            ' At least one language is required – re-check the previous one.
            Dim keep = Languages.Find(_settings.TargetLanguages.FirstOrDefault())
            If keep IsNot Nothing Then _targetList.SetItemChecked(_targetList.Items.IndexOf(keep), True)
            Return
        End If
        _settings.TargetLanguages = codes
        _settings.Save()
        UpdateTargetsText()
        UpdateUiState()
    End Sub

    Private Sub UpdateTargetsText()
        Dim names = _settings.Targets().Select(Function(l) l.Name).ToList()
        btnTargets.Text = If(names.Count <= 3, String.Join(", ", names), $"{names.Count} languages") & "  ▾"
        If _settings.TargetLanguages.Count = 1 Then
            chkReplaceOriginal.Text = $"Replace original subtitle file (no .{_settings.TargetLanguages(0)}.srt copy)"
        Else
            chkReplaceOriginal.Text = "Replace original subtitle file (only with one language)"
        End If
    End Sub

    ' =====================================================================
    '  Settings from UI
    ' =====================================================================
    Private Sub cmbSubSource_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbSubSource.SelectedIndexChanged
        If _loadingUi OrElse _settings Is Nothing OrElse cmbSubSource.SelectedIndex < 0 Then Return
        _settings.SubtitleSource = {"auto", "embedded", "audio"}(cmbSubSource.SelectedIndex)
        _settings.Save()
    End Sub

    Private Sub cmbSource_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbSource.SelectedIndexChanged
        If _loadingUi OrElse _settings Is Nothing Then Return
        Dim l = TryCast(cmbSource.SelectedItem, LanguageInfo)
        _settings.TranscribeLanguage = If(l IsNot Nothing, l.Code, "auto")
        _settings.Save()
    End Sub

    Private Sub cmbModel_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbModel.SelectedIndexChanged
        If _loadingUi OrElse _settings Is Nothing Then Return
        Dim c = TryCast(cmbModel.SelectedItem, ModelChoice)
        If c Is Nothing Then Return
        _settings.TranslateModelSize = c.Size
        _settings.Save()
        If Not New TranslateModelComponent(_settings).IsInstalled() Then
            lblStatus.Text = $"TranslateGemma {_settings.EffectiveModelSize().ToUpperInvariant()} is not downloaded yet – it will be offered when you press Start."
        End If
        If c.Size = "27b" AndAlso HardwareInfo.Gpu.VramGB < HardwareInfo.VramFor27B OrElse
           c.Size = "12b" AndAlso HardwareInfo.Gpu.VramGB < HardwareInfo.VramFor12B Then
            lblStatus.Text = "Note: this model is larger than your GPU memory – it will work, but slower (partly on the CPU)."
        End If
    End Sub

    Private Sub Options_CheckedChanged(sender As Object, e As EventArgs) Handles chkReplaceOriginal.CheckedChanged, chkOverwrite.CheckedChanged
        If _loadingUi OrElse _settings Is Nothing Then Return
        _settings.ReplaceOriginal = chkReplaceOriginal.Checked
        _settings.OverwriteExisting = chkOverwrite.Checked
        _settings.Save()
    End Sub

    ' =====================================================================
    '  Components & updates
    ' =====================================================================
    Private Sub btnComponents_Click(sender As Object, e As EventArgs) Handles btnComponents.Click
        ShowComponents(autoInstall:=False)
    End Sub

    Private Sub ShowComponents(autoInstall As Boolean)
        DisposeTranslator() ' engine files may be replaced
        Using f As New ComponentsForm(_settings, autoInstall)
            f.ShowDialog(Me)
        End Using
        PopulateModelCombo()
        ApplyTheme()
        lblStatus.Text = If(DependencyManager.MissingRequired(_settings).Count = 0, "Ready", "Some components are missing – click Components.")
    End Sub

    ''' <summary>Checks GitHub; downloads a newer version in the background. It is installed when the app closes (or on "Restart now").</summary>
    Private Async Function AutoUpdateAppAsync(interactive As Boolean) As Task(Of Boolean)
        If SelfUpdater.PendingExe IsNot Nothing Then
            ShowUpdateBanner()
            Return True
        End If
        Try
            Using cts As New CancellationTokenSource(TimeSpan.FromMinutes(10))
                Dim rel = Await SelfUpdater.CheckAsync(cts.Token)
                If rel Is Nothing Then Return False
                Logger.Log($"New version {rel.Tag} found, downloading…")
                Dim p As New Progress(Of DownloadProgress)(Sub(d) If interactive Then lblStatus.Text = $"Downloading update {rel.Tag}… {d.Percent}%")
                Await SelfUpdater.DownloadAsync(rel, p, cts.Token)
                ShowUpdateBanner()
                Return True
            End Using
        Catch ex As Exception
            Logger.Log("App update check failed: " & ex.Message)
            If interactive Then MessageBox.Show(Me, "Could not check for updates: " & ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End Try
    End Function

    Private Sub ShowUpdateBanner()
        lblUpdate.Text = $"Subtitle Machine {SelfUpdater.PendingTag} has been downloaded. It will be installed when you close the app."
        pnlUpdate.Visible = True
    End Sub

    Private Sub btnRestart_Click(sender As Object, e As EventArgs) Handles btnRestart.Click
        If _cts IsNot Nothing AndAlso MessageBox.Show(Me, "Processing is running. Stop it and restart?", "Restart", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        _cts?.Cancel()
        _restartAfterUpdate = True
        Close()
    End Sub
    Private _restartAfterUpdate As Boolean

    Private Async Function CheckComponentUpdatesAsync() As Task
        Try
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(30))
                Dim outdated As New List(Of String)
                For Each c In DependencyManager.GetComponents(_settings).Where(Function(x) x.IsInstalled())
                    Try
                        If c.NeedsInstallOrUpdate(Await c.GetLatestAsync(cts.Token)) Then outdated.Add(c.DisplayName.Split("("c)(0).Trim())
                    Catch
                    End Try
                Next
                If outdated.Count > 0 AndAlso _cts Is Nothing Then lblStatus.Text = $"Updates available: {String.Join(", ", outdated)} – click Components."
            End Using
        Catch ex As Exception
            Logger.Log("Component update check failed: " & ex.Message)
        End Try
    End Function

    Private Async Sub btnUpdate_Click(sender As Object, e As EventArgs) Handles btnUpdate.Click
        btnUpdate.Enabled = False
        lblStatus.Text = "Checking for a new version…"
        Try
            If Await AutoUpdateAppAsync(interactive:=True) Then
                lblStatus.Text = "Update ready."
                If MessageBox.Show(Me, $"Version {SelfUpdater.PendingTag} is ready. Restart now to install it?", "Update",
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then btnRestart.PerformClick()
            Else
                lblStatus.Text = "Subtitle Machine is up to date."
                ShowComponents(autoInstall:=False) ' also check FFmpeg, Whisper, llama.cpp and the models
            End If
        Finally
            btnUpdate.Enabled = _cts Is Nothing
        End Try
    End Sub

    ' =====================================================================
    '  Task list: add / remove / drag & drop
    ' =====================================================================
    Private Sub btnAddFiles_Click(sender As Object, e As EventArgs) Handles btnAddFiles.Click
        Dim exts = String.Join(";", SubtitlePipeline.VideoExtensions.Concat(SubtitlePipeline.AudioExtensions).Concat({".srt"}).Select(Function(x) "*" & x))
        Using ofd As New OpenFileDialog With {.Filter = $"Video, audio & subtitles|{exts}|All files|*.*", .Multiselect = True}
            If ofd.ShowDialog(Me) = DialogResult.OK Then AddPaths(ofd.FileNames)
        End Using
    End Sub

    Private Sub btnAddFolder_Click(sender As Object, e As EventArgs) Handles btnAddFolder.Click
        Using fbd As New FolderBrowserDialog With {.Description = "Choose a folder – all videos, audio and .srt files inside (and in subfolders) will be added."}
            If fbd.ShowDialog(Me) = DialogResult.OK Then AddPaths({fbd.SelectedPath})
        End Using
    End Sub

    Private Sub Files_DragEnter(sender As Object, e As DragEventArgs) Handles MyBase.DragEnter, lvTasks.DragEnter, lblDropHint.DragEnter
        If _cts Is Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
            lvTasks.BackColor = Theme.Current.Selected ' highlight drop zone
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub

    Private Sub Files_DragLeave(sender As Object, e As EventArgs) Handles MyBase.DragLeave, lvTasks.DragLeave, lblDropHint.DragLeave
        lvTasks.BackColor = Theme.Current.Back
    End Sub

    Private Sub Files_DragDrop(sender As Object, e As DragEventArgs) Handles MyBase.DragDrop, lvTasks.DragDrop, lblDropHint.DragDrop
        lvTasks.BackColor = Theme.Current.Back
        Dim paths = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If paths IsNot Nothing Then AddPaths(paths)
    End Sub

    Private Sub AddPaths(paths As IEnumerable(Of String))
        Dim added = 0
        lvTasks.BeginUpdate()
        For Each p In paths
            If Directory.Exists(p) Then
                Try
                    For Each f In Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories).Where(AddressOf SubtitlePipeline.IsSupportedFile).OrderBy(Function(x) x)
                        If AddTask(f) Then added += 1
                    Next
                Catch ex As Exception
                    Logger.Log($"Cannot read folder {p}: {ex.Message}")
                End Try
            ElseIf File.Exists(p) AndAlso SubtitlePipeline.IsSupportedFile(p) Then
                If AddTask(p) Then added += 1
            End If
        Next
        lvTasks.EndUpdate()
        lblStatus.Text = If(added > 0, $"{added} file(s) added – {Tasks.Count} in the list.", "No supported files found.")
        UpdateUiState()
    End Sub

    Private Function AddTask(filePath As String) As Boolean
        If Tasks.Any(Function(t) String.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) Then Return False
        Dim ext = Path.GetExtension(filePath).ToLowerInvariant()
        Dim item As New TaskItem With {.FilePath = filePath, .IsVideo = ext <> ".srt"}
        Tasks.Add(item)
        Dim kind = If(ext = ".srt", "Subtitle", If(SubtitlePipeline.AudioExtensions.Contains(ext), "Audio", "Video"))
        Dim lvi As New ListViewItem(Path.GetFileName(filePath)) With {.Tag = item, .ToolTipText = filePath}
        lvi.SubItems.Add(kind)
        lvi.SubItems.Add(item.Status)
        lvi.SubItems.Add("")
        lvTasks.Items.Add(lvi)
        Return True
    End Function

    Private Sub RefreshTaskRow(item As TaskItem)
        For Each lvi As ListViewItem In lvTasks.Items
            If lvi.Tag Is item Then
                lvi.SubItems(2).Text = item.Status
                lvi.SubItems(3).Text = String.Join(", ", item.Outputs.Select(Function(o) Path.GetFileName(o)))
                Exit For
            End If
        Next
    End Sub

    Private Sub btnRemove_Click(sender As Object, e As EventArgs) Handles btnRemove.Click, mnuRemove.Click
        If _cts IsNot Nothing Then Return
        For Each lvi In lvTasks.SelectedItems.Cast(Of ListViewItem)().ToList()
            Tasks.Remove(DirectCast(lvi.Tag, TaskItem))
            lvTasks.Items.Remove(lvi)
        Next
        UpdateUiState()
    End Sub

    Private Sub btnClear_Click(sender As Object, e As EventArgs) Handles btnClear.Click
        If _cts IsNot Nothing Then Return
        Tasks.Clear()
        lvTasks.Items.Clear()
        lblStatus.Text = "Ready"
        progressBar.Value = 0
        UpdateUiState()
    End Sub

    Private Sub lvTasks_DoubleClick(sender As Object, e As EventArgs) Handles lvTasks.DoubleClick
        If lvTasks.SelectedItems.Count = 0 Then Return
        If SubtitleEditorForm.CanEdit(DirectCast(lvTasks.SelectedItems(0).Tag, TaskItem)) Then mnuEdit.PerformClick() Else mnuOpenFolder.PerformClick()
    End Sub

    Private Sub ctxTasks_Opening(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles ctxTasks.Opening
        Dim item = If(lvTasks.SelectedItems.Count > 0, DirectCast(lvTasks.SelectedItems(0).Tag, TaskItem), Nothing)
        mnuEdit.Enabled = _cts Is Nothing AndAlso SubtitleEditorForm.CanEdit(item)
        mnuOpenFolder.Enabled = item IsNot Nothing
        mnuRemove.Enabled = _cts Is Nothing AndAlso item IsNot Nothing
    End Sub

    Private Sub mnuEdit_Click(sender As Object, e As EventArgs) Handles mnuEdit.Click
        If lvTasks.SelectedItems.Count = 0 OrElse _cts IsNot Nothing Then Return
        Dim item = DirectCast(lvTasks.SelectedItems(0).Tag, TaskItem)
        If Not SubtitleEditorForm.CanEdit(item) Then Return
        Using f As New SubtitleEditorForm(_settings, item)
            f.Icon = Icon
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Sub mnuOpenFolder_Click(sender As Object, e As EventArgs) Handles mnuOpenFolder.Click
        If lvTasks.SelectedItems.Count = 0 Then Return
        Dim item = DirectCast(lvTasks.SelectedItems(0).Tag, TaskItem)
        Dim target = If(item.OutputPath IsNot Nothing AndAlso File.Exists(item.OutputPath), item.OutputPath, item.FilePath)
        Process.Start("explorer.exe", $"/select,""{target}""")
    End Sub

    Private Sub lvTasks_KeyDown(sender As Object, e As KeyEventArgs) Handles lvTasks.KeyDown
        If e.KeyCode = Keys.Delete Then btnRemove.PerformClick()
        If e.Control AndAlso e.KeyCode = Keys.A Then
            For Each lvi As ListViewItem In lvTasks.Items : lvi.Selected = True : Next
        End If
    End Sub

    Private Sub UpdateUiState()
        Dim busy = _cts IsNot Nothing
        btnStart.Enabled = Not busy AndAlso Tasks.Count > 0
        btnStop.Enabled = busy
        For Each c As Control In New Control() {btnAddFiles, btnAddFolder, btnRemove, btnClear, btnComponents, btnUpdate, cmbSource, btnTargets, cmbModel, cmbSubSource, chkOverwrite, cmbVideoOut}
            c.Enabled = Not busy
        Next
        chkReplaceOriginal.Enabled = Not busy AndAlso _settings IsNot Nothing AndAlso _settings.TargetLanguages.Count = 1
        lblDropHint.Visible = Tasks.Count = 0
        If lblDropHint.Visible Then lblDropHint.BringToFront()
    End Sub

    ' ----- Dark list rendering -----
    Private Sub pnlList_Resize(sender As Object, e As EventArgs) Handles pnlList.Resize, MyBase.Shown
        lblDropHint.Left = (pnlList.ClientSize.Width - lblDropHint.Width) \ 2
        lblDropHint.Top = Math.Max(30, (pnlList.ClientSize.Height - lblDropHint.Height) \ 2)
        Dim fixedCols = colType.Width + colStatus.Width + colOutput.Width + SystemInformation.VerticalScrollBarWidth + 4
        colFile.Width = Math.Max(200, lvTasks.ClientSize.Width - fixedCols)
    End Sub

    Private Sub lvTasks_DrawColumnHeader(sender As Object, e As DrawListViewColumnHeaderEventArgs) Handles lvTasks.DrawColumnHeader
        Dim pal = Theme.Current
        Using b As New SolidBrush(pal.Panel)
            e.Graphics.FillRectangle(b, e.Bounds)
        End Using
        Dim r = e.Bounds
        r.Inflate(-6, 0)
        TextRenderer.DrawText(e.Graphics, e.Header.Text, lvTasks.Font, r, pal.Muted, TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis)
        Using p As New Pen(pal.Border)
            e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4)
        End Using
    End Sub

    Private Sub lvTasks_DrawItem(sender As Object, e As DrawListViewItemEventArgs) Handles lvTasks.DrawItem
        ' drawn per sub-item
    End Sub

    Private Sub lvTasks_DrawSubItem(sender As Object, e As DrawListViewSubItemEventArgs) Handles lvTasks.DrawSubItem
        Dim pal = Theme.Current
        Dim bg = If(e.Item.Selected, pal.Selected, If(e.ItemIndex Mod 2 = 0, pal.Back, pal.AltRow))
        Using b As New SolidBrush(bg)
            e.Graphics.FillRectangle(b, e.Bounds)
        End Using
        Dim fg = pal.Text
        If e.ColumnIndex = 2 Then
            Dim s = e.SubItem.Text
            If s.StartsWith("Completed") OrElse s.StartsWith("Done") Then
                fg = pal.Ok
            ElseIf s.StartsWith("Failed") Then
                fg = pal.Err
            ElseIf s.StartsWith("Skipped") OrElse s.StartsWith("Cancelled") Then
                fg = pal.Warn
            ElseIf s <> "Pending" Then
                fg = pal.Busy
            End If
        ElseIf e.ColumnIndex = 1 OrElse e.ColumnIndex = 3 Then
            fg = pal.Muted
        End If
        Dim r = e.Bounds
        r.Inflate(-6, 0)
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvTasks.Font, r, fg, TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
    End Sub

    ' =====================================================================
    '  Processing
    ' =====================================================================
    Private Sub DisposeTranslator()
        _translator?.Dispose()
        _translator = Nothing
    End Sub

    Private Async Sub btnStart_Click(sender As Object, e As EventArgs) Handles btnStart.Click
        If Tasks.Count = 0 OrElse _cts IsNot Nothing Then Return

        Dim missing = DependencyManager.MissingRequired(_settings)
        If missing.Count > 0 Then
            If MessageBox.Show(Me, "Missing components:" & vbCrLf & String.Join(vbCrLf, missing.Select(Function(c) "  • " & c.DisplayName)) &
                               vbCrLf & vbCrLf & "Download them now?", "Subtitle Machine", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
            ShowComponents(autoInstall:=True)
            If DependencyManager.MissingRequired(_settings).Count > 0 Then Return
        End If

        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        UpdateUiState()
        progressBar.Value = 0
        Dim failed = 0
        Dim runWhenDone = False
        Dim pending = Tasks.Where(Function(t) t.Status = "Pending" OrElse t.Status.StartsWith("Failed") OrElse t.Status = "Cancelled").ToList()
        If pending.Count = 0 Then
            ' Everything was processed already – run again (e.g. with another target language).
            For Each t In Tasks : t.Status = "Pending" : t.OutputPath = Nothing : t.Outputs.Clear() : RefreshTaskRow(t) : Next
            pending = Tasks.ToList()
        End If

        Try
            _translator = New LlamaTranslator(_settings)
            Dim prep As New Progress(Of StatusUpdate)(Sub(s)
                                                          lblStatus.Text = s.Text
                                                          progressBar.Style = If(s.Percent >= 0, ProgressBarStyle.Continuous, ProgressBarStyle.Marquee)
                                                          If s.Percent >= 0 Then progressBar.Value = Math.Min(100, s.Percent)
                                                      End Sub)
            Await _translator.PrepareAsync(prep, ct) ' loads the AI model
            progressBar.Style = ProgressBarStyle.Continuous

            Dim pipeline As New SubtitlePipeline(_settings, _translator)
            For i = 0 To pending.Count - 1
                ct.ThrowIfCancellationRequested()
                Dim item = pending(i)
                Dim index = i
                item.Status = "Processing…"
                RefreshTaskRow(item)
                Dim lastUi As Long = 0
                Dim report As Action(Of String, Double) =
                    Sub(text, fraction)
                        Dim now = Environment.TickCount64
                        If now - Interlocked.Read(lastUi) < 100 AndAlso fraction < 1 Then Return ' throttle UI updates
                        Interlocked.Exchange(lastUi, now)
                        Dim overall = CInt((index + Math.Min(1.0, fraction)) * 100 / pending.Count)
                        BeginInvoke(Sub()
                                        lblStatus.Text = $"[{index + 1}/{pending.Count}] {text}"
                                        progressBar.Value = Math.Min(100, Math.Max(0, overall))
                                    End Sub)
                    End Sub
                Try
                    Await Task.Run(Function() pipeline.ProcessAsync(item, report, ct), ct)
                Catch ex As OperationCanceledException
                    item.Status = "Cancelled"
                    RefreshTaskRow(item)
                    Throw
                Catch ex As Exception
                    failed += 1
                    item.Status = "Failed: " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    Logger.Log($"FAILED {item.FilePath}: {ex}")
                End Try
                RefreshTaskRow(item)
            Next
            progressBar.Value = 100
            lblStatus.Text = If(failed = 0, $"All done – {pending.Count} file(s) processed.", $"Finished – {failed} of {pending.Count} file(s) failed (see the list or the log).")
            NotifyFinished(pending.Count, failed)
            runWhenDone = True
        Catch ex As OperationCanceledException
            lblStatus.Text = "Cancelled."
        Catch ex As Exception
            Logger.Log("ERROR: " & ex.ToString())
            lblStatus.Text = "Error – " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            MessageBox.Show(Me, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            progressBar.Style = ProgressBarStyle.Continuous
            _cts.Dispose()
            _cts = Nothing
            DisposeTranslator() ' frees the GPU memory used by the AI model
            UpdateUiState()
        End Try
        If runWhenDone Then RunWhenDoneAction()
    End Sub

    ' =====================================================================
    '  Finished: notification and sleep / hibernate / shut down
    ' =====================================================================
    Private Sub NotifyFinished(total As Integer, failed As Integer)
        If Not _settings.NotifyWhenDone Then Return
        System.Media.SystemSounds.Asterisk.Play()
        ' Only pop up a Windows notification when the user is looking at another window.
        If Form.ActiveForm Is Me AndAlso WindowState <> FormWindowState.Minimized Then Return
        Try
            _notify.Visible = True
            Dim text = If(failed = 0, $"{total} file(s) processed successfully.", $"{total - failed} of {total} file(s) done, {failed} failed.")
            _notify.ShowBalloonTip(8000, "Subtitle Machine – finished", text, If(failed = 0, ToolTipIcon.Info, ToolTipIcon.Warning))
        Catch ex As Exception
            Logger.Log("Notification failed: " & ex.Message)
        End Try
    End Sub

    Private Sub Notify_Click(sender As Object, e As EventArgs) Handles _notify.BalloonTipClicked, _notify.MouseClick
        If WindowState = FormWindowState.Minimized Then WindowState = FormWindowState.Normal
        Activate()
        _notify.Visible = False
    End Sub

    Private Sub RunWhenDoneAction()
        Dim action = {"nothing", "sleep", "hibernate", "shutdown"}(Math.Max(0, cmbWhenDone.SelectedIndex))
        If action = "nothing" Then Return
        Dim verb = {"", "go to sleep", "hibernate", "shut down"}(cmbWhenDone.SelectedIndex)
        If Not WhenDoneCountdown.Confirm(Me, $"The PC will {verb} in", 60) Then
            lblStatus.Text = "Cancelled – the PC stays on."
            Return
        End If
        Logger.Log("When finished: " & action)
        _settings.Save()
        Select Case action
            Case "sleep" : Application.SetSuspendState(PowerState.Suspend, False, False)
            Case "hibernate" : Application.SetSuspendState(PowerState.Hibernate, False, False)
            Case "shutdown"
                Process.Start(New ProcessStartInfo("shutdown.exe", "/s /t 0") With {.CreateNoWindow = True, .UseShellExecute = False})
        End Select
        ' "When finished" is for this batch only – go back to "Do nothing".
        cmbWhenDone.SelectedIndex = 0
    End Sub

    Private Sub ApplyTheme()
        Theme.SetMode(_settings.Theme)
        Theme.Apply(Me)
        _targetList.BackColor = Theme.Current.Control
        _targetList.ForeColor = Theme.Current.Text
        lvTasks.Invalidate()
    End Sub

    Private Sub cmbVideoOut_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbVideoOut.SelectedIndexChanged
        If _loadingUi OrElse _settings Is Nothing OrElse cmbVideoOut.SelectedIndex < 0 Then Return
        _settings.VideoOutput = {"none", "mkv", "burn", "both"}(cmbVideoOut.SelectedIndex)
        _settings.Save()
    End Sub

    Private Sub btnStop_Click(sender As Object, e As EventArgs) Handles btnStop.Click
        If _cts Is Nothing Then Return
        lblStatus.Text = "Cancelling…"
        btnStop.Enabled = False
        _cts.Cancel()
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        _cts?.Cancel()
        DisposeTranslator()
        LlamaServer.StopAll()
        _settings?.Save()
        _notify.Visible = False
        _notify.Dispose()
    End Sub

    Private Sub Form1_FormClosed(sender As Object, e As FormClosedEventArgs) Handles MyBase.FormClosed
        ' Install a downloaded update (and restart if the user asked for it).
        Try
            SelfUpdater.ApplyPending(_restartAfterUpdate)
        Catch ex As Exception
            Logger.Log("Applying update failed: " & ex.Message)
        End Try
    End Sub

End Class


''' <summary>"The PC will shut down in 60 s" – with a Cancel button.</summary>
Friend Class WhenDoneCountdown
    Inherits Form

    Private ReadOnly _timer As New System.Windows.Forms.Timer With {.Interval = 1000}
    Private ReadOnly _label As New Label()
    Private _left As Integer
    Private ReadOnly _message As String

    Private Sub New(message As String, seconds As Integer)
        _message = message
        _left = seconds
        Text = "Subtitle Machine"
        Font = New Font("Segoe UI", 10.0F)
        ClientSize = New Size(380, 130)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False : MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent
        TopMost = True
        _label.SetBounds(16, 16, 348, 50)
        _label.TextAlign = ContentAlignment.MiddleCenter
        Dim btnNow As New Button With {.Text = "Do it now", .DialogResult = DialogResult.OK, .Tag = "accent"}
        btnNow.SetBounds(90, 80, 100, 32)
        Dim btnCancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel}
        btnCancel.SetBounds(196, 80, 100, 32)
        Controls.AddRange(New Control() {_label, btnNow, btnCancel})
        CancelButton = btnCancel
        AddHandler _timer.Tick, Sub()
                                    _left -= 1
                                    UpdateText()
                                    If _left <= 0 Then DialogResult = DialogResult.OK
                                End Sub
        UpdateText()
        Theme.Apply(Me)
        _timer.Start()
    End Sub

    Private Sub UpdateText()
        _label.Text = $"{_message} {_left} seconds."
    End Sub

    Public Shared Function Confirm(owner As IWin32Window, message As String, seconds As Integer) As Boolean
        Using f As New WhenDoneCountdown(message, seconds)
            Dim r = f.ShowDialog(owner)
            f._timer.Stop()
            Return r = DialogResult.OK
        End Using
    End Function
End Class
