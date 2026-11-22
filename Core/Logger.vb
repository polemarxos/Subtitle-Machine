Imports System.IO
Imports System.Text

''' <summary>
''' Thread-safe logger: writes to subtitle_machine.log (unless switched off in the settings); also to the console when
''' started with -debug. At 5 MB the file is renamed to .log.old and a new one is started, so it never takes more than ~10 MB.
''' </summary>
Public Module Logger

    Private ReadOnly _lock As New Object()
    Public Property DebugConsole As Boolean = False
    ''' <summary>Write the log file (setting "Write a log file").</summary>
    Public Property Enabled As Boolean = False
    Private Const MaxLogBytes As Long = 5L * 1024 * 1024

    Public Sub Log(message As String)
        Dim line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}"
        If Enabled Then
            SyncLock _lock
                Try
                    Dim fi As New FileInfo(AppPaths.LogFile)
                    If fi.Exists AndAlso fi.Length > MaxLogBytes Then
                        File.Copy(fi.FullName, fi.FullName & ".old", True)
                        File.Delete(fi.FullName)
                    End If
                    File.AppendAllText(AppPaths.LogFile, line & Environment.NewLine, Encoding.UTF8)
                Catch
                End Try
            End SyncLock
        End If
        If DebugConsole Then
            Try
                Console.WriteLine(line)
            Catch
            End Try
        End If
        Debug.WriteLine(line)
    End Sub

End Module

' ----- Native Methods for Debug Console -----
Friend Class NativeMethods
    <Runtime.InteropServices.DllImport("kernel32.dll")>
    Public Shared Function AllocConsole() As Boolean
    End Function

    <Runtime.InteropServices.DllImport("kernel32.dll", CharSet:=Runtime.InteropServices.CharSet.Unicode)>
    Public Shared Function SetConsoleTitle(lpConsoleTitle As String) As Boolean
    End Function
End Class
