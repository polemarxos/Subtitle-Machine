Imports System.Globalization
Imports System.Text.RegularExpressions

''' <summary>
''' Makes subtitles comfortable to read:
'''  • lines longer than MaxLineLength are re-wrapped into two balanced lines
'''  • cues shown for less than MinDuration are extended (without overlapping the next one)
'''  • overlapping cues are trimmed
'''  • very short consecutive cues are merged when they fit on two lines
''' </summary>
Public Module SubtitleFormatter

    Public ReadOnly MinDuration As TimeSpan = TimeSpan.FromSeconds(1.0)
    Private ReadOnly MinGap As TimeSpan = TimeSpan.FromMilliseconds(40)
    Private ReadOnly MergeBelow As TimeSpan = TimeSpan.FromMilliseconds(700)

    Private ReadOnly TimingRx As New Regex("(\d+):(\d{1,2}):(\d{1,2})[,.](\d{1,3})\s*-->\s*(\d+):(\d{1,2}):(\d{1,2})[,.](\d{1,3})")
    Private ReadOnly TagRx As New Regex("<[^>]*>|\{[^}]*\}")

    Public Function TryParseTiming(timing As String, ByRef start As TimeSpan, ByRef [end] As TimeSpan) As Boolean
        Dim m = TimingRx.Match(If(timing, ""))
        If Not m.Success Then Return False
        start = ToSpan(m, 1)
        [end] = ToSpan(m, 5)
        Return True
    End Function

    Private Function ToSpan(m As Match, g As Integer) As TimeSpan
        Dim ms = m.Groups(g + 3).Value.PadRight(3, "0"c)
        Return New TimeSpan(0, CInt(m.Groups(g).Value), CInt(m.Groups(g + 1).Value), CInt(m.Groups(g + 2).Value), CInt(ms))
    End Function

    Public Function FormatTiming(start As TimeSpan, [end] As TimeSpan) As String
        Return $"{Fmt(start)} --> {Fmt([end])}"
    End Function

    Public Function Fmt(t As TimeSpan) As String
        If t < TimeSpan.Zero Then t = TimeSpan.Zero
        Return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00},{3:000}", Math.Floor(t.TotalHours), t.Minutes, t.Seconds, t.Milliseconds)
    End Function

    ''' <summary>Visible length (without &lt;i&gt; / {\an8} tags).</summary>
    Public Function VisibleLength(s As String) As Integer
        Return TagRx.Replace(s, "").Length
    End Function

    Public Function Apply(cues As List(Of SrtCue), maxLineLength As Integer) As List(Of SrtCue)
        Dim timed As New List(Of (Cue As SrtCue, Start As TimeSpan, [End] As TimeSpan))
        For Each c In cues
            Dim s As TimeSpan, e As TimeSpan
            If TryParseTiming(c.Timing, s, e) Then timed.Add((c, s, e)) Else timed.Add((c, TimeSpan.MinValue, TimeSpan.MinValue))
        Next

        ' 1) merge very short neighbours
        Dim merged As New List(Of (Cue As SrtCue, Start As TimeSpan, [End] As TimeSpan))
        For Each t In timed
            If merged.Count > 0 Then
                Dim prev = merged(merged.Count - 1)
                If prev.Start <> TimeSpan.MinValue AndAlso t.Start <> TimeSpan.MinValue AndAlso
                   prev.End - prev.Start < MergeBelow AndAlso t.Start - prev.End < TimeSpan.FromMilliseconds(250) AndAlso
                   Not prev.Cue.Text.Contains(vbLf) AndAlso Not t.Cue.Text.Contains(vbLf) AndAlso
                   VisibleLength(prev.Cue.Text) + VisibleLength(t.Cue.Text) + 1 <= maxLineLength * 2 Then
                    Dim combined As New SrtCue With {.Index = prev.Cue.Index, .Text = prev.Cue.Text & vbLf & t.Cue.Text}
                    merged(merged.Count - 1) = (combined, prev.Start, t.End)
                    Continue For
                End If
            End If
            merged.Add(t)
        Next

        ' 2) minimum duration + no overlaps, 3) line wrapping
        Dim result As New List(Of SrtCue)
        For i = 0 To merged.Count - 1
            Dim t = merged(i)
            Dim cue = t.Cue
            Dim text = Wrap(cue.Text, maxLineLength)
            Dim timing = cue.Timing
            If t.Start <> TimeSpan.MinValue Then
                Dim e = t.End
                Dim nextStart = If(i + 1 < merged.Count AndAlso merged(i + 1).Start <> TimeSpan.MinValue, merged(i + 1).Start, TimeSpan.MaxValue)
                If e - t.Start < MinDuration Then e = t.Start + MinDuration
                If nextStart <> TimeSpan.MaxValue AndAlso e > nextStart - MinGap Then e = Max(nextStart - MinGap, t.Start + TimeSpan.FromMilliseconds(100))
                timing = FormatTiming(t.Start, e)
            End If
            result.Add(New SrtCue With {.Index = result.Count + 1, .Timing = timing, .Text = text})
        Next
        Return result
    End Function

    Private Function Max(a As TimeSpan, b As TimeSpan) As TimeSpan
        Return If(a > b, a, b)
    End Function

    ''' <summary>Re-wraps text whose lines are too long into (at most) two balanced lines. Dialogue lines ("- …") are kept separate.</summary>
    Public Function Wrap(text As String, maxLen As Integer) As String
        If String.IsNullOrEmpty(text) Then Return text
        Dim lines = text.Split(vbLf)
        If lines.All(Function(l) VisibleLength(l) <= maxLen) AndAlso lines.Length <= 2 Then Return text
        ' Dialogue: keep each speaker on its own line.
        If lines.Length >= 2 AndAlso lines.All(Function(l) TagRx.Replace(l, "").TrimStart().StartsWith("-")) Then Return text

        Dim words = String.Join(" ", lines.Select(Function(l) l.Trim())).Split(" "c, StringSplitOptions.RemoveEmptyEntries)
        Dim total = VisibleLength(String.Join(" ", words))
        If total <= maxLen Then Return String.Join(" ", words)
        ' Find the split that best balances two lines (prefers breaking after punctuation).
        Dim bestIdx = -1, bestScore = Double.MaxValue
        Dim left = 0
        For i = 0 To words.Length - 2
            left += VisibleLength(words(i)) + If(i > 0, 1, 0)
            Dim right = total - left - 1
            Dim longest = Math.Max(left, right)
            Dim score As Double = Math.Abs(left - right)
            Dim lastChar = TagRx.Replace(words(i), "").LastOrDefault()
            If longest <= maxLen Then
                ' Prefer natural breaks: after a sentence, then after a comma.
                If ".!?…".Contains(lastChar) Then score *= 0.1 Else If ",;:".Contains(lastChar) Then score *= 0.5
            Else
                score += (longest - maxLen) * 10
            End If
            If score < bestScore Then bestScore = score : bestIdx = i
        Next
        If bestIdx < 0 Then Return text
        Return String.Join(" ", words.Take(bestIdx + 1)) & vbLf & String.Join(" ", words.Skip(bestIdx + 1))
    End Function

End Module
