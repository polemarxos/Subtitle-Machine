Imports System.IO
Imports System.Text
Imports Xunit

Public Class SrtFileTests
    Private Shared Function TempFile(name As String) As String
        Dim dir = Path.Combine(Path.GetTempPath(), "sm_tests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Return Path.Combine(dir, name)
    End Function

    <Fact>
    Public Sub Load_HandlesBomTagsAndNumberOnlyText()
        Dim p = TempFile("Movie.en.srt")
        Dim srt = "1" & vbCrLf & "00:00:01,000 --> 00:00:02,000" & vbCrLf & "{\an8}Hello" & vbCrLf & vbCrLf &
                  "2" & vbCrLf & "00:00:03,000 --> 00:00:04,000" & vbCrLf & "42" & vbCrLf & "- Second" & vbCrLf & vbCrLf & vbCrLf &
                  "3" & vbCrLf & "00:00:05,000 --> 00:00:06,000" & vbCrLf & "Γεια" & vbCrLf
        File.WriteAllText(p, srt, New UTF8Encoding(True))
        Dim cues = SrtFile.Load(p)
        Assert.Equal(3, cues.Count)
        Assert.Equal("{\an8}Hello", cues(0).Text)
        Assert.Equal("42" & vbLf & "- Second", cues(1).Text)
        Assert.Equal("Γεια", cues(2).Text)
    End Sub

    <Fact>
    Public Sub SaveAndReload_RoundTrips()
        Dim p = TempFile("out.srt")
        Dim cues As New List(Of SrtCue) From {
            New SrtCue With {.Timing = "00:00:01,000 --> 00:00:02,000", .Text = "A" & vbLf & "B"},
            New SrtCue With {.Timing = "00:00:03,000 --> 00:00:04,000", .Text = "C"}}
        SrtFile.Save(p, cues)
        Dim back = SrtFile.Load(p)
        Assert.Equal(2, back.Count)
        Assert.Equal("A" & vbLf & "B", back(0).Text)
    End Sub

    <Theory>
    <InlineData("C:\m\Movie.mkv", "el", "Movie.el.srt")>
    <InlineData("C:\m\Movie.en.srt", "el", "Movie.el.srt")>
    <InlineData("C:\m\My.Movie.2024.mkv", "de", "My.Movie.2024.de.srt")>
    <InlineData("C:\m\Movie.srt", "fr", "Movie.fr.srt")>
    Public Sub OutputPathFor_UsesLanguageCode(input As String, code As String, expected As String)
        Dim p = input.Replace("\", Path.DirectorySeparatorChar)
        Assert.Equal(expected, Path.GetFileName(SrtFile.OutputPathFor(p, Languages.Find(code))))
    End Sub

    <Fact>
    Public Sub LanguageFromFileName_ReadsSuffix()
        Assert.Equal("en", SrtFile.LanguageFromFileName("Movie.en.srt").Code)
        Assert.Equal("el", SrtFile.LanguageFromFileName("Movie.gre.srt").Code)
        Assert.Null(SrtFile.LanguageFromFileName("Movie.srt"))
        Assert.Null(SrtFile.LanguageFromFileName("My.Movie.2024.srt"))
    End Sub
End Class

Public Class LanguageAndSettingsTests
    <Theory>
    <InlineData("el")>
    <InlineData("gre")>
    <InlineData("ell")>
    <InlineData("Greek")>
    <InlineData("GREEK")>
    Public Sub Find_AcceptsCodesAndNames(key As String)
        Assert.Equal("el", Languages.Find(key).Code)
    End Sub

    <Fact>
    Public Sub Normalize_MigratesOldSingleLanguage()
        Dim s As New AppSettings With {.TargetLanguage = "Greek", .TargetLanguages = New List(Of String) From {"xx"}}
        s.Normalize()
        Assert.Equal({"el"}, s.TargetLanguages.ToArray())
        Assert.Null(s.TargetLanguage)
    End Sub

    <Fact>
    Public Sub Normalize_FixesInvalidValues()
        Dim s As New AppSettings With {.TranslateModelSize = "70b", .GpuBackend = "metal", .SubtitleSource = "x", .MaxLineLength = 5,
                                       .TargetLanguages = New List(Of String) From {"en", "en", "de"}}
        s.Normalize()
        Assert.Equal("auto", s.TranslateModelSize)
        Assert.Equal("auto", s.GpuBackend)
        Assert.Equal("auto", s.SubtitleSource)
        Assert.Equal(42, s.MaxLineLength)
        Assert.Equal({"en", "de"}, s.TargetLanguages.ToArray())
    End Sub

    <Theory>
    <InlineData("4b")>
    <InlineData("12b")>
    <InlineData("27b")>
    Public Sub EffectiveModelSize_RespectsManualChoice(size As String)
        Assert.Equal(size, New AppSettings With {.TranslateModelSize = size}.EffectiveModelSize())
    End Sub

    <Fact>
    Public Sub ParseTag_HandlesPrefixes()
        Assert.Equal(New Version(1, 3, 0), SelfUpdater.ParseTag("v1.3.0"))
        Assert.Equal(New Version(1, 0, 0, 1), SelfUpdater.ParseTag("1.0.0.1"))
        Assert.Null(SelfUpdater.ParseTag("update"))
    End Sub
End Class

Public Class TranslationPromptTests
    <Fact>
    Public Sub Build_ContainsOfficialWordingAndTwoBlankLines()
        Dim p = TranslationPrompt.Build("Hello", Languages.Find("en"), Languages.Find("el"))
        Assert.StartsWith("You are a professional English (en) to Greek (el) translator.", p)
        Assert.EndsWith("into Greek:" & vbLf & vbLf & vbLf & "Hello", p)
    End Sub

    <Fact>
    Public Sub Batch_RoundTrip()
        Dim lines = New List(Of String) From {"Maria, are you ready?", "I'm tired." & vbLf & "Very tired.", "Go."}
        Dim joined = TranslationPrompt.JoinBatch(lines)
        Assert.Equal("<1>Maria, are you ready?" & vbLf & "<2>I'm tired. <br> Very tired." & vbLf & "<3>Go.", joined)
        Dim answer = "<1>Μαρία, είσαι έτοιμη;" & vbLf & "<2>Είμαι κουρασμένη. <br> Πολύ κουρασμένη." & vbLf & "<3>Πάμε."
        Dim split = TranslationPrompt.SplitBatch(answer, 3)
        Assert.Equal(3, split.Count)
        Assert.Equal("Είμαι κουρασμένη." & vbLf & "Πολύ κουρασμένη.", split(1))
    End Sub

    <Theory>
    <InlineData("<1>A" & vbLf & "<2>B", 3)>
    <InlineData("<1>A" & vbLf & "<3>B", 2)>
    <InlineData("<1>A" & vbLf & "<2>", 2)>
    <InlineData("Just text", 2)>
    Public Sub SplitBatch_RejectsBrokenNumbering(answer As String, expected As Integer)
        Assert.Null(TranslationPrompt.SplitBatch(answer, expected))
    End Sub

    <Fact>
    Public Sub Clean_StripsQuotesAndEndTokens()
        Assert.Equal("Γεια", TranslationPrompt.Clean("""Γεια""<end_of_turn>", "Hello"))
        Assert.Equal("Hello", TranslationPrompt.Clean("   ", "Hello"))
    End Sub
End Class

Public Class FormatterTests
    <Fact>
    Public Sub Timing_ParseAndFormat()
        Dim s As TimeSpan, e As TimeSpan
        Assert.True(SubtitleFormatter.TryParseTiming("01:02:03,045 --> 01:02:04,5", s, e))
        Assert.Equal(New TimeSpan(0, 1, 2, 3, 45), s)
        Assert.Equal(New TimeSpan(0, 1, 2, 4, 500), e)
        Assert.Equal("01:02:03,045 --> 01:02:04,500", SubtitleFormatter.FormatTiming(s, e))
    End Sub

    <Fact>
    Public Sub Wrap_SplitsLongLineIntoTwoBalancedLines()
        Dim w = SubtitleFormatter.Wrap("This is a very long subtitle line that certainly does not fit on one line.", 42)
        Dim lines = w.Split(vbLf)
        Assert.Equal(2, lines.Length)
        Assert.All(lines, Sub(l) Assert.True(l.Length <= 42, l))
    End Sub

    <Fact>
    Public Sub Wrap_PrefersSentenceBoundary()
        Assert.Equal("Ich bin müde." & vbLf & "Ich habe den ganzen Tag gearbeitet.", SubtitleFormatter.Wrap("Ich bin müde. Ich habe den ganzen Tag gearbeitet.", 42))
    End Sub

    <Fact>
    Public Sub Wrap_KeepsDialogueAndShortText()
        Dim dialogue = "- Did you see him walking down the long road yesterday?" & vbLf & "- No."
        Assert.Equal(dialogue, SubtitleFormatter.Wrap(dialogue, 42))
        Assert.Equal("Short" & vbLf & "text", SubtitleFormatter.Wrap("Short" & vbLf & "text", 42))
    End Sub

    <Fact>
    Public Sub Wrap_IgnoresTagsWhenMeasuring()
        Dim t = "<i>Exactly forty characters long text!!</i>"
        Assert.Equal(t, SubtitleFormatter.Wrap(t, 42))
    End Sub

    <Fact>
    Public Sub Apply_ExtendsShortCuesWithoutOverlap()
        Dim cues As New List(Of SrtCue) From {
            New SrtCue With {.Timing = "00:00:01,000 --> 00:00:01,800", .Text = "Hi there, how are you doing today?"},
            New SrtCue With {.Timing = "00:00:02,500 --> 00:00:04,000", .Text = "Fine."},
            New SrtCue With {.Timing = "00:00:03,900 --> 00:00:05,000", .Text = "Overlap"}}
        Dim r = SubtitleFormatter.Apply(cues, 42)
        Assert.Equal(3, r.Count)
        Assert.Equal("00:00:01,000 --> 00:00:02,000", r(0).Timing)   ' extended to 1 s
        Assert.Equal("00:00:02,500 --> 00:00:03,860", r(1).Timing)   ' trimmed before next cue
    End Sub

    <Fact>
    Public Sub Apply_MergesVeryShortNeighbours()
        Dim cues As New List(Of SrtCue) From {
            New SrtCue With {.Timing = "00:00:01,000 --> 00:00:01,400", .Text = "Yes."},
            New SrtCue With {.Timing = "00:00:01,500 --> 00:00:03,000", .Text = "Let's go."}}
        Dim r = SubtitleFormatter.Apply(cues, 42)
        Assert.Single(r)
        Assert.Equal("Yes." & vbLf & "Let's go.", r(0).Text)
        Assert.Equal("00:00:01,000 --> 00:00:03,000", r(0).Timing)
    End Sub
End Class

Public Class ImageSubtitleTests
    <Fact>
    Public Sub ParseHexDump_ReadsFfprobeFormat()
        Dim dump = vbLf & "00000000: 7369 7a65 3a20 3732 3078 3438 300a 7061  size: 720x480.pa" & vbLf & "00000010: 6c                                       l" & vbLf
        Assert.Equal("size: 720x480" & vbLf & "pal", Encoding.ASCII.GetString(ImageSubtitleDecoders.ParseHexDump(dump)))
    End Sub

    <Fact>
    Public Sub ParseIdxPalette_ConvertsToLuma()
        Dim luma = ImageSubtitleDecoders.ParseIdxPalette("size: 720x480" & vbLf & "palette: 000000, ffffff, 808080")
        Assert.Equal(0, luma(0))
        Assert.Equal(255, luma(1))
        Assert.InRange(CInt(luma(2)), 126, 130)
    End Sub

    ''' <summary>Builds a minimal PGS display set: one 4x2 object, white pixels, shown at 1 s and cleared at 3 s.</summary>
    <Fact>
    Public Sub DecodePgs_ReadsObjectAndTiming()
        Dim ms As New MemoryStream()
        Dim seg = Sub(pts As Integer, type As Byte, data As Byte())
                      ms.Write({&H50, &H47}, 0, 2)
                      ms.Write(BE32(pts), 0, 4) : ms.Write(BE32(0), 0, 4)
                      ms.WriteByte(type)
                      ms.Write({CByte(data.Length >> 8), CByte(data.Length And 255)}, 0, 2)
                      ms.Write(data, 0, data.Length)
                  End Sub
        ' Display set 1 at 1 s (90 kHz clock)
        seg(90000, &H16, {7, &H80, 4, &H38, &H10, 0, 1, &H80, 0, 0, 1, 0, 1, 0, 0, 0, 100, 0, 200})   ' PCS: 1 object at (100,200)
        seg(90000, &H14, {0, 0, 1, 235, 128, 128, 255})                                            ' PDS: colour 1 = white, opaque
        ' ODS: id 1, last-in-sequence, size 4x2, RLE: "1 1 1 1 EOL" twice
        Dim rle As Byte() = {1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0}
        Dim ods As New List(Of Byte) From {0, 1, 0, &HC0, 0, 0, CByte(rle.Length + 4), 0, 4, 0, 2}
        ods.AddRange(rle)
        seg(90000, &H15, ods.ToArray())
        seg(90000, &H80, {})
        ' Display set 2 at 3 s: no objects → clears the screen
        seg(270000, &H16, {7, &H80, 4, &H38, &H10, 0, 2, 0, 0, 0, 0})
        seg(270000, &H80, {})

        Dim cues = ImageSubtitleDecoders.DecodePgs(ms.ToArray())
        Assert.Single(cues)
        Assert.Equal(TimeSpan.FromSeconds(1), cues(0).Start)
        Assert.Equal(TimeSpan.FromSeconds(3), cues(0).End)
        Assert.Equal(4, cues(0).Image.Width)
        Assert.Equal(2, cues(0).Image.Height)
        Assert.All(cues(0).Image.Luma, Sub(v) Assert.Equal(235, CInt(v)))
        Assert.All(cues(0).Image.Alpha, Sub(v) Assert.Equal(255, CInt(v)))

        Dim w = 0, h = 0
        Dim gray = ImageSubtitleDecoders.PrepareForOcr(cues(0).Image, w, h)
        Assert.True(w > 4 AndAlso h > 2)
        Assert.Equal(0, CInt(gray(16 * w + 16)))       ' text pixel became black
        Assert.Equal(255, CInt(gray(0)))               ' margin is white
    End Sub

    Private Shared Function BE32(v As Integer) As Byte()
        Return {CByte((v >> 24) And 255), CByte((v >> 16) And 255), CByte((v >> 8) And 255), CByte(v And 255)}
    End Function
End Class

Public Class ReleaseAssetTests
    Private Shared Function Rel(tag As String, ParamArray names As String()) As WebHelper.GitHubRelease
        Dim r As New WebHelper.GitHubRelease With {.Tag = tag}
        For Each n In names
            r.Assets.Add(New WebHelper.GitHubAsset With {.Name = n, .Url = "https://x/" & n, .Size = 1})
        Next
        Return r
    End Function

    Private Shared ReadOnly OldStyle As WebHelper.GitHubRelease = Rel("b7000",
        "llama-b7000-bin-win-cpu-x64.zip", "llama-b7000-bin-win-cpu-arm64.zip", "llama-b7000-bin-win-vulkan-x64.zip",
        "llama-b7000-bin-win-cuda-12.4-x64.zip", "cudart-llama-bin-win-cuda-12.4-x64.zip",
        "llama-b7000-bin-win-cuda-13.1-x64.zip", "cudart-llama-bin-win-cuda-13.1-x64.zip", "llama-b7000-bin-ubuntu-x64.zip")

    <Fact>
    Public Sub Cuda_PicksMainZipAndRuntime()
        Dim rv = LlamaEngineComponent.PickLlamaBuild(OldStyle, "cuda")
        Assert.Equal({"llama-b7000-bin-win-cuda-12.4-x64.zip", "cudart-llama-bin-win-cuda-12.4-x64.zip"}, rv.Files.Select(Function(f) f.FileName).ToArray())
        Assert.Equal("b7000 (cuda)", rv.Version)
    End Sub

    <Fact>
    Public Sub Vulkan_And_Cpu_SkipArm()
        Assert.Equal("llama-b7000-bin-win-vulkan-x64.zip", LlamaEngineComponent.PickLlamaBuild(OldStyle, "vulkan").Files.Single().FileName)
        Assert.Equal("llama-b7000-bin-win-cpu-x64.zip", LlamaEngineComponent.PickLlamaBuild(OldStyle, "cpu").Files.Single().FileName)
    End Sub

    <Fact>
    Public Sub NewVersionNaming_IsRecognised()
        Dim r = Rel("v1.0.0", "llama-v1.0.0-bin-win-cuda-13.5-x64.zip", "llama-v1.0.0-bin-win-vulkan-x64.zip", "llama-v1.0.0-bin-win-cpu-x64.zip")
        Dim rv = LlamaEngineComponent.PickLlamaBuild(r, "cuda")
        Assert.Equal("v1.0.0 (cuda)", rv.Version)
        Assert.Equal("llama-v1.0.0-bin-win-cuda-13.5-x64.zip", rv.Files.Single().FileName)
    End Sub

    <Fact>
    Public Sub ReleaseWithoutWindowsBinaries_ReturnsNothing()
        Assert.Null(LlamaEngineComponent.PickLlamaBuild(Rel("v9", "source.zip", "llama-v9-bin-macos-arm64.zip"), "cpu"))
    End Sub

    <Fact>
    Public Sub CudaMissing_FallsBackToVulkan()
        Dim r = Rel("b1", "llama-b1-bin-win-vulkan-x64.zip", "llama-b1-bin-win-cpu-x64.zip")
        Assert.Equal("b1 (vulkan)", LlamaEngineComponent.PickLlamaBuild(r, "cuda").Version)
    End Sub
End Class

Public Class GlossaryTests
    Private Shared ReadOnly G As Glossary = Glossary.Parse(
        "# comment" & vbLf & "Nick = Nick" & vbLf & "Winterfell =" & vbLf & "[el]" & vbLf & "Nick = Νίκος" & vbLf & "Ministry of Magic = Υπουργείο Μαγείας" & vbLf & "[de]" & vbLf & "Lord = Herr")

    <Fact>
    Public Sub Parse_ReadsSectionsAndEmptyTranslations()
        Assert.Equal(5, G.Entries.Count)
        Assert.Equal("Winterfell", G.Entries(1).Target)       ' empty → keep as is
        Assert.Equal("el", G.Entries(2).Language)
    End Sub

    <Fact>
    Public Sub ForText_PrefersLanguageSpecificAndMatchesWholeWords()
        Dim el = G.ForText("He left with Nick to Winterfell.", Languages.Find("el"))
        Assert.Equal(2, el.Count)
        Assert.Equal("Νίκος", el.First(Function(e) e.Source = "Nick").Target)
        Dim en = G.ForText("Nickname at the ministry of magic", Languages.Find("en"))
        Assert.Empty(en)                                     ' "Nickname" is not "Nick"; Ministry entry is Greek-only
        Assert.Single(G.ForText("lord", Languages.Find("de")))  ' case-insensitive
    End Sub

    <Fact>
    Public Sub Prompt_ContainsOnlyGivenTerms()
        Dim terms = G.ForText("Nick is here", Languages.Find("el"))
        Dim p = TranslationPrompt.Build("Nick is here", Languages.Find("en"), Languages.Find("el"), terms)
        Assert.Contains("Always use these translations: ""Nick"" = ""Νίκος"".", p)
        Assert.DoesNotContain("Always use", TranslationPrompt.Build("Hi", Languages.Find("en"), Languages.Find("el")))
    End Sub
End Class

Public Class ResumeAndSettingsTests
    <Fact>
    Public Sub Progress_IsReusedOnlyForSameSourceAndModel()
        Dim dir = Path.Combine(Path.GetTempPath(), "sm_prog_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Dim src = Path.Combine(dir, "a.en.srt")
        File.WriteAllText(src, "1" & vbLf & "00:00:01,000 --> 00:00:02,000" & vbLf & "Hi")
        Dim outp = Path.Combine(dir, "a.el.srt")
        Dim el = Languages.Find("el")

        Dim p1 = TranslationProgress.Open(outp, src, el, "4B")
        p1.Add(0, "Γεια")
        p1.Flush()
        Assert.Equal("Γεια", TranslationProgress.Open(outp, src, el, "4B").Done(0))
        Assert.Empty(TranslationProgress.Open(outp, src, el, "12B").Done)   ' other model → start over
        Dim p2 = TranslationProgress.Open(outp, src, el, "4B")
        Assert.Empty(p2.Done)                                                ' the 12B open removed the stale file
        p2.Add(0, "x") : p2.Flush() : p2.Complete()
        Assert.False(File.Exists(TranslationProgress.ProgressPath(outp)))
    End Sub

    <Fact>
    Public Sub Normalize_NewOptions()
        Dim s As New AppSettings With {.VideoOutput = "avi", .Theme = "pink", .WhenDone = "reboot"}
        s.Normalize()
        Assert.Equal("none", s.VideoOutput)
        Assert.Equal("system", s.Theme)
        Assert.Equal("nothing", s.WhenDone)
    End Sub

    <Fact>
    Public Sub WhenDone_IsNeverSaved()
        Dim json = Text.Json.JsonSerializer.Serialize(New AppSettings With {.WhenDone = "shutdown"})
        Assert.DoesNotContain("""WhenDone""", json)
    End Sub

    <Fact>
    Public Sub Encoders_AlwaysEndWithCpuFallback()
        Assert.StartsWith("-c:v libx264", VideoOutput.EncoderCandidates().Last())
    End Sub
End Class

Public Class CompareTests
    Private Shared Function Cue(t As String, text As String) As SrtCue
        Return New SrtCue With {.Timing = t, .Text = text}
    End Function

    <Fact>
    Public Sub Compare_FindsAllKindsOfDifferences()
        Dim a = New List(Of SrtCue) From {
            Cue("00:00:01,000 --> 00:00:02,000", "Hello there"),
            Cue("00:00:03,000 --> 00:00:04,000", "Where were you?"),
            Cue("00:00:05,000 --> 00:00:06,000", "Same text"),
            Cue("00:00:07,000 --> 00:00:08,000", "Only in A")}
        Dim b = New List(Of SrtCue) From {
            Cue("00:00:01,000 --> 00:00:02,000", "<i>Hello  there</i>"),       ' tags/spaces ignored
            Cue("00:00:03,000 --> 00:00:04,000", "Where are you?"),
            Cue("00:00:05,600 --> 00:00:06,500", "Same text"),
            Cue("00:00:10,000 --> 00:00:11,000", "Only in B")}
        Dim rows = SubtitleCompare.Compare(a, b, TimeSpan.FromMilliseconds(100))
        Assert.Equal({DiffKind.Same, DiffKind.TextChanged, DiffKind.TimingChanged, DiffKind.OnlyInA, DiffKind.OnlyInB}, rows.Select(Function(r) r.Kind).ToArray())
        Assert.Contains("1 same", SubtitleCompare.Summary(rows))
    End Sub

    <Fact>
    Public Sub Tolerance_HidesSmallTimingShifts()
        Dim a = New List(Of SrtCue) From {Cue("00:00:05,000 --> 00:00:06,000", "x")}
        Dim b = New List(Of SrtCue) From {Cue("00:00:05,080 --> 00:00:06,050", "x")}
        Assert.Equal(DiffKind.Same, SubtitleCompare.Compare(a, b, TimeSpan.FromMilliseconds(100))(0).Kind)
        Assert.Equal(DiffKind.TimingChanged, SubtitleCompare.Compare(a, b, TimeSpan.Zero)(0).Kind)
    End Sub

    <Fact>
    Public Sub WordDiff_MarksOnlyChangedWords()
        Dim ta As List(Of DiffToken) = Nothing, tb As List(Of DiffToken) = Nothing
        SubtitleCompare.WordDiff("Where were you last night?", "Where are you tonight?", ta, tb)
        Assert.Equal({"were", "last", "night"}, ta.Where(Function(t) t.Changed).Select(Function(t) t.Text).ToArray())
        Assert.Equal({"are", "tonight"}, tb.Where(Function(t) t.Changed).Select(Function(t) t.Text).ToArray())
        Assert.Equal("Where were you last night?", String.Concat(ta.Select(Function(t) t.Text)))
    End Sub
End Class

Public Class DefaultGlossaryTests
    <Fact>
    Public Sub StarterGlossary_HasEnglishAndGreekEntries()
        Dim g = Glossary.Parse(Glossary.Template)
        Assert.Equal("κ.", g.ForText("Mr. Smith is here", Languages.Find("el")).Single().Target)
        Assert.Equal("FBI", g.ForText("Call the FBI", Languages.Find("el")).Single().Target)
        Assert.Equal("Mr.", g.ForText("Ο κ. Παπαδόπουλος", Languages.Find("en")).Single().Target)
        Assert.Empty(g.ForText("κ.λπ.", Languages.Find("en")))   ' abbreviation inside another word is not matched
    End Sub

    <Fact>
    Public Sub ReplaceOriginal_IsDefaultAndMigrated()
        Assert.True(New AppSettings().ReplaceOriginal)
    End Sub

    <Fact>
    Public Sub HearingImpaired_IsOnByDefault()
        Assert.True(New AppSettings().RemoveHearingImpaired)
    End Sub

    <Fact>
    Public Sub StarterGlossary_HasNamesAndNoDuplicates()
        Dim g = Glossary.Parse(Glossary.Template)
        Assert.True(g.Entries.Count > 200)
        Dim dupes = g.Entries.GroupBy(Function(e) (If(e.Language, "*"), e.Source.ToLowerInvariant())).Where(Function(x) x.Count() > 1).Select(Function(x) x.Key.Item2).ToList()
        Assert.Empty(dupes)
        Dim el = Languages.Find("el")
        Assert.Equal("Τζον", g.ForText("John, wait!", el).Single().Target)
        Assert.Equal("Ένταλμα έρευνας", g.ForText("We have a search warrant.", el).OrderByDescending(Function(e) e.Source.Length).First().Target)
        Assert.Equal("Nikos", g.ForText("Νίκο, έλα!", Languages.Find("en")).Single().Target)
    End Sub

    <Fact>
    Public Sub OldUneditedGlossary_IsRecognised()
        Assert.False(Glossary.IsUneditedOldTemplate(Glossary.Template))
        Assert.False(Glossary.IsUneditedOldTemplate("Nick = Νίκος"))
    End Sub
End Class

Public Class CleanerTests
    <Theory>
    <InlineData("[MUSIC PLAYING]", "")>
    <InlineData("(laughs) That's funny.", "That's funny.")>
    <InlineData("JOHN: Where are you?", "Where are you?")>
    <InlineData("- JOHN: Hi." & vbLf & "- MARY: Hello.", "- Hi." & vbLf & "- Hello.")>
    <InlineData("- Get down!" & vbLf & "- [gunshots]", "Get down!")>
    <InlineData("♪ I will always love you ♪", "")>
    <InlineData("<i>♪ la la la ♪</i>", "")>
    <InlineData("ΓΙΑΝΝΗΣ: Τι κάνεις;", "Τι κάνεις;")>
    <InlineData("<i>(narrator) Long ago...</i>", "<i>Long ago...</i>")>
    <InlineData("It's 10:30 already.", "It's 10:30 already.")>
    <InlineData("I said: no.", "I said: no.")>
    Public Sub CleanText_RemovesHearingImpairedParts(input As String, expected As String)
        Assert.Equal(expected, SubtitleCleaner.CleanText(input))
    End Sub

    <Fact>
    Public Sub Clean_DropsEmptyCuesAndRenumbers()
        Dim cues As New List(Of SrtCue) From {
            New SrtCue With {.Timing = "00:00:01,000 --> 00:00:02,000", .Text = "[door opens]"},
            New SrtCue With {.Timing = "00:00:03,000 --> 00:00:04,000", .Text = "Hello."}}
        Dim r = SubtitleCleaner.Clean(cues)
        Assert.Single(r)
        Assert.Equal(1, r(0).Index)
        Assert.Equal("Hello.", r(0).Text)
    End Sub
End Class

Public Class LocalizationTests
    Private Shared Function RepoRoot() As String
        Dim d = New DirectoryInfo(AppContext.BaseDirectory)
        While d IsNot Nothing AndAlso Not File.Exists(Path.Combine(d.FullName, "Subtitle Machine.vbproj"))
            d = d.Parent
        End While
        Return d?.FullName
    End Function

    ''' <summary>Every text passed to Loc.Tr (and every designer caption) must have a Greek translation.</summary>
    <Fact>
    Public Sub EveryInterfaceText_HasAGreekTranslation()
        Dim root = RepoRoot()
        Assert.NotNull(root)
        Dim missing As New List(Of String)
        Dim lit = """((?:[^""]|"""")*)"""
        For Each f In Directory.EnumerateFiles(root, "*.vb", SearchOption.AllDirectories)
            If f.Contains(Path.DirectorySeparatorChar & "tests" & Path.DirectorySeparatorChar) OrElse f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) Then Continue For
            Dim src = File.ReadAllText(f)
            Dim patterns = New List(Of String) From {"Loc\.Tr\(" & lit}
            If f.EndsWith(".Designer.vb") Then patterns.Add("\.(?:Text|HeaderText) = " & lit)
            For Each p In patterns
                For Each m As Text.RegularExpressions.Match In Text.RegularExpressions.Regex.Matches(src, p)
                    Dim key = m.Groups(1).Value.Replace("""""", """")
                    If Not Text.RegularExpressions.Regex.IsMatch(key, "[A-Za-z]{2,}") Then Continue For
                    If Not Loc.HasTranslation(key) Then missing.Add($"{Path.GetFileName(f)}: {key}")
                Next
            Next
        Next
        Assert.True(missing.Count = 0, "Missing Greek texts:" & vbLf & String.Join(vbLf, missing.Distinct()))
    End Sub

    <Fact>
    Public Sub Tr_FormatsInGreekAndFallsBackToEnglish()
        Dim previous = Loc.Language
        Try
            Loc.Language = "el"
            Assert.Equal("Προστέθηκαν 3 αρχεία – 5 στη λίστα.", Loc.Tr("{0} file(s) added – {1} in the list.", 3, 5))
            Assert.Equal("Something new", Loc.Tr("Something new"))
            Assert.Equal("Ελληνικά", Languages.Find("el").DisplayName)
            Assert.Equal("Greek", Languages.Find("el").Name)          ' the AI prompt keeps English names
            Loc.Language = "en"
            Assert.Equal("3 file(s) added – 5 in the list.", Loc.Tr("{0} file(s) added – {1} in the list.", 3, 5))
        Finally
            Loc.Language = previous
        End Try
    End Sub
End Class
