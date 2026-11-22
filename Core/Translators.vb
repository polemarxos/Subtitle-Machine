Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading

Public Interface ITranslator
    Inherits IDisposable
    ReadOnly Property DisplayName As String
    ''' <summary>How many requests may run concurrently.</summary>
    ReadOnly Property MaxParallel As Integer
    Function PrepareAsync(progress As IProgress(Of StatusUpdate), ct As CancellationToken) As Task
    ''' <summary>Translates one piece of text (may contain line breaks).</summary>
    Function TranslateAsync(text As String, source As LanguageInfo, target As LanguageInfo, ct As CancellationToken,
                            Optional terms As IList(Of Glossary.Entry) = Nothing) As Task(Of String)
End Interface

''' <summary>The official TranslateGemma prompt (same as ollama.com/library/translategemma) and helpers.</summary>
Public Module TranslationPrompt

    Public Function Build(text As String, source As LanguageInfo, target As LanguageInfo, Optional terms As IList(Of Glossary.Entry) = Nothing) As String
        Dim t = $"{target.Name} ({target.Code})"
        Dim sb As New StringBuilder()
        If source IsNot Nothing Then
            Dim s = $"{source.Name} ({source.Code})"
            sb.Append($"You are a professional {s} to {t} translator. Your goal is to accurately convey the meaning and nuances of the original {source.Name} text while adhering to {target.Name} grammar, vocabulary, and cultural sensitivities.")
            sb.Append(vbLf)
            sb.Append($"Produce only the {target.Name} translation, without any additional explanations or commentary.")
            AppendGlossary(sb, terms)
            sb.Append($" Please translate the following {source.Name} text into {target.Name}:")
        Else
            sb.Append($"You are a professional translator into {t}. Your goal is to accurately convey the meaning and nuances of the original text while adhering to {target.Name} grammar, vocabulary, and cultural sensitivities.")
            sb.Append(vbLf)
            sb.Append($"Produce only the {target.Name} translation, without any additional explanations or commentary.")
            AppendGlossary(sb, terms)
            sb.Append($" Please translate the following text into {target.Name}:")
        End If
        sb.Append(vbLf).Append(vbLf).Append(vbLf) ' two blank lines before the text
        sb.Append(text)
        Return sb.ToString()
    End Function

    ''' <summary>Tested with TranslateGemma: one sentence listing the required translations is followed reliably (and declined correctly).</summary>
    Private Sub AppendGlossary(sb As StringBuilder, terms As IList(Of Glossary.Entry))
        If terms Is Nothing OrElse terms.Count = 0 Then Return
        sb.Append(" Always use these translations: ")
        sb.Append(String.Join("; ", terms.Select(Function(e) $"""{e.Source}"" = ""{e.Target}""")))
        sb.Append(".")
    End Sub

    ''' <summary>Gemma chat format for raw completion (does not depend on the GGUF's embedded chat template).</summary>
    Public Function BuildGemmaRaw(text As String, source As LanguageInfo, target As LanguageInfo, Optional terms As IList(Of Glossary.Entry) = Nothing) As String
        Return "<start_of_turn>user" & vbLf & Build(text, source, target, terms) & "<end_of_turn>" & vbLf & "<start_of_turn>model" & vbLf
    End Function

    Public Function Clean(output As String, original As String) As String
        If output Is Nothing Then Return original
        Dim r = output.Replace("<end_of_turn>", "").Replace(vbCrLf, vbLf).Trim()
        If r.Length > 1 AndAlso r.StartsWith("""") AndAlso r.EndsWith("""") AndAlso Not original.TrimStart().StartsWith("""") Then
            r = r.Substring(1, r.Length - 2).Trim()
        End If
        If r.Length = 0 Then Return original
        Return r
    End Function

    ' ---- context batches: "<1>line one <br> line two" ----
    Private ReadOnly MarkerRx As New Regex("<(\d{1,2})>")

    ''' <summary>Joins several subtitle cues into one numbered text, so the model sees them in context.</summary>
    Public Function JoinBatch(cues As IList(Of String)) As String
        Dim sb As New StringBuilder()
        For i = 0 To cues.Count - 1
            If i > 0 Then sb.Append(vbLf)
            sb.Append("<").Append(i + 1).Append(">").Append(cues(i).Replace(vbLf, " <br> "))
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Splits the model's answer back into cues. Returns Nothing if the numbering does not match.</summary>
    Public Function SplitBatch(output As String, expected As Integer) As List(Of String)
        If output Is Nothing Then Return Nothing
        Dim text = output.Replace("<end_of_turn>", "").Replace(vbCrLf, vbLf).Trim()
        Dim ms = MarkerRx.Matches(text)
        If ms.Count <> expected Then Return Nothing
        Dim result As New List(Of String)
        For i = 0 To ms.Count - 1
            If CInt(ms(i).Groups(1).Value) <> i + 1 Then Return Nothing
            Dim start = ms(i).Index + ms(i).Length
            Dim [end] = If(i + 1 < ms.Count, ms(i + 1).Index, text.Length)
            Dim part = text.Substring(start, [end] - start).Trim()
            part = Regex.Replace(part, "\s*<br\s*/?>\s*", vbLf, RegexOptions.IgnoreCase).Trim()
            If part.Length = 0 Then Return Nothing
            result.Add(part)
        Next
        Return result
    End Function
End Module

''' <summary>Starts and stops llama-server.exe (llama.cpp) with the TranslateGemma GGUF.</summary>
Public Module LlamaServer

    Private ReadOnly Running As New List(Of Process)

    Public Sub StopAll()
        SyncLock Running
            For Each p In Running
                ProcessRunner.KillQuietly(p)
            Next
            Running.Clear()
        End SyncLock
    End Sub

    Friend Sub Register(p As Process)
        SyncLock Running
            Running.Add(p)
        End SyncLock
    End Sub

    Friend Sub Unregister(p As Process)
        SyncLock Running
            Running.Remove(p)
        End SyncLock
    End Sub

    Friend Function GetFreePort() As Integer
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Dim port = CType(l.LocalEndpoint, IPEndPoint).Port
        l.Stop()
        Return port
    End Function
End Module

''' <summary>TranslateGemma via llama-server.exe – runs locally, nothing else to install.</summary>
Public Class LlamaTranslator
    Implements ITranslator

    Private ReadOnly _settings As AppSettings
    Private _process As Process
    Private _baseUrl As String
    Private ReadOnly _log As New Queue(Of String)

    Public Sub New(settings As AppSettings)
        _settings = settings
    End Sub

    Public ReadOnly Property DisplayName As String Implements ITranslator.DisplayName
        Get
            Return $"TranslateGemma {_settings.EffectiveModelSize().ToUpperInvariant()} ({_settings.EffectiveGpuBackend().ToUpperInvariant()})"
        End Get
    End Property

    Public ReadOnly Property MaxParallel As Integer Implements ITranslator.MaxParallel
        Get
            Return If(_settings.EffectiveGpuBackend() = "cpu", 2, 4)
        End Get
    End Property

    Public Async Function PrepareAsync(progress As IProgress(Of StatusUpdate), ct As CancellationToken) As Task Implements ITranslator.PrepareAsync
        If _process IsNot Nothing AndAlso Not _process.HasExited Then Return

        Dim exe = LlamaEngineComponent.FindExe()
        If exe Is Nothing Then Throw New Exception("llama.cpp is not installed. Open 'Components' and install it.")
        Dim model = New TranslateModelComponent(_settings).LocalPath
        If Not File.Exists(model) Then Throw New Exception($"TranslateGemma {_settings.EffectiveModelSize().ToUpperInvariant()} is not downloaded. Open 'Components'.")

        Dim port = LlamaServer.GetFreePort()
        _baseUrl = $"http://127.0.0.1:{port}"
        Dim parallel = MaxParallel
        ' 4096 tokens per slot: enough for a context batch of several lines.
        Dim args = $"-m ""{model}"" --host 127.0.0.1 --port {port} -c {4096 * parallel} -np {parallel} -ngl {GpuLayers(model)}"

        progress?.Report(New StatusUpdate($"Loading {DisplayName}…"))
        Logger.Log("Starting llama-server " & args)
        Dim psi As New ProcessStartInfo(exe, args) With {
            .UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True,
            .WorkingDirectory = Path.GetDirectoryName(exe)
        }
        _process = New Process With {.StartInfo = psi}
        AddHandler _process.ErrorDataReceived, AddressOf OnServerOutput
        AddHandler _process.OutputDataReceived, AddressOf OnServerOutput
        _process.Start()
        _process.BeginErrorReadLine()
        _process.BeginOutputReadLine()
        LlamaServer.Register(_process)

        Dim sw = Stopwatch.StartNew()
        Do
            ct.ThrowIfCancellationRequested()
            If _process.HasExited Then
                Throw New Exception("The AI translator stopped while loading the model." & vbCrLf &
                                    "Update your graphics driver, or choose 'Vulkan' or 'CPU' in Components." & vbCrLf & vbCrLf & LastLog())
            End If
            Try
                Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    cts.CancelAfter(2000)
                    Using resp = Await WebHelper.Http.GetAsync(_baseUrl & "/health", cts.Token)
                        If resp.IsSuccessStatusCode Then Exit Do
                    End Using
                End Using
            Catch ex As Exception When Not ct.IsCancellationRequested
            End Try
            If sw.Elapsed.TotalMinutes > 5 Then Throw New TimeoutException("The AI model took too long to load." & vbCrLf & LastLog())
            Await Task.Delay(500, ct)
        Loop
        Logger.Log($"llama-server ready in {sw.Elapsed.TotalSeconds:0.0}s")
    End Function

    Public Async Function TranslateAsync(text As String, source As LanguageInfo, target As LanguageInfo, ct As CancellationToken,
                                         Optional terms As IList(Of Glossary.Entry) = Nothing) As Task(Of String) Implements ITranslator.TranslateAsync
        Dim body = JsonSerializer.Serialize(New With {
            .prompt = TranslationPrompt.BuildGemmaRaw(text, source, target, terms),
            .n_predict = Math.Max(256, text.Length * 3),
            .temperature = 0.0,
            .top_k = 1,
            .stop = New String() {"<end_of_turn>", "<start_of_turn>"},
            .cache_prompt = True
        })
        Using content As New StringContent(body, Encoding.UTF8, "application/json")
            Using resp = Await WebHelper.Http.PostAsync(_baseUrl & "/completion", content, ct)
                Dim json = Await resp.Content.ReadAsStringAsync(ct)
                If Not resp.IsSuccessStatusCode Then Throw New HttpRequestException($"llama-server error {CInt(resp.StatusCode)}: {json}")
                Using doc = JsonDocument.Parse(json)
                    Return doc.RootElement.GetProperty("content").GetString()
                End Using
            End Using
        End Using
    End Function

    ''' <summary>All layers on the GPU if the model fits, otherwise a proportional part (rest on the CPU).</summary>
    Private Function GpuLayers(modelPath As String) As Integer
        If _settings.EffectiveGpuBackend() = "cpu" Then Return 0
        Dim vram = HardwareInfo.Gpu.VramBytes
        If vram <= 0 Then Return If(_settings.GpuBackend = "auto", 0, 999)
        Dim modelSize = New FileInfo(modelPath).Length
        Dim usable = vram - 1.5 * 1024 * 1024 * 1024 ' context, CUDA runtime, Windows desktop
        If usable >= modelSize Then Return 999
        Dim layers = TotalLayers()
        Return Math.Max(0, CInt(Math.Floor(layers * usable / modelSize)))
    End Function

    ''' <summary>Transformer layers of Gemma 3 4B / 12B / 27B.</summary>
    Private Function TotalLayers() As Integer
        Select Case _settings.EffectiveModelSize()
            Case "27b" : Return 62
            Case "12b" : Return 48
            Case Else : Return 34
        End Select
    End Function

    Private Sub OnServerOutput(sender As Object, e As DataReceivedEventArgs)
        If e.Data Is Nothing Then Return
        SyncLock _log
            _log.Enqueue(e.Data)
            While _log.Count > 40 : _log.Dequeue() : End While
        End SyncLock
        If Logger.DebugConsole OrElse e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) Then Logger.Log("[llama] " & e.Data)
    End Sub

    Private Function LastLog() As String
        SyncLock _log
            Return String.Join(vbCrLf, _log.Skip(Math.Max(0, _log.Count - 8)))
        End SyncLock
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _process IsNot Nothing Then
            ProcessRunner.KillQuietly(_process)
            LlamaServer.Unregister(_process)
            _process.Dispose()
            _process = Nothing
        End If
    End Sub
End Class
