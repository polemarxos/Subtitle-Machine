Imports System.Text.RegularExpressions

''' <summary>
''' Removes texts meant for the hearing impaired, so they are not translated or shown:
'''   [MUSIC PLAYING]  (laughs)  JOHN: Hello  ♪ song lyrics ♪
''' </summary>
Public Module SubtitleCleaner

    Private ReadOnly Brackets As New Regex("\[[^\]]*\]|\([^)]*\)|\{\*[^}]*\}|＜[^＞]*＞")
    ' "JOHN:", "MAN 2:", "DR. HOUSE:", "ΓΙΑΝΝΗΣ:" at the start of a line (after an optional dash / italic tag)
    Private ReadOnly SpeakerLabel As New Regex("^(?<pre>\s*(?:<i>)?\s*-?\s*(?:<i>)?\s*)(?<name>\p{Lu}[\p{Lu}\d .'&-]{0,30}):\s*", RegexOptions.Multiline)
    Private ReadOnly MusicOnly As New Regex("^[\s♪♫#*~-]*$")
    Private ReadOnly SungLine As New Regex("^\s*(?:<i>)?\s*[♪♫#].*[♪♫#]\s*(?:</i>)?\s*$")
    Private ReadOnly EmptyTags As New Regex("<(i|b|u)>\s*</\1>", RegexOptions.IgnoreCase)
    Private ReadOnly HasText As New Regex("[\p{L}\p{N}]")

    ''' <summary>Cleans one cue. Returns an empty string when nothing is left.</summary>
    Public Function CleanText(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Dim lines = text.Replace(vbCrLf, vbLf).Split(vbLf).ToList()
        Dim result As New List(Of String)
        For Each raw In lines
            If SungLine.IsMatch(raw) Then Continue For ' ♪ song lyrics ♪
            Dim l = Brackets.Replace(raw, "")
            l = SpeakerLabel.Replace(l, Function(m) If(m.Groups("name").Value.Count(AddressOf Char.IsLetter) >= 2, m.Groups("pre").Value, m.Value))
            l = EmptyTags.Replace(l, "")
            l = Regex.Replace(l, "\s{2,}", " ").Trim()
            l = Regex.Replace(l, "<(i|b|u)>\s+", "<$1>", RegexOptions.IgnoreCase)  ' "<i> text" → "<i>text"
            l = Regex.Replace(l, "\s+</(i|b|u)>", "</$1>", RegexOptions.IgnoreCase)
            If MusicOnly.IsMatch(l) OrElse Not HasText.IsMatch(l) Then Continue For
            result.Add(l)
        Next
        ' A single remaining line does not need a dialogue dash.
        If result.Count = 1 Then result(0) = Regex.Replace(result(0), "^((?:<i>)?)\s*-\s*", "$1")
        Return String.Join(vbLf, result)
    End Function

    ''' <summary>Cleans all cues and drops the ones that become empty.</summary>
    Public Function Clean(cues As IEnumerable(Of SrtCue)) As List(Of SrtCue)
        Dim list As New List(Of SrtCue)
        For Each c In cues
            Dim t = CleanText(c.Text)
            If t.Length > 0 Then list.Add(New SrtCue With {.Index = list.Count + 1, .Timing = c.Timing, .Text = t})
        Next
        Return list
    End Function

End Module
