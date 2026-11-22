Imports System.IO

''' <summary>
''' Central place for every folder/file the application uses.
''' The app is "portable": if the folder next to the .exe is writable, everything lives there.
''' Otherwise (e.g. installed under Program Files) it falls back to %LocalAppData%\Subtitle Machine.
''' </summary>
Public Module AppPaths

    Private _dataDir As String

    Public ReadOnly Property DataDir As String
        Get
            If _dataDir Is Nothing Then _dataDir = ResolveDataDir()
            Return _dataDir
        End Get
    End Property

    Public ReadOnly Property ToolsDir As String
        Get
            Return EnsureDir(Path.Combine(DataDir, "tools"))
        End Get
    End Property

    Public ReadOnly Property ModelsDir As String
        Get
            Return EnsureDir(Path.Combine(ToolsDir, "models"))
        End Get
    End Property

    Public ReadOnly Property DownloadsDir As String
        Get
            Return EnsureDir(Path.Combine(ToolsDir, "_downloads"))
        End Get
    End Property

    Public ReadOnly Property SettingsFile As String
        Get
            Return Path.Combine(DataDir, "config.json")
        End Get
    End Property

    Public ReadOnly Property VersionsFile As String
        Get
            Return Path.Combine(ToolsDir, "versions.json")
        End Get
    End Property

    Public ReadOnly Property LogFile As String
        Get
            Return Path.Combine(DataDir, "subtitle_machine.log")
        End Get
    End Property

    Public Function EnsureDir(dir As String) As String
        Directory.CreateDirectory(dir)
        Return dir
    End Function

    Private Function ResolveDataDir() As String
        Dim exeDir = AppContext.BaseDirectory
        If IsWritable(exeDir) Then Return exeDir
        Dim fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Subtitle Machine")
        Directory.CreateDirectory(fallback)
        Return fallback
    End Function

    Private Function IsWritable(dir As String) As Boolean
        Try
            Dim probe = Path.Combine(dir, ".write_test_" & Guid.NewGuid().ToString("N"))
            File.WriteAllText(probe, "x")
            File.Delete(probe)
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Moves files left by older versions (tools\ffmpeg.exe, tools\whisper\ggml-*.bin) to the new layout.</summary>
    Public Sub MigrateLegacyLayout()
        Try
            Dim ffDir = EnsureDir(Path.Combine(ToolsDir, "ffmpeg"))
            For Each exe In {"ffmpeg.exe", "ffprobe.exe", "ffplay.exe"}
                Dim legacy = Path.Combine(ToolsDir, exe)
                Dim target = Path.Combine(ffDir, exe)
                If File.Exists(legacy) AndAlso Not File.Exists(target) Then File.Move(legacy, target)
            Next

            Dim legacyWhisperDir = Path.Combine(ToolsDir, "whisper")
            If Directory.Exists(legacyWhisperDir) Then
                For Each bin In Directory.GetFiles(legacyWhisperDir, "ggml-*.bin")
                    Dim target = Path.Combine(ModelsDir, Path.GetFileName(bin))
                    If Not File.Exists(target) Then File.Move(bin, target)
                Next
            End If
        Catch ex As Exception
            Logger.Log("Legacy migration failed: " & ex.Message)
        End Try
    End Sub

End Module
