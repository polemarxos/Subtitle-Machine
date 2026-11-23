Imports System.IO
Imports System.Threading

''' <summary>
''' Review and correct a translation: original and translated text side by side,
''' editable text and times, re-translate selected lines with the AI, find, spell check,
''' sync the timing to the video, save.
''' </summary>
Public Class SubtitleEditorForm
    Inherits Form

    Private ReadOnly _settings As AppSettings
    Private ReadOnly _item As TaskItem
    Private ReadOnly _files As List(Of String)
    Private _source As List(Of (Start As TimeSpan, [End] As TimeSpan, Text As String))
    Private _current As String
    Private _dirty As Boolean
    Private _loading As Boolean
    Private _translator As LlamaTranslator
    Private _cts As CancellationTokenSource

    Private ReadOnly cmbFile As New ComboBox()
    Private ReadOnly grid As New DataGridView()
    Private ReadOnly txtFind As New TextBox()
    Private ReadOnly btnFind As New Button()
    Private ReadOnly btnRetranslate As New Button()
    Private ReadOnly btnSave As New Button()
    Private ReadOnly btnClose As New Button()
    Private ReadOnly lblStatus As New Label()
    Private ReadOnly chkSpell As New CheckBox()
    Private ReadOnly btnNextMistake As New Button()
    Private ReadOnly btnSync As New Button()
    Private ReadOnly spellMenu As New ContextMenuStrip()
    Private _spell As SpellChecker
    Private _spellLoading As Boolean
    Private _speech As Byte()
    Private _speechMedia As String

    Public Sub New(settings As AppSettings, item As TaskItem)
        _settings = settings
        _item = item
        _files = item.Outputs.Where(Function(f) f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(f)).ToList()
        BuildUi()
    End Sub

    Public Shared Function CanEdit(item As TaskItem) As Boolean
        Return item IsNot Nothing AndAlso item.Outputs.Any(Function(f) f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(f))
    End Function

    Private Sub BuildUi()
        SuspendLayout()
        Text = Loc.Tr("Subtitle editor") & " – " & Path.GetFileName(_item.FilePath)
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1080, 640)
        MinimumSize = New Size(1000, 420)
        StartPosition = FormStartPosition.CenterParent
        KeyPreview = True

        Dim top As New Panel With {.Dock = DockStyle.Top, .Width = ClientSize.Width, .Height = 48, .Tag = "panel", .Padding = New Padding(10, 10, 10, 6)}
        Dim lblFile As New Label With {.Text = Loc.Tr("Translation:"), .AutoSize = True, .Location = New Point(12, 16)}
        cmbFile.SetBounds(100, 12, 280, 25)
        cmbFile.DropDownStyle = ComboBoxStyle.DropDownList
        cmbFile.FlatStyle = FlatStyle.Flat
        For Each f In _files : cmbFile.Items.Add(Path.GetFileName(f)) : Next
        txtFind.SetBounds(396, 13, 200, 25)
        txtFind.PlaceholderText = Loc.Tr("Find text…")
        btnFind.Text = Loc.Tr("Find next") : btnFind.SetBounds(602, 11, Math.Max(90, TextRenderer.MeasureText(btnFind.Text, Font).Width + 20), 28)
        chkSpell.Text = Loc.Tr("Spell check") : chkSpell.AutoSize = True : chkSpell.Checked = _settings.SpellCheck
        chkSpell.Location = New Point(btnFind.Right + 14, 16)
        btnNextMistake.Text = Loc.Tr("Next mistake (F7)")
        btnNextMistake.SetBounds(chkSpell.Left + chkSpell.PreferredSize.Width + 8, 11, Math.Max(130, TextRenderer.MeasureText(btnNextMistake.Text, Font).Width + 24), 28)
        top.Controls.AddRange(New Control() {lblFile, cmbFile, txtFind, btnFind, chkSpell, btnNextMistake})

        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Width = ClientSize.Width, .Height = 54, .Tag = "panel"}
        btnRetranslate.Text = Loc.Tr("Re-translate selected") : btnRetranslate.SetBounds(12, 12, 170, 32)
        btnSync.Text = Loc.Tr("Sync to video") : btnSync.SetBounds(190, 12, Math.Max(140, TextRenderer.MeasureText(btnSync.Text, Font).Width + 24), 32)
        lblStatus.SetBounds(btnSync.Right + 12, 18, 848 - btnSync.Right - 12, 22) : lblStatus.Tag = "muted" : lblStatus.AutoEllipsis = True : lblStatus.UseMnemonic = False
        lblStatus.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        btnSave.Text = Loc.Tr("Save") : btnSave.Tag = "accent" : btnSave.SetBounds(860, 12, 100, 32) : btnSave.Anchor = AnchorStyles.Right Or AnchorStyles.Top
        btnClose.Text = Loc.Tr("Close") : btnClose.SetBounds(968, 12, 100, 32) : btnClose.Anchor = AnchorStyles.Right Or AnchorStyles.Top
        bottom.Controls.AddRange(New Control() {btnRetranslate, btnSync, lblStatus, btnSave, btnClose})

        grid.Dock = DockStyle.Fill
        grid.AllowUserToAddRows = False
        grid.AllowUserToDeleteRows = False
        grid.RowHeadersVisible = False
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "No", .HeaderText = "#", .Width = 50, .ReadOnly = True})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Start", .HeaderText = "Start", .Width = 105})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "End", .HeaderText = "End", .Width = 105})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Original", .HeaderText = "Original", .ReadOnly = True, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Translation", .HeaderText = Loc.Tr("Translation (editable)"), .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})

        Controls.Add(grid)
        Controls.Add(bottom)
        Controls.Add(top)

        AddHandler cmbFile.SelectedIndexChanged, Sub() SwitchFile()
        AddHandler grid.CellValueChanged, Sub()
                                              If Not _loading Then _dirty = True : UpdateTitle()
                                          End Sub
        AddHandler btnFind.Click, Sub() FindNext()
        AddHandler txtFind.KeyDown, Sub(s, e)
                                        If e.KeyCode = Keys.Enter Then FindNext() : e.SuppressKeyPress = True
                                    End Sub
        AddHandler btnSave.Click, Sub() SaveCurrent()
        AddHandler btnClose.Click, Sub() Close()
        AddHandler btnRetranslate.Click, Async Sub() Await RetranslateSelectedAsync()
        AddHandler btnSync.Click, Async Sub() Await SyncToVideoAsync()
        AddHandler btnNextMistake.Click, Sub() NextMistake()
        AddHandler chkSpell.CheckedChanged, Async Sub()
                                                _settings.SpellCheck = chkSpell.Checked
                                                _settings.Save()
                                                Await EnsureSpellCheckerAsync()
                                                grid.Invalidate()
                                            End Sub
        AddHandler grid.CellPainting, AddressOf Grid_CellPainting
        AddHandler grid.CellMouseDown, AddressOf Grid_CellMouseDown
        Theme.Apply(Me)
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ResumeLayout(False)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.Control AndAlso e.KeyCode = Keys.S Then SaveCurrent() : e.Handled = True
        If e.Control AndAlso e.KeyCode = Keys.F Then txtFind.Focus() : e.Handled = True
        If e.KeyCode = Keys.F7 Then NextMistake() : e.Handled = True
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _source = New List(Of (TimeSpan, TimeSpan, String))
        If _item.SourcePath IsNot Nothing AndAlso File.Exists(_item.SourcePath) Then
            For Each c In SrtFile.Load(_item.SourcePath)
                Dim s As TimeSpan, en As TimeSpan
                If SubtitleFormatter.TryParseTiming(c.Timing, s, en) Then _source.Add((s, en, c.Text))
            Next
        End If
        If cmbFile.Items.Count > 0 Then cmbFile.SelectedIndex = 0
    End Sub

    Private Sub UpdateTitle()
        Text = Loc.Tr("Subtitle editor") & $" – {Path.GetFileName(_current)}{If(_dirty, " *", "")}"
    End Sub

    ''' <summary>Original text shown next to a translated line = source lines overlapping its time.</summary>
    Private Function OriginalFor(start As TimeSpan, [end] As TimeSpan) As String
        If _source Is Nothing OrElse _source.Count = 0 Then Return ""
        Dim hits = _source.Where(Function(o) o.Start < [end] AndAlso o.End > start).Select(Function(o) o.Text)
        Return String.Join(vbLf, hits)
    End Function

    Private Sub SwitchFile()
        If _dirty AndAlso Not ConfirmDiscard() Then Return
        _current = _files(cmbFile.SelectedIndex)
        _loading = True
        grid.Rows.Clear()
        Dim n = 0
        For Each c In SrtFile.Load(_current)
            n += 1
            Dim s As TimeSpan, en As TimeSpan
            Dim ok = SubtitleFormatter.TryParseTiming(c.Timing, s, en)
            grid.Rows.Add(n, If(ok, SubtitleFormatter.Fmt(s), ""), If(ok, SubtitleFormatter.Fmt(en), ""),
                          If(ok, OriginalFor(s, en), "").Replace(vbLf, vbCrLf), c.Text.Replace(vbLf, vbCrLf))
        Next
        _loading = False
        _dirty = False
        UpdateTitle()
        lblStatus.Text = Loc.Tr("{0} lines. Edit text or times directly; Ctrl+S saves.", n)
        Dim unused = EnsureSpellCheckerAsync()
    End Sub

    Private Function ConfirmDiscard() As Boolean
        Select Case MessageBox.Show(Me, Loc.Tr("Save changes to ") & Path.GetFileName(_current) & "?", Loc.Tr("Subtitle editor"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            Case DialogResult.Yes : Return SaveCurrent()
            Case DialogResult.No : Return True
            Case Else : Return False
        End Select
    End Function

    Private Function SaveCurrent() As Boolean
        grid.EndEdit()
        Dim cues As New List(Of SrtCue)
        For Each r As DataGridViewRow In grid.Rows
            Dim s As TimeSpan, en As TimeSpan
            Dim timing = $"{r.Cells("Start").Value} --> {r.Cells("End").Value}"
            If Not SubtitleFormatter.TryParseTiming(timing, s, en) OrElse en <= s Then
                grid.ClearSelection()
                r.Selected = True
                grid.FirstDisplayedScrollingRowIndex = r.Index
                MessageBox.Show(Me, Loc.Tr("Line {0}: the times must look like 00:01:02,500 and End must be after Start.", r.Cells("No").Value), Loc.Tr("Subtitle editor"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If
            cues.Add(New SrtCue With {.Timing = SubtitleFormatter.FormatTiming(s, en), .Text = CStr(If(r.Cells("Translation").Value, "")).Replace(vbCrLf, vbLf).Trim()})
        Next
        SrtFile.Save(_current, cues)
        _dirty = False
        UpdateTitle()
        lblStatus.Text = Loc.Tr("Saved {0}.", Path.GetFileName(_current))
        Return True
    End Function

    Private Sub FindNext()
        Dim q = txtFind.Text.Trim()
        If q.Length = 0 OrElse grid.Rows.Count = 0 Then Return
        Dim start = If(grid.CurrentRow IsNot Nothing, grid.CurrentRow.Index + 1, 0)
        For k = 0 To grid.Rows.Count - 1
            Dim r = grid.Rows((start + k) Mod grid.Rows.Count)
            Dim hay = $"{r.Cells("Original").Value} {r.Cells("Translation").Value}"
            If hay.Contains(q, StringComparison.CurrentCultureIgnoreCase) Then
                grid.CurrentCell = r.Cells("Translation")
                grid.ClearSelection()
                r.Selected = True
                lblStatus.Text = Loc.Tr("Found on line {0}.", r.Cells("No").Value)
                Return
            End If
        Next
        lblStatus.Text = Loc.Tr("""{0}"" not found.", q)
    End Sub

    Private Function TargetOfCurrent() As LanguageInfo
        Return If(SrtFile.LanguageFromFileName(_current), _settings.Targets().FirstOrDefault())
    End Function

    Private Async Function RetranslateSelectedAsync() As Task
        Dim rows = grid.SelectedRows.Cast(Of DataGridViewRow)().OrderBy(Function(r) r.Index).ToList()
        If rows.Count = 0 OrElse _cts IsNot Nothing Then Return
        If rows.All(Function(r) String.IsNullOrWhiteSpace(CStr(r.Cells("Original").Value))) Then
            lblStatus.Text = Loc.Tr("The selected lines have no original text to translate.")
            Return
        End If
        _cts = New CancellationTokenSource()
        btnRetranslate.Enabled = False
        Try
            If _translator Is Nothing Then _translator = New LlamaTranslator(_settings)
            Await _translator.PrepareAsync(New Progress(Of StatusUpdate)(Sub(s) lblStatus.Text = s.Text), _cts.Token)
            Dim target = TargetOfCurrent()
            Dim sourceLang = If(SrtFile.LanguageFromFileName(_item.SourcePath), Languages.Find(_settings.TranscribeLanguage))
            Dim gl = Glossary.Load()
            Dim n = 0
            For Each r In rows
                Dim original = CStr(If(r.Cells("Original").Value, "")).Replace(vbCrLf, vbLf).Trim()
                If original.Length = 0 Then Continue For
                n += 1
                lblStatus.Text = Loc.Tr("Translating line {0} ({1}/{2})…", r.Cells("No").Value, n, rows.Count)
                Dim raw = Await _translator.TranslateAsync(original, sourceLang, target, _cts.Token, gl.ForText(original, target))
                r.Cells("Translation").Value = TranslationPrompt.Clean(raw, original).Replace(vbLf, vbCrLf)
            Next
            lblStatus.Text = Loc.Tr("Re-translated {0} line(s) – remember to save.", n)
        Catch ex As OperationCanceledException
            lblStatus.Text = Loc.Tr("Cancelled.")
        Catch ex As Exception
            Logger.Log("Editor re-translate failed: " & ex.ToString())
            MessageBox.Show(Me, ex.Message, Loc.Tr("Re-translate"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            lblStatus.Text = Loc.Tr("Re-translate failed.")
        Finally
            _cts.Dispose()
            _cts = Nothing
            If Not IsDisposed Then btnRetranslate.Enabled = True
        End Try
    End Function

    ' ------------------------------------------------------------------
    '  Spell check
    ' ------------------------------------------------------------------

    Private Async Function EnsureSpellCheckerAsync() As Task
        If Not chkSpell.Checked OrElse _spellLoading OrElse _current Is Nothing Then Return
        Dim lang = TargetOfCurrent()
        If _spell IsNot Nothing AndAlso _spell.Language Is lang Then
            ShowMistakeCount()
            Return
        End If
        If Not SpellChecker.IsSupported(lang) Then
            _spell = Nothing
            lblStatus.Text = Loc.Tr("No spelling dictionary for {0}.", If(lang?.DisplayName, "?"))
            grid.Invalidate()
            Return
        End If
        _spellLoading = True
        Try
            If Not SpellChecker.IsDownloaded(lang) Then lblStatus.Text = Loc.Tr("Downloading the {0} dictionary…", lang.DisplayName)
            Dim prog As New Progress(Of DownloadProgress)(Sub(d) lblStatus.Text = Loc.Tr("Downloading the {0} dictionary…", lang.DisplayName) & " " & d.ToString())
            Dim checker = Await SpellChecker.LoadAsync(lang, prog, CancellationToken.None)
            ' names and terms from the glossary are never marked
            checker.AddKnownWords(Glossary.Load().Entries.Where(Function(g) g.Language Is Nothing OrElse g.Language = lang.Code).Select(Function(g) g.Target))
            _spell = checker
            If IsDisposed Then Return
            grid.Invalidate()
            ShowMistakeCount()
        Catch ex As Exception
            Logger.Log("Spell check unavailable: " & ex.Message)
            If Not IsDisposed Then lblStatus.Text = Loc.Tr("Spell check is not available: {0}", ex.Message)
        Finally
            _spellLoading = False
        End Try
    End Function

    Private Function SpellActive() As Boolean
        Return chkSpell.Checked AndAlso _spell IsNot Nothing AndAlso _spell.Language Is TargetOfCurrent()
    End Function

    Private Function MistakesIn(r As DataGridViewRow) As List(Of (Start As Integer, Length As Integer, Word As String))
        Return _spell.FindMistakes(CStr(If(r.Cells("Translation").Value, "")))
    End Function

    Private Sub ShowMistakeCount()
        If Not SpellActive() Then Return
        Dim lines = grid.Rows.Cast(Of DataGridViewRow)().Count(Function(r) MistakesIn(r).Count > 0)
        lblStatus.Text = If(lines = 0, Loc.Tr("No spelling mistakes found."), Loc.Tr("{0} line(s) with possible spelling mistakes – F7 jumps to the next one, right-click for suggestions.", lines))
    End Sub

    Private Sub NextMistake()
        If Not SpellActive() OrElse grid.Rows.Count = 0 Then Return
        grid.EndEdit()
        Dim start = If(grid.CurrentRow IsNot Nothing, grid.CurrentRow.Index + 1, 0)
        For k = 0 To grid.Rows.Count - 1
            Dim r = grid.Rows((start + k) Mod grid.Rows.Count)
            Dim m = MistakesIn(r)
            If m.Count > 0 Then
                grid.CurrentCell = r.Cells("Translation")
                grid.ClearSelection()
                r.Selected = True
                lblStatus.Text = Loc.Tr("Line {0}: {1}", r.Cells("No").Value, String.Join(", ", m.Select(Function(x) x.Word)))
                Return
            End If
        Next
        lblStatus.Text = Loc.Tr("No spelling mistakes found.")
    End Sub

    ''' <summary>Draws the translation text with a red wavy line under misspelled words.</summary>
    Private Sub Grid_CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex <> grid.Columns("Translation").Index OrElse Not SpellActive() Then Return
        Dim text = CStr(If(e.FormattedValue, ""))
        e.PaintBackground(e.ClipBounds, True)
        Dim selected = (e.State And DataGridViewElementStates.Selected) <> 0
        Dim fore = If(selected, e.CellStyle.SelectionForeColor, e.CellStyle.ForeColor)
        Dim font = If(e.CellStyle.Font, grid.Font)
        Dim pad = e.CellStyle.Padding
        Dim area = New Rectangle(e.CellBounds.X + 3 + pad.Left, e.CellBounds.Y + 2 + pad.Top,
                                 e.CellBounds.Width - 6 - pad.Horizontal, e.CellBounds.Height - 4 - pad.Vertical)
        Const flags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
        Dim lineH = TextRenderer.MeasureText(e.Graphics, "Ág", font, Size.Empty, flags).Height
        Dim mistakes = _spell.FindMistakes(text)
        Dim g = e.Graphics
        Dim oldClip = g.Clip
        g.SetClip(e.CellBounds)
        Dim y = area.Y
        For Each seg In WrapLines(g, text, font, area.Width, flags)
            Dim line = text.Substring(seg.Start, seg.Length)
            TextRenderer.DrawText(g, line, font, New Point(area.X, y), fore, flags)
            For Each m In mistakes
                If m.Start < seg.Start OrElse m.Start + m.Length > seg.Start + seg.Length Then Continue For
                Dim x0 = area.X + If(m.Start > seg.Start, TextRenderer.MeasureText(g, text.Substring(seg.Start, m.Start - seg.Start), font, Size.Empty, flags).Width, 0)
                Dim w = TextRenderer.MeasureText(g, m.Word, font, Size.Empty, flags).Width
                DrawWave(g, x0, y + lineH - 1, w)
            Next
            y += lineH
        Next
        g.Clip = oldClip
        e.Handled = True
    End Sub

    ''' <summary>Splits text into lines that fit the width (at spaces), keeping positions in the original text.</summary>
    Private Shared Function WrapLines(g As Graphics, text As String, font As Font, width As Integer, flags As TextFormatFlags) As List(Of (Start As Integer, Length As Integer))
        Dim result As New List(Of (Start As Integer, Length As Integer))
        Dim pos = 0
        For Each hard In text.Replace(vbCr, "").Split(vbLf)
            ' positions in the original text (which may contain CR LF)
            Dim lineStart = text.IndexOf(hard, pos, StringComparison.Ordinal)
            If lineStart < 0 Then lineStart = pos
            Dim s = lineStart, lastFit = lineStart
            Dim i = lineStart
            While i <= lineStart + hard.Length
                If i = lineStart + hard.Length OrElse text(i) = " "c Then
                    Dim w = TextRenderer.MeasureText(g, text.Substring(s, i - s), font, Size.Empty, flags).Width
                    If w > width AndAlso lastFit > s Then
                        result.Add((s, lastFit - s))
                        s = lastFit + 1
                    End If
                    lastFit = i
                End If
                i += 1
            End While
            result.Add((s, lineStart + hard.Length - s))
            pos = lineStart + hard.Length
        Next
        Return result
    End Function

    Private Shared Sub DrawWave(g As Graphics, x As Integer, y As Integer, width As Integer)
        If width < 4 Then Return
        Dim pts As New List(Of Point)
        Dim up = True
        For px = x To x + width Step 2
            pts.Add(New Point(px, If(up, y - 1, y + 1)))
            up = Not up
        Next
        If pts.Count < 2 Then Return
        Using pen As New Pen(Color.FromArgb(232, 64, 64), 1)
            g.DrawLines(pen, pts.ToArray())
        End Using
    End Sub

    ''' <summary>Right-click on a translation: spelling suggestions, add to dictionary.</summary>
    Private Sub Grid_CellMouseDown(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.Button <> MouseButtons.Right OrElse e.RowIndex < 0 OrElse e.ColumnIndex <> grid.Columns("Translation").Index Then Return
        If grid.IsCurrentCellInEditMode Then Return
        Dim cell = grid.Rows(e.RowIndex).Cells(e.ColumnIndex)
        grid.CurrentCell = cell
        grid.ClearSelection()
        grid.Rows(e.RowIndex).Selected = True
        spellMenu.Items.Clear()
        If Not SpellActive() Then Return
        Dim mistakes = MistakesIn(grid.Rows(e.RowIndex))
        If mistakes.Count = 0 Then
            spellMenu.Items.Add(New ToolStripMenuItem(Loc.Tr("No spelling mistakes in this line")) With {.Enabled = False})
        Else
            For Each m In mistakes.Take(6)
                Dim mistake = m
                Dim parent As ToolStripItemCollection = spellMenu.Items
                If mistakes.Count > 1 Then
                    Dim sub1 As New ToolStripMenuItem(mistake.Word)
                    spellMenu.Items.Add(sub1)
                    parent = sub1.DropDownItems
                End If
                Dim sugg = _spell.Suggest(mistake.Word)
                If sugg.Count = 0 Then parent.Add(New ToolStripMenuItem(Loc.Tr("(no suggestions)")) With {.Enabled = False})
                For Each sgg In sugg
                    Dim replacement = sgg
                    Dim mi As New ToolStripMenuItem(replacement) With {.Font = New Font(spellMenu.Font, FontStyle.Bold)}
                    AddHandler mi.Click, Sub() ReplaceWord(cell, mistake.Word, replacement)
                    parent.Add(mi)
                Next
                parent.Add(New ToolStripSeparator())
                Dim add As New ToolStripMenuItem(Loc.Tr("Add ""{0}"" to the dictionary", mistake.Word))
                AddHandler add.Click, Sub()
                                          _spell.AddToDictionary(mistake.Word)
                                          grid.Invalidate()
                                          ShowMistakeCount()
                                      End Sub
                parent.Add(add)
            Next
        End If
        spellMenu.Show(grid, grid.PointToClient(Cursor.Position))
    End Sub

    Private Sub ReplaceWord(cell As DataGridViewCell, word As String, replacement As String)
        Dim text = CStr(If(cell.Value, ""))
        ' replace the first whole-word occurrence that is still misspelled
        For Each m In _spell.FindMistakes(text)
            If m.Word = word Then
                cell.Value = text.Substring(0, m.Start) & replacement & text.Substring(m.Start + m.Length)
                Exit For
            End If
        Next
        ShowMistakeCount()
    End Sub

    ' ------------------------------------------------------------------
    '  Sync to video
    ' ------------------------------------------------------------------

    Private Function MediaForSync() As String
        Dim ext = Path.GetExtension(_item.FilePath).ToLowerInvariant()
        If _item.IsVideo OrElse SubtitlePipeline.AudioExtensions.Contains(ext) Then Return _item.FilePath
        Dim v = SubtitleSync.FindVideoFor(_item.FilePath)
        If v IsNot Nothing Then Return v
        Using ofd As New OpenFileDialog With {
            .Title = Loc.Tr("Choose the video these subtitles belong to"),
            .Filter = Loc.Tr("Videos") & "|" & String.Join(";", SubtitlePipeline.VideoExtensions.Select(Function(x) "*" & x)) & "|" & Loc.Tr("All files") & "|*.*",
            .InitialDirectory = Path.GetDirectoryName(_item.FilePath)}
            Return If(ofd.ShowDialog(Me) = DialogResult.OK, ofd.FileName, Nothing)
        End Using
    End Function

    Private Async Function SyncToVideoAsync() As Task
        If _cts IsNot Nothing OrElse grid.Rows.Count = 0 Then Return
        grid.EndEdit()
        If Not File.Exists(FfmpegComponent.FfmpegExe) Then
            MessageBox.Show(Me, Loc.Tr("FFmpeg is missing – install it in Components & settings."), Loc.Tr("Sync to video"), MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim media = MediaForSync()
        If media Is Nothing Then Return
        ' current times of the grid
        Dim times As New List(Of (Start As Double, [End] As Double))
        Dim rows As New List(Of DataGridViewRow)
        For Each r As DataGridViewRow In grid.Rows
            Dim s As TimeSpan, en As TimeSpan
            If SubtitleFormatter.TryParseTiming($"{r.Cells("Start").Value} --> {r.Cells("End").Value}", s, en) AndAlso
               Not String.IsNullOrWhiteSpace(CStr(r.Cells("Translation").Value)) Then
                times.Add((s.TotalSeconds, en.TotalSeconds))
                rows.Add(r)
            End If
        Next
        _cts = New CancellationTokenSource()
        btnSync.Enabled = False : btnRetranslate.Enabled = False
        Try
            If _speech Is Nothing OrElse Not String.Equals(_speechMedia, media, StringComparison.OrdinalIgnoreCase) Then
                lblStatus.Text = Loc.Tr("Listening to the audio of {0}…", Path.GetFileName(media))
                Dim duration = Await SubtitlePipeline.ProbeDurationAsync(media, _cts.Token)
                _speech = Await SubtitleSync.DetectSpeechBestAsync(FfmpegComponent.FfmpegExe, media, 0, _cts.Token,
                                                              Sub(f) BeginInvoke(Sub() lblStatus.Text = Loc.Tr("Listening to the audio of {0}…", Path.GetFileName(media)) & $" {f:P0}"), duration)
                _speechMedia = media
            End If
            Dim result = Await Task.Run(Function() SubtitleSync.FindBest(_speech, times))
            Logger.Log($"Editor sync: {result.Describe()} confidence {result.Confidence:0.0} → {result.Reason}")
            If result.ShouldApply Then
                If MessageBox.Show(Me, Loc.Tr("The subtitles match the speech best when moved by {0}.", result.Describe()) & vbCrLf & vbCrLf & Loc.Tr("Change the times of all lines?"),
                                   Loc.Tr("Sync to video"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                    Dim matched As New HashSet(Of DataGridViewRow)(rows)
                    Dim line = -1
                    For Each r As DataGridViewRow In grid.Rows
                        If matched.Contains(r) Then line += 1 ' rows used for matching, in order
                        Dim s As TimeSpan, en As TimeSpan
                        If SubtitleFormatter.TryParseTiming($"{r.Cells("Start").Value} --> {r.Cells("End").Value}", s, en) Then
                            r.Cells("Start").Value = SubtitleFormatter.Fmt(SubtitleSync.Shift(s, result, Math.Max(0, line)))
                            r.Cells("End").Value = SubtitleFormatter.Fmt(SubtitleSync.Shift(en, result, Math.Max(0, line)))
                        End If
                    Next
                    lblStatus.Text = Loc.Tr("Timing changed by {0} – remember to save.", result.Describe())
                Else
                    lblStatus.Text = Loc.Tr("Timing not changed.")
                End If
            ElseIf result.Reason = "already in sync" Then
                lblStatus.Text = Loc.Tr("The subtitles are already in sync with the video.")
            Else
                lblStatus.Text = Loc.Tr("No clear match with the speech – the subtitles may be for a different version of the video.")
            End If
        Catch ex As OperationCanceledException
            lblStatus.Text = Loc.Tr("Cancelled.")
        Catch ex As Exception
            Logger.Log("Editor sync failed: " & ex.ToString())
            MessageBox.Show(Me, ex.Message, Loc.Tr("Sync to video"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            _cts.Dispose()
            _cts = Nothing
            If Not IsDisposed Then btnSync.Enabled = True : btnRetranslate.Enabled = True
        End Try
    End Function

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If _dirty AndAlso Not ConfirmDiscard() Then
            e.Cancel = True
            Return
        End If
        _cts?.Cancel()
        _translator?.Dispose() ' frees the AI model from memory
        MyBase.OnFormClosing(e)
    End Sub
End Class
