Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json

''' <summary>
''' Saves translated lines while a file is being translated ("Movie.el.srt.progress.json"),
''' so Stop, a crash or a power cut does not lose the work: the next Start continues from there.
''' The file is only reused for the same source subtitles, target language and model.
''' </summary>
Public Class TranslationProgress

    Private Class Data
        Public Property Key As String
        Public Property Done As Dictionary(Of Integer, String)
    End Class

    Private ReadOnly _path As String
    Private ReadOnly _key As String
    Private ReadOnly _lock As New Object()
    Private _dirty As Boolean
    Private _lastSave As DateTime = DateTime.MinValue

    Public ReadOnly Property Done As New Dictionary(Of Integer, String)

    Private Sub New(path As String, key As String)
        _path = path
        _key = key
    End Sub

    Public Shared Function ProgressPath(outputSrt As String) As String
        Return outputSrt & ".progress.json"
    End Function

    Public Shared Function Open(outputSrt As String, inputSrt As String, target As LanguageInfo, model As String) As TranslationProgress
        Dim key As String
        Using sha = SHA256.Create()
            Dim hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(inputSrt)))
            key = $"{hash}|{target.Code}|{model}"
        End Using
        Dim p As New TranslationProgress(ProgressPath(outputSrt), key)
        Try
            If File.Exists(p._path) Then
                Dim d = JsonSerializer.Deserialize(Of Data)(File.ReadAllText(p._path, Encoding.UTF8))
                If d IsNot Nothing AndAlso d.Key = key AndAlso d.Done IsNot Nothing Then
                    For Each kv In d.Done
                        p.Done(kv.Key) = kv.Value
                    Next
                Else
                    File.Delete(p._path) ' made from a different source/language/model
                End If
            End If
        Catch ex As Exception
            Logger.Log("Progress file ignored: " & ex.Message)
        End Try
        Return p
    End Function

    Public Sub Add(index As Integer, text As String)
        SyncLock _lock
            Done(index) = text
            _dirty = True
            If (DateTime.UtcNow - _lastSave).TotalSeconds >= 3 Then SaveLocked()
        End SyncLock
    End Sub

    Public Sub Flush()
        SyncLock _lock
            If _dirty Then SaveLocked()
        End SyncLock
    End Sub

    Private Sub SaveLocked()
        Try
            Dim tmp = _path & ".tmp"
            File.WriteAllText(tmp, JsonSerializer.Serialize(New Data With {.Key = _key, .Done = New Dictionary(Of Integer, String)(Done)}), Encoding.UTF8)
            File.Move(tmp, _path, True)
            _dirty = False
            _lastSave = DateTime.UtcNow
        Catch ex As Exception
            Logger.Log("Could not save translation progress: " & ex.Message)
        End Try
    End Sub

    ''' <summary>The translation is finished – the progress file is no longer needed.</summary>
    Public Sub Complete()
        SyncLock _lock
            _dirty = False
            Try : File.Delete(_path) : Catch : End Try
        End SyncLock
    End Sub
End Class
