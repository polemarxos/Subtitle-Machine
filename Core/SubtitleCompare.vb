Imports System.Text.RegularExpressions

Public Enum DiffKind
    Same
    TextChanged
    TimingChanged
    TextAndTiming
    OnlyInA
    OnlyInB
End Enum

''' <summary>One line of a comparison: a cue of file A and/or the matching cue of file B.</summary>
Public Class DiffRow
    Public Property A As SrtCue
    Public Property B As SrtCue
    Public Property AStart As TimeSpan
    Public Property AEnd As TimeSpan
    Public Property BStart As TimeSpan
    Public Property BEnd As TimeSpan
    Public Property Kind As DiffKind

    Public ReadOnly Property IsDifferent As Boolean
        Get
            Return Kind <> DiffKind.Same
        End Get
    End Property
End Class

''' <summary>A piece of text in a word-level diff; Changed = not present in the other file.</summary>
Public Structure DiffToken
    Public Text As String
    Public Changed As Boolean
End Structure

''' <summary>
''' Compares two subtitle files: cues are matched by time (overlap), then text and timing are compared;
''' changed words are marked with a word-level diff (LCS).
''' </summary>
Public Module SubtitleCompare

    Private ReadOnly TagRx As New Regex("<[^>]*>|\{[^}]*\}")
    Private ReadOnly TokenRx As New Regex("\w+|[^\w\s]|\s+")

    Private Class Timed
        Public Cue As SrtCue
        Public Start As TimeSpan
        Public [End] As TimeSpan
    End Class

    Private Function ToTimed(cues As IEnumerable(Of SrtCue)) As List(Of Timed)
        Dim list As New List(Of Timed)
        For Each c In cues
            Dim s As TimeSpan, e As TimeSpan
            If SubtitleFormatter.TryParseTiming(c.Timing, s, e) Then list.Add(New Timed With {.Cue = c, .Start = s, .End = e})
        Next
        Return list.OrderBy(Function(t) t.Start).ToList()
    End Function

    ''' <summary>Text as it is shown on screen: no tags, single spaces, one line.</summary>
    Public Function NormalizeText(s As String) As String
        Return Regex.Replace(TagRx.Replace(If(s, ""), ""), "\s+", " ").Trim()
    End Function

    Public Function Compare(a As IEnumerable(Of SrtCue), b As IEnumerable(Of SrtCue), timingTolerance As TimeSpan) As List(Of DiffRow)
        Dim ta = ToTimed(a), tb = ToTimed(b)
        Dim usedB(tb.Count - 1) As Boolean
        Dim rows As New List(Of DiffRow)

        ' Match each cue of A with the free cue of B that overlaps it most (at least 30 % of the shorter one).
        Dim j0 = 0
        For Each x In ta
            Dim best = -1
            Dim bestScore = 0.0
            While j0 < tb.Count AndAlso tb(j0).End <= x.Start - TimeSpan.FromSeconds(10)
                j0 += 1
            End While
            For j = j0 To tb.Count - 1
                Dim y = tb(j)
                If y.Start >= x.End + TimeSpan.FromSeconds(1) Then Exit For
                If usedB(j) Then Continue For
                Dim overlap = (If(x.End < y.End, x.End, y.End) - If(x.Start > y.Start, x.Start, y.Start)).TotalMilliseconds
                Dim shorter = Math.Max(1, Math.Min((x.End - x.Start).TotalMilliseconds, (y.End - y.Start).TotalMilliseconds))
                Dim score = overlap / shorter
                ' Same start (±tolerance) also counts, for very short cues.
                If Math.Abs((x.Start - y.Start).TotalMilliseconds) <= timingTolerance.TotalMilliseconds Then score = Math.Max(score, 1)
                If score > bestScore Then bestScore = score : best = j
            Next
            If best >= 0 AndAlso bestScore >= 0.3 Then
                usedB(best) = True
                Dim y = tb(best)
                Dim textSame = NormalizeText(x.Cue.Text) = NormalizeText(y.Cue.Text)
                Dim timeSame = Math.Abs((x.Start - y.Start).TotalMilliseconds) <= timingTolerance.TotalMilliseconds AndAlso
                               Math.Abs((x.End - y.End).TotalMilliseconds) <= timingTolerance.TotalMilliseconds
                Dim kind = If(textSame, If(timeSame, DiffKind.Same, DiffKind.TimingChanged), If(timeSame, DiffKind.TextChanged, DiffKind.TextAndTiming))
                rows.Add(New DiffRow With {.A = x.Cue, .AStart = x.Start, .AEnd = x.End, .B = y.Cue, .BStart = y.Start, .BEnd = y.End, .Kind = kind})
            Else
                rows.Add(New DiffRow With {.A = x.Cue, .AStart = x.Start, .AEnd = x.End, .Kind = DiffKind.OnlyInA})
            End If
        Next
        For j = 0 To tb.Count - 1
            If Not usedB(j) Then rows.Add(New DiffRow With {.B = tb(j).Cue, .BStart = tb(j).Start, .BEnd = tb(j).End, .Kind = DiffKind.OnlyInB})
        Next
        Return rows.OrderBy(Function(r) If(r.A IsNot Nothing, r.AStart, r.BStart)).ToList()
    End Function

    ''' <summary>Word-level diff of two texts. Returns the tokens of A and of B, with Changed = not in the other text.</summary>
    Public Sub WordDiff(a As String, b As String, ByRef tokensA As List(Of DiffToken), ByRef tokensB As List(Of DiffToken))
        Dim wa = TokenRx.Matches(NormalizeText(a)).Select(Function(m) m.Value).ToArray()
        Dim wb = TokenRx.Matches(NormalizeText(b)).Select(Function(m) m.Value).ToArray()
        ' LCS table (subtitle lines are short).
        Dim lcs(wa.Length, wb.Length) As Integer
        For i = wa.Length - 1 To 0 Step -1
            For j = wb.Length - 1 To 0 Step -1
                lcs(i, j) = If(SameToken(wa(i), wb(j)), lcs(i + 1, j + 1) + 1, Math.Max(lcs(i + 1, j), lcs(i, j + 1)))
            Next
        Next
        Dim ra As New List(Of DiffToken), rb As New List(Of DiffToken)
        Dim x = 0, y = 0
        While x < wa.Length AndAlso y < wb.Length
            If SameToken(wa(x), wb(y)) Then
                ra.Add(New DiffToken With {.Text = wa(x)}) : rb.Add(New DiffToken With {.Text = wb(y)})
                x += 1 : y += 1
            ElseIf lcs(x + 1, y) >= lcs(x, y + 1) Then
                ra.Add(New DiffToken With {.Text = wa(x), .Changed = Not String.IsNullOrWhiteSpace(wa(x))}) : x += 1
            Else
                rb.Add(New DiffToken With {.Text = wb(y), .Changed = Not String.IsNullOrWhiteSpace(wb(y))}) : y += 1
            End If
        End While
        While x < wa.Length
            ra.Add(New DiffToken With {.Text = wa(x), .Changed = Not String.IsNullOrWhiteSpace(wa(x))}) : x += 1
        End While
        While y < wb.Length
            rb.Add(New DiffToken With {.Text = wb(y), .Changed = Not String.IsNullOrWhiteSpace(wb(y))}) : y += 1
        End While
        tokensA = ra
        tokensB = rb
    End Sub

    Private Function SameToken(p As String, q As String) As Boolean
        If String.IsNullOrWhiteSpace(p) AndAlso String.IsNullOrWhiteSpace(q) Then Return True
        Return String.Equals(p, q, StringComparison.Ordinal)
    End Function

    Public Function Summary(rows As List(Of DiffRow)) As String
        Dim c = Function(k As DiffKind) rows.Where(Function(r) r.Kind = k).Count()
        Return $"{rows.Count} lines: {c(DiffKind.Same)} same, {c(DiffKind.TextChanged) + c(DiffKind.TextAndTiming)} text changed, " &
               $"{c(DiffKind.TimingChanged) + c(DiffKind.TextAndTiming)} timing changed, {c(DiffKind.OnlyInA)} only in A, {c(DiffKind.OnlyInB)} only in B"
    End Function
End Module
