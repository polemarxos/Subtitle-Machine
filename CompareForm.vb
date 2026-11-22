Imports System.IO

''' <summary>
''' Compares two subtitle files side by side and marks the differences:
''' changed words, changed timing, and lines that exist only in one of the files.
''' </summary>
Public Class CompareForm
    Inherits Form

    Private ReadOnly txtA As New TextBox()
    Private ReadOnly txtB As New TextBox()
    Private ReadOnly btnBrowseA As New Button()
    Private ReadOnly btnBrowseB As New Button()
    Private ReadOnly btnSwap As New Button()
    Private ReadOnly btnCompare As New Button()
    Private ReadOnly chkOnlyDiff As New CheckBox()
    Private ReadOnly numTolerance As New NumericUpDown()
    Private ReadOnly btnPrev As New Button()
    Private ReadOnly btnNext As New Button()
    Private ReadOnly lblSummary As New Label()
    Private ReadOnly lblLegend As New Label()
    Private ReadOnly grid As New DataGridView()
    Private _rows As New List(Of DiffRow)
    Private _shown As New List(Of DiffRow)

    Public Sub New(Optional fileA As String = Nothing, Optional fileB As String = Nothing)
        SuspendLayout()
        Text = Loc.Tr("Compare subtitles")
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1100, 680)
        MinimumSize = New Size(800, 450)
        StartPosition = FormStartPosition.CenterParent
        AllowDrop = True
        KeyPreview = True

        Dim top As New Panel With {.Dock = DockStyle.Top, .Width = ClientSize.Width, .Height = 112, .Tag = "panel"}
        AddFileRow(top, Loc.Tr("File A:"), txtA, btnBrowseA, 12)
        AddFileRow(top, Loc.Tr("File B:"), txtB, btnBrowseB, 44)
        btnSwap.Text = Loc.Tr("⇅ Swap") : btnSwap.SetBounds(780, 26, 80, 28) : btnSwap.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnCompare.Text = Loc.Tr("Compare") : btnCompare.Tag = "accent" : btnCompare.SetBounds(870, 26, 110, 28) : btnCompare.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        chkOnlyDiff.Text = Loc.Tr("Show only differences") : chkOnlyDiff.SetBounds(70, 80, 180, 24)
        Dim lblTol As New Label With {.Text = Loc.Tr("Ignore timing differences up to"), .AutoSize = True, .Location = New Point(262, 83)}
        numTolerance.SetBounds(460, 80, 70, 25) : numTolerance.Minimum = 0 : numTolerance.Maximum = 5000 : numTolerance.Increment = 50 : numTolerance.Value = 100
        Dim lblMs As New Label With {.Text = "ms", .AutoSize = True, .Location = New Point(535, 83)}
        btnPrev.Text = Loc.Tr("▲ Previous difference") : btnPrev.SetBounds(700, 78, 160, 28) : btnPrev.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnNext.Text = Loc.Tr("▼ Next difference") : btnNext.SetBounds(866, 78, 150, 28) : btnNext.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        top.Controls.AddRange(New Control() {btnSwap, btnCompare, chkOnlyDiff, lblTol, numTolerance, lblMs, btnPrev, btnNext})

        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Width = ClientSize.Width, .Height = 52, .Tag = "panel"}
        lblSummary.SetBounds(12, 6, 1070, 20) : lblSummary.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        lblSummary.Text = Loc.Tr("Choose two subtitle files (or drop them here) and press Compare.")
        lblLegend.SetBounds(12, 28, 1070, 20) : lblLegend.Tag = "muted" : lblLegend.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        lblLegend.Text = Loc.Tr("Marked words = changed.  Yellow row = text changed · orange time = timing changed · red row = only in A · green row = only in B.")
        bottom.Controls.AddRange(New Control() {lblSummary, lblLegend})

        grid.Dock = DockStyle.Fill
        grid.ReadOnly = True
        grid.AllowUserToAddRows = False
        grid.AllowUserToDeleteRows = False
        grid.AllowUserToResizeRows = False
        grid.RowHeadersVisible = False
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "No", .HeaderText = "#", .Width = 50})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "TimeA", .HeaderText = Loc.Tr("Time A"), .Width = 115})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "TextA", .HeaderText = Loc.Tr("Text A"), .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "TimeB", .HeaderText = Loc.Tr("Time B"), .Width = 115})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "TextB", .HeaderText = Loc.Tr("Text B"), .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
        grid.RowTemplate.Height = 26

        Controls.Add(grid)
        Controls.Add(bottom)
        Controls.Add(top)

        AddHandler btnBrowseA.Click, Sub() Browse(txtA)
        AddHandler btnBrowseB.Click, Sub() Browse(txtB)
        AddHandler btnSwap.Click, Sub()
                                      Dim t = txtA.Text : txtA.Text = txtB.Text : txtB.Text = t
                                      RunCompare()
                                  End Sub
        AddHandler btnCompare.Click, Sub() RunCompare()
        ' Compare automatically as soon as both files are chosen (typed, browsed or dropped).
        AddHandler txtA.TextChanged, Sub() AutoCompare()
        AddHandler txtB.TextChanged, Sub() AutoCompare()
        AddHandler chkOnlyDiff.CheckedChanged, Sub() FillGrid()
        AddHandler numTolerance.ValueChanged, Sub()
                                                  If _rows.Count > 0 Then RunCompare()
                                              End Sub
        AddHandler btnNext.Click, Sub() JumpToDifference(+1)
        AddHandler btnPrev.Click, Sub() JumpToDifference(-1)
        AddHandler grid.CellPainting, AddressOf PaintCell
        AddHandler DragEnter, Sub(s, e)
                                  If e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effect = DragDropEffects.Copy
                              End Sub
        AddHandler DragDrop, AddressOf OnFilesDropped

        txtA.Text = If(fileA, "")
        txtB.Text = If(fileB, "")
        Theme.Apply(Me)
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ResumeLayout(False)
    End Sub

    Private Sub AddFileRow(parent As Control, caption As String, box As TextBox, browse As Button, y As Integer)
        Dim l As New Label With {.Text = caption, .AutoSize = True, .Location = New Point(12, y + 4)}
        box.SetBounds(70, y, 600, 25)
        box.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        box.AllowDrop = True
        AddHandler box.DragEnter, Sub(s, e)
                                      If e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effect = DragDropEffects.Copy
                                  End Sub
        AddHandler box.DragDrop, Sub(s, e)
                                     Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
                                     If files IsNot Nothing AndAlso files.Length > 0 Then box.Text = files(0)
                                 End Sub
        browse.Text = Loc.Tr("Browse…")
        browse.SetBounds(676, y - 1, 90, 27)
        browse.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        parent.Controls.AddRange(New Control() {l, box, browse})
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If File.Exists(txtA.Text) AndAlso File.Exists(txtB.Text) Then RunCompare()
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.F3 OrElse (e.Control AndAlso e.KeyCode = Keys.Down) Then JumpToDifference(If(e.Shift, -1, +1)) : e.Handled = True
        If e.Control AndAlso e.KeyCode = Keys.Up Then JumpToDifference(-1) : e.Handled = True
        MyBase.OnKeyDown(e)
    End Sub

    Private Sub Browse(box As TextBox)
        Using ofd As New OpenFileDialog With {.Filter = Loc.Tr("Subtitles") & " (*.srt)|*.srt|" & Loc.Tr("All files") & "|*.*"}
            If File.Exists(box.Text) Then ofd.InitialDirectory = Path.GetDirectoryName(box.Text)
            If ofd.ShowDialog(Me) = DialogResult.OK Then box.Text = ofd.FileName
        End Using
    End Sub

    Private Sub OnFilesDropped(sender As Object, e As DragEventArgs)
        Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files Is Nothing OrElse files.Length = 0 Then Return
        If files.Length >= 2 Then
            txtA.Text = files(0) : txtB.Text = files(1)
        ElseIf txtA.Text.Length = 0 OrElse txtB.Text.Length > 0 Then
            txtA.Text = files(0)
        Else
            txtB.Text = files(0)
        End If
        If File.Exists(txtA.Text) AndAlso File.Exists(txtB.Text) Then RunCompare()
    End Sub

    Private Sub AutoCompare()
        If IsHandleCreated AndAlso File.Exists(txtA.Text) AndAlso File.Exists(txtB.Text) Then RunCompare()
    End Sub

    Private Sub RunCompare()
        If Not File.Exists(txtA.Text) OrElse Not File.Exists(txtB.Text) Then
            lblSummary.Text = Loc.Tr("Choose two existing subtitle files.")
            Return
        End If
        Try
            _rows = SubtitleCompare.Compare(SrtFile.Load(txtA.Text), SrtFile.Load(txtB.Text), TimeSpan.FromMilliseconds(CDbl(numTolerance.Value)))
            Text = Loc.Tr("Compare – {0}  ↔  {1}", Path.GetFileName(txtA.Text), Path.GetFileName(txtB.Text))
            lblSummary.Text = SubtitleCompare.Summary(_rows)
            FillGrid()
        Catch ex As Exception
            lblSummary.Text = Loc.Tr("Could not compare: ") & ex.Message
        End Try
    End Sub

    Private Sub FillGrid()
        _shown = If(chkOnlyDiff.Checked, _rows.Where(Function(r) r.IsDifferent).ToList(), _rows)
        grid.SuspendLayout()
        grid.Rows.Clear()
        For i = 0 To _shown.Count - 1
            Dim r = _shown(i)
            Dim idx = grid.Rows.Add(_rows.IndexOf(r) + 1,
                                    If(r.A IsNot Nothing, SubtitleFormatter.Fmt(r.AStart), ""),
                                    If(r.A IsNot Nothing, SubtitleCompare.NormalizeText(r.A.Text), ""),
                                    If(r.B IsNot Nothing, SubtitleFormatter.Fmt(r.BStart), ""),
                                    If(r.B IsNot Nothing, SubtitleCompare.NormalizeText(r.B.Text), ""))
            grid.Rows(idx).Tag = r
        Next
        grid.ResumeLayout()
    End Sub

    Private Sub JumpToDifference(direction As Integer)
        If grid.Rows.Count = 0 Then Return
        Dim start = If(grid.CurrentRow IsNot Nothing, grid.CurrentRow.Index, -1)
        For k = 1 To grid.Rows.Count
            Dim i = start + direction * k
            If i < 0 OrElse i >= grid.Rows.Count Then Exit For
            Dim r = TryCast(grid.Rows(i).Tag, DiffRow)
            If r IsNot Nothing AndAlso r.IsDifferent Then
                grid.CurrentCell = grid.Rows(i).Cells("TextA")
                grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 3)
                Return
            End If
        Next
        lblSummary.Text = SubtitleCompare.Summary(_rows) & If(direction > 0, Loc.Tr("  –  no more differences below."), Loc.Tr("  –  no more differences above."))
    End Sub

    ' ----- drawing: tinted rows, marked words -----
    Private Function Tint(kind As DiffKind) As Color?
        Dim dark = Theme.Current.IsDark
        Select Case kind
            Case DiffKind.OnlyInA : Return If(dark, Color.FromArgb(78, 38, 38), Color.FromArgb(255, 226, 226))
            Case DiffKind.OnlyInB : Return If(dark, Color.FromArgb(34, 66, 42), Color.FromArgb(222, 245, 222))
            Case DiffKind.TextChanged, DiffKind.TextAndTiming : Return If(dark, Color.FromArgb(70, 62, 28), Color.FromArgb(255, 246, 205))
            Case Else : Return Nothing
        End Select
    End Function

    Private Sub PaintCell(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        Dim r = TryCast(grid.Rows(e.RowIndex).Tag, DiffRow)
        If r Is Nothing Then Return
        Dim pal = Theme.Current
        Dim selected = (e.State And DataGridViewElementStates.Selected) <> 0
        Dim bg = If(selected, pal.Selected, If(Tint(r.Kind), If(e.RowIndex Mod 2 = 0, pal.Back, pal.AltRow)))
        Using b As New SolidBrush(bg)
            e.Graphics.FillRectangle(b, e.CellBounds)
        End Using
        Using p As New Pen(pal.Border)
            e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1)
        End Using
        Dim fg = If(selected AndAlso pal.IsDark, Color.White, pal.Text)
        Dim col = grid.Columns(e.ColumnIndex).Name
        Dim area = e.CellBounds
        area.Inflate(-5, 0)
        Const flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding

        If (col = "TextA" OrElse col = "TextB") AndAlso (r.Kind = DiffKind.TextChanged OrElse r.Kind = DiffKind.TextAndTiming) Then
            Dim ta As List(Of DiffToken) = Nothing, tb As List(Of DiffToken) = Nothing
            SubtitleCompare.WordDiff(r.A.Text, r.B.Text, ta, tb)
            DrawTokens(e.Graphics, If(col = "TextA", ta, tb), area, fg,
                       If(col = "TextA", If(pal.IsDark, Color.FromArgb(150, 55, 55), Color.FromArgb(255, 170, 170)),
                                         If(pal.IsDark, Color.FromArgb(45, 120, 60), Color.FromArgb(160, 225, 160))))
        Else
            Dim timeChanged = (col = "TimeA" OrElse col = "TimeB") AndAlso (r.Kind = DiffKind.TimingChanged OrElse r.Kind = DiffKind.TextAndTiming)
            TextRenderer.DrawText(e.Graphics, CStr(If(e.Value, "")), grid.Font, area, If(timeChanged, Color.FromArgb(230, 140, 30), fg), flags)
        End If
        e.Handled = True
    End Sub

    Private Sub DrawTokens(g As Graphics, tokens As List(Of DiffToken), area As Rectangle, fg As Color, mark As Color)
        Dim x = area.Left
        Const flags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
        Dim h = TextRenderer.MeasureText(g, "Ag", grid.Font, area.Size, flags).Height
        Dim y = area.Top + (area.Height - h) \ 2
        For Each t In tokens
            Dim w = TextRenderer.MeasureText(g, t.Text, grid.Font, New Size(Integer.MaxValue, h), flags).Width
            If x + w > area.Right Then
                TextRenderer.DrawText(g, "…", grid.Font, New Point(x, y), fg, flags)
                Exit For
            End If
            If t.Changed Then
                Using b As New SolidBrush(mark)
                    g.FillRectangle(b, x, y, w, h)
                End Using
            End If
            TextRenderer.DrawText(g, t.Text, grid.Font, New Point(x, y), fg, flags)
            x += w
        Next
    End Sub
End Class
