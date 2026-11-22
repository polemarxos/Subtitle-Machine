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
        ' The interface language must be known before any window is built.
        _settings = AppSettings.Load()
        Loc.Init(_settings.UiLanguage)
        InitializeComponent()
        Localizer.Apply(Me)
        FitButtonsToText()
    End Sub

    ''' <summary>Translated captions can be longer (Greek) – widen the toolbar/header buttons and line them up again.</summary>
    Private Sub FitButtonsToText()
        Dim fit = Sub(b As Button)
                      Dim need = TextRenderer.MeasureText(b.Text, b.Font).Width + 28
                      If need > b.Width Then b.Width = need
                  End Sub
        Dim x = btnAddFiles.Left
        For Each b In New Button() {btnAddFiles, btnAddFolder, btnRemove, btnClear, btnCompare}
            fit(b)
            b.Left = x
            x = b.Right + 6
        Next
        Dim right = pnlHeader.ClientSize.Width - 12
        For Each b In New Button() {btnUpdate, btnComponents}
            fit(b)
            b.Left = right - b.Width
            right = b.Left - 6
        Next
    End Sub

    ' =====================================================================
    '  Startup
    ' =====================================================================
    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Dim ver = SelfUpdater.CurrentVersion().ToString(3)
        Text = $"Subtitle Machine {ver}"
        lblVersion.Text = "v" & ver
        Logger.Log($"=== Subtitle Machine {ver} started. Data folder: {AppPaths.DataDir} ===")
        AppPaths.MigrateLegacyLayout()
        SelfUpdater.CleanupLeftovers()
        SubtitlePipeline.OcrFactory = AddressOf WindowsOcr.Create

        _loadingUi = True
        cmbMode.Items.AddRange(New Object() {Loc.Tr("Translate subtitles"), Loc.Tr("Only sync subtitle timing to the video (no translation)")})
        cmbMode.SelectedIndex = If(_settings.JobMode = "sync", 1, 0)
        cmbSource.Items.Add(Loc.Tr("Auto-detect"))
        For Each l In Languages.All
            cmbSource.Items.Add(l)
        Next
        BuildTargetPicker()
        cmbSubSource.Items.AddRange(New Object() {Loc.Tr("Embedded subtitles, otherwise audio"), Loc.Tr("Embedded subtitles only"), Loc.Tr("Audio only (Whisper speech-to-text)")})
        cmbSubSource.SelectedIndex = Array.IndexOf({"auto", "embedded", "audio"}, _settings.SubtitleSource)
        Dim src = Languages.Find(_settings.TranscribeLanguage)
        If src IsNot Nothing Then cmbSource.SelectedItem = src Else cmbSource.SelectedIndex = 0
        chkReplaceOriginal.Checked = _settings.ReplaceOriginal
        chkOverwrite.Checked = _settings.OverwriteExisting
        lblGpu.Text = Loc.Tr("GPU: ") & HardwareInfo.Gpu.ToString()
        _loadingUi = False
        PopulateModelCombo()
        UpdateTargetsText()
        UpdateUiState()
        toolTip.SetToolTip(chkOverwrite,
            Loc.Tr("Example: you already made Movie.el.srt yesterday and add Movie.mkv to the list again.") & vbCrLf & vbCrLf &
            Loc.Tr("Unticked (normal): Movie.el.srt is left as it is and the file is skipped – fast, nothing is lost.") & vbCrLf &
            Loc.Tr("Ticked: everything is made again from the start and the old files are replaced.") & vbCrLf & vbCrLf &
            Loc.Tr("Tick it after changing the AI model, the glossary or the Whisper model, or when a result was bad."))
        toolTip.SetToolTip(cmbModel, Loc.Tr("Google TranslateGemma, running locally with llama.cpp.") & vbCrLf & Loc.Tr("Automatic chooses the size that fits your graphics card.") & vbCrLf & Loc.Tr("✓ = already downloaded"))
        toolTip.SetToolTip(cmbSubSource, Loc.Tr("Embedded: use subtitles inside the video (text, or image subtitles read with OCR).") & vbCrLf &
                                         Loc.Tr("Audio: always create the subtitles from the speech with Whisper."))
        toolTip.SetToolTip(cmbMode, Loc.Tr("Only sync: add subtitle files (or the videos next to them) – their timing is matched to the speech of the video. Nothing is translated.") & vbCrLf &
                                    Loc.Tr("With 'Replace original subtitle file' ticked the file itself is corrected, otherwise a copy Movie.en.synced.srt is written."))
        toolTip.SetToolTip(btnTargets, Loc.Tr("Choose one or more languages – one subtitle file is created per language."))
        toolTip.SetToolTip(cmbVideoOut, Loc.Tr("MKV: the new subtitles are added to a copy of the video as extra tracks (fast, no quality loss).") & vbCrLf &
                                        Loc.Tr("Burned-in: an MP4 copy with the subtitles drawn on the picture (first language; takes longer)."))

        _loadingUi = True
        cmbVideoOut.Items.AddRange(New Object() {Loc.Tr("Subtitle files only"), Loc.Tr("Also add subtitles into the video (MKV)"), Loc.Tr("Also burn subtitles into the picture (MP4)"), Loc.Tr("Both MKV and burned-in MP4")})
        cmbVideoOut.SelectedIndex = Array.IndexOf({"none", "mkv", "burn", "both"}, _settings.VideoOutput)
        cmbWhenDone.Items.AddRange(New Object() {Loc.Tr("Do nothing"), Loc.Tr("Sleep"), Loc.Tr("Hibernate"), Loc.Tr("Shut down")})
        cmbWhenDone.SelectedIndex = 0
        _loadingUi = False

        ' Theme
        btnAddFiles.Tag = "accent" : btnStart.Tag = "accent" : btnStop.Tag = "danger"
        lblGpu.Tag = "muted" : lblVersion.Tag = "muted" : lblDropHint.Tag = "muted"
        pnlHeader.Tag = "panel" : pnlFooter.Tag = "panel" : pnlUpdate.Tag = "keep"
        ApplyTheme()

        _notify.Icon = Icon
        _notify.Text = Loc.Tr("Subtitle Machine")

    End Sub

    ''' <summary>Runs after the main window is visible (so setup dialogs have a window and a taskbar button behind them).</summary>
    Private Async Sub Form1_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        ' Missing tools → offer to download everything.
        Dim missing = DependencyManager.MissingRequired(_settings)
        If missing.Count > 0 Then
            If MessageBox.Show(Me, Loc.Tr("Subtitle Machine needs these components. They will be downloaded from their official sites:") & vbCrLf & vbCrLf &
                               String.Join(vbCrLf, missing.Select(Function(c) "  • " & c.DisplayName)) & vbCrLf & vbCrLf & Loc.Tr("Download them now?"),
                               Loc.Tr("Subtitle Machine – setup"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
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
        cmbModel.Items.Add(New ModelChoice With {.Size = "auto", .Caption = Loc.Tr("Automatic – TranslateGemma {0} – {1}, {2}{3}", rec.ToUpperInvariant(), recInfo.Download, recInfo.Vram, Downloaded(rec))})
        For Each m In TranslateModelComponent.Sizes
            cmbModel.Items.Add(New ModelChoice With {.Size = m.Tag, .Caption = Loc.Tr("TranslateGemma {0} – {1}, {2}{3}", m.Tag.ToUpperInvariant(), m.Download, m.Vram, Downloaded(m.Tag))})
        Next
        cmbModel.SelectedItem = If(cmbModel.Items.Cast(Of ModelChoice)().FirstOrDefault(Function(c) c.Size = _settings.TranslateModelSize), cmbModel.Items(0))
        _loadingUi = False
    End Sub

    Private Function Downloaded(size As String) As String
        Return If(File.Exists(TranslateModelComponent.ModelPath(size)), "  ✓", "")
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
        Dim names = _settings.Targets().Select(Function(l) l.DisplayName).ToList()
        btnTargets.Text = If(names.Count <= 3, String.Join(", ", names), Loc.Tr("{0} languages", names.Count)) & "  ▾"
        chkReplaceOriginal.Text = Loc.Tr("Replace original subtitle file")
        toolTip.SetToolTip(chkReplaceOriginal, If(_settings.TargetLanguages.Count = 1,
            Loc.Tr("On: the translation is written into the original subtitle file (e.g. Movie.srt).") & vbCrLf & Loc.Tr("Off: a new file is created (e.g. Movie.{0}.srt) and the original is kept.", _settings.TargetLanguages(0)),
            Loc.Tr("Only possible with one target language – with several languages each one gets its own file (Movie.el.srt, Movie.en.srt…).")))
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
            lblStatus.Text = Loc.Tr("TranslateGemma {0} is not downloaded yet – it will be offered when you press Start.", _settings.EffectiveModelSize().ToUpperInvariant())
        End If
        If c.Size = "27b" AndAlso HardwareInfo.Gpu.VramGB < HardwareInfo.VramFor27B OrElse
           c.Size = "12b" AndAlso HardwareInfo.Gpu.VramGB < HardwareInfo.VramFor12B Then
            lblStatus.Text = Loc.Tr("Note: this model is larger than your GPU memory – it will work, but slower (partly on the CPU).")
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
        lblStatus.Text = If(DependencyManager.MissingRequired(_settings).Count = 0, Loc.Tr("Ready"), Loc.Tr("Some components are missing – click Components & settings."))
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
                Dim p As New Progress(Of DownloadProgress)(Sub(d) If interactive Then lblStatus.Text = Loc.Tr("Downloading update {0}… {1}%", rel.Tag, d.Percent))
                Await SelfUpdater.DownloadAsync(rel, p, cts.Token)
                ShowUpdateBanner()
                Return True
            End Using
        Catch ex As Exception
            Logger.Log("App update check failed: " & ex.Message)
            If interactive Then MessageBox.Show(Me, Loc.Tr("Could not check for updates: ") & ex.Message, Loc.Tr("Update"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End Try
    End Function

    Private Sub ShowUpdateBanner()
        lblUpdate.Text = Loc.Tr("Subtitle Machine {0} has been downloaded. It will be installed when you close the app.", SelfUpdater.PendingTag)
        pnlUpdate.Visible = True
    End Sub

    Private Sub btnRestart_Click(sender As Object, e As EventArgs) Handles btnRestart.Click
        If _cts IsNot Nothing AndAlso MessageBox.Show(Me, Loc.Tr("Processing is running. Stop it and restart?"), Loc.Tr("Restart"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
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
                If outdated.Count > 0 AndAlso _cts Is Nothing Then lblStatus.Text = Loc.Tr("Updates available: {0} – click Components & settings.", String.Join(", ", outdated))
            End Using
        Catch ex As Exception
            Logger.Log("Component update check failed: " & ex.Message)
        End Try
    End Function

    Private Async Sub btnUpdate_Click(sender As Object, e As EventArgs) Handles btnUpdate.Click
        btnUpdate.Enabled = False
        lblStatus.Text = Loc.Tr("Checking for a new version…")
        Try
            If Await AutoUpdateAppAsync(interactive:=True) Then
                lblStatus.Text = Loc.Tr("Update ready.")
                If MessageBox.Show(Me, Loc.Tr("Version {0} is ready. Restart now to install it?", SelfUpdater.PendingTag), Loc.Tr("Update"),
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then btnRestart.PerformClick()
            Else
                lblStatus.Text = Loc.Tr("Subtitle Machine is up to date.")
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
        Using ofd As New OpenFileDialog With {.Filter = Loc.Tr("Video, audio & subtitles") & $"|{exts}|" & Loc.Tr("All files") & "|*.*", .Multiselect = True}
            If ofd.ShowDialog(Me) = DialogResult.OK Then AddPaths(ofd.FileNames)
        End Using
    End Sub

    Private Sub btnAddFolder_Click(sender As Object, e As EventArgs) Handles btnAddFolder.Click
        Using fbd As New FolderBrowserDialog With {.Description = Loc.Tr("Choose a folder – all videos, audio and .srt files inside (and in subfolders) will be added.")}
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
        lblStatus.Text = If(added > 0, Loc.Tr("{0} file(s) added – {1} in the list.", added, Tasks.Count), Loc.Tr("No supported files found."))
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
        lblStatus.Text = Loc.Tr("Ready")
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
        mnuCompare.Enabled = item IsNot Nothing AndAlso SubtitleEditorForm.CanEdit(item) AndAlso item.SourcePath IsNot Nothing AndAlso File.Exists(item.SourcePath) AndAlso
                             Not item.Outputs.Any(Function(o) String.Equals(o, item.SourcePath, StringComparison.OrdinalIgnoreCase))
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

    Private Sub btnCompare_Click(sender As Object, e As EventArgs) Handles btnCompare.Click
        ' Pre-fill with the selected file's original and translation, if there is one.
        Dim item = If(lvTasks.SelectedItems.Count > 0, DirectCast(lvTasks.SelectedItems(0).Tag, TaskItem), Nothing)
        Dim a = If(item?.SourcePath, If(item IsNot Nothing AndAlso Path.GetExtension(item.FilePath).Equals(".srt", StringComparison.OrdinalIgnoreCase), item.FilePath, Nothing))
        Dim b = item?.Outputs.FirstOrDefault(Function(o) o.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) AndAlso Not String.Equals(o, a, StringComparison.OrdinalIgnoreCase))
        ShowCompare(a, b)
    End Sub

    Private Sub mnuCompare_Click(sender As Object, e As EventArgs) Handles mnuCompare.Click
        btnCompare.PerformClick()
    End Sub

    Private Sub ShowCompare(a As String, b As String)
        Dim f As New CompareForm(a, b) With {.Icon = Icon}
        f.Show(Me) ' not modal: several comparisons can stay open
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

    Private Function SyncOnlyMode() As Boolean
        Return _settings IsNot Nothing AndAlso _settings.JobMode = "sync"
    End Function

    Private Sub cmbMode_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbMode.SelectedIndexChanged
        If _loadingUi OrElse _settings Is Nothing Then Return
        _settings.JobMode = If(cmbMode.SelectedIndex = 1, "sync", "translate")
        _settings.Save()
        UpdateUiState()
    End Sub

    Private Sub UpdateUiState()
        Dim busy = _cts IsNot Nothing
        btnStart.Enabled = Not busy AndAlso Tasks.Count > 0
        btnStop.Enabled = busy
        For Each c As Control In New Control() {btnAddFiles, btnAddFolder, btnRemove, btnClear, btnComponents, btnUpdate, cmbSource, btnTargets, cmbModel, cmbSubSource, chkOverwrite, cmbVideoOut}
            c.Enabled = Not busy
        Next
        Dim syncOnly = SyncOnlyMode()
        cmbMode.Enabled = Not busy
        ' settings that only matter for translating
        For Each c As Control In New Control() {cmbSource, btnTargets, cmbModel, cmbSubSource, chkOverwrite, cmbVideoOut}
            c.Enabled = Not busy AndAlso Not syncOnly
        Next
        For Each c As Control In New Control() {lblSource, lblTarget, lblModel, lblSubSource, lblVideoOut}
            c.Enabled = Not syncOnly
        Next
        chkReplaceOriginal.Enabled = Not busy AndAlso _settings IsNot Nothing AndAlso (syncOnly OrElse _settings.TargetLanguages.Count = 1)
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
            Select Case DirectCast(e.Item.Tag, TaskItem).State
                Case TaskState.Done : fg = pal.Ok
                Case TaskState.Failed : fg = pal.Err
                Case TaskState.Cancelled : fg = pal.Warn
                Case TaskState.Processing : fg = pal.Busy
            End Select
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

        Dim syncOnly = SyncOnlyMode()
        Dim missing = DependencyManager.MissingRequired(_settings)
        If syncOnly Then missing = missing.Where(Function(c) TypeOf c Is FfmpegComponent).ToList() ' only FFmpeg is needed
        If missing.Count > 0 Then
            If MessageBox.Show(Me, Loc.Tr("Missing components:") & vbCrLf & String.Join(vbCrLf, missing.Select(Function(c) "  • " & c.DisplayName)) &
                               vbCrLf & vbCrLf & Loc.Tr("Download them now?"), "Subtitle Machine", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
            ShowComponents(autoInstall:=True)
            If DependencyManager.MissingRequired(_settings).Any(Function(c) Not syncOnly OrElse TypeOf c Is FfmpegComponent) Then Return
        End If

        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        UpdateUiState()
        progressBar.Value = 0
        Dim failed = 0
        Dim runWhenDone = False
        Dim pending = Tasks.Where(Function(t) t.State <> TaskState.Done).ToList()
        If pending.Count = 0 Then
            ' Everything was processed already – run again (e.g. with another target language).
            For Each t In Tasks
                t.State = TaskState.Pending : t.Status = Tr("Pending") : t.OutputPath = Nothing : t.Outputs.Clear()
                RefreshTaskRow(t)
            Next
            pending = Tasks.ToList()
        End If

        Try
            If Not syncOnly Then _translator = New LlamaTranslator(_settings)
            Dim prep As New Progress(Of StatusUpdate)(Sub(s)
                                                          lblStatus.Text = s.Text
                                                          progressBar.Style = If(s.Percent >= 0, ProgressBarStyle.Continuous, ProgressBarStyle.Marquee)
                                                          If s.Percent >= 0 Then progressBar.Value = Math.Min(100, s.Percent)
                                                      End Sub)
            If Not syncOnly Then Await _translator.PrepareAsync(prep, ct) ' loads the AI model
            progressBar.Style = ProgressBarStyle.Continuous

            Dim pipeline As New SubtitlePipeline(_settings, _translator)
            For i = 0 To pending.Count - 1
                ct.ThrowIfCancellationRequested()
                Dim item = pending(i)
                Dim index = i
                item.State = TaskState.Processing
                item.Status = Tr("Processing…")
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
                    If syncOnly Then
                        Await Task.Run(Function() pipeline.SyncOnlyAsync(item, report, ct), ct)
                    Else
                        Await Task.Run(Function() pipeline.ProcessAsync(item, report, ct), ct)
                    End If
                Catch ex As OperationCanceledException
                    item.State = TaskState.Cancelled
                    item.Status = Tr("Cancelled")
                    RefreshTaskRow(item)
                    Throw
                Catch ex As Exception
                    failed += 1
                    item.State = TaskState.Failed
                    item.Status = Tr("Failed:") & " " & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    Logger.Log($"FAILED {item.FilePath}: {ex}")
                End Try
                RefreshTaskRow(item)
            Next
            progressBar.Value = 100
            lblStatus.Text = If(failed = 0, Loc.Tr("All done – {0} file(s) processed.", pending.Count), Loc.Tr("Finished – {0} of {1} file(s) failed (see the list or the log).", failed, pending.Count))
            NotifyFinished(pending.Count, failed)
            runWhenDone = True
        Catch ex As OperationCanceledException
            lblStatus.Text = Loc.Tr("Cancelled.")
        Catch ex As Exception
            Logger.Log("ERROR: " & ex.ToString())
            lblStatus.Text = Loc.Tr("Error – ") & ex.Message.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            MessageBox.Show(Me, ex.Message, Loc.Tr("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error)
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
            Dim text = If(failed = 0, Loc.Tr("{0} file(s) processed successfully.", total), Loc.Tr("{0} of {1} file(s) done, {2} failed.", total - failed, total, failed))
            _notify.ShowBalloonTip(8000, Loc.Tr("Subtitle Machine – finished"), text, If(failed = 0, ToolTipIcon.Info, ToolTipIcon.Warning))
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
        Dim message = {"", Loc.Tr("The PC will go to sleep in {0} seconds."), Loc.Tr("The PC will hibernate in {0} seconds."), Loc.Tr("The PC will shut down in {0} seconds.")}(cmbWhenDone.SelectedIndex)
        If Not WhenDoneCountdown.Confirm(Me, message, 60) Then
            lblStatus.Text = Loc.Tr("Cancelled – the PC stays on.")
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
        lblStatus.Text = Loc.Tr("Cancelling…")
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
        Dim btnNow As New Button With {.Text = Loc.Tr("Do it now"), .DialogResult = DialogResult.OK, .Tag = "accent"}
        btnNow.SetBounds(90, 80, 100, 32)
        Dim btnCancel As New Button With {.Text = Loc.Tr("Cancel"), .DialogResult = DialogResult.Cancel}
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
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        _timer.Start()
    End Sub

    Private Sub UpdateText()
        _label.Text = String.Format(_message, _left)
    End Sub

    Public Shared Function Confirm(owner As IWin32Window, message As String, seconds As Integer) As Boolean
        Using f As New WhenDoneCountdown(message, seconds)
            Dim r = f.ShowDialog(owner)
            f._timer.Stop()
            Return r = DialogResult.OK
        End Using
    End Function
End Class
