Imports System.IO
Imports System.IO.Compression
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading

Public Class StatusUpdate
    Public Property Text As String
    ''' <summary>0-100, or -1 when unknown.</summary>
    Public Property Percent As Integer = -1
    Public Sub New(text As String, Optional percent As Integer = -1)
        Me.Text = text
        Me.Percent = percent
    End Sub
End Class

Public Class RemoteFile
    Public Property Url As String
    Public Property FileName As String
    Public Property Size As Long
    Public Property Sha256 As String
End Class

Public Class RemoteVersion
    Public Property Version As String
    Public Property Files As New List(Of RemoteFile)
    Public ReadOnly Property TotalSize As Long
        Get
            Return Files.Sum(Function(f) Math.Max(0L, f.Size))
        End Get
    End Property
End Class

''' <summary>Installed versions, stored in tools\versions.json.</summary>
Public Module VersionStore
    Private ReadOnly _lock As New Object()
    Private _data As Dictionary(Of String, String)

    Private Sub EnsureLoaded()
        If _data IsNot Nothing Then Return
        _data = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            If File.Exists(AppPaths.VersionsFile) Then
                _data = New Dictionary(Of String, String)(
                    JsonSerializer.Deserialize(Of Dictionary(Of String, String))(File.ReadAllText(AppPaths.VersionsFile)),
                    StringComparer.OrdinalIgnoreCase)
            End If
        Catch ex As Exception
            Logger.Log("versions.json unreadable: " & ex.Message)
        End Try
    End Sub

    Public Function GetVersion(id As String) As String
        SyncLock _lock
            EnsureLoaded()
            Dim v As String = Nothing
            Return If(_data.TryGetValue(id, v), v, Nothing)
        End SyncLock
    End Function

    Public Sub SetVersion(id As String, version As String)
        SyncLock _lock
            EnsureLoaded()
            _data(id) = version
            File.WriteAllText(AppPaths.VersionsFile, JsonSerializer.Serialize(_data, New JsonSerializerOptions With {.WriteIndented = True}))
        End SyncLock
    End Sub
End Module

''' <summary>A downloadable external dependency (ffmpeg, whisper.cpp, a model, …).</summary>
Public MustInherit Class ToolComponent
    Protected ReadOnly Settings As AppSettings

    Protected Sub New(settings As AppSettings)
        Me.Settings = settings
    End Sub

    Public MustOverride ReadOnly Property Id As String
    Public MustOverride ReadOnly Property DisplayName As String
    Public MustOverride ReadOnly Property Source As String
    Public MustOverride Function IsInstalled() As Boolean
    Public MustOverride Function GetLatestAsync(ct As CancellationToken) As Task(Of RemoteVersion)
    Protected MustOverride Function InstallFilesAsync(latest As RemoteVersion, downloaded As List(Of String), ct As CancellationToken) As Task

    Public Overridable Function IsRequired() As Boolean
        Return True
    End Function

    Public Overridable Function InstalledVersion() As String
        If Not IsInstalled() Then Return Nothing
        Return If(VersionStore.GetVersion(Id), "unknown")
    End Function

    Public Function NeedsInstallOrUpdate(latest As RemoteVersion) As Boolean
        If Not IsInstalled() Then Return True
        If latest Is Nothing Then Return False
        Return Not String.Equals(InstalledVersion(), latest.Version, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Downloads every file of <paramref name="latest"/> (resumable, verified) and installs it.</summary>
    Public Overridable Async Function InstallAsync(latest As RemoteVersion, progress As IProgress(Of StatusUpdate), ct As CancellationToken) As Task
        Dim downloaded As New List(Of String)
        Dim dlProgress As New Progress(Of DownloadProgress)(Sub(p) progress?.Report(New StatusUpdate($"{DisplayName}: {p}", p.Percent)))
        For Each f In latest.Files
            Dim dest = Path.Combine(AppPaths.DownloadsDir, f.FileName)
            If f.Size > 0 AndAlso File.Exists(dest) AndAlso New FileInfo(dest).Length = f.Size Then
                downloaded.Add(dest) ' already downloaded earlier
                Continue For
            End If
            Await WebHelper.DownloadFileAsync(f.Url, dest, dlProgress, ct, f.Sha256)
            downloaded.Add(dest)
        Next
        progress?.Report(New StatusUpdate(Loc.Tr("{0}: installing…", DisplayName)))
        Await InstallFilesAsync(latest, downloaded, ct)
        VersionStore.SetVersion(Id, latest.Version)
        progress?.Report(New StatusUpdate(Loc.Tr("{0}: installed {1}", DisplayName, latest.Version), 100))
        Logger.Log($"Installed {Id} {latest.Version}")
    End Function

    ' ---- helpers ----
    Protected Shared Function FindFile(dir As String, ParamArray names As String()) As String
        If Not Directory.Exists(dir) Then Return Nothing
        For Each n In names
            Dim hit = Directory.EnumerateFiles(dir, n, SearchOption.AllDirectories).FirstOrDefault()
            If hit IsNot Nothing Then Return hit
        Next
        Return Nothing
    End Function

    Protected Shared Sub ExtractZipClean(zips As IEnumerable(Of String), targetDir As String)
        ' Extract into a temp folder first, then swap – a failed extraction never leaves a half-installed tool.
        Dim tmp = targetDir & ".new"
        If Directory.Exists(tmp) Then Directory.Delete(tmp, True)
        Directory.CreateDirectory(tmp)
        For Each z In zips
            ZipFile.ExtractToDirectory(z, tmp, overwriteFiles:=True)
        Next
        If Directory.Exists(targetDir) Then Directory.Delete(targetDir, True)
        Directory.Move(tmp, targetDir)
        For Each z In zips
            Try : File.Delete(z) : Catch : End Try
        Next
    End Sub

    ''' <summary>First asset matching any of the patterns (tried in order).</summary>
    Protected Shared Function PickAsset(rel As WebHelper.GitHubRelease, ParamArray patterns As String()) As WebHelper.GitHubAsset
        For Each pattern In patterns
            Dim hit = rel.Assets.FirstOrDefault(Function(a) Regex.IsMatch(a.Name, pattern, RegexOptions.IgnoreCase))
            If hit IsNot Nothing Then Return hit
        Next
        Return Nothing
    End Function

    Protected Shared Function ToRemote(a As WebHelper.GitHubAsset) As RemoteFile
        Return New RemoteFile With {.Url = a.Url, .FileName = a.Name, .Size = a.Size, .Sha256 = a.Sha256}
    End Function
End Class

' =====================================================================
'  FFmpeg  – gyan.dev "release essentials" build (official Windows builds linked from ffmpeg.org)
' =====================================================================
Public Class FfmpegComponent
    Inherits ToolComponent

    Public Sub New(s As AppSettings)
        MyBase.New(s)
    End Sub

    Public Shared ReadOnly Property Dir As String
        Get
            Return Path.Combine(AppPaths.ToolsDir, "ffmpeg")
        End Get
    End Property
    Public Shared ReadOnly Property FfmpegExe As String
        Get
            Return Path.Combine(Dir, "ffmpeg.exe")
        End Get
    End Property
    Public Shared ReadOnly Property FfprobeExe As String
        Get
            Return Path.Combine(Dir, "ffprobe.exe")
        End Get
    End Property

    Public Overrides ReadOnly Property Id As String = "ffmpeg"
    Public Overrides ReadOnly Property DisplayName As String = "FFmpeg / FFprobe / FFplay"
    Public Overrides ReadOnly Property Source As String = "gyan.dev (release essentials)"

    Public Overrides Function IsInstalled() As Boolean
        Return File.Exists(FfmpegExe) AndAlso File.Exists(FfprobeExe)
    End Function

    Public Overrides Async Function GetLatestAsync(ct As CancellationToken) As Task(Of RemoteVersion)
        Try
            Dim ver = (Await WebHelper.GetStringAsync("https://www.gyan.dev/ffmpeg/builds/release-version", ct)).Trim()
            Dim sha As String = Nothing
            Try
                sha = (Await WebHelper.GetStringAsync("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256", ct)).Trim().Split({" "c, "*"c}, StringSplitOptions.RemoveEmptyEntries)(0)
            Catch
            End Try
            Dim rv As New RemoteVersion With {.Version = ver}
            rv.Files.Add(New RemoteFile With {.Url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
                                              .FileName = $"ffmpeg-{ver}-essentials.zip", .Size = 0, .Sha256 = sha})
            Return rv
        Catch ex As Exception When Not ct.IsCancellationRequested
            ' gyan.dev unreachable → use the author's GitHub mirror.
            Logger.Log("gyan.dev failed, using GitHub mirror: " & ex.Message)
        End Try
        Dim rel = Await WebHelper.GetLatestGitHubReleaseAsync("GyanD/codexffmpeg", ct)
        Dim asset = PickAsset(rel, "essentials_build\.zip$")
        If asset Is Nothing Then Throw New Exception("No FFmpeg essentials build found.")
        Dim r As New RemoteVersion With {.Version = rel.Tag}
        r.Files.Add(ToRemote(asset))
        Return r
    End Function

    Protected Overrides Function InstallFilesAsync(latest As RemoteVersion, downloaded As List(Of String), ct As CancellationToken) As Task
        Dim tmp = Path.Combine(AppPaths.DownloadsDir, "ffmpeg_extract")
        If Directory.Exists(tmp) Then Directory.Delete(tmp, True)
        ZipFile.ExtractToDirectory(downloaded(0), tmp)
        Directory.CreateDirectory(Dir)
        For Each exe In {"ffmpeg.exe", "ffprobe.exe", "ffplay.exe"}
            Dim src = FindFile(tmp, exe)
            If src IsNot Nothing Then File.Copy(src, Path.Combine(Dir, exe), True)
        Next
        Directory.Delete(tmp, True)
        File.Delete(downloaded(0))
        If Not IsInstalled() Then Throw New Exception("FFmpeg archive did not contain ffmpeg.exe / ffprobe.exe")
        Return Task.CompletedTask
    End Function
End Class

' =====================================================================
'  whisper.cpp – official GitHub releases (ggml-org/whisper.cpp)
' =====================================================================
Public Class WhisperEngineComponent
    Inherits ToolComponent

    Public Sub New(s As AppSettings)
        MyBase.New(s)
    End Sub

    Public Shared ReadOnly Property Dir As String
        Get
            Return Path.Combine(AppPaths.ToolsDir, "whisper-cpp")
        End Get
    End Property

    ''' <summary>whisper-cli.exe (new) / main.exe (old releases) / legacy tools\whisper\whisper.exe.</summary>
    Public Shared Function FindExe() As String
        Return If(FindFile(Dir, "whisper-cli.exe", "main.exe"),
                  FindFile(Path.Combine(AppPaths.ToolsDir, "whisper"), "whisper-cli.exe", "whisper.exe"))
    End Function

    ''' <summary>Speech detection tool of whisper.cpp (used by auto-sync); part of the official Windows builds.</summary>
    Public Shared Function FindVadExe() As String
        Return FindFile(Dir, "whisper-vad-speech-segments.exe")
    End Function

    Public Overrides ReadOnly Property Id As String = "whisper"
    Public Overrides ReadOnly Property DisplayName As String = Loc.Tr("whisper.cpp (speech-to-text engine)")
    Public Overrides ReadOnly Property Source As String = "github.com/ggml-org/whisper.cpp"

    Private Function BuildVariant() As String
        Return If(Settings.EffectiveGpuBackend() = "cuda", "cuda", "cpu")
    End Function

    Public Overrides Function IsInstalled() As Boolean
        Return FindExe() IsNot Nothing
    End Function

    Public Overrides Async Function GetLatestAsync(ct As CancellationToken) As Task(Of RemoteVersion)
        ' Not every whisper.cpp release ships Windows binaries → use the newest one that does.
        Dim wantCuda = BuildVariant() = "cuda"
        For Each rel In Await WebHelper.GetGitHubReleasesAsync("ggml-org/whisper.cpp", ct)
            Dim v = "cpu"
            Dim asset As WebHelper.GitHubAsset = Nothing
            If wantCuda Then
                asset = PickAsset(rel, "^whisper-cublas-12[\d.]*-bin-x64\.zip$", "^whisper.*win.*cuda.?12.*x64.*\.zip$")
                If asset IsNot Nothing Then v = "cuda"
            End If
            If asset Is Nothing Then asset = PickAsset(rel, "^whisper-blas-bin-x64\.zip$", "^whisper-bin-x64\.zip$", "^whisper.*win.*cpu.*x64.*\.zip$")
            If asset Is Nothing Then Continue For
            Dim rv As New RemoteVersion With {.Version = $"{rel.Tag} ({v})"}
            rv.Files.Add(ToRemote(asset))
            Return rv
        Next
        Throw New Exception("No recent whisper.cpp release contains Windows x64 binaries.")
    End Function

    Protected Overrides Function InstallFilesAsync(latest As RemoteVersion, downloaded As List(Of String), ct As CancellationToken) As Task
        ExtractZipClean(downloaded, Dir)
        If FindFile(Dir, "whisper-cli.exe", "main.exe") Is Nothing Then Throw New Exception("whisper-cli.exe not found in the downloaded archive")
        Return Task.CompletedTask
    End Function
End Class

' =====================================================================
'  Whisper model – huggingface.co/ggerganov/whisper.cpp
' =====================================================================
Public Class WhisperModelComponent
    Inherits ToolComponent

    Public Shared ReadOnly Models As (Name As String, Description As String)() = {
        ("large-v3-turbo", Loc.Tr("Large v3 Turbo – best speed/quality (1.6 GB) – recommended")),
        ("large-v3-turbo-q5_0", Loc.Tr("Large v3 Turbo Q5 – smaller, almost same quality (0.6 GB)")),
        ("large-v3", Loc.Tr("Large v3 – best quality, slow (3.1 GB)")),
        ("medium", Loc.Tr("Medium (1.5 GB)")),
        ("small", Loc.Tr("Small – fast, for weak PCs (0.5 GB)")),
        ("base", Loc.Tr("Base – very fast, low quality (0.15 GB)"))
    }

    Public Sub New(s As AppSettings)
        MyBase.New(s)
    End Sub

    Public Shared Function ModelPath(name As String) As String
        Return Path.Combine(AppPaths.ModelsDir, $"ggml-{name}.bin")
    End Function

    Public Overrides ReadOnly Property Id As String = "whisper-model"
    Public Overrides ReadOnly Property DisplayName As String
        Get
            Return Loc.Tr("Whisper model ({0})", Settings.WhisperModel)
        End Get
    End Property
    Public Overrides ReadOnly Property Source As String = "huggingface.co/ggerganov/whisper.cpp"

    Public Overrides Function IsInstalled() As Boolean
        Return File.Exists(ModelPath(Settings.WhisperModel))
    End Function

    Public Overrides Function InstalledVersion() As String
        Return If(IsInstalled(), Settings.WhisperModel, Nothing)
    End Function

    Public Overrides Async Function GetLatestAsync(ct As CancellationToken) As Task(Of RemoteVersion)
        Dim fileName = $"ggml-{Settings.WhisperModel}.bin"
        Dim rf As New RemoteFile With {.FileName = fileName, .Url = WebHelper.HuggingFaceUrl("ggerganov/whisper.cpp", fileName)}
        Dim list = Await WebHelper.ListHuggingFaceFilesAsync("ggerganov/whisper.cpp", ct)
        Dim hit = list?.FirstOrDefault(Function(f) f.Name = fileName)
        If list IsNot Nothing AndAlso hit Is Nothing Then Throw New Exception($"Whisper model '{fileName}' does not exist on Hugging Face.")
        If hit IsNot Nothing Then
            rf.Size = hit.Size
            rf.Sha256 = hit.Sha256
        End If
        Dim rv As New RemoteVersion With {.Version = Settings.WhisperModel}
        rv.Files.Add(rf)
        Return rv
    End Function

    Protected Overrides Function InstallFilesAsync(latest As RemoteVersion, downloaded As List(Of String), ct As CancellationToken) As Task
        File.Move(downloaded(0), ModelPath(Settings.WhisperModel), True)
        Return Task.CompletedTask
    End Function
End Class

' =====================================================================
'  llama.cpp server – official GitHub releases (ggml-org/llama.cpp). Runs TranslateGemma locally.
' =====================================================================
Public Class LlamaEngineComponent
    Inherits ToolComponent

    Public Sub New(s As AppSettings)
        MyBase.New(s)
    End Sub

    Public Shared ReadOnly Property Dir As String
        Get
            Return Path.Combine(AppPaths.ToolsDir, "llama-cpp")
        End Get
    End Property

    Public Shared Function FindExe() As String
        Return FindFile(Dir, "llama-server.exe")
    End Function

    Public Overrides ReadOnly Property Id As String = "llama"
    Public Overrides ReadOnly Property DisplayName As String = Loc.Tr("llama.cpp (AI translation engine)")
    Public Overrides ReadOnly Property Source As String = "github.com/ggml-org/llama.cpp"

    Public Overrides Function IsInstalled() As Boolean
        Return FindExe() IsNot Nothing
    End Function

    Public Overrides Async Function GetLatestAsync(ct As CancellationToken) As Task(Of RemoteVersion)
        Dim wanted = Settings.EffectiveGpuBackend()
        For Each rel In Await WebHelper.GetGitHubReleasesAsync("ggml-org/llama.cpp", ct)
            Dim rv = PickLlamaBuild(rel, wanted)
            If rv IsNot Nothing Then Return rv
        Next
        Throw New Exception("No recent llama.cpp release contains Windows x64 binaries.")
    End Function

    ''' <summary>CUDA (12, then 13) → Vulkan → CPU. Asset names changed over time (bNNNN / v1.0.0), so matching is loose.</summary>
    Friend Shared Function PickLlamaBuild(rel As WebHelper.GitHubRelease, wanted As String) As RemoteVersion
        Dim v = wanted
        Dim files As New List(Of WebHelper.GitHubAsset)
        Dim notArm = Function(a As WebHelper.GitHubAsset) Not a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase)
        If v = "cuda" Then
            For Each major In {"12", "13"}
                Dim main = rel.Assets.FirstOrDefault(Function(a) notArm(a) AndAlso Not a.Name.StartsWith("cudart", StringComparison.OrdinalIgnoreCase) AndAlso
                                                          Regex.IsMatch(a.Name, $"^llama.*win.*cuda.?{major}[\d.]*.*x64.*\.zip$", RegexOptions.IgnoreCase))
                If main Is Nothing Then Continue For
                files.Add(main)
                Dim rt = rel.Assets.FirstOrDefault(Function(a) Regex.IsMatch(a.Name, $"^cudart.*win.*cuda.?{major}[\d.]*.*\.zip$", RegexOptions.IgnoreCase))
                If rt IsNot Nothing Then files.Add(rt) ' CUDA runtime DLLs (may be bundled in newer builds)
                Exit For
            Next
            If files.Count = 0 Then v = "vulkan"
        End If
        If v = "vulkan" Then
            Dim a = rel.Assets.FirstOrDefault(Function(x) notArm(x) AndAlso Regex.IsMatch(x.Name, "^llama.*win.*vulkan.*x64.*\.zip$", RegexOptions.IgnoreCase))
            If a IsNot Nothing Then files.Add(a) Else v = "cpu"
        End If
        If v = "cpu" Then
            Dim a = rel.Assets.FirstOrDefault(Function(x) notArm(x) AndAlso Regex.IsMatch(x.Name, "^llama.*win.*(cpu|avx2).*x64.*\.zip$", RegexOptions.IgnoreCase))
            If a Is Nothing Then Return Nothing
            files.Add(a)
        End If
        Dim rv As New RemoteVersion With {.Version = $"{rel.Tag} ({v})"}
        rv.Files.AddRange(files.Select(AddressOf ToRemote))
        Return rv
    End Function

    Protected Overrides Function InstallFilesAsync(latest As RemoteVersion, downloaded As List(Of String), ct As CancellationToken) As Task
        LlamaServer.StopAll() ' the exe must not be in use
        ExtractZipClean(downloaded, Dir)
        If FindExe() Is Nothing Then Throw New Exception("llama-server.exe not found in the downloaded archive")
        Return Task.CompletedTask
    End Function
End Class

' =====================================================================
'  TranslateGemma GGUF (4B / 12B / 27B, Q4_K_M) from Hugging Face –
'  GGUF conversions of google/translategemma-*-it, the same models as ollama.com/library/translategemma.
' =====================================================================
Public Class TranslateModelComponent
    Inherits ToolComponent

    Public Shared ReadOnly Sizes As (Tag As String, Download As String, Vram As String)() = {
        ("4b", "2.5 GB", Loc.Tr("4+ GB VRAM or CPU")),
        ("12b", "7.3 GB", "10+ GB VRAM"),
        ("27b", "16.5 GB", "22+ GB VRAM")
    }

    Public Sub New(s As AppSettings)
        MyBase.New(s)
    End Sub

    Private ReadOnly Property Size As String
        Get
            Return Settings.EffectiveModelSize()
        End Get
    End Property

    Public Shared Function ModelPath(size As String) As String
        Return Path.Combine(AppPaths.ModelsDir, $"translategemma-{size}-it.Q4_K_M.gguf")
    End Function

    Public ReadOnly Property LocalPath As String
        Get
            Return ModelPath(Size)
        End Get
    End Property

    Public Overrides ReadOnly Property Id As String
        Get
            Return "translategemma-" & Size
        End Get
    End Property
    Public Overrides ReadOnly Property DisplayName As String
        Get
            Return Loc.Tr("TranslateGemma {0} (AI translation model)", Size.ToUpperInvariant())
        End Get
    End Property
    Public Overrides ReadOnly Property Source As String = "huggingface.co (GGUF of google/translategemma)"

    Public Overrides Function IsInstalled() As Boolean
        Return File.Exists(LocalPath)
    End Function

    Public Overrides Function InstalledVersion() As String
        If Not IsInstalled() Then Return Nothing
        Return If(VersionStore.GetVersion(Id), "installed")
    End Function

    ''' <summary>Repos tried in order; the first one with a matching Q4_K_M GGUF wins.</summary>
    Private Function CandidateRepos() As String()
        Return {
            $"mradermacher/translategemma-{Size}-it-GGUF",
            "anilb/translategemma-gguf",
            $"bartowski/google_translategemma-{Size}-it-GGUF"
        }
    End Function

    Public Overrides Async Function GetLatestAsync(ct As CancellationToken) As Task(Of RemoteVersion)
        For Each repo In CandidateRepos()
            Dim files = Await WebHelper.ListHuggingFaceFilesAsync(repo, ct)
            If files Is Nothing Then Continue For
            Dim hit = files.FirstOrDefault(Function(f)
                                               Dim n = f.Name.ToLowerInvariant()
                                               Return n.EndsWith(".gguf") AndAlso Not n.Contains("mmproj") AndAlso n.Contains("q4_k_m") AndAlso
                                                      (n.Contains("-" & Size & "-") OrElse n.Contains("_" & Size & "-"))
                                           End Function)
            If hit Is Nothing Then Continue For
            ' Version = the file's SHA-256: a re-upload of the model on Hugging Face shows up as an update.
            Dim rv As New RemoteVersion With {.Version = If(hit.Sha256 IsNot Nothing, hit.Sha256.Substring(0, 12), $"{repo}/{hit.Name}")}
            rv.Files.Add(New RemoteFile With {.Url = WebHelper.HuggingFaceUrl(repo, hit.Name), .FileName = Path.GetFileName(hit.Name), .Size = hit.Size, .Sha256 = hit.Sha256})
            Return rv
        Next
        Throw New Exception(Loc.Tr("Could not find TranslateGemma {0} on Hugging Face.", Size))
    End Function

    Protected Overrides Function InstallFilesAsync(latest As RemoteVersion, downloaded As List(Of String), ct As CancellationToken) As Task
        LlamaServer.StopAll()
        File.Move(downloaded(0), LocalPath, True)
        Return Task.CompletedTask
    End Function
End Class

' =====================================================================
'  Silero VAD model for whisper.cpp (skips silence → fewer invented lines)
' =====================================================================
Public Class WhisperVadComponent
    Inherits ToolComponent

    Public Sub New(s As AppSettings)
        MyBase.New(s)
    End Sub

    Public Shared Function FindModel() As String
        Return Directory.EnumerateFiles(AppPaths.ModelsDir, "ggml-silero-*.bin").OrderByDescending(Function(f) f).FirstOrDefault()
    End Function

    Public Overrides ReadOnly Property Id As String = "whisper-vad"
    Public Overrides ReadOnly Property DisplayName As String = Loc.Tr("Voice detection model (Silero VAD)")
    Public Overrides ReadOnly Property Source As String = "huggingface.co/ggml-org/whisper-vad"

    Public Overrides Function IsRequired() As Boolean
        Return True ' under 1 MB; used by Whisper (voice detection) and by auto-sync
    End Function

    Public Overrides Function IsInstalled() As Boolean
        Return FindModel() IsNot Nothing
    End Function

    Public Overrides Function InstalledVersion() As String
        Dim m = FindModel()
        Return If(m Is Nothing, Nothing, Path.GetFileNameWithoutExtension(m).Replace("ggml-silero-", ""))
    End Function

    Public Overrides Async Function GetLatestAsync(ct As CancellationToken) As Task(Of RemoteVersion)
        Dim files = Await WebHelper.ListHuggingFaceFilesAsync("ggml-org/whisper-vad", ct)
        Dim best = files?.Where(Function(f) Regex.IsMatch(f.Name, "^ggml-silero-v[\d.]+\.bin$")).
                          OrderByDescending(Function(f) New Version(Regex.Match(f.Name, "v([\d.]+)\.bin").Groups(1).Value)).FirstOrDefault()
        If best Is Nothing Then Throw New Exception("No Silero VAD model found on Hugging Face.")
        Dim rv As New RemoteVersion With {.Version = best.Name.Replace("ggml-silero-", "").Replace(".bin", "")}
        rv.Files.Add(New RemoteFile With {.Url = WebHelper.HuggingFaceUrl("ggml-org/whisper-vad", best.Name), .FileName = best.Name, .Size = best.Size, .Sha256 = best.Sha256})
        Return rv
    End Function

    Protected Overrides Function InstallFilesAsync(latest As RemoteVersion, downloaded As List(Of String), ct As CancellationToken) As Task
        For Each old In Directory.EnumerateFiles(AppPaths.ModelsDir, "ggml-silero-*.bin").ToList()
            File.Delete(old)
        Next
        File.Move(downloaded(0), Path.Combine(AppPaths.ModelsDir, Path.GetFileName(downloaded(0))), True)
        Return Task.CompletedTask
    End Function
End Class

''' <summary>Builds the list of components relevant for the current settings.</summary>
Public Module DependencyManager

    Public Function GetComponents(settings As AppSettings) As List(Of ToolComponent)
        Return New List(Of ToolComponent) From {
            New FfmpegComponent(settings),
            New WhisperEngineComponent(settings),
            New WhisperModelComponent(settings),
            New WhisperVadComponent(settings),
            New LlamaEngineComponent(settings),
            New TranslateModelComponent(settings)
        }
    End Function

    Public Function MissingRequired(settings As AppSettings) As List(Of ToolComponent)
        Return GetComponents(settings).Where(Function(c) c.IsRequired() AndAlso Not c.IsInstalled()).ToList()
    End Function

End Module
