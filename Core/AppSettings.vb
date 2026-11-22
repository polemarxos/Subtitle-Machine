Imports System.IO
Imports System.Text
Imports System.Text.Json

''' <summary>User settings, persisted to config.json next to the exe (or in LocalAppData).</summary>
Public Class AppSettings
    ''' <summary>Target languages (ISO 639-1). One subtitle file is created per language.</summary>
    Public Property TargetLanguages As New List(Of String) From {"el"}
    ''' <summary>Old single-language setting (v1.0–1.2). Read once for migration.</summary>
    Public Property TargetLanguage As String

    ''' <summary>Source language for audio / SRT, ISO 639-1 code or "auto".</summary>
    Public Property TranscribeLanguage As String = "auto"

    ''' <summary>"auto" = embedded subtitles if present, otherwise audio; "embedded" = only embedded subtitles; "audio" = always transcribe the audio.</summary>
    Public Property SubtitleSource As String = "auto"

    ''' <summary>"auto" (choose by VRAM), "4b", "12b" or "27b".</summary>
    Public Property TranslateModelSize As String = "auto"
    ''' <summary>"auto", "cuda", "vulkan" or "cpu" (llama.cpp build; whisper uses CUDA or CPU).</summary>
    Public Property GpuBackend As String = "auto"

    ''' <summary>whisper.cpp model name, e.g. "large-v3-turbo" → ggml-large-v3-turbo.bin</summary>
    Public Property WhisperModel As String = "large-v3-turbo"
    ''' <summary>Use Silero voice detection so Whisper skips silence (fewer invented lines).</summary>
    Public Property UseVad As Boolean = True

    ''' <summary>Read image subtitles (PGS / VobSub) with Windows OCR.</summary>
    Public Property OcrImageSubtitles As Boolean = True
    ''' <summary>Translate several neighbouring lines together so names and tone stay consistent.</summary>
    Public Property ContextTranslation As Boolean = True
    ''' <summary>Re-wrap long lines and fix too-short / overlapping display times.</summary>
    ''' <summary>Remove texts for the hearing impaired: [MUSIC], (laughs), JOHN:, ♪ lyrics ♪.</summary>
    Public Property RemoveHearingImpaired As Boolean = True
    ''' <summary>Match the timing of subtitle files to the speech of the video next to them.</summary>
    Public Property AutoSyncSubtitles As Boolean = False
    ''' <summary>"translate" (default) or "sync" (only match the subtitle timing to the video).</summary>
    Public Property JobMode As String = "translate"
    ''' <summary>Mark spelling mistakes in the subtitle editor.</summary>
    Public Property SpellCheck As Boolean = True
    ''' <summary>Write subtitle_machine.log (max ~10 MB: 5 MB + one old file).</summary>
    Public Property WriteLog As Boolean = False
    Public Property FixFormatting As Boolean = True
    Public Property MaxLineLength As Integer = 42

    ''' <summary>Write the translation into the original subtitle file instead of creating "Movie.el.srt".</summary>
    Public Property ReplaceOriginal As Boolean = True
    ''' <summary>Settings format version (used to apply new defaults once).</summary>
    Public Property ConfigVersion As Integer = 4
    ''' <summary>Re-create output files that already exist.</summary>
    Public Property OverwriteExisting As Boolean = False

    ''' <summary>"none", "mkv" (add subtitles into an MKV copy), "burn" (burned-in MP4 copy) or "both".</summary>
    Public Property VideoOutput As String = "none"
    ''' <summary>Keep the .srt files after adding them into the MKV.</summary>
    Public Property KeepSrtFiles As Boolean = True
    ''' <summary>"system", "dark" or "light".</summary>
    Public Property Theme As String = "system"
    ''' <summary>Windows notification + sound when a batch finishes.</summary>
    Public Property NotifyWhenDone As Boolean = True

    ''' <summary>Interface language: "auto" (like Windows), "en" or "el".</summary>
    Public Property UiLanguage As String = "auto"

    Public Property AutoUpdateApp As Boolean = True
    Public Property CheckComponentUpdates As Boolean = True

    ' ----- persistence -----
    Private Shared ReadOnly JsonOpts As New JsonSerializerOptions With {.WriteIndented = True}

    Public Shared Function Load() As AppSettings
        Dim s As AppSettings = Nothing
        Try
            If File.Exists(AppPaths.SettingsFile) Then
                s = JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8))
            End If
        Catch ex As Exception
            Logger.Log("Failed to read config.json: " & ex.Message)
        End Try
        If s Is Nothing Then s = New AppSettings()
        If s.ConfigVersion < 2 Then
            s.ReplaceOriginal = True ' 1.4.1: replacing the original subtitle file is the default again
            s.ConfigVersion = 2
        End If
        If s.ConfigVersion < 3 Then
            s.RemoveHearingImpaired = True ' 1.5.1: removing hearing-impaired texts is on by default
            s.ConfigVersion = 3
        End If
        If s.ConfigVersion < 4 Then
            ' 1.8.3: auto-sync and the log file are off by default
            s.AutoSyncSubtitles = False
            s.WriteLog = False
            s.ConfigVersion = 4
        End If
        s.Normalize()
        Logger.Enabled = s.WriteLog
        Return s
    End Function

    ''' <summary>Fixes invalid values and migrates settings of older versions.</summary>
    Public Sub Normalize()
        If JobMode <> "sync" Then JobMode = "translate"
        ' v1.0–1.2 stored one language (sometimes as a name, "Greek").
        If Not String.IsNullOrWhiteSpace(TargetLanguage) Then
            Dim old = Languages.Find(TargetLanguage)
            If old IsNot Nothing Then TargetLanguages = New List(Of String) From {old.Code}
            TargetLanguage = Nothing
        End If
        TargetLanguages = If(TargetLanguages, New List(Of String)).
            Select(Function(c) Languages.Find(c)).Where(Function(l) l IsNot Nothing).
            Select(Function(l) l.Code).Distinct().ToList()
        If TargetLanguages.Count = 0 Then TargetLanguages.Add("el")

        If TranscribeLanguage <> "auto" Then
            Dim src = Languages.Find(TranscribeLanguage)
            TranscribeLanguage = If(src IsNot Nothing, src.Code, "auto")
        End If
        If Not {"auto", "embedded", "audio"}.Contains(SubtitleSource) Then SubtitleSource = "auto"
        If Not {"auto", "4b", "12b", "27b"}.Contains(If(TranslateModelSize, "").ToLowerInvariant()) Then TranslateModelSize = "auto"
        If Not {"auto", "cuda", "vulkan", "cpu"}.Contains(If(GpuBackend, "").ToLowerInvariant()) Then GpuBackend = "auto"
        If String.IsNullOrWhiteSpace(WhisperModel) Then WhisperModel = "large-v3-turbo"
        If MaxLineLength < 20 OrElse MaxLineLength > 80 Then MaxLineLength = 42
        If Not {"none", "mkv", "burn", "both"}.Contains(VideoOutput) Then VideoOutput = "none"
        If Not {"system", "dark", "light"}.Contains(Theme) Then Theme = "system"
        If Not {"auto", "en", "el"}.Contains(UiLanguage) Then UiLanguage = "auto"
    End Sub

    Public Sub Save()
        Logger.Enabled = WriteLog
        Try
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Me, JsonOpts), Encoding.UTF8)
        Catch ex As Exception
            Logger.Log("Failed to save config.json: " & ex.Message)
        End Try
    End Sub

    Public Function Targets() As List(Of LanguageInfo)
        Return TargetLanguages.Select(Function(c) Languages.Find(c)).Where(Function(l) l IsNot Nothing).ToList()
    End Function

    ''' <summary>Resolves "auto" to 4b/12b/27b using the detected VRAM.</summary>
    Public Function EffectiveModelSize() As String
        Dim s = If(TranslateModelSize, "auto").ToLowerInvariant()
        If s = "4b" OrElse s = "12b" OrElse s = "27b" Then Return s
        Return HardwareInfo.RecommendedTranslateSize()
    End Function

    ''' <summary>Resolves "auto" to cuda/vulkan/cpu using the detected GPU.</summary>
    Public Function EffectiveGpuBackend() As String
        Dim s = If(GpuBackend, "auto").ToLowerInvariant()
        If s = "cuda" OrElse s = "vulkan" OrElse s = "cpu" Then Return s
        Return HardwareInfo.RecommendedGpuBackend()
    End Function
End Class
