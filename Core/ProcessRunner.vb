Imports System.Text
Imports System.Threading

Public Class ProcessResult
    Public Property ExitCode As Integer
    Public Property StdOut As String
    Public Property StdErr As String
End Class

''' <summary>
''' Runs external tools (ffmpeg, ffprobe, whisper-cli) asynchronously.
''' stdout and stderr are read concurrently – reading them one after the other (as the old code did)
''' can dead-lock when a tool writes a lot to stderr (whisper does).
''' Cancelling the token kills the whole process tree.
''' </summary>
Public Module ProcessRunner

    Public Async Function RunAsync(exe As String, args As String, ct As CancellationToken,
                                   Optional workingDir As String = Nothing,
                                   Optional onStdErrLine As Action(Of String) = Nothing,
                                   Optional onStdOutLine As Action(Of String) = Nothing) As Task(Of ProcessResult)
        Logger.Log($"RUN: ""{exe}"" {args}")
        Dim psi As New ProcessStartInfo(exe, args) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8,
            .WorkingDirectory = If(workingDir, IO.Path.GetDirectoryName(exe))
        }
        Dim out As New StringBuilder()
        Dim err As New StringBuilder()

        Using p As New Process With {.StartInfo = psi, .EnableRaisingEvents = True}
            AddHandler p.OutputDataReceived, Sub(s, e)
                                                 If e.Data Is Nothing Then Return
                                                 SyncLock out : out.AppendLine(e.Data) : End SyncLock
                                                 onStdOutLine?.Invoke(e.Data)
                                             End Sub
            AddHandler p.ErrorDataReceived, Sub(s, e)
                                                If e.Data Is Nothing Then Return
                                                SyncLock err : err.AppendLine(e.Data) : End SyncLock
                                                onStdErrLine?.Invoke(e.Data)
                                            End Sub
            p.Start()
            p.BeginOutputReadLine()
            p.BeginErrorReadLine()
            Try
                Await p.WaitForExitAsync(ct)
            Catch ex As OperationCanceledException
                KillQuietly(p)
                Throw
            End Try
            p.WaitForExit() ' flush async readers
            Dim result As New ProcessResult With {.ExitCode = p.ExitCode, .StdOut = out.ToString(), .StdErr = err.ToString()}
            If result.ExitCode <> 0 Then Logger.Log($"Exit code {result.ExitCode}. stderr tail: {Tail(result.StdErr, 1500)}")
            Return result
        End Using
    End Function

    Public Sub KillQuietly(p As Process)
        Try
            If p IsNot Nothing AndAlso Not p.HasExited Then p.Kill(entireProcessTree:=True)
        Catch
        End Try
    End Sub

    Public Function Tail(s As String, maxChars As Integer) As String
        If String.IsNullOrEmpty(s) OrElse s.Length <= maxChars Then Return s
        Return "..." & s.Substring(s.Length - maxChars)
    End Function

End Module
