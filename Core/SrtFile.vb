Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions

Public Class SrtCue
    Public Property Index As Integer
    Public Property Timing As String
    Public Property Text As String
End Class

''' <summary>Robust SRT reader/writer.</summary>
Public Module SrtFile

    Private _providerRegistered As Boolean

    ''' <summary>
    ''' Reads a text file detecting its encoding: BOM (UTF-8/UTF-16) → strict UTF-8 → the system ANSI code page
    ''' (e.g. Windows-1253 for Greek, 1252 for Western). Many downloaded SRTs are not UTF-8.
    ''' </summary>
    Public Function ReadAllTextAutoEncoding(path As String) As String
        Dim bytes = File.ReadAllBytes(path)
        If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
            Return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
        End If
        If bytes.Length >= 2 AndAlso bytes(0) = &HFF AndAlso bytes(1) = &HFE Then Return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
        If bytes.Length >= 2 AndAlso bytes(0) = &HFE AndAlso bytes(1) = &HFF Then Return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
        Try
            Return New UTF8Encoding(False, True).GetString(bytes)
        Catch ex As DecoderFallbackException
            If Not _providerRegistered Then
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
                _providerRegistered = True
            End If
            Dim cp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage
            Logger.Log($"{IO.Path.GetFileName(path)} is not UTF-8, reading as code page {cp}")
            Return Encoding.GetEncoding(cp).GetString(bytes)
        End Try
    End Function

    Public Function Load(path As String) As List(Of SrtCue)
        Dim text = ReadAllTextAutoEncoding(path).Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        Dim cues As New List(Of SrtCue)
        For Each block In Regex.Split(text.Trim(), "\n[ \t]*\n")
            Dim lines = block.Split(vbLf).Select(Function(l) l.TrimEnd()).ToList()
            Dim timingIdx = lines.FindIndex(Function(l) l.Contains("-->"))
            If timingIdx < 0 Then Continue For
            Dim cue As New SrtCue With {
                .Index = cues.Count + 1,
                .Timing = lines(timingIdx).Trim(),
                .Text = String.Join(vbLf, lines.Skip(timingIdx + 1)).Trim()
            }
            cues.Add(cue)
        Next
        Return cues
    End Function

    ''' <summary>Writes UTF-8 with BOM (best compatibility with TVs / media players) and renumbers cues.</summary>
    Public Sub Save(path As String, cues As IEnumerable(Of SrtCue))
        Dim sb As New StringBuilder()
        Dim n = 0
        For Each c In cues
            n += 1
            sb.Append(n).Append(vbCrLf)
            sb.Append(c.Timing).Append(vbCrLf)
            sb.Append(If(c.Text, "").Replace(vbLf, vbCrLf)).Append(vbCrLf)
            sb.Append(vbCrLf)
        Next
        File.WriteAllText(path, sb.ToString(), New UTF8Encoding(True))
    End Sub

    ''' <summary>"Movie.en.srt" → English; "Movie.srt" → Nothing.</summary>
    Public Function LanguageFromFileName(path As String) As LanguageInfo
        Dim stem = IO.Path.GetFileNameWithoutExtension(path)
        Dim dot = stem.LastIndexOf("."c)
        If dot < 0 Then Return Nothing
        Dim suffix = stem.Substring(dot + 1)
        If suffix.Length < 2 OrElse suffix.Length > 3 Then Return Nothing
        Return Languages.Find(suffix)
    End Function

    ''' <summary>"Movie.en.srt" + Greek → "Movie.el.srt"; "Movie.mkv" + Greek → "Movie.el.srt".</summary>
    Public Function OutputPathFor(sourcePath As String, target As LanguageInfo) As String
        Dim dir = IO.Path.GetDirectoryName(sourcePath)
        Dim stem = IO.Path.GetFileNameWithoutExtension(sourcePath)
        If IO.Path.GetExtension(sourcePath).Equals(".srt", StringComparison.OrdinalIgnoreCase) AndAlso LanguageFromFileName(sourcePath) IsNot Nothing Then
            stem = stem.Substring(0, stem.LastIndexOf("."c))
        End If
        Return IO.Path.Combine(dir, $"{stem}.{target.Code}.srt")
    End Function

End Module
