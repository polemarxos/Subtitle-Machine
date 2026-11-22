Imports System.IO
Imports System.Net.Http
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading

Public Enum TaskState
    Pending
    Processing
    Done
    Failed
    Cancelled
End Enum

Public Class TaskItem
    Public Property FilePath As String
    Public Property IsVideo As Boolean
    ''' <summary>What happened to the file (used by the program logic).</summary>
    Public Property State As TaskState = TaskState.Pending
    ''' <summary>Text shown in the list (translated).</summary>
    Public Property Status As String = Loc.Tr("Pending")
    Public Property OutputPath As String
    ''' <summary>All files written for this item (one per target language, plus videos).</summary>
    Public Property Outputs As New List(Of String)
    ''' <summary>The subtitle file the translations were made from (for the editor).</summary>
    Public Property SourcePath As String
End Class

''' <summary>Video / audio / SRT → (extract | OCR | transcribe) → translate into every target language.</summary>
Public Class SubtitlePipeline

    Public Shared ReadOnly VideoExtensions As String() = {".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".m2ts", ".mpg", ".mpeg"}
    Public Shared ReadOnly AudioExtensions As String() = {".mp3", ".wav", ".m4a", ".flac", ".aac", ".ogg", ".opus", ".wma"}
    Private Shared ReadOnly TextCodecs As String() = {"subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text", "sami", "realtext", "microdvd", "subviewer", "subviewer1", "jacosub", "stl", "mpl2", "pjs", "vplayer"}

    ''' <summary>Creates an OCR engine for a language (set by the app – Windows OCR).</summary>
    Public Shared Property OcrFactory As Func(Of LanguageInfo, IOcrEngine)

    ''' <summary>How many subtitle lines are sent together for context.</summary>
    Public Const ContextBatchSize As Integer = 4

    Private ReadOnly _s As AppSettings
    Private ReadOnly _translator As ITranslator
    Private ReadOnly _targets As List(Of LanguageInfo)
    Private ReadOnly _sourceSetting As LanguageInfo ' Nothing = auto
    Private ReadOnly _glossary As Glossary

    Public Sub New(settings As AppSettings, translator As ITranslator)
        _s = settings
        _translator = translator
        _targets = settings.Targets()
        If _targets.Count = 0 Then _targets.Add(Languages.Find("en"))
        _sourceSetting = If(settings.TranscribeLanguage = "auto", Nothing, Languages.Find(settings.TranscribeLanguage))
        _glossary = Glossary.Load()
    End Sub

    Public Shared Function IsSupportedFile(path As String) As Boolean
        Dim ext = IO.Path.GetExtension(path).ToLowerInvariant()
        Return ext = ".srt" OrElse VideoExtensions.Contains(ext) OrElse AudioExtensions.Contains(ext)
    End Function

    Private Function ReplaceMode() As Boolean
        Return _s.ReplaceOriginal AndAlso _targets.Count = 1
    End Function

    ' ------------------------------------------------------------------
    '  Main entry
    ' ------------------------------------------------------------------
    Public Async Function ProcessAsync(item As TaskItem, report As Action(Of String, Double), ct As CancellationToken) As Task
        Dim name = Path.GetFileName(item.FilePath)
        item.Outputs.Clear()
        Dim streams As New List(Of StreamInfo)
        Dim isAudioFile = AudioExtensions.Contains(Path.GetExtension(item.FilePath).ToLowerInvariant())
        If item.IsVideo OrElse isAudioFile Then streams = Await ProbeStreamsAsync(item.FilePath, ct)

        ' 1) Get the source subtitles once.
        Dim source = Await GetSourceAsync(item, streams, report, ct)
        Dim notes As New List(Of String)
        If source.Note IsNot Nothing Then notes.Add(source.Note)
        item.SourcePath = source.Path

        ' 1b) Downloaded subtitle file: match its timing to the speech of the video next to it.
        Dim translateInput = source.Path
        If Not item.IsVideo AndAlso Not isAudioFile AndAlso _s.AutoSyncSubtitles Then
            Dim synced = Await SyncToVideoAsync(item.FilePath, source.Path, report, ct)
            If synced.Path IsNot Nothing Then
                translateInput = synced.Path
                item.SourcePath = synced.Path ' the editor pairs lines by time
            End If
            If synced.Note IsNot Nothing Then notes.Add(synced.Note)
        End If
        Dim subtitleFiles As New List(Of (Path As String, Language As LanguageInfo))
        Dim wantsVideo = item.IsVideo AndAlso Not isAudioFile AndAlso _s.VideoOutput <> "none"
        Dim videoShare = If(wantsVideo, 0.3, 0.0) ' part of the progress bar used by MKV / burn-in

        ' 2) One output per target language.
        Dim translateFrom = If(source.Path = item.FilePath, 0.0, 0.4) * (1 - videoShare)
        Dim translateEnd = 1 - videoShare
        For ti = 0 To _targets.Count - 1
            ct.ThrowIfCancellationRequested()
            Dim target = _targets(ti)
            Dim baseFrac = translateFrom + (translateEnd - translateFrom) * ti / _targets.Count
            Dim span = (translateEnd - translateFrom) / _targets.Count

            Dim outPath As String
            If ReplaceMode() Then
                outPath = source.Path
            Else
                outPath = SrtFile.OutputPathFor(item.FilePath, target)
                If String.Equals(outPath, source.Path, StringComparison.OrdinalIgnoreCase) Then
                    outPath = Path.Combine(Path.GetDirectoryName(outPath), Path.GetFileNameWithoutExtension(outPath) & ".translated.srt")
                End If
                If File.Exists(outPath) AndAlso Not _s.OverwriteExisting Then
                    notes.Add(Loc.Tr("{0}: already exists", target.Code))
                    item.Outputs.Add(outPath)
                    subtitleFiles.Add((outPath, target))
                    Continue For
                End If
            End If

            ' The video already contains subtitles in this language → extract instead of translating.
            Dim existing = streams.FirstOrDefault(Function(st) st.CodecType = "subtitle" AndAlso TextCodecs.Contains(st.Codec) AndAlso st.Language Is target AndAlso Not st.Forced)
            If existing IsNot Nothing AndAlso _s.SubtitleSource <> "audio" Then
                report(Loc.Tr("{0}: extracting existing {1} subtitles", name, target.DisplayName), baseFrac)
                Await ExtractTextSubtitleAsync(item.FilePath, existing.SubIndex, outPath, ct)
                PostProcess(outPath)
                notes.Add(Loc.Tr("{0}: from video", target.Code))
                item.Outputs.Add(outPath)
                subtitleFiles.Add((outPath, target))
                Continue For
            End If

            If source.Language Is target Then
                ' Source is already in the target language: just copy/format it.
                If Not String.Equals(outPath, translateInput, StringComparison.OrdinalIgnoreCase) Then File.Copy(translateInput, outPath, True)
                PostProcess(outPath)
                notes.Add(Loc.Tr("{0}: already {1}", target.Code, target.DisplayName))
                item.Outputs.Add(outPath)
                subtitleFiles.Add((outPath, target))
                Continue For
            End If

            Await TranslateFileAsync(translateInput, outPath, source.Language, target,
                                     Sub(done, total) report(Loc.Tr("{0}: translating {1}/{2} lines → {3}", name, done, total, target.DisplayName), baseFrac + span * done / Math.Max(1, total)),
                                     ct)
            item.Outputs.Add(outPath)
            subtitleFiles.Add((outPath, target))
        Next

        ' 3) Video outputs.
        If wantsVideo AndAlso subtitleFiles.Count > 0 Then
            Dim mkv = _s.VideoOutput = "mkv" OrElse _s.VideoOutput = "both"
            Dim burn = _s.VideoOutput = "burn" OrElse _s.VideoOutput = "both"
            Dim vStart = 1 - videoShare
            Dim vSpan = videoShare / If(mkv AndAlso burn, 2, 1)
            If mkv Then
                ct.ThrowIfCancellationRequested()
                report(Loc.Tr("{0}: adding subtitles to MKV", name), vStart)
                Dim outMkv = Await VideoOutput.MuxIntoMkvAsync(item.FilePath, subtitleFiles, Sub(f) report(Loc.Tr("{0}: adding subtitles to MKV {1:P0}", name, f), vStart + vSpan * f), ct)
                item.Outputs.Add(outMkv)
                vStart += vSpan
                notes.Add("MKV")
            End If
            If burn Then
                ct.ThrowIfCancellationRequested()
                Dim first = subtitleFiles(0)
                report(Loc.Tr("{0}: burning {1} subtitles into the video", name, first.Language.DisplayName), vStart)
                Dim outMp4 = Await VideoOutput.BurnInAsync(item.FilePath, first.Path, first.Language, Sub(f) report(Loc.Tr("{0}: burning subtitles {1:P0}", name, f), vStart + vSpan * f), ct)
                item.Outputs.Add(outMp4)
                notes.Add(Loc.Tr("burned-in"))
            End If
            If mkv AndAlso Not _s.KeepSrtFiles Then
                For Each f In subtitleFiles
                    If Not String.Equals(f.Path, item.SourcePath, StringComparison.OrdinalIgnoreCase) Then
                        Try : File.Delete(f.Path) : item.Outputs.Remove(f.Path) : Catch : End Try
                    End If
                Next
            End If
        End If

        item.OutputPath = item.Outputs.FirstOrDefault()
        item.State = TaskState.Done
        item.Status = Loc.Tr("Completed") & If(notes.Count > 0, " (" & String.Join(", ", notes) & ")", "")
    End Function

    ' ------------------------------------------------------------------
    '  Step 1: source subtitles
    ' ------------------------------------------------------------------
    Private Structure SourceResult
        Public Path As String
        Public Language As LanguageInfo
        Public Note As String
    End Structure

    Private Async Function GetSourceAsync(item As TaskItem, streams As List(Of StreamInfo), report As Action(Of String, Double), ct As CancellationToken) As Task(Of SourceResult)
        Dim name = Path.GetFileName(item.FilePath)
        If Not item.IsVideo Then
            ' .srt file
            Return New SourceResult With {.Path = item.FilePath, .Language = If(_sourceSetting, SrtFile.LanguageFromFileName(item.FilePath))}
        End If

        Dim mode = _s.SubtitleSource
        Dim subs = streams.Where(Function(st) st.CodecType = "subtitle").ToList()

        If mode <> "audio" Then
            ' a) text subtitles
            Dim textSubs = subs.Where(Function(st) TextCodecs.Contains(st.Codec)).ToList()
            Dim pick = PickStream(textSubs)
            If pick IsNot Nothing Then
                Dim srt = SidecarPath(item.FilePath, pick.Language)
                If Not File.Exists(srt) OrElse _s.OverwriteExisting Then
                    report(Loc.Tr("{0}: extracting subtitles ({1})", name, If(pick.Language?.DisplayName, Loc.Tr("unknown language"))), 0.1)
                    Await ExtractTextSubtitleAsync(item.FilePath, pick.SubIndex, srt, ct)
                End If
                Return New SourceResult With {.Path = srt, .Language = If(pick.Language, _sourceSetting)}
            End If

            ' b) image subtitles (PGS / VobSub) → OCR
            Dim imageSubs = subs.Where(Function(st) ImageSubtitleDecoders.ImageCodecs.Contains(st.Codec)).ToList()
            Dim imgPick = PickStream(imageSubs)
            If imgPick IsNot Nothing AndAlso _s.OcrImageSubtitles AndAlso OcrFactory IsNot Nothing Then
                Dim srt = SidecarPath(item.FilePath, imgPick.Language)
                Dim note As String = Nothing
                If Not File.Exists(srt) OrElse _s.OverwriteExisting Then
                    note = Await OcrImageSubtitlesAsync(item.FilePath, imgPick, srt, report, ct)
                End If
                Return New SourceResult With {.Path = srt, .Language = If(imgPick.Language, _sourceSetting), .Note = If(note, "OCR")}
            End If

            If mode = "embedded" Then Throw New Exception(Loc.Tr("The video has no usable subtitles ('Create from' is set to embedded subtitles only)."))
        End If

        ' c) speech-to-text
        Dim audio = PickAudioStream(streams)
        If audio Is Nothing AndAlso streams.Count > 0 AndAlso Not streams.Any(Function(st) st.CodecType = "audio") Then
            Throw New Exception(Loc.Tr("The file has no audio track and no subtitles."))
        End If
        Dim r = Await TranscribeAsync(item.FilePath, audio, report, ct)
        Return New SourceResult With {.Path = r.Item1, .Language = If(r.Item2, _sourceSetting), .Note = Loc.Tr("from audio")}
    End Function

    ''' <summary>
    ''' Syncs a subtitle file to the video with the same name. Returns the synced copy (Nothing when the
    ''' timing is already right, no video was found or the match was not clear) and a note for the status.
    ''' </summary>
    Private Async Function SyncToVideoAsync(srtPath As String, sourcePath As String, report As Action(Of String, Double), ct As CancellationToken) As Task(Of (Path As String, Note As String))
        Dim video = SubtitleSync.FindVideoFor(srtPath)
        If video Is Nothing Then Return (Nothing, Nothing)
        Dim name = Path.GetFileName(srtPath)
        Try
            Dim streams = Await ProbeStreamsAsync(video, ct)
            Dim audio = PickAudioStream(streams)
            If audio Is Nothing Then Return (Nothing, Nothing)
            Dim duration = Await ProbeDurationAsync(video, ct)
            report(Loc.Tr("{0}: matching the timing to the video", name), 0.02)
            Dim temp = SyncedCopyPath(srtPath)
            Dim r = Await SubtitleSync.SyncFileAsync(FfmpegComponent.FfmpegExe, video, audio.SubIndex, sourcePath, temp, ct,
                                                    Sub(f) report(Loc.Tr("{0}: matching the timing to the video {1:P0}", name, f), 0.02 + 0.08 * f), duration)
            If r.ShouldApply Then Return (temp, Loc.Tr("timing fixed: {0}", r.Describe()))
            If r.Reason = "already in sync" Then Return (Nothing, Nothing)
            Return (Nothing, Loc.Tr("timing not changed – no clear match with the video"))
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Logger.Log("Sync failed: " & ex.Message)
            Return (Nothing, Loc.Tr("timing not checked"))
        End Try
    End Function

    ''' <summary>Synced copies of the originals are kept for 14 days in the data folder (the editor shows them next to the translation).</summary>
    Private Shared Function SyncedCopyPath(srtPath As String) As String
        Dim dir = AppPaths.EnsureDir(Path.Combine(AppPaths.DataDir, "synced"))
        Try
            For Each f In Directory.GetFiles(dir, "*.srt")
                If File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14) Then File.Delete(f)
            Next
        Catch
        End Try
        Return Path.Combine(dir, Path.GetFileNameWithoutExtension(srtPath) & "." & Guid.NewGuid().ToString("N").Substring(0, 8) & ".srt")
    End Function

    Public Shared Async Function ProbeDurationAsync(media As String, ct As CancellationToken) As Task(Of Double)
        Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfprobeExe, $"-v error -show_entries format=duration -of csv=p=0 ""{media}""", ct)
        Dim d As Double
        Return If(Double.TryParse(r.StdOut.Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, d), d, 0)
    End Function

    ''' <summary>Hearing-impaired cleanup and line/timing fixes for files that are not translated.</summary>
    Private Sub PostProcess(path As String)
        If Not _s.RemoveHearingImpaired AndAlso Not _s.FixFormatting Then Return
        Dim cues = SrtFile.Load(path)
        If _s.RemoveHearingImpaired Then cues = SubtitleCleaner.Clean(cues)
        If _s.FixFormatting Then cues = SubtitleFormatter.Apply(cues, _s.MaxLineLength)
        SrtFile.Save(path, cues)
    End Sub

    ''' <summary>Source-language stream → English → first non-forced → any.</summary>
    Private Function PickStream(list As List(Of StreamInfo)) As StreamInfo
        Dim pick As StreamInfo = Nothing
        If _sourceSetting IsNot Nothing Then pick = list.FirstOrDefault(Function(st) st.Language Is _sourceSetting AndAlso Not st.Forced)
        If pick Is Nothing Then pick = list.FirstOrDefault(Function(st) st.Language IsNot Nothing AndAlso st.Language.Code = "en" AndAlso Not st.Forced)
        If pick Is Nothing Then pick = list.FirstOrDefault(Function(st) Not st.Forced)
        Return If(pick, list.FirstOrDefault())
    End Function

    ''' <summary>"Movie.en.srt", or "Movie.srt" in replace-original mode / unknown language.</summary>
    Private Function SidecarPath(media As String, lang As LanguageInfo) As String
        Dim stem = Path.GetFileNameWithoutExtension(media)
        Dim code = If(lang IsNot Nothing AndAlso Not ReplaceMode(), "." & lang.Code, "")
        Return Path.Combine(Path.GetDirectoryName(media), stem & code & ".srt")
    End Function

    ' ------------------------------------------------------------------
    '  Translation (with context batches)
    ' ------------------------------------------------------------------
    Private Shared ReadOnly LeadingTags As New Regex("^(\{[^}]*\})+")

    Private Async Function TranslateFileAsync(inputSrt As String, outputSrt As String, sourceLang As LanguageInfo, target As LanguageInfo,
                                              onProgress As Action(Of Integer, Integer), ct As CancellationToken) As Task
        Dim cues = SrtFile.Load(inputSrt)
        If cues.Count = 0 Then Throw New Exception(Loc.Tr("The subtitle file is empty or not a valid SRT."))
        If _s.RemoveHearingImpaired Then cues = SubtitleCleaner.Clean(cues)
        If cues.Count = 0 Then Throw New Exception(Loc.Tr("Nothing left to translate after removing hearing-impaired texts."))
        Dim total = cues.Count
        Dim done = 0

        ' Split each cue into "{\an8}" prefix + text; only cues with letters are sent to the model.
        Dim prefixes = cues.Select(Function(c)
                                       Dim m = LeadingTags.Match(If(c.Text, ""))
                                       Return If(m.Success, m.Value, "")
                                   End Function).ToArray()
        Dim texts = cues.Select(Function(c, i) If(c.Text, "").Substring(prefixes(i).Length)).ToArray()
        Dim todo = Enumerable.Range(0, cues.Count).Where(Function(i) texts(i).Any(AddressOf Char.IsLetter)).ToList()
        done = total - todo.Count

        ' Groups of consecutive cues (context) or single cues.
        Dim size = If(_s.ContextTranslation, ContextBatchSize, 1)
        Dim results = CType(texts.Clone(), String())

        ' Resume: lines finished in an earlier, interrupted run are taken from the progress file.
        Dim progress = TranslationProgress.Open(outputSrt, inputSrt, target, _translator.DisplayName & If(_s.ContextTranslation, "|ctx", ""))
        For Each kv In progress.Done
            If kv.Key >= 0 AndAlso kv.Key < results.Length Then results(kv.Key) = kv.Value
        Next
        If progress.Done.Count > 0 Then
            Logger.Log($"Resuming {Path.GetFileName(outputSrt)}: {progress.Done.Count} lines already translated")
            todo = todo.Where(Function(i) Not progress.Done.ContainsKey(i)).ToList()
            done = total - todo.Count
            onProgress(done, total)
        End If
        Dim groups = todo.Select(Function(idx, n) (idx, n \ size)).GroupBy(Function(x) x.Item2).Select(Function(g) g.Select(Function(x) x.idx).ToList()).ToList()
        Dim sem As New SemaphoreSlim(Math.Max(1, _translator.MaxParallel))

        Dim work = groups.Select(Async Function(g)
                                     Await sem.WaitAsync(ct)
                                     Try
                                         Dim translated = Await TranslateGroupAsync(g.Select(Function(i) texts(i)).ToList(), sourceLang, target, ct)
                                         For k = 0 To g.Count - 1
                                             results(g(k)) = translated(k)
                                             progress.Add(g(k), translated(k))
                                         Next
                                     Finally
                                         sem.Release()
                                     End Try
                                     onProgress(Interlocked.Add(done, g.Count), total)
                                 End Function).ToList()
        Try
            Await Task.WhenAll(work)
        Finally
            progress.Flush() ' keep what was translated, also when stopped or failed
        End Try
        ct.ThrowIfCancellationRequested()

        For i = 0 To cues.Count - 1
            cues(i).Text = prefixes(i) & results(i)
        Next
        Dim final = If(_s.FixFormatting, SubtitleFormatter.Apply(cues, _s.MaxLineLength), cues)
        SrtFile.Save(outputSrt, final)
        progress.Complete()
    End Function

    ''' <summary>Translates several lines together; falls back to one-by-one if the model breaks the numbering.</summary>
    Private Async Function TranslateGroupAsync(lines As List(Of String), sourceLang As LanguageInfo, target As LanguageInfo, ct As CancellationToken) As Task(Of List(Of String))
        If lines.Count > 1 Then
            Try
                Dim joined = TranslationPrompt.JoinBatch(lines)
                Dim terms = _glossary.ForText(joined, target)
                Dim raw = Await WithRetryAsync(Function() _translator.TranslateAsync(joined, sourceLang, target, ct, terms), ct)
                Dim split = TranslationPrompt.SplitBatch(raw, lines.Count)
                If split IsNot Nothing Then Return split
                Logger.Log("Context batch mismatch – translating these lines one by one.")
            Catch ex As HttpRequestException
                Logger.Log("Context batch failed: " & ex.Message)
            End Try
        End If
        Dim result As New List(Of String)
        For Each l In lines
            Dim raw As String = Nothing
            Try
                raw = Await WithRetryAsync(Function() _translator.TranslateAsync(l, sourceLang, target, ct, _glossary.ForText(l, target)), ct)
            Catch ex As HttpRequestException
                Logger.Log("Line left untranslated: " & ex.Message)
            End Try
            result.Add(TranslationPrompt.Clean(raw, l))
        Next
        Return result
    End Function

    Private Shared Async Function WithRetryAsync(action As Func(Of Task(Of String)), ct As CancellationToken) As Task(Of String)
        For attempt = 1 To 3
            Try
                Return Await action()
            Catch ex As HttpRequestException When attempt < 3
                Logger.Log($"Retry {attempt}: {ex.Message}")
            End Try
            Await Task.Delay(1000 * attempt, ct)
        Next
        Return Await action()
    End Function

    ' ------------------------------------------------------------------
    '  FFprobe / FFmpeg
    ' ------------------------------------------------------------------
    Private Class StreamInfo
        Public Property CodecType As String
        Public Property Codec As String
        ''' <summary>Index among streams of the same type (for -map 0:s:N / 0:a:N).</summary>
        Public Property SubIndex As Integer
        Public Property Language As LanguageInfo
        Public Property Forced As Boolean
    End Class

    Private Async Function ProbeStreamsAsync(media As String, ct As CancellationToken) As Task(Of List(Of StreamInfo))
        Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfprobeExe, $"-v quiet -print_format json -show_streams ""{media}""", ct)
        Dim list As New List(Of StreamInfo)
        If String.IsNullOrWhiteSpace(r.StdOut) Then Return list
        Dim counters As New Dictionary(Of String, Integer)
        Using doc = JsonDocument.Parse(r.StdOut)
            Dim streams As JsonElement
            If Not doc.RootElement.TryGetProperty("streams", streams) Then Return list
            For Each st In streams.EnumerateArray()
                Dim info As New StreamInfo With {.CodecType = Str(st, "codec_type"), .Codec = If(Str(st, "codec_name"), "").ToLowerInvariant()}
                If info.CodecType Is Nothing Then Continue For
                Dim n = 0
                counters.TryGetValue(info.CodecType, n)
                info.SubIndex = n
                counters(info.CodecType) = n + 1
                Dim tags As JsonElement
                If st.TryGetProperty("tags", tags) Then
                    info.Language = Languages.Find(Str(tags, "language"))
                    If If(Str(tags, "title"), "").ToLowerInvariant().Contains("forced") Then info.Forced = True
                End If
                Dim disp As JsonElement, forced As JsonElement
                If st.TryGetProperty("disposition", disp) AndAlso disp.TryGetProperty("forced", forced) AndAlso forced.ValueKind = JsonValueKind.Number AndAlso forced.GetInt32() = 1 Then info.Forced = True
                list.Add(info)
            Next
        End Using
        Return list
    End Function

    Private Function PickAudioStream(streams As List(Of StreamInfo)) As StreamInfo
        Dim audio = streams.Where(Function(st) st.CodecType = "audio").ToList()
        If _sourceSetting IsNot Nothing Then
            Dim hit = audio.FirstOrDefault(Function(a) a.Language Is _sourceSetting)
            If hit IsNot Nothing Then Return hit
        End If
        Return audio.FirstOrDefault()
    End Function

    Private Shared Function Str(e As JsonElement, prop As String) As String
        Dim v As JsonElement
        If e.TryGetProperty(prop, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return Nothing
    End Function

    Private Async Function ExtractTextSubtitleAsync(video As String, subIndex As Integer, output As String, ct As CancellationToken) As Task
        Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfmpegExe, $"-hide_banner -y -i ""{video}"" -map 0:s:{subIndex} -c:s srt ""{output}""", ct)
        If r.ExitCode <> 0 OrElse Not File.Exists(output) Then
            Throw New Exception(Loc.Tr("FFmpeg could not extract the subtitles:") & " " & ProcessRunner.Tail(r.StdErr, 400))
        End If
    End Function

    ' ------------------------------------------------------------------
    '  OCR of image subtitles
    ' ------------------------------------------------------------------
    Private Async Function OcrImageSubtitlesAsync(video As String, st As StreamInfo, outputSrt As String, report As Action(Of String, Double), ct As CancellationToken) As Task(Of String)
        Dim name = Path.GetFileName(video)
        report(Loc.Tr("{0}: reading image subtitles ({1})", name, st.Codec), 0.05)
        Dim cues As List(Of ImageCue)
        If st.Codec.Contains("pgs") Then
            Dim sup = Path.Combine(Path.GetTempPath(), $"sm_{Guid.NewGuid():N}.sup")
            Try
                Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfmpegExe, $"-hide_banner -y -i ""{video}"" -map 0:s:{st.SubIndex} -c:s copy ""{sup}""", ct)
                If r.ExitCode <> 0 OrElse Not File.Exists(sup) Then Throw New Exception(Loc.Tr("FFmpeg could not extract the PGS subtitles:") & " " & ProcessRunner.Tail(r.StdErr, 300))
                cues = ImageSubtitleDecoders.DecodePgs(File.ReadAllBytes(sup))
            Finally
                Try : File.Delete(sup) : Catch : End Try
            End Try
        Else
            Dim r = Await ProcessRunner.RunAsync(FfmpegComponent.FfprobeExe, $"-v quiet -select_streams s:{st.SubIndex} -show_packets -show_data -show_streams -of json ""{video}""", ct)
            Dim pal As Byte() = Nothing
            Dim packets = ImageSubtitleDecoders.ParseFfprobePackets(r.StdOut, pal)
            cues = ImageSubtitleDecoders.DecodeVobSub(packets, pal)
        End If
        If cues.Count = 0 Then Throw New Exception(Loc.Tr("No pictures found in the image subtitle track."))

        Dim engine = OcrFactory(If(st.Language, _sourceSetting))
        Dim srt As New List(Of SrtCue)
        For i = 0 To cues.Count - 1
            ct.ThrowIfCancellationRequested()
            Dim w = 0, h = 0
            Dim gray = ImageSubtitleDecoders.PrepareForOcr(cues(i).Image, w, h)
            If gray Is Nothing Then Continue For
            Dim text = Await engine.RecognizeAsync(gray, w, h)
            If Not String.IsNullOrWhiteSpace(text) Then
                srt.Add(New SrtCue With {.Index = srt.Count + 1, .Timing = SubtitleFormatter.FormatTiming(cues(i).Start, cues(i).End), .Text = text})
            End If
            If i Mod 10 = 0 Then report(Loc.Tr("{0}: reading text (OCR) {1}/{2}", name, i + 1, cues.Count), 0.05 + 0.35 * i / cues.Count)
        Next
        If srt.Count = 0 Then Throw New Exception(Loc.Tr("OCR did not recognise any text in the image subtitles."))
        SrtFile.Save(outputSrt, srt)
        Logger.Log($"OCR: {srt.Count} lines from {cues.Count} pictures → {outputSrt}")
        Return If(engine.Warning IsNot Nothing, Loc.Tr("OCR – language pack missing"), "OCR")
    End Function

    ' ------------------------------------------------------------------
    '  Whisper
    ' ------------------------------------------------------------------
    Private Shared ReadOnly DetectedLangRx As New Regex("auto-detected language:\s*([a-z]{2,3})", RegexOptions.IgnoreCase)
    Private Shared ReadOnly ProgressRx As New Regex("progress\s*=\s*(\d+)%")

    ''' <returns>(path of the generated SRT, detected language)</returns>
    Private Async Function TranscribeAsync(mediaPath As String, audio As StreamInfo, report As Action(Of String, Double), ct As CancellationToken) As Task(Of Tuple(Of String, LanguageInfo))
        Dim name = Path.GetFileName(mediaPath)
        Dim whisperExe = WhisperEngineComponent.FindExe()
        If whisperExe Is Nothing Then Throw New Exception(Loc.Tr("whisper.cpp is not installed. Open 'Components'."))
        Dim model = WhisperModelComponent.ModelPath(_s.WhisperModel)
        If Not File.Exists(model) Then Throw New Exception(Loc.Tr("Whisper model '{0}' is not downloaded. Open 'Components'.", _s.WhisperModel))

        Dim tempDir = Path.Combine(Path.GetTempPath(), "subtitle_machine_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
        Try
            report(Loc.Tr("{0}: extracting audio", name), 0.02)
            Dim wav = Path.Combine(tempDir, "audio.wav")
            Dim map = If(audio IsNot Nothing, $"-map 0:a:{audio.SubIndex} ", "")
            Dim ff = Await ProcessRunner.RunAsync(FfmpegComponent.FfmpegExe, $"-hide_banner -y -i ""{mediaPath}"" {map}-vn -ac 1 -ar 16000 -c:a pcm_s16le ""{wav}""", ct)
            If ff.ExitCode <> 0 Then Throw New Exception(Loc.Tr("FFmpeg could not extract the audio:") & " " & ProcessRunner.Tail(ff.StdErr, 400))

            Dim lang = If(_sourceSetting IsNot Nothing, _sourceSetting.Code, "auto")
            Dim outBase = Path.Combine(tempDir, "audio")
            Dim threads = Math.Max(2, Math.Min(8, Environment.ProcessorCount - 1))
            Dim args = $"-m ""{model}"" -f ""{wav}"" -osrt -of ""{outBase}"" -l {lang} -t {threads} -pp"
            Dim vad = WhisperVadComponent.FindModel()
            If _s.UseVad AndAlso vad IsNot Nothing Then args &= $" --vad --vad-model ""{vad}"""
            Dim detected As LanguageInfo = Nothing
            report(Loc.Tr("{0}: transcribing audio (Whisper {1})", name, _s.WhisperModel), 0.05)
            Dim wr = Await ProcessRunner.RunAsync(whisperExe, args, ct, onStdErrLine:=
                Sub(line)
                    Dim dm = DetectedLangRx.Match(line)
                    If dm.Success Then detected = Languages.Find(dm.Groups(1).Value)
                    Dim pm = ProgressRx.Match(line)
                    If pm.Success Then report(Loc.Tr("{0}: transcribing audio {1}%", name, pm.Groups(1).Value), 0.05 + 0.35 * CInt(pm.Groups(1).Value) / 100.0)
                End Sub)
            If wr.ExitCode <> 0 Then Throw New Exception(Loc.Tr("Whisper failed:") & " " & ProcessRunner.Tail(wr.StdErr, 600))

            Dim generated = outBase & ".srt"
            If Not File.Exists(generated) Then Throw New Exception(Loc.Tr("Whisper did not produce an SRT file."))
            Dim srcLang = If(_sourceSetting, detected)
            Dim dest = SidecarPath(mediaPath, srcLang)
            Dim cues = SrtFile.Load(generated)
            SrtFile.Save(dest, If(_s.FixFormatting, SubtitleFormatter.Apply(cues, _s.MaxLineLength), cues))
            Return Tuple.Create(dest, srcLang)
        Finally
            Try : Directory.Delete(tempDir, True) : Catch : End Try
        End Try
    End Function

End Class
