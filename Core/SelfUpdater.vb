Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Threading

''' <summary>
''' Automatic self-update from the GitHub releases of the project:
''' the new exe is downloaded in the background, then swapped in when the app closes (or on "Restart now").
''' </summary>
Public Module SelfUpdater

    Public Const GitHubRepo As String = "polemarxos/Subtitle-Machine"

#If SELF_CONTAINED Then
    ''' <summary>Release asset for this edition (the self-contained exe includes the .NET runtime).</summary>
    Public Const AssetName As String = "SubtitleMachine-standalone.exe"
    Public Const Edition As String = "standalone"
#Else
    Public Const AssetName As String = "SubtitleMachine.exe"
    Public Const Edition As String = "needs .NET 10"
#End If

    ''' <summary>Downloaded, verified exe waiting to be installed (Nothing if none).</summary>
    Public Property PendingExe As String
    Public Property PendingTag As String

    Public Function CurrentVersion() As Version
        Return Assembly.GetExecutingAssembly().GetName().Version
    End Function

    Private ReadOnly Property NewExePath As String
        Get
            Dim cur = Environment.ProcessPath
            Return Path.Combine(Path.GetDirectoryName(cur), Path.GetFileNameWithoutExtension(cur) & ".new.exe")
        End Get
    End Property

    Public Function ParseTag(tag As String) As Version
        Dim t = If(tag, "").Trim().TrimStart("v"c, "V"c)
        Dim v As Version = Nothing
        If Version.TryParse(t, v) Then Return v
        Return Nothing
    End Function

    ''' <summary>Deletes a half-downloaded/old update left by a previous run.</summary>
    Public Sub CleanupLeftovers()
        Try
            If File.Exists(NewExePath) Then File.Delete(NewExePath)
            If File.Exists(NewExePath & ".part") Then File.Delete(NewExePath & ".part")
        Catch
        End Try
    End Sub

    ''' <summary>Returns the latest release if it is newer than this build, otherwise Nothing.</summary>
    Public Async Function CheckAsync(ct As CancellationToken) As Task(Of WebHelper.GitHubRelease)
        Dim rel = Await WebHelper.GetLatestGitHubReleaseAsync(GitHubRepo, ct)
        Dim latest = If(ParseTag(rel.Tag), ParseTag(rel.Name))
        If latest Is Nothing Then Return Nothing
        Dim cur = CurrentVersion()
        Dim curNorm As New Version(cur.Major, cur.Minor, Math.Max(cur.Build, 0))
        Dim latNorm As New Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0))
        If latNorm <= curNorm Then Return Nothing
        If FindAsset(rel) Is Nothing Then Return Nothing
        Return rel
    End Function

    ''' <summary>The exe for this edition; older releases only had one exe.</summary>
    Public Function FindAsset(rel As WebHelper.GitHubRelease) As WebHelper.GitHubAsset
        Dim exact = rel.Assets.FirstOrDefault(Function(a) a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
        If exact IsNot Nothing Then Return exact
#If Not SELF_CONTAINED Then
        Return rel.Assets.FirstOrDefault(Function(a) a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) AndAlso Not a.Name.Contains("standalone", StringComparison.OrdinalIgnoreCase))
#Else
        Return Nothing
#End If
    End Function

    ''' <summary>Downloads the release exe next to the current one (verified with the GitHub SHA-256 digest).</summary>
    Public Async Function DownloadAsync(rel As WebHelper.GitHubRelease, progress As IProgress(Of DownloadProgress), ct As CancellationToken) As Task
        Dim asset = FindAsset(rel)
        Await WebHelper.DownloadFileAsync(asset.Url, NewExePath, progress, ct, asset.Sha256)
        PendingExe = NewExePath
        PendingTag = rel.Tag
        Logger.Log($"Update {rel.Tag} downloaded, will be installed on exit.")
    End Function

    ''' <summary>
    ''' Starts a small script that waits for this process to exit, replaces the exe and (optionally) restarts it.
    ''' Call right before the application exits.
    ''' </summary>
    Public Sub ApplyPending(restart As Boolean)
        If PendingExe Is Nothing OrElse Not File.Exists(PendingExe) Then Return
        Dim currentExe = Environment.ProcessPath
        Dim pid = Environment.ProcessId
        Dim batchPath = Path.Combine(Path.GetTempPath(), "update_subtitle_machine.cmd")
        Dim script As New StringBuilder()
        script.AppendLine("@echo off")
        script.AppendLine("chcp 65001 >nul")
        script.AppendLine(":wait")
        script.AppendLine($"tasklist /FI ""PID eq {pid}"" 2>nul | find ""{pid}"" >nul")
        script.AppendLine("if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)")
        script.AppendLine("set tries=0")
        script.AppendLine(":move")
        script.AppendLine($"move /y ""{PendingExe}"" ""{currentExe}"" >nul 2>&1")
        script.AppendLine("if errorlevel 1 (set /a tries+=1 & timeout /t 1 /nobreak >nul & if %tries% lss 10 goto move)")
        If restart Then script.AppendLine($"start """" ""{currentExe}""")
        script.AppendLine("del ""%~f0""")
        File.WriteAllText(batchPath, script.ToString(), New UTF8Encoding(False))
        Process.Start(New ProcessStartInfo("cmd.exe", $"/c ""{batchPath}""") With {.UseShellExecute = False, .CreateNoWindow = True})
        PendingExe = Nothing
    End Sub

End Module
