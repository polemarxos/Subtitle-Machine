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
        ''' <summary>Extra seconds per line (lines with text, in file order) on top of ratio and offset; Nothing = none.</summary>
        Public Property LineShifts As Double()
        ''' <summary>Number of parts of the file that were moved separately.</summary>
        Public Property PartsMoved As Integer

        Public Function Describe() As String
            Dim parts As New List(Of String)
            If Math.Abs(Offset) >= 0.005 Then parts.Add(If(Offset > 0, "+", "−") & Math.Abs(Offset).ToString("0.00") & " s")
            If Math.Abs(Ratio - 1) > 0.0001 Then parts.Add(DescribeRatio(Ratio))
            If PartsMoved > 0 Then parts.Add(Loc.Tr("{0} part(s) moved separately", PartsMoved))
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

    ''' <summary>
    ''' Speech detection with the Silero voice model of whisper.cpp (whisper-vad-speech-segments.exe + ggml-silero-*.bin).
    ''' Much better than loudness at ignoring music, laughter and noise. Returns Nothing when it is not available.
    ''' </summary>
    Public Async Function DetectSpeechSileroAsync(ffmpegExe As String, vadExe As String, vadModel As String, media As String, audioStreamIndex As Integer,
                                                  ct As CancellationToken, Optional progress As Action(Of Double) = Nothing,
                                                  Optional durationSeconds As Double = 0) As Task(Of Byte())
        If vadExe Is Nothing OrElse vadModel Is Nothing OrElse Not File.Exists(vadExe) OrElse Not File.Exists(vadModel) Then Return Nothing
        Dim temp = Path.Combine(Path.GetTempPath(), "subtitle_machine_vad_" & Guid.NewGuid().ToString("N") & ".wav")
        Try
            Dim ff = Await RunAsync(ffmpegExe, $"-nostdin -hide_banner -y -i ""{media}"" -map 0:a:{Math.Max(0, audioStreamIndex)} -vn -sn -dn -ac 1 -ar 16000 -c:a pcm_s16le ""{temp}""", ct,
                                    onStdErrLine:=Sub(line)
                                                      If progress Is Nothing OrElse durationSeconds <= 0 Then Return
                                                      Dim m = Text.RegularExpressions.Regex.Match(line, "time=(\d+):(\d+):(\d+(?:\.\d+)?)")
                                                      If m.Success Then progress(Math.Min(1, (CInt(m.Groups(1).Value) * 3600 + CInt(m.Groups(2).Value) * 60 +
                                                                                    Double.Parse(m.Groups(3).Value, Globalization.CultureInfo.InvariantCulture)) / durationSeconds) * 0.3)
                                                  End Sub)
            If ff.ExitCode <> 0 OrElse Not File.Exists(temp) Then Return Nothing
            Dim seconds = (New FileInfo(temp).Length - 44) / 32000.0
            Dim threads = Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1))
            Dim r = Await RunAsync(vadExe, $"-vm ""{vadModel}"" -f ""{temp}"" -np -t {threads} -vsd 100 -vp 30 -vspd 150", ct)
            If r.ExitCode <> 0 Then Return Nothing
            progress?.Invoke(1)
            Return ParseSileroSegments(r.StdOut, seconds)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Logger.Log("Silero speech detection failed, using the loudness detector: " & ex.Message)
            Return Nothing
        Finally
            Try : File.Delete(temp) : Catch : End Try
        End Try
    End Function

    ''' <summary>"Speech segment 3: start = 1747.00, end = 1878.00" (centiseconds; older builds print seconds) → speech per 10 ms.</summary>
    Public Function ParseSileroSegments(output As String, audioSeconds As Double) As Byte()
        Dim segs As New List(Of (A As Double, B As Double))
        For Each m As Text.RegularExpressions.Match In Text.RegularExpressions.Regex.Matches(If(output, ""), "start\s*=\s*([\d.]+),\s*end\s*=\s*([\d.]+)")
            segs.Add((Double.Parse(m.Groups(1).Value, Globalization.CultureInfo.InvariantCulture), Double.Parse(m.Groups(2).Value, Globalization.CultureInfo.InvariantCulture)))
        Next
        If segs.Count = 0 Then Return Nothing
        ' centiseconds or seconds? the last segment cannot end long after the audio does
        Dim factor = If(segs.Max(Function(s) s.B) > audioSeconds * 1.5 + 5, 1.0, 100.0) ' → frames of 10 ms
        Dim n = CInt(Math.Ceiling(audioSeconds * 1000 / FrameMs)) + 1
        Dim speech(n - 1) As Byte
        For Each s In segs
            Dim a = Math.Max(0, CInt(s.A * factor)), b = Math.Min(n - 1, CInt(s.B * factor))
            For i = a To b : speech(i) = 1 : Next
        Next
        Return speech
    End Function

    ''' <summary>Silero when available, otherwise the built-in loudness detector.</summary>
    Public Async Function DetectSpeechBestAsync(ffmpegExe As String, media As String, audioStreamIndex As Integer, ct As CancellationToken,
                                                Optional progress As Action(Of Double) = Nothing, Optional durationSeconds As Double = 0) As Task(Of Byte())
        Dim speech = Await DetectSpeechSileroAsync(ffmpegExe, WhisperEngineComponent.FindVadExe(), WhisperVadComponent.FindModel(), media, audioStreamIndex, ct, progress, durationSeconds)
        If speech IsNot Nothing AndAlso speech.Any(Function(b) b = 1) Then Return speech
        Return Await DetectSpeechAsync(ffmpegExe, media, audioStreamIndex, ct, progress, durationSeconds)
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
        Dim ratio1Offset As Double = 0
        ' buffers reused for every ratio (they can be tens of MB for a long film)
        Dim uRe(size - 1) As Double, uIm(size - 1) As Double
        Dim cRe(size - 1) As Double, cIm(size - 1) As Double
        For Each ratio In Ratios
            Dim u = SubtitleSignal(cues, ratio, len)
            Dim uMean = u.Average()
            Array.Clear(uRe) : Array.Clear(uIm)
            Dim uNorm As Double = 0
            For i = 0 To len - 1
                uRe(i) = u(i) - uMean
                uNorm += uRe(i) * uRe(i)
            Next
            uNorm = Math.Sqrt(uNorm)
            If uNorm = 0 Then Continue For
            Fft(uRe, uIm, False)
            ' correlation(k) = Σ s(t)·u(t−k)  →  IFFT( S · conj(U) )
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
            If ratio = 1.0 Then
                res.CurrentScore = scores(maxOff)
                ratio1Offset = (bi - maxOff) * FrameMs / 1000.0
            End If
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

        If res.Confidence < MinConfidence Then
            ' No clear match at all: probably subtitles of another video (or another cut everywhere) – change nothing.
            res.Offset = 0 : res.Ratio = 1
            res.Reason = $"match not clear enough (confidence {res.Confidence:0.0})"
            Return res
        End If
        Dim fine1 = ratio1Offset + FineOffset(speech, cues, 1.0, ratio1Offset)

        ' Candidates for the whole-file correction; each gets the part-by-part pass and the best total wins.
        ' (A file where only the second half is late can look like a small frame-rate change as a whole.)
        Dim candidates As New List(Of (Ratio As Double, Offset As Double))
        candidates.Add((1.0, 0.0))
        If Math.Abs(res.Offset) >= 0.15 OrElse Math.Abs(res.Ratio - 1) > 0.0001 Then candidates.Add((res.Ratio, res.Offset))
        If Math.Abs(fine1) >= 0.15 AndAlso Math.Abs(res.Ratio - 1) > 0.0001 Then candidates.Add((1.0, fine1))
        Dim bestValue = Double.MinValue
        Dim chosen = candidates(0)
        Dim chosenSeg As (Shifts As Double(), Total As Double, Parts As Integer) = Nothing
        For ci = 0 To candidates.Count - 1
            Dim c = candidates(ci)
            Dim seg = SegmentShifts(speech, cues, c.Ratio, c.Offset)
            Dim value = seg.Total - PartCost * seg.Parts - If(ci = 0, 0, GlobalCost)
            If value > bestValue Then bestValue = value : chosen = c : chosenSeg = seg
        Next
        res.Ratio = chosen.Ratio
        res.Offset = chosen.Offset
        If chosenSeg.Parts > 0 Then
            res.LineShifts = chosenSeg.Shifts
            res.PartsMoved = chosenSeg.Parts
        End If
        Dim changes = Math.Abs(res.Offset) >= 0.15 OrElse Math.Abs(res.Ratio - 1) > 0.0001 OrElse res.PartsMoved > 0
        If Not changes Then
            res.Offset = 0
            res.Reason = "already in sync"
        Else
            res.ShouldApply = True
            res.Reason = "ok"
        End If
        Return res
    End Function

    ' ------------------------------------------------------------------
    '  Part-by-part correction (a different cut: intro, recap or ad break removed / added)
    ' ------------------------------------------------------------------

    ''' <summary>Largest correction for a part of the file or a single line, on top of the whole-file correction.</summary>
    Private Const SegmentRangeSeconds As Double = 30
    ''' <summary>Weight of a speech onset at the start of a line (in frames of speech).</summary>
    Private Const OnsetWeight As Double = 40
    ''' <summary>A part is moved only when at least this many lines in a row agree.</summary>
    Private Const MinPartLines As Integer = 5
    ''' <summary>Cost of every separately moved part or line, and of a whole-file change, when choosing between corrections.</summary>
    Private Const PartCost As Double = 150
    Private Const GlobalCost As Double = 30

    ''' <summary>
    ''' Extra shift per line (seconds) for files where only some lines are off:
    '''  • parts: every line gets its own best position; a run of at least MinPartLines lines whose
    '''    positions agree (median of 7 neighbours) is moved together (a different cut, a removed intro…);
    ''' Single lines that were moved on their own are not corrected: one line alone fits almost anywhere
    ''' in dense dialogue, so its right place cannot be found reliably.
    ''' Also returns how well all lines then match the speech, and the number of moved parts / lines.
    ''' </summary>
    Private Function SegmentShifts(speech As Byte(), cues As IList(Of (Start As Double, [End] As Double)), ratio As Double, offset As Double) As (Shifts As Double(), Total As Double, Parts As Integer)
        Dim n = cues.Count
        Dim result(n - 1) As Double
        Dim frames = speech.Length
        Dim p = speech.Average(Function(b) CDbl(b))
        If n < MinPartLines OrElse frames < 100 OrElse p <= 0 OrElse p >= 1 Then Return (result, 0, 0)
        Dim neg = -p / (1 - p)
        Dim prefix(frames) As Double
        For i = 0 To frames - 1 : prefix(i + 1) = prefix(i) + If(speech(i) = 1, 1.0, neg) : Next
        Dim onset = OnsetDensity(speech)
        Dim range = CInt(SegmentRangeSeconds * 1000 / FrameMs)

        ' line positions (frames) after the whole-file correction; the ~0.3 s a subtitle stays after the speech is ignored
        Dim st(n - 1) As Integer, en(n - 1) As Integer
        For i = 0 To n - 1
            st(i) = CInt(Math.Round((cues(i).Start * ratio + offset) * 1000 / FrameMs))
            en(i) = Math.Max(st(i) + 10, CInt(Math.Round((cues(i).End * ratio + offset) * 1000 / FrameMs)) - 30)
        Next
        Dim score = Function(i As Integer, k As Integer) As Double
                        Dim a = Math.Min(frames, Math.Max(0, st(i) + k)), b = Math.Min(frames, Math.Max(0, en(i) + k))
                        Dim v = prefix(b) - prefix(a)
                        If st(i) + k >= 0 AndAlso st(i) + k < frames Then v += OnsetWeight * onset(st(i) + k)
                        Return v
                    End Function

        ' 1) best position of every line: judged together with its neighbours (one line alone fits almost anywhere
        '    in dense dialogue; the pattern of 9 lines and their pauses does not).
        Const half As Integer = 4
        Dim local(n - 1) As Integer
        Dim keys As New List(Of Integer)
        For k = -range To range Step 2 : keys.Add(k) : Next
        Dim wScores(n - 1)() As Double
        For i = 0 To n - 1 : wScores(i) = New Double(keys.Count - 1) {} : Next
        Dim sc(n - 1) As Double
        For ki = 0 To keys.Count - 1
            Dim k = keys(ki)
            For i = 0 To n - 1 : sc(i) = score(i, k) : Next
            Dim run As Double = 0
            For i = 0 To Math.Min(n - 1, half) : run += sc(i) : Next
            For i = 0 To n - 1
                wScores(i)(ki) = run
                If i + half + 1 < n Then run += sc(i + half + 1)
                If i - half >= 0 Then run -= sc(i - half)
            Next
        Next
        For i = 0 To n - 1
            Dim bw = 0
            For ki = 1 To keys.Count - 1
                If wScores(i)(ki) > wScores(i)(bw) OrElse (wScores(i)(ki) = wScores(i)(bw) AndAlso Math.Abs(keys(ki)) < Math.Abs(keys(bw))) Then bw = ki
            Next
            local(i) = keys(bw)
        Next
        wScores = Nothing

        ' 2) parts: runs of lines whose (neighbourhood) positions agree
        Dim med(n - 1) As Integer
        For i = 0 To n - 1
            Dim lo = Math.Max(0, i - 2), hi = Math.Min(n - 1, i + 2)
            Dim w = Enumerable.Range(lo, hi - lo + 1).Select(Function(j) local(j)).OrderBy(Function(x) x).ToList()
            med(i) = w(w.Count \ 2)
        Next
        Dim shiftF(n - 1) As Integer
        Dim inPart(n - 1) As Boolean
        Dim parts = 0
        Dim r = 0
        While r < n
            If Math.Abs(med(r)) < 30 Then r += 1 : Continue While
            Dim e = r
            While e + 1 < n AndAlso Math.Abs(med(e + 1)) >= 30 AndAlso Math.Abs(med(e + 1) - med(e)) <= 30 AndAlso Math.Abs(med(e + 1) - med(r)) <= 100
                e += 1
            End While
            If e - r + 1 >= MinPartLines Then
                ' one shift for the whole part: best total score near the median
                Dim center = med(r + (e - r) \ 2)
                Dim bestK = center, bestV = Double.MinValue
                For k = center - 60 To center + 60
                    Dim v As Double = 0
                    For i = r To e : v += score(i, k) : Next
                    If v > bestV Then bestV = v : bestK = k
                Next
                Dim baseV As Double = 0
                For i = r To e : baseV += score(i, 0) : Next
                If bestV > baseV + 20 * (e - r + 1) Then
                    For i = r To e : shiftF(i) = bestK : inPart(i) = True : Next
                    parts += 1
                    ' the edges: neighbourhood windows blur the exact first / last line of a part
                    Dim a = r - 1
                    While a >= 0 AndAlso Not inPart(a) AndAlso r - a <= half + 1 AndAlso score(a, bestK) > score(a, shiftF(a)) + 10
                        shiftF(a) = bestK : inPart(a) = True : a -= 1
                    End While
                    Dim b = e + 1
                    While b < n AndAlso b - e <= half + 1 AndAlso score(b, bestK) > score(b, 0) + 10
                        shiftF(b) = bestK : inPart(b) = True : b += 1
                    End While
                    e = b - 1
                End If
            End If
            r = e + 1
        End While
        ' lines inside a part that clearly belong to the unshifted timing (part edges found too wide)
        For i = 0 To n - 1
            If inPart(i) AndAlso (i = 0 OrElse Not inPart(i - 1) OrElse i = n - 1 OrElse Not inPart(i + 1)) AndAlso score(i, 0) > score(i, shiftF(i)) + 10 Then
                shiftF(i) = 0 : inPart(i) = False
            End If
        Next

        Dim total As Double = 0
        For i = 0 To n - 1
            result(i) = shiftF(i) * FrameMs / 1000.0
            total += score(i, shiftF(i))
        Next
        Return (result, total, parts)
    End Function

    ''' <summary>Speech onsets (after at least 200 ms of silence), spread with a triangle of ±150 ms.</summary>
    Private Function OnsetDensity(speech As Byte()) As Double()
        Dim n = speech.Length
        Dim density(n - 1) As Double
        Const spread As Integer = 15
        Dim quiet = 0
        For i = 0 To n - 1
            If speech(i) = 0 Then quiet += 1 : Continue For
            If i > 0 AndAlso speech(i - 1) = 0 AndAlso quiet >= 20 Then
                For d = -spread To spread
                    Dim j = i + d
                    If j >= 0 AndAlso j < n Then density(j) = Math.Max(density(j), 1 - Math.Abs(d) / (spread + 1.0))
                Next
            End If
            quiet = 0
        Next
        Return density
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

    ''' <summary>Times of the lines that have text (the lines used for matching, in file order).</summary>
    Public Function CueTimes(cues As IEnumerable(Of SrtCue)) As List(Of (Start As Double, [End] As Double))
        Dim list As New List(Of (Start As Double, [End] As Double))
        For Each c In cues
            Dim s As TimeSpan, e As TimeSpan
            If IsTimedLine(c, s, e) Then list.Add((s.TotalSeconds, e.TotalSeconds))
        Next
        Return list
    End Function

    Private Function IsTimedLine(c As SrtCue, ByRef s As TimeSpan, ByRef e As TimeSpan) As Boolean
        Return Not String.IsNullOrWhiteSpace(c.Text) AndAlso SubtitleFormatter.TryParseTiming(c.Timing, s, e)
    End Function

    ''' <summary>Returns new cues with every time changed to time × ratio + offset (negative times become 0).</summary>
    Public Function Apply(cues As IEnumerable(Of SrtCue), r As SyncResult) As List(Of SrtCue)
        Dim result As New List(Of SrtCue)
        Dim line = -1
        For Each c In cues
            Dim s As TimeSpan, e As TimeSpan
            Dim timing = c.Timing
            If IsTimedLine(c, s, e) Then line += 1
            If SubtitleFormatter.TryParseTiming(c.Timing, s, e) Then
                Dim li = Math.Max(0, line)
                timing = SubtitleFormatter.FormatTiming(Shift(s, r, li), Shift(e, r, li))
            End If
            result.Add(New SrtCue With {.Index = c.Index, .Timing = timing, .Text = c.Text})
        Next
        Return result
    End Function

    ''' <summary>New time of a moment of line number <paramref name="line"/> (counting only lines with text).</summary>
    Public Function Shift(t As TimeSpan, r As SyncResult, line As Integer) As TimeSpan
        Dim extra = If(r.LineShifts IsNot Nothing AndAlso r.LineShifts.Length > 0, r.LineShifts(Math.Min(line, r.LineShifts.Length - 1)), 0.0)
        Return TimeSpan.FromMilliseconds(Math.Max(0, Math.Round((t.TotalSeconds * r.Ratio + r.Offset + extra) * 1000)))
    End Function

    ''' <summary>Detects speech and syncs the subtitle file; writes the result to outputSrt when it should be changed.</summary>
    Public Async Function SyncFileAsync(ffmpegExe As String, media As String, audioStreamIndex As Integer, inputSrt As String, outputSrt As String,
                                        ct As CancellationToken, Optional progress As Action(Of Double) = Nothing,
                                        Optional durationSeconds As Double = 0) As Task(Of SyncResult)
        Dim speech = Await DetectSpeechBestAsync(ffmpegExe, media, audioStreamIndex, ct, progress, durationSeconds)
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
