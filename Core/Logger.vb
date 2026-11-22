Imports System.IO
Imports System.Text

''' <summary>Thread-safe logger. Always writes to the log file; also to the console when started with -debug.</summary>
Public Module Logger

    Private ReadOnly _lock As New Object()
    Public Property DebugConsole As Boolean = False
    Private Const MaxLogBytes As Long = 5L * 1024 * 1024

    Public Sub Log(message As String)
        Dim line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}"
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
