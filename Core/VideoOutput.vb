Imports System.Globalization
Imports System.IO
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading

''' <summary>
''' Video outputs:
'''  • MKV copy with the new subtitles added as extra tracks (no re-encoding, language tags set)
'''  • MP4 copy with the subtitles burned into the picture (re-encoded; GPU encoder when available)
''' </summary>
Public Module VideoOutput

    Private ReadOnly TimeRx As New Regex("time=(\d+):(\d+):(\d+(?:\.\d+)?)")

    ''' <summary>Duration of the media in seconds (0 if unknown).</summary>
    Public Async Function GetDurationAsync(media As String, ct As CancellationToken) As Task(Of Double)
        Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfprobeExe, $"-v quiet -print_format json -show_format ""{media}""", ct)
        Try
            Using doc = JsonDocument.Parse(r.StdOut)
                Dim fmt As JsonElement, dur As JsonElement
                If doc.RootElement.TryGetProperty("format", fmt) AndAlso fmt.TryGetProperty("duration", dur) Then
                    Return Double.Parse(dur.GetString(), CultureInfo.InvariantCulture)
                End If
            End Using
        Catch
        End Try
        Return 0
    End Function

    Private Function ProgressHandler(duration As Double, report As Action(Of Double)) As Action(Of String)
        Return Sub(line)
                   If duration <= 0 OrElse report Is Nothing Then Return
                   Dim m = TimeRx.Match(line)
                   If Not m.Success Then Return
                   Dim t = CInt(m.Groups(1).Value) * 3600 + CInt(m.Groups(2).Value) * 60 + Double.Parse(m.Groups(3).Value, CultureInfo.InvariantCulture)
                   report(Math.Min(1.0, t / duration))
               End Sub
    End Function

    ''' <summary>Existing subtitle stream codecs of the video, in order.</summary>
    Private Async Function SubtitleCodecsAsync(video As String, ct As CancellationToken) As Task(Of List(Of String))
        Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfprobeExe, $"-v quiet -print_format json -show_streams -select_streams s ""{video}""", ct)
        Dim list As New List(Of String)
        Try
            Using doc = JsonDocument.Parse(r.StdOut)
                Dim streams As JsonElement
                If doc.RootElement.TryGetProperty("streams", streams) Then
                    For Each st In streams.EnumerateArray()
                        Dim c As JsonElement
                        list.Add(If(st.TryGetProperty("codec_name", c), c.GetString(), ""))
                    Next
                End If
            End Using
        Catch
        End Try
        Return list
    End Function

    ''' <summary>Creates "Movie.subtitled.mkv" with all original streams plus the new subtitle files (first new one = default).</summary>
    Public Async Function MuxIntoMkvAsync(video As String, subs As IList(Of (Path As String, Language As LanguageInfo)),
                                          report As Action(Of Double), ct As CancellationToken) As Task(Of String)
        If subs.Count = 0 Then Return Nothing
        Dim output = Path.Combine(Path.GetDirectoryName(video), Path.GetFileNameWithoutExtension(video) & ".subtitled.mkv")
        Dim temp = output & ".part.mkv"
        Dim existing = Await SubtitleCodecsAsync(video, ct)
        Dim duration = Await GetDurationAsync(video, ct)

        Dim a As New Text.StringBuilder("-hide_banner -y")
        a.Append($" -i ""{video}""")
        For Each s In subs
            a.Append($" -sub_charenc UTF-8 -i ""{s.Path}""")
        Next
        a.Append(" -map 0:v? -map 0:a? -map 0:s? -map 0:t?")
        For i = 1 To subs.Count
            a.Append($" -map {i}:0")
        Next
        a.Append(" -c copy")
        ' MP4 text subtitles (mov_text) cannot be copied into MKV → convert them to SRT.
        For i = 0 To existing.Count - 1
            If existing(i) = "mov_text" Then a.Append($" -c:s:{i} srt")
        Next
        For k = 0 To subs.Count - 1
            Dim idx = existing.Count + k
            Dim lang = subs(k).Language
            a.Append($" -c:s:{idx} srt")
            If lang IsNot Nothing Then
                a.Append($" -metadata:s:s:{idx} language={lang.Code3(0)} -metadata:s:s:{idx} title=""{lang.Name}""")
            End If
            a.Append($" -disposition:s:{idx} {If(k = 0, "default", "0")}")
        Next
        a.Append($" -max_interleave_delta 0 ""{temp}""")

        Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfmpegExe, a.ToString(), ct, onStdErrLine:=ProgressHandler(duration, report))
        If r.ExitCode <> 0 OrElse Not File.Exists(temp) Then
            Try : File.Delete(temp) : Catch : End Try
            Throw New Exception(Loc.Tr("Could not add the subtitles to the MKV:") & " " & ProcessRunner.Tail(r.StdErr, 400))
        End If
        File.Move(temp, output, True)
        Return output
    End Function

    ''' <summary>Video encoders to try, fastest first: the GPU's hardware encoder, then x264 on the CPU.</summary>
    Public Function EncoderCandidates() As List(Of String)
        Dim list As New List(Of String)
        Select Case HardwareInfo.Gpu.Vendor
            Case GpuVendor.Nvidia : list.Add("-c:v h264_nvenc -preset p5 -cq 21 -b:v 0")
            Case GpuVendor.Amd : list.Add("-c:v h264_amf -quality quality -rc cqp -qp_i 20 -qp_p 22")
            Case GpuVendor.Intel : list.Add("-c:v h264_qsv -global_quality 21")
        End Select
        list.Add("-c:v libx264 -preset veryfast -crf 20")
        Return list
    End Function

    ''' <summary>Creates "Movie.el.hardsub.mp4" with the subtitles drawn on the picture.</summary>
    Public Async Function BurnInAsync(video As String, srt As String, lang As LanguageInfo,
                                      report As Action(Of Double), ct As CancellationToken) As Task(Of String)
        Dim output = Path.Combine(Path.GetDirectoryName(video), Path.GetFileNameWithoutExtension(video) & $".{If(lang?.Code, "sub")}.hardsub.mp4")
        Dim temp = output & ".part.mp4"
        Dim duration = Await GetDurationAsync(video, ct)

        ' The subtitles filter needs a path without drive letters/backslashes → run from a temp folder.
        Dim work = Path.Combine(Path.GetTempPath(), "sm_burn_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(work)
        Try
            File.Copy(srt, Path.Combine(work, "sub.srt"), True)
            Dim lastErr = ""
            For Each enc In EncoderCandidates()
                ct.ThrowIfCancellationRequested()
                Dim args = $"-hide_banner -y -i ""{video}"" -map 0:v:0 -map 0:a:0? " &
                           "-vf ""subtitles=sub.srt:charenc=UTF-8:force_style='Fontsize=22,Outline=1.5,Shadow=0.5,MarginV=24'"" " &
                           $"{enc} -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart ""{temp}"""
                Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfmpegExe, args, ct, workingDir:=work, onStdErrLine:=ProgressHandler(duration, report))
                If r.ExitCode = 0 AndAlso File.Exists(temp) Then
                    File.Move(temp, output, True)
                    Logger.Log($"Burned subtitles with: {enc}")
                    Return output
                End If
                lastErr = ProcessRunner.Tail(r.StdErr, 400)
                Logger.Log($"Encoder failed ({enc}), trying the next one.")
            Next
            Throw New Exception(Loc.Tr("Could not create the video with burned-in subtitles:") & " " & lastErr)
        Finally
            Try : File.Delete(temp) : Catch : End Try
            Try : Directory.Delete(work, True) : Catch : End Try
        End Try
    End Function

End Module
