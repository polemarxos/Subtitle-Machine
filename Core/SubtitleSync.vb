Imports System.IO
Imports System.Threading

''' <summary>
''' Fixes subtitles that are early, late or drift (e.g. a 25 fps subtitle on a 23.976 fps video)
''' by matching them to the speech in the video's audio:
'''   1. ffmpeg decodes the audio (mono, 16 kHz, speech band only);
'''   2. a small voice-activity detector marks every 10 ms as speech / no speech;
'''   3. the subtitle "on" times are cross-correlated with that (FFT), for every usual frame-rate ratio;
'''   4. the best match is used only when it is clearly better than the current timing.
''' </summary>
Public Module SubtitleSync

    Public Const FrameMs As Integer = 10
    Private Const SampleRate As Integer = 16000
    Private Const SamplesPerFrame As Integer = SampleRate * FrameMs \ 1000

    ''' <summary>Frame-rate ratios between the subtitle and the video (subtitle time × ratio = video time).</summary>
    Public ReadOnly Ratios As Double() = {
        1.0,
        25 / (24000 / 1001.0), (24000 / 1001.0) / 25,
        25 / 24.0, 24 / 25.0,
        24 / (24000 / 1001.0), (24000 / 1001.0) / 24,
        (30000 / 1001.0) / 25, 25 / (30000 / 1001.0)
    }

    Public Class SyncResult
        ''' <summary>Seconds added after scaling.</summary>
        Public Property Offset As Double
        Public Property Ratio As Double = 1
        ''' <summary>How clearly the best match stands out (peak height in standard deviations).</summary>
        Public Property Confidence As Double
        Public Property Score As Double
        Public Property CurrentScore As Double
        ''' <summary>True when the timing should be changed.</summary>
        Public Property ShouldApply As Boolean
        Public Property Reason As String

        Public Function Describe() As String
            Dim parts As New List(Of String)
            If Math.Abs(Offset) >= 0.005 Then parts.Add(If(Offset > 0, "+", "−") & Math.Abs(Offset).ToString("0.00") & " s")
            If Math.Abs(Ratio - 1) > 0.0001 Then parts.Add(DescribeRatio(Ratio))
            Return If(parts.Count = 0, "0 s", String.Join(", ", parts))
        End Function

        Private Shared Function DescribeRatio(r As Double) As String
            Dim names = New(Value As Double, Name As String)() {
                (24000 / 1001.0, "23.976"), (24.0, "24"), (25.0, "25"), (30000 / 1001.0, "29.97")}
            For Each a In names
                For Each b In names
                    If Math.Abs(a.Value / b.Value - r) < 0.00001 Then Return $"{a.Name} → {b.Name} fps"
                Next
            Next
            Return "×" & r.ToString("0.0000")
        End Function
    End Class

    Public Const MinConfidence As Double = 8.0

    ' ------------------------------------------------------------------
    '  Audio → speech frames
    ' ------------------------------------------------------------------

    ''' <summary>Decodes the audio of a media file with ffmpeg and returns speech (1) / no speech (0) for every 10 ms.</summary>
    Public Async Function DetectSpeechAsync(ffmpegExe As String, media As String, audioStreamIndex As Integer, ct As CancellationToken,
                                            Optional progress As Action(Of Double) = Nothing, Optional durationSeconds As Double = 0) As Task(Of Byte())
        Dim map = $"-map 0:a:{Math.Max(0, audioStreamIndex)}" ' index among the audio streams
        Dim args = $"-nostdin -v error -i ""{media}"" {map} -vn -sn -dn -ac 1 -ar {SampleRate} -af ""highpass=f=200,lowpass=f=3800"" -f s16le -"
        Logger.Log($"RUN: ""{ffmpegExe}"" {args}")
        Dim psi As New ProcessStartInfo(ffmpegExe, args) With {
            .UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True
        }
        Dim energies As New List(Of Single)(1 << 20)
        Using p As New Process With {.StartInfo = psi}
            p.Start()
            Dim errTask = p.StandardError.ReadToEndAsync()
            Dim stream = p.StandardOutput.BaseStream
            Dim buf(SamplesPerFrame * 2 * 256 - 1) As Byte
            Dim carry(SamplesPerFrame * 2 - 1) As Byte
            Dim carryLen = 0
            Try
                Do
                    Dim n = Await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct)
                    If n <= 0 Then Exit Do
                    Dim pos = 0
                    ' finish a frame started in the previous read
                    If carryLen > 0 Then
                        Dim take = Math.Min(carry.Length - carryLen, n)
                        Array.Copy(buf, 0, carry, carryLen, take)
                        carryLen += take : pos = take
                        If carryLen = carry.Length Then energies.Add(FrameEnergy(carry, 0)) : carryLen = 0
                    End If
                    While n - pos >= carry.Length
                        energies.Add(FrameEnergy(buf, pos))
                        pos += carry.Length
                    End While
                    If n - pos > 0 Then Array.Copy(buf, pos, carry, 0, n - pos) : carryLen = n - pos
                    If progress IsNot Nothing AndAlso durationSeconds > 0 AndAlso energies.Count Mod 3000 = 0 Then
                        progress(Math.Min(1, energies.Count * FrameMs / 1000.0 / durationSeconds))
                    End If
                Loop
            Catch ex As OperationCanceledException
                KillQuietly(p)
                Throw
            End Try
            p.WaitForExit()
            Dim err = Await errTask
            If energies.Count = 0 Then Throw New Exception(Loc.Tr("Could not read the audio of the video.") & " " & Tail(err, 300))
        End Using
        Return SpeechFromEnergy(energies.ToArray())
    End Function

    ''' <summary>Log energy (dB) of one 10 ms frame of 16-bit PCM.</summary>
    Private Function FrameEnergy(b As Byte(), offset As Integer) As Single
        Dim sum As Double = 0
        For i = 0 To SamplesPerFrame - 1
            Dim s = CShort(BitConverter.ToInt16(b, offset + i * 2))
            sum += CDbl(s) * s
        Next
        Return CSng(10 * Math.Log10(sum / SamplesPerFrame + 1))
    End Function

    ''' <summary>
    ''' Simple voice activity detection: a frame is speech when its (lightly smoothed) energy is well above
    ''' the local noise floor (the quietest moment within ±1.5 s). Steady music or noise raises the floor
    ''' and is therefore ignored; speech, with its pauses between words, stands out.
    ''' </summary>
    Private Const FloorRadius As Integer = 150
    Private Const MinRiseDb As Single = 6
    Private Const RiseShare As Single = 0.25F

    Public Function SpeechFromEnergy(energyDb As Single()) As Byte()
        Dim n = energyDb.Length
        Dim result(n - 1) As Byte
        If n = 0 Then Return result
        ' smooth over 30 ms
        Dim sm(n - 1) As Single
        For i = 0 To n - 1
            Dim a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1)
            Dim s As Single = 0
            For j = a To b : s += energyDb(j) : Next
            sm(i) = s / (b - a + 1)
        Next
        Dim floor = SlidingMin(sm, FloorRadius)
        Dim sorted = CType(sm.Clone(), Single()) : Array.Sort(sorted)
        Dim globalQuiet = sorted(CInt(n * 0.1))
        Dim globalLoud = sorted(Math.Min(n - 1, CInt(n * 0.95)))
        Dim minRise = Math.Max(MinRiseDb, (globalLoud - globalQuiet) * RiseShare)
        For i = 0 To n - 1
            If sm(i) > floor(i) + minRise AndAlso sm(i) > globalQuiet + 6 Then result(i) = 1
        Next
        ' fill tiny gaps (< 150 ms) inside speech and drop clicks (< 60 ms)
        FillRuns(result, 0, 15)
        FillRuns(result, 1, 6)
        Return result
    End Function

    ''' <summary>Minimum over a window of ±radius frames (monotonic deque, O(n)).</summary>
    Private Function SlidingMin(x As Single(), radius As Integer) As Single()
        Dim n = x.Length
        Dim r(n - 1) As Single
        Dim dq As New LinkedList(Of Integer)
        Dim nextIn = 0
        For i = 0 To n - 1
            While nextIn <= Math.Min(n - 1, i + radius)
                While dq.Count > 0 AndAlso x(dq.Last.Value) >= x(nextIn) : dq.RemoveLast() : End While
                dq.AddLast(nextIn) : nextIn += 1
            End While
            While dq.First.Value < i - radius : dq.RemoveFirst() : End While
            r(i) = x(dq.First.Value)
        Next
        Return r
    End Function

    ''' <summary>Replaces runs of <paramref name="value"/> shorter than maxLen frames that lie between other values.</summary>
    Private Sub FillRuns(x As Byte(), value As Byte, maxLen As Integer)
        Dim i = 0
        While i < x.Length
            If x(i) <> value Then i += 1 : Continue While
            Dim j = i
            While j < x.Length AndAlso x(j) = value : j += 1 : End While
            If i > 0 AndAlso j < x.Length AndAlso j - i < maxLen Then
                For k = i To j - 1 : x(k) = CByte(1 - value) : Next
            End If
            i = j
        End While
    End Sub

    ' ------------------------------------------------------------------
    '  Matching
    ' ------------------------------------------------------------------

    ''' <summary>Finds the offset and frame-rate ratio that best match the subtitles to the speech.</summary>
    Public Function FindBest(speech As Byte(), cues As IList(Of (Start As Double, [End] As Double)),
                             Optional maxOffsetSeconds As Double = 300) As SyncResult
        Dim res As New SyncResult With {.Reason = "no match"}
        If speech.Length < 100 OrElse cues.Count < 5 Then
            res.Reason = "too little speech or too few lines"
            Return res
        End If
        Dim maxOff = CInt(Math.Min(maxOffsetSeconds, speech.Length * FrameMs / 1000.0 * 0.5) * 1000 / FrameMs)
        Dim subEnd = cues.Max(Function(c) c.End) * Ratios.Max()
        Dim len = Math.Max(speech.Length, CInt(subEnd * 1000 / FrameMs) + 1)
        Dim size = 1
        While size < len + maxOff + 1 : size <<= 1 : End While

        ' speech: zero-mean signal, FFT once
        Dim sMean = speech.Average(Function(b) CDbl(b))
        Dim sRe(size - 1) As Double, sIm(size - 1) As Double
        Dim sNorm As Double = 0
        For i = 0 To speech.Length - 1
            sRe(i) = speech(i) - sMean
            sNorm += sRe(i) * sRe(i)
        Next
        sNorm = Math.Sqrt(sNorm)
        If sNorm = 0 Then res.Reason = "no speech found" : Return res
        Fft(sRe, sIm, False)

        Dim best As Double = Double.MinValue
        For Each ratio In Ratios
            Dim u = SubtitleSignal(cues, ratio, len)
            Dim uMean = u.Average()
            Dim uRe(size - 1) As Double, uIm(size - 1) As Double
            Dim uNorm As Double = 0
            For i = 0 To len - 1
                uRe(i) = u(i) - uMean
                uNorm += uRe(i) * uRe(i)
            Next
            uNorm = Math.Sqrt(uNorm)
            If uNorm = 0 Then Continue For
            Fft(uRe, uIm, False)
            ' correlation(k) = Σ s(t)·u(t−k)  →  IFFT( S · conj(U) )
            Dim cRe(size - 1) As Double, cIm(size - 1) As Double
            For i = 0 To size - 1
                cRe(i) = sRe(i) * uRe(i) + sIm(i) * uIm(i)
                cIm(i) = sIm(i) * uRe(i) - sRe(i) * uIm(i)
            Next
            Fft(cRe, cIm, True)
            Dim norm = sNorm * uNorm
            ' scores for offsets −maxOff…+maxOff
            Dim scores(2 * maxOff) As Double
            For k = -maxOff To maxOff
                Dim idx = If(k >= 0, k, size + k)
                scores(k + maxOff) = cRe(idx) / norm
            Next
            Dim bi = 0
            For i = 1 To scores.Length - 1
                If scores(i) > scores(bi) Then bi = i
            Next
            If ratio = 1.0 Then res.CurrentScore = scores(maxOff)
            If scores(bi) > best Then
                best = scores(bi)
                res.Score = scores(bi)
                res.Ratio = ratio
                res.Offset = (bi - maxOff) * FrameMs / 1000.0
                res.Confidence = PeakConfidence(scores, bi)
            End If
        Next

        ' Fine pass: subtitle start times usually match the moment speech begins (end times linger),
        ' so line the starts up with speech onsets within ±1 s of the coarse result.
        res.Offset += FineOffset(speech, cues, res.Ratio, res.Offset)

        Dim changes = Math.Abs(res.Offset) >= 0.15 OrElse Math.Abs(res.Ratio - 1) > 0.0001
        If Not changes Then
            res.Reason = "already in sync"
        ElseIf res.Confidence < MinConfidence Then
            res.Reason = $"match not clear enough (confidence {res.Confidence:0.0})"
        ElseIf res.Score < res.CurrentScore + 0.03 Then
            res.Reason = "not clearly better than the current timing"
        Else
            res.ShouldApply = True
            res.Reason = "ok"
        End If
        Return res
    End Function

    ''' <summary>How far the peak stands above the other offsets (excluding ±2 s around it), in standard deviations.</summary>
    Private Function PeakConfidence(scores As Double(), peak As Integer) As Double
        Dim excl = 2000 \ FrameMs
        Dim sum As Double = 0, sum2 As Double = 0, n = 0
        For i = 0 To scores.Length - 1
            If Math.Abs(i - peak) <= excl Then Continue For
            sum += scores(i) : sum2 += scores(i) * scores(i) : n += 1
        Next
        If n < 10 Then Return 0
        Dim mean = sum / n
        Dim sd = Math.Sqrt(Math.Max(1.0E-12, sum2 / n - mean * mean))
        Return (scores(peak) - mean) / sd
    End Function

    Private Function FineOffset(speech As Byte(), cues As IList(Of (Start As Double, [End] As Double)), ratio As Double, offset As Double) As Double
        ' onset density: every 0→1 change after at least 200 ms of silence, spread with a triangle of ±150 ms
        Dim n = speech.Length
        Dim density(n - 1) As Double
        Const spread As Integer = 15
        Dim quiet = 0
        For i = 0 To n - 1
            If speech(i) = 0 Then quiet += 1 : Continue For
            If i > 0 AndAlso speech(i - 1) = 0 AndAlso quiet >= 20 Then
                For d = -spread To spread
                    Dim j = i + d
                    If j >= 0 AndAlso j < n Then density(j) += 1 - Math.Abs(d) / (spread + 1.0)
                Next
            End If
            quiet = 0
        Next
        Dim range = 1000 \ FrameMs
        Dim bestK = 0, bestScore = Double.MinValue
        For k = -range To range
            Dim score As Double = 0
            For Each c In cues
                Dim f = CInt(Math.Round((c.Start * ratio + offset) * 1000 / FrameMs)) + k
                If f >= 0 AndAlso f < n Then score += density(f)
            Next
            ' prefer the smallest correction when scores are equal
            If score > bestScore + 0.000001 OrElse (Math.Abs(score - bestScore) <= 0.000001 AndAlso Math.Abs(k) < Math.Abs(bestK)) Then
                bestScore = score : bestK = k
            End If
        Next
        Return bestK * FrameMs / 1000.0
    End Function

    Private Function SubtitleSignal(cues As IList(Of (Start As Double, [End] As Double)), ratio As Double, len As Integer) As Double()
        Dim u(len - 1) As Double
        For Each c In cues
            Dim a = Math.Max(0, CInt(c.Start * ratio * 1000 / FrameMs))
            Dim b = Math.Min(len - 1, CInt(c.End * ratio * 1000 / FrameMs))
            For i = a To b : u(i) = 1 : Next
        Next
        Return u
    End Function

    ''' <summary>In-place iterative radix-2 FFT (length must be a power of two).</summary>
    Public Sub Fft(re As Double(), im As Double(), inverse As Boolean)
        Dim n = re.Length
        Dim j = 0
        For i = 1 To n - 1
            Dim bit = n >> 1
            While (j And bit) <> 0 : j = j Xor bit : bit >>= 1 : End While
            j = j Xor bit
            If i < j Then
                Dim t = re(i) : re(i) = re(j) : re(j) = t
                t = im(i) : im(i) = im(j) : im(j) = t
            End If
        Next
        Dim len = 2
        While len <= n
            Dim ang = 2 * Math.PI / len * If(inverse, 1, -1)
            Dim wRe = Math.Cos(ang), wIm = Math.Sin(ang)
            Dim half = len \ 2
            For i = 0 To n - 1 Step len
                Dim cRe = 1.0, cIm = 0.0
                For k = 0 To half - 1
                    Dim a = i + k, b = i + k + half
                    Dim tRe = re(b) * cRe - im(b) * cIm
                    Dim tIm = re(b) * cIm + im(b) * cRe
                    re(b) = re(a) - tRe : im(b) = im(a) - tIm
                    re(a) += tRe : im(a) += tIm
                    Dim nRe = cRe * wRe - cIm * wIm
                    cIm = cRe * wIm + cIm * wRe : cRe = nRe
                Next
            Next
            len <<= 1
        End While
        If inverse Then
            For i = 0 To n - 1 : re(i) /= n : im(i) /= n : Next
        End If
    End Sub

    ' ------------------------------------------------------------------
    '  Cues
    ' ------------------------------------------------------------------

    Public Function CueTimes(cues As IEnumerable(Of SrtCue)) As List(Of (Start As Double, [End] As Double))
        Dim list As New List(Of (Start As Double, [End] As Double))
        For Each c In cues
            Dim s As TimeSpan, e As TimeSpan
            If String.IsNullOrWhiteSpace(c.Text) OrElse Not SubtitleFormatter.TryParseTiming(c.Timing, s, e) Then Continue For
            list.Add((s.TotalSeconds, e.TotalSeconds))
        Next
        Return list
    End Function

    ''' <summary>Returns new cues with every time changed to time × ratio + offset (negative times become 0).</summary>
    Public Function Apply(cues As IEnumerable(Of SrtCue), r As SyncResult) As List(Of SrtCue)
        Dim result As New List(Of SrtCue)
        For Each c In cues
            Dim s As TimeSpan, e As TimeSpan
            Dim timing = c.Timing
            If SubtitleFormatter.TryParseTiming(c.Timing, s, e) Then
                timing = SubtitleFormatter.FormatTiming(Shift(s, r), Shift(e, r))
            End If
            result.Add(New SrtCue With {.Index = c.Index, .Timing = timing, .Text = c.Text})
        Next
        Return result
    End Function

    Public Function Shift(t As TimeSpan, r As SyncResult) As TimeSpan
        Return TimeSpan.FromMilliseconds(Math.Max(0, Math.Round((t.TotalSeconds * r.Ratio + r.Offset) * 1000)))
    End Function

    ''' <summary>Detects speech and syncs the subtitle file; writes the result to outputSrt when it should be changed.</summary>
    Public Async Function SyncFileAsync(ffmpegExe As String, media As String, audioStreamIndex As Integer, inputSrt As String, outputSrt As String,
                                        ct As CancellationToken, Optional progress As Action(Of Double) = Nothing,
                                        Optional durationSeconds As Double = 0) As Task(Of SyncResult)
        Dim speech = Await DetectSpeechAsync(ffmpegExe, media, audioStreamIndex, ct, progress, durationSeconds)
        Dim cues = SrtFile.Load(inputSrt)
        Dim r = FindBest(speech, CueTimes(cues))
        Logger.Log($"Sync {Path.GetFileName(inputSrt)}: {r.Describe()} confidence {r.Confidence:0.0} score {r.Score:0.000} (current {r.CurrentScore:0.000}) → {r.Reason}")
        If r.ShouldApply Then SrtFile.Save(outputSrt, Apply(cues, r))
        Return r
    End Function

    ''' <summary>The video next to a subtitle file: "Movie.en.srt" / "Movie.srt" → "Movie.mkv" (any video extension).</summary>
    Public Function FindVideoFor(srtPath As String) As String
        Dim dir = Path.GetDirectoryName(srtPath)
        If String.IsNullOrEmpty(dir) OrElse Not Directory.Exists(dir) Then Return Nothing
        Dim stem = Path.GetFileNameWithoutExtension(srtPath)
        Dim candidates As New List(Of String) From {stem}
        ' strip ".en", ".el.forced", ".sdh" … one part at a time
        Dim s = stem
        While s.Contains("."c)
            s = s.Substring(0, s.LastIndexOf("."c))
            candidates.Add(s)
        End While
        Dim videos = Directory.EnumerateFiles(dir).Where(Function(f) SubtitlePipeline.VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
        For Each c In candidates
            Dim match = videos.FirstOrDefault(Function(v) String.Equals(Path.GetFileNameWithoutExtension(v), c, StringComparison.OrdinalIgnoreCase))
            If match IsNot Nothing Then Return match
        Next
        ' a folder with a single video (typical for downloaded movies with differently named subtitles)
        Return If(videos.Count = 1, videos(0), Nothing)
    End Function

End Module
