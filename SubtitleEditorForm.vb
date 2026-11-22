Imports System.IO
Imports System.Threading

''' <summary>
''' Review and correct a translation: original and translated text side by side,
''' editable text and times, re-translate selected lines with the AI, find, save.
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
        Text = "Subtitle editor – " & Path.GetFileName(_item.FilePath)
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1000, 640)
        MinimumSize = New Size(760, 420)
        StartPosition = FormStartPosition.CenterParent
        KeyPreview = True

        Dim top As New Panel With {.Dock = DockStyle.Top, .Height = 48, .Tag = "panel", .Padding = New Padding(10, 10, 10, 6)}
        Dim lblFile As New Label With {.Text = "Translation:", .AutoSize = True, .Location = New Point(12, 16)}
        cmbFile.SetBounds(100, 12, 330, 25)
        cmbFile.DropDownStyle = ComboBoxStyle.DropDownList
        cmbFile.FlatStyle = FlatStyle.Flat
        For Each f In _files : cmbFile.Items.Add(Path.GetFileName(f)) : Next
        txtFind.SetBounds(460, 13, 240, 25)
        txtFind.PlaceholderText = "Find text…"
        btnFind.SetBounds(706, 11, 90, 28) : btnFind.Text = "Find next"
        top.Controls.AddRange(New Control() {lblFile, cmbFile, txtFind, btnFind})

        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 54, .Tag = "panel"}
        btnRetranslate.Text = "Re-translate selected" : btnRetranslate.SetBounds(12, 12, 170, 32)
        lblStatus.SetBounds(194, 18, 480, 22) : lblStatus.Tag = "muted" : lblStatus.AutoEllipsis = True
        lblStatus.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        btnSave.Text = "Save" : btnSave.Tag = "accent" : btnSave.SetBounds(780, 12, 100, 32) : btnSave.Anchor = AnchorStyles.Right Or AnchorStyles.Top
        btnClose.Text = "Close" : btnClose.SetBounds(888, 12, 100, 32) : btnClose.Anchor = AnchorStyles.Right Or AnchorStyles.Top
        bottom.Controls.AddRange(New Control() {btnRetranslate, lblStatus, btnSave, btnClose})

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
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Translation", .HeaderText = "Translation (editable)", .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})

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
        Theme.Apply(Me)
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ResumeLayout(False)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.Control AndAlso e.KeyCode = Keys.S Then SaveCurrent() : e.Handled = True
        If e.Control AndAlso e.KeyCode = Keys.F Then txtFind.Focus() : e.Handled = True
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
        Text = $"Subtitle editor – {Path.GetFileName(_current)}{If(_dirty, " *", "")}"
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
        lblStatus.Text = $"{n} lines. Edit text or times directly; Ctrl+S saves."
    End Sub

    Private Function ConfirmDiscard() As Boolean
        Select Case MessageBox.Show(Me, "Save changes to " & Path.GetFileName(_current) & "?", "Subtitle editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
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
                MessageBox.Show(Me, $"Line {r.Cells("No").Value}: the times must look like 00:01:02,500 and End must be after Start.", "Subtitle editor", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If
            cues.Add(New SrtCue With {.Timing = SubtitleFormatter.FormatTiming(s, en), .Text = CStr(If(r.Cells("Translation").Value, "")).Replace(vbCrLf, vbLf).Trim()})
        Next
        SrtFile.Save(_current, cues)
        _dirty = False
        UpdateTitle()
        lblStatus.Text = $"Saved {Path.GetFileName(_current)}."
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
                lblStatus.Text = $"Found on line {r.Cells("No").Value}."
                Return
            End If
        Next
        lblStatus.Text = $"""{q}"" not found."
    End Sub

    Private Function TargetOfCurrent() As LanguageInfo
        Return If(SrtFile.LanguageFromFileName(_current), _settings.Targets().FirstOrDefault())
    End Function

    Private Async Function RetranslateSelectedAsync() As Task
        Dim rows = grid.SelectedRows.Cast(Of DataGridViewRow)().OrderBy(Function(r) r.Index).ToList()
        If rows.Count = 0 OrElse _cts IsNot Nothing Then Return
        If rows.All(Function(r) String.IsNullOrWhiteSpace(CStr(r.Cells("Original").Value))) Then
            lblStatus.Text = "The selected lines have no original text to translate."
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
                lblStatus.Text = $"Translating line {r.Cells("No").Value} ({n}/{rows.Count})…"
                Dim raw = Await _translator.TranslateAsync(original, sourceLang, target, _cts.Token, gl.ForText(original, target))
                r.Cells("Translation").Value = TranslationPrompt.Clean(raw, original).Replace(vbLf, vbCrLf)
            Next
            lblStatus.Text = $"Re-translated {n} line(s) – remember to save."
        Catch ex As OperationCanceledException
            lblStatus.Text = "Cancelled."
        Catch ex As Exception
            Logger.Log("Editor re-translate failed: " & ex.ToString())
            MessageBox.Show(Me, ex.Message, "Re-translate", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            lblStatus.Text = "Re-translate failed."
        Finally
            _cts.Dispose()
            _cts = Nothing
            If Not IsDisposed Then btnRetranslate.Enabled = True
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
