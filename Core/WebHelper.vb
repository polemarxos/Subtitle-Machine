Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Threading

Public Class DownloadProgress
    Public Property FileName As String
    Public Property BytesReceived As Long
    Public Property TotalBytes As Long
    Public Property BytesPerSecond As Double

    Public ReadOnly Property Percent As Integer
        Get
            If TotalBytes <= 0 Then Return 0
            Return CInt(Math.Min(100, BytesReceived * 100L \ TotalBytes))
        End Get
    End Property

    Public Overrides Function ToString() As String
        Dim speed = If(BytesPerSecond > 0, $" – {FormatBytes(CLng(BytesPerSecond))}/s", "")
        If TotalBytes > 0 Then
            Return Loc.Tr("Downloading {0}: {1} / {2} ({3}%){4}", FileName, FormatBytes(BytesReceived), FormatBytes(TotalBytes), Percent, speed)
        End If
        Return Loc.Tr("Downloading {0}: {1}{2}", FileName, FormatBytes(BytesReceived), speed)
    End Function
End Class

''' <summary>Shared HTTP helpers: one HttpClient for the whole app, resumable downloads, GitHub / Hugging Face API helpers.</summary>
Public Module WebHelper

    Public ReadOnly Http As HttpClient = CreateClient()

    Private Function CreateClient() As HttpClient
        Dim handler As New SocketsHttpHandler With {
            .AllowAutoRedirect = True,
            .AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate,
            .PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }
        Dim c As New HttpClient(handler) With {.Timeout = Timeout.InfiniteTimeSpan}
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"SubtitleMachine/{GetType(WebHelper).Assembly.GetName().Version}")
        Return c
    End Function

    Public Function FormatBytes(bytes As Long) As String
        Dim units = {"B", "KB", "MB", "GB", "TB"}
        Dim size As Double = bytes
        Dim u = 0
        While size >= 1024 AndAlso u < units.Length - 1
            size /= 1024
            u += 1
        End While
        Return $"{size:0.##} {units(u)}"
    End Function

    ''' <summary>GET a small text/JSON resource with a timeout.</summary>
    Public Async Function GetStringAsync(url As String, ct As CancellationToken, Optional timeoutSeconds As Integer = 30, Optional accept As String = Nothing) As Task(Of String)
        Using cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds))
            Using req As New HttpRequestMessage(HttpMethod.Get, url)
                If url.Contains("api.github.com") Then req.Headers.Accept.ParseAdd("application/vnd.github+json")
                If accept IsNot Nothing Then req.Headers.Accept.ParseAdd(accept)
                Using resp = Await Http.SendAsync(req, cts.Token)
                    resp.EnsureSuccessStatusCode()
                    Return Await resp.Content.ReadAsStringAsync(cts.Token)
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Downloads a file with resume support (.part file + HTTP Range) and optional SHA-256 verification.
    ''' Large models can be several GB, so an interrupted download continues where it stopped.
    ''' </summary>
    Public Async Function DownloadFileAsync(url As String, destination As String, progress As IProgress(Of DownloadProgress),
                                            ct As CancellationToken, Optional expectedSha256 As String = Nothing) As Task
        Dim partFile = destination & ".part"
        Dim name = Path.GetFileName(destination)
        Dim existing As Long = If(File.Exists(partFile), New FileInfo(partFile).Length, 0L)

        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            If existing > 0 Then req.Headers.Range = New RangeHeaderValue(existing, Nothing)
            Using resp = Await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                If resp.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable Then
                    ' The .part file is already complete.
                Else
                    resp.EnsureSuccessStatusCode()
                    If existing > 0 AndAlso resp.StatusCode <> HttpStatusCode.PartialContent Then existing = 0 ' server ignored Range
                    Dim total As Long = If(resp.Content.Headers.ContentLength.HasValue, resp.Content.Headers.ContentLength.Value + existing, -1L)
                    Dim info As New DownloadProgress With {.FileName = name, .TotalBytes = total, .BytesReceived = existing}

                    Using fs As New FileStream(partFile, If(existing > 0, FileMode.Append, FileMode.Create), FileAccess.Write, FileShare.None, 1 << 16, True)
                        Using src = Await resp.Content.ReadAsStreamAsync(ct)
                            Dim buffer(1 << 17) As Byte
                            Dim sw = Stopwatch.StartNew()
                            Dim lastReport As Long = 0
                            Dim speedBytes As Long = 0
                            Dim speedStart As Long = 0
                            Do
                                Dim read = Await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                                If read = 0 Then Exit Do
                                Await fs.WriteAsync(buffer.AsMemory(0, read), ct)
                                info.BytesReceived += read
                                speedBytes += read
                                If sw.ElapsedMilliseconds - lastReport >= 300 Then
                                    Dim elapsed = (sw.ElapsedMilliseconds - speedStart) / 1000.0
                                    If elapsed > 0 Then info.BytesPerSecond = speedBytes / elapsed
                                    If elapsed > 3 Then speedStart = sw.ElapsedMilliseconds : speedBytes = 0
                                    lastReport = sw.ElapsedMilliseconds
                                    progress?.Report(info)
                                End If
                            Loop
                            progress?.Report(info)
                        End Using
                    End Using
                End If
            End Using
        End Using

        If Not String.IsNullOrWhiteSpace(expectedSha256) Then
            Dim actual = Await ComputeSha256Async(partFile, ct)
            If Not String.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                File.Delete(partFile)
                Throw New InvalidDataException($"Checksum mismatch for {name}. The download was corrupted, please try again.")
            End If
        End If
        File.Move(partFile, destination, True)
    End Function

    Public Async Function ComputeSha256Async(file As String, ct As CancellationToken) As Task(Of String)
        Using fs As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, True)
            Dim hash = Await SHA256.HashDataAsync(fs, ct)
            Return Convert.ToHexString(hash).ToLowerInvariant()
        End Using
    End Function

    ' ---------------- GitHub ----------------

    Public Class GitHubAsset
        Public Property Name As String
        Public Property Url As String
        Public Property Size As Long
        Public Property Sha256 As String
    End Class

    Public Class GitHubRelease
        Public Property Tag As String
        Public Property Name As String
        Public Property Body As String
        Public Property HtmlUrl As String
        Public Property Assets As New List(Of GitHubAsset)
    End Class

    Public Async Function GetLatestGitHubReleaseAsync(ownerRepo As String, ct As CancellationToken) As Task(Of GitHubRelease)
        Dim json = Await GetStringAsync($"https://api.github.com/repos/{ownerRepo}/releases/latest", ct)
        Using doc = JsonDocument.Parse(json)
            Return ParseRelease(doc.RootElement)
        End Using
    End Function

    ''' <summary>Newest releases first (including pre-releases), so a release without binaries can be skipped.</summary>
    Public Async Function GetGitHubReleasesAsync(ownerRepo As String, ct As CancellationToken, Optional count As Integer = 20) As Task(Of List(Of GitHubRelease))
        Dim json = Await GetStringAsync($"https://api.github.com/repos/{ownerRepo}/releases?per_page={count}", ct)
        Dim list As New List(Of GitHubRelease)
        Using doc = JsonDocument.Parse(json)
            For Each r In doc.RootElement.EnumerateArray()
                Dim draft As JsonElement
                If r.TryGetProperty("draft", draft) AndAlso draft.ValueKind = JsonValueKind.True Then Continue For
                list.Add(ParseRelease(r))
            Next
        End Using
        Return list
    End Function

    Private Function ParseRelease(r As JsonElement) As GitHubRelease
        Dim rel As New GitHubRelease With {
            .Tag = r.GetProperty("tag_name").GetString(),
            .Name = GetStr(r, "name"),
            .Body = GetStr(r, "body"),
            .HtmlUrl = GetStr(r, "html_url")
        }
        For Each a In r.GetProperty("assets").EnumerateArray()
            Dim digest = GetStr(a, "digest") ' "sha256:..." (GitHub adds this for new uploads)
            rel.Assets.Add(New GitHubAsset With {
                .Name = a.GetProperty("name").GetString(),
                .Url = a.GetProperty("browser_download_url").GetString(),
                .Size = a.GetProperty("size").GetInt64(),
                .Sha256 = If(digest IsNot Nothing AndAlso digest.StartsWith("sha256:"), digest.Substring(7), Nothing)
            })
        Next
        Return rel
    End Function

    ' ---------------- Hugging Face ----------------

    Public Class HfFile
        Public Property Name As String
        Public Property Size As Long
        Public Property Sha256 As String
    End Class

    ''' <summary>Lists files of a Hugging Face model repo (with sizes and LFS SHA-256). Returns Nothing if the repo does not exist.</summary>
    Public Async Function ListHuggingFaceFilesAsync(repo As String, ct As CancellationToken) As Task(Of List(Of HfFile))
        Try
            Dim json = Await GetStringAsync($"https://huggingface.co/api/models/{repo}/tree/main", ct)
            Dim files As New List(Of HfFile)
            Using doc = JsonDocument.Parse(json)
                For Each f In doc.RootElement.EnumerateArray()
                    If GetStr(f, "type") <> "file" Then Continue For
                    Dim item As New HfFile With {.Name = GetStr(f, "path")}
                    Dim sz As JsonElement
                    If f.TryGetProperty("size", sz) AndAlso sz.ValueKind = JsonValueKind.Number Then item.Size = sz.GetInt64()
                    Dim lfs As JsonElement
                    If f.TryGetProperty("lfs", lfs) AndAlso lfs.ValueKind = JsonValueKind.Object Then
                        item.Sha256 = GetStr(lfs, "oid")
                        Dim lsz As JsonElement
                        If lfs.TryGetProperty("size", lsz) AndAlso lsz.ValueKind = JsonValueKind.Number Then item.Size = lsz.GetInt64()
                    End If
                    files.Add(item)
                Next
            End Using
            Return files
        Catch ex As HttpRequestException
            Logger.Log($"Hugging Face repo {repo} not available: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Public Function HuggingFaceUrl(repo As String, file As String) As String
        Return $"https://huggingface.co/{repo}/resolve/main/{Uri.EscapeDataString(file).Replace("%2F", "/")}?download=true"
    End Function

    Private Function GetStr(e As JsonElement, prop As String) As String
        Dim v As JsonElement
        If e.TryGetProperty(prop, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return Nothing
    End Function

End Module
