Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions

''' <summary>A decoded subtitle picture: luminance + alpha per pixel.</summary>
Public Class SubBitmap
    Public ReadOnly Width As Integer
    Public ReadOnly Height As Integer
    Public ReadOnly Luma As Byte()
    Public ReadOnly Alpha As Byte()

    Public Sub New(width As Integer, height As Integer)
        Me.Width = width
        Me.Height = height
        Luma = New Byte(width * height - 1) {}
        Alpha = New Byte(width * height - 1) {}
    End Sub
End Class

Public Class ImageCue
    Public Property Start As TimeSpan
    Public Property [End] As TimeSpan
    Public Property Image As SubBitmap
End Class

''' <summary>
''' Decoders for image-based subtitles, so they can be OCR'd into SRT:
'''  • PGS / SUP  (hdmv_pgs_subtitle – Blu-ray)
'''  • VobSub     (dvd_subtitle – DVD)
''' </summary>
Public Module ImageSubtitleDecoders

    Public ReadOnly ImageCodecs As String() = {"hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub"}

    ' =================================================================
    '  PGS (.sup): "PG" | PTS(4) | DTS(4) | type(1) | size(2) | data
    ' =================================================================
    Private Class PgsObject
        Public Width, Height As Integer
        Public Data As New List(Of Byte)
    End Class

    Public Function DecodePgs(sup As Byte()) As List(Of ImageCue)
        Dim cues As New List(Of ImageCue)
        Dim palettes As New Dictionary(Of Integer, (Y As Byte(), A As Byte()))
        Dim objects As New Dictionary(Of Integer, PgsObject)
        Dim placements As New List(Of (Id As Integer, X As Integer, Y As Integer))
        Dim paletteId = 0
        Dim pcsPts As Long = 0
        Dim open As ImageCue = Nothing

        Dim pos = 0
        While pos + 13 <= sup.Length
            If sup(pos) <> &H50 OrElse sup(pos + 1) <> &H47 Then pos += 1 : Continue While ' resync on "PG"
            Dim pts = (CLng(sup(pos + 2)) << 24) Or (CLng(sup(pos + 3)) << 16) Or (CLng(sup(pos + 4)) << 8) Or sup(pos + 5)
            Dim type = sup(pos + 10)
            Dim size = (CInt(sup(pos + 11)) << 8) Or sup(pos + 12)
            Dim d = pos + 13
            If d + size > sup.Length Then Exit While

            Select Case type
                Case &H16 ' PCS – presentation composition
                    pcsPts = pts
                    Dim state = sup(d + 7)
                    paletteId = sup(d + 9)
                    Dim count = sup(d + 10)
                    If (state And &H80) <> 0 Then objects.Clear() ' epoch start
                    placements.Clear()
                    Dim p = d + 11
                    For i = 0 To count - 1
                        Dim objId = (CInt(sup(p)) << 8) Or sup(p + 1)
                        Dim cropped = (sup(p + 3) And &H40) <> 0
                        Dim x = (CInt(sup(p + 4)) << 8) Or sup(p + 5)
                        Dim y = (CInt(sup(p + 6)) << 8) Or sup(p + 7)
                        placements.Add((objId, x, y))
                        p += If(cropped, 16, 8)
                    Next
                Case &H14 ' PDS – palette (Y, Cr, Cb, alpha)
                    Dim id = sup(d)
                    Dim yv(255) As Byte, av(255) As Byte
                    If palettes.ContainsKey(id) Then
                        yv = CType(palettes(id).Y.Clone(), Byte())
                        av = CType(palettes(id).A.Clone(), Byte())
                    End If
                    Dim p = d + 2
                    While p + 5 <= d + size
                        Dim entry = sup(p)
                        yv(entry) = sup(p + 1)
                        av(entry) = sup(p + 4)
                        p += 5
                    End While
                    palettes(id) = (yv, av)
                Case &H15 ' ODS – object (RLE), may be split over several segments
                    Dim objId = (CInt(sup(d)) << 8) Or sup(d + 1)
                    Dim seq = sup(d + 3)
                    If (seq And &H80) <> 0 Then
                        Dim o As New PgsObject With {
                            .Width = (CInt(sup(d + 7)) << 8) Or sup(d + 8),
                            .Height = (CInt(sup(d + 9)) << 8) Or sup(d + 10)
                        }
                        For i = d + 11 To d + size - 1 : o.Data.Add(sup(i)) : Next
                        objects(objId) = o
                    ElseIf objects.ContainsKey(objId) Then
                        For i = d + 4 To d + size - 1 : objects(objId).Data.Add(sup(i)) : Next
                    End If
                Case &H80 ' END of display set
                    Dim t = TimeSpan.FromMilliseconds(pcsPts / 90.0)
                    If open IsNot Nothing Then
                        open.End = t
                        cues.Add(open)
                        open = Nothing
                    End If
                    If placements.Count > 0 AndAlso palettes.ContainsKey(paletteId) Then
                        Dim img = ComposePgs(placements, objects, palettes(paletteId))
                        If img IsNot Nothing Then open = New ImageCue With {.Start = t, .Image = img}
                    End If
            End Select
            pos = d + size
        End While
        If open IsNot Nothing Then
            open.End = open.Start + TimeSpan.FromSeconds(4)
            cues.Add(open)
        End If
        Return cues.Where(Function(c) c.End > c.Start).ToList()
    End Function

    Private Function ComposePgs(placements As List(Of (Id As Integer, X As Integer, Y As Integer)),
                                objects As Dictionary(Of Integer, PgsObject),
                                pal As (Y As Byte(), A As Byte())) As SubBitmap
        Dim items = placements.Where(Function(p) objects.ContainsKey(p.Id)).ToList()
        If items.Count = 0 Then Return Nothing
        Dim minX = items.Min(Function(p) p.X), minY = items.Min(Function(p) p.Y)
        Dim maxX = items.Max(Function(p) p.X + objects(p.Id).Width)
        Dim maxY = items.Max(Function(p) p.Y + objects(p.Id).Height)
        If maxX <= minX OrElse maxY <= minY Then Return Nothing
        Dim bmp As New SubBitmap(maxX - minX, maxY - minY)
        For Each p In items
            Dim o = objects(p.Id)
            Dim idx = DecodePgsRle(o.Data, o.Width, o.Height)
            For yy = 0 To o.Height - 1
                For xx = 0 To o.Width - 1
                    Dim c = idx(yy * o.Width + xx)
                    Dim t = (p.Y - minY + yy) * bmp.Width + (p.X - minX + xx)
                    bmp.Luma(t) = pal.Y(c)
                    bmp.Alpha(t) = pal.A(c)
                Next
            Next
        Next
        Return bmp
    End Function

    Private Function DecodePgsRle(data As List(Of Byte), w As Integer, h As Integer) As Byte()
        Dim px(w * h - 1) As Byte
        Dim x = 0, y = 0, i = 0
        While i < data.Count AndAlso y < h
            Dim b = data(i) : i += 1
            Dim run = 1
            Dim color As Byte = b
            If b = 0 Then
                If i >= data.Count Then Exit While
                Dim f = data(i) : i += 1
                If f = 0 Then
                    x = 0 : y += 1 ' end of line
                    Continue While
                End If
                Select Case f >> 6
                    Case 0 : run = f And &H3F : color = 0
                    Case 1 : run = ((f And &H3F) << 8) Or data(i) : i += 1 : color = 0
                    Case 2 : run = f And &H3F : color = data(i) : i += 1
                    Case Else : run = ((f And &H3F) << 8) Or data(i) : color = data(i + 1) : i += 2
                End Select
            End If
            For k = 1 To run
                If x < w AndAlso y < h Then px(y * w + x) = color
                x += 1
            Next
        End While
        Return px
    End Function

    ' =================================================================
    '  VobSub (dvd_subtitle) packets – from ffprobe -show_packets -show_data
    ' =================================================================
    Public Class SpuPacket
        Public Property Pts As TimeSpan
        Public Property Data As Byte()
    End Class

    ''' <summary>Reads the 16-colour palette from the VobSub .idx header ("palette: rrggbb, …").</summary>
    Public Function ParseIdxPalette(idxText As String) As Byte()
        Dim luma(15) As Byte
        Dim m = Regex.Match(If(idxText, ""), "palette:\s*([0-9a-fA-F,\s]+)")
        If m.Success Then
            Dim cols = m.Groups(1).Value.Split(","c).Select(Function(c) c.Trim()).Where(Function(c) c.Length = 6).ToList()
            For i = 0 To Math.Min(15, cols.Count - 1)
                Dim rgb = Integer.Parse(cols(i), NumberStyles.HexNumber)
                luma(i) = CByte(Math.Min(255, 0.299 * ((rgb >> 16) And 255) + 0.587 * ((rgb >> 8) And 255) + 0.114 * (rgb And 255)))
            Next
        Else
            For i = 0 To 15 : luma(i) = CByte(i * 17) : Next
        End If
        Return luma
    End Function

    ''' <summary>Converts ffprobe's hex dump ("00000000: 7369 7a65  size") to bytes.</summary>
    Public Function ParseHexDump(dump As String) As Byte()
        Dim bytes As New List(Of Byte)
        For Each rawLine In If(dump, "").Split({vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
            Dim colon = rawLine.IndexOf(": ")
            If colon < 0 Then Continue For
            Dim hexPart = rawLine.Substring(colon + 2)
            If hexPart.Length > 39 Then hexPart = hexPart.Substring(0, 39)
            hexPart = hexPart.Replace(" ", "")
            For i = 0 To hexPart.Length - 2 Step 2
                bytes.Add(Byte.Parse(hexPart.Substring(i, 2), NumberStyles.HexNumber))
            Next
        Next
        Return bytes.ToArray()
    End Function

    Public Function DecodeVobSub(packets As List(Of SpuPacket), paletteLuma As Byte()) As List(Of ImageCue)
        Dim cues As New List(Of ImageCue)
        For n = 0 To packets.Count - 1
            Try
                Dim cue = DecodeSpu(packets(n), paletteLuma)
                If cue Is Nothing Then Continue For
                If cue.End <= cue.Start Then
                    cue.End = If(n + 1 < packets.Count, packets(n + 1).Pts, cue.Start + TimeSpan.FromSeconds(4))
                End If
                If cue.End > cue.Start Then cues.Add(cue)
            Catch ex As Exception
                Logger.Log($"VobSub packet {n} skipped: {ex.Message}")
            End Try
        Next
        Return cues
    End Function

    Private Function DecodeSpu(pkt As SpuPacket, pal As Byte()) As ImageCue
        Dim d = pkt.Data
        If d.Length < 4 Then Return Nothing
        Dim ctrl = (CInt(d(2)) << 8) Or d(3)
        Dim colors = {0, 1, 2, 3}
        Dim alphas = {0, 15, 15, 15}
        Dim x1 = 0, x2 = -1, y1 = 0, y2 = -1, top = -1, bottom = -1
        Dim startDelay = -1, stopDelay = -1
        Dim seqPos = ctrl
        Dim guard = 0
        While seqPos + 4 <= d.Length AndAlso guard < 32
            guard += 1
            Dim delay = (CInt(d(seqPos)) << 8) Or d(seqPos + 1)
            Dim nextSeq = (CInt(d(seqPos + 2)) << 8) Or d(seqPos + 3)
            Dim p = seqPos + 4
            Dim done = False
            While p < d.Length AndAlso Not done
                Dim cmd = d(p) : p += 1
                Select Case cmd
                    Case &H0, &H1 : startDelay = delay
                    Case &H2 : stopDelay = delay
                    Case &H3
                        colors = {CInt(d(p + 1) And &HF), CInt(d(p + 1) >> 4), CInt(d(p) And &HF), CInt(d(p) >> 4)} ' background, pattern, emph1, emph2
                        p += 2
                    Case &H4
                        alphas = {CInt(d(p + 1) And &HF), CInt(d(p + 1) >> 4), CInt(d(p) And &HF), CInt(d(p) >> 4)}
                        p += 2
                    Case &H5
                        x1 = (CInt(d(p)) << 4) Or (d(p + 1) >> 4)
                        x2 = ((d(p + 1) And &HF) << 8) Or d(p + 2)
                        y1 = (CInt(d(p + 3)) << 4) Or (d(p + 4) >> 4)
                        y2 = ((d(p + 4) And &HF) << 8) Or d(p + 5)
                        p += 6
                    Case &H6
                        top = (CInt(d(p)) << 8) Or d(p + 1)
                        bottom = (CInt(d(p + 2)) << 8) Or d(p + 3)
                        p += 4
                    Case &HFF : done = True
                    Case Else : done = True ' unknown command
                End Select
            End While
            If nextSeq = seqPos OrElse nextSeq <= 0 Then Exit While
            seqPos = nextSeq
        End While
        If x2 < x1 OrElse y2 < y1 OrElse top < 0 OrElse bottom < 0 Then Return Nothing

        Dim w = x2 - x1 + 1, h = y2 - y1 + 1
        Dim bmp As New SubBitmap(w, h)
        DecodeSpuField(d, top, ctrl, bmp, 0, colors, alphas, pal)
        DecodeSpuField(d, bottom, ctrl, bmp, 1, colors, alphas, pal)
        Dim unit = 1024.0 / 90.0 ' delay ticks → milliseconds
        Return New ImageCue With {
            .Start = pkt.Pts + TimeSpan.FromMilliseconds(Math.Max(0, startDelay) * unit),
            .[End] = If(stopDelay > 0, pkt.Pts + TimeSpan.FromMilliseconds(stopDelay * unit), TimeSpan.Zero),
            .Image = bmp
        }
    End Function

    Private Sub DecodeSpuField(d As Byte(), offset As Integer, limit As Integer, bmp As SubBitmap, firstLine As Integer,
                               colors As Integer(), alphas As Integer(), pal As Byte())
        Dim nib = offset * 2 ' position in nibbles
        Dim getNib = Function() As Integer
                         Dim b = d(nib \ 2)
                         Dim v = If(nib Mod 2 = 0, b >> 4, b And &HF)
                         nib += 1
                         Return v
                     End Function
        Dim y = firstLine
        While y < bmp.Height AndAlso nib \ 2 < limit
            Dim x = 0
            While x < bmp.Width AndAlso nib \ 2 < limit
                Dim v = getNib()
                If v < &H4 Then
                    v = (v << 4) Or getNib()
                    If v < &H10 Then
                        v = (v << 4) Or getNib()
                        If v < &H40 Then
                            v = (v << 4) Or getNib()
                            If v < &H4 Then v = v Or ((bmp.Width - x) << 2) ' run to end of line
                        End If
                    End If
                End If
                Dim run = v >> 2, c = v And 3
                Dim lum = pal(colors(c) And 15)
                Dim a = CByte(alphas(c) * 17)
                For k = 1 To run
                    If x >= bmp.Width Then Exit For
                    bmp.Luma(y * bmp.Width + x) = lum
                    bmp.Alpha(y * bmp.Width + x) = a
                    x += 1
                Next
            End While
            If nib Mod 2 = 1 Then nib += 1 ' lines are byte aligned
            y += 2
        End While
    End Sub

    ''' <summary>Parses "ffprobe -show_packets -show_data -show_streams -of json" output of one dvd_subtitle stream.</summary>
    Public Function ParseFfprobePackets(json As String, ByRef paletteLuma As Byte()) As List(Of SpuPacket)
        Dim list As New List(Of SpuPacket)
        paletteLuma = ParseIdxPalette(Nothing)
        Using doc = System.Text.Json.JsonDocument.Parse(json)
            Dim root = doc.RootElement
            Dim streams As System.Text.Json.JsonElement
            If root.TryGetProperty("streams", streams) AndAlso streams.GetArrayLength() > 0 Then
                Dim extra As System.Text.Json.JsonElement
                If streams(0).TryGetProperty("extradata", extra) Then
                    paletteLuma = ParseIdxPalette(Text.Encoding.ASCII.GetString(ParseHexDump(extra.GetString())))
                End If
            End If
            Dim packets As System.Text.Json.JsonElement
            If root.TryGetProperty("packets", packets) Then
                For Each p In packets.EnumerateArray()
                    Dim ptsEl As System.Text.Json.JsonElement, dataEl As System.Text.Json.JsonElement
                    If Not p.TryGetProperty("pts_time", ptsEl) OrElse Not p.TryGetProperty("data", dataEl) Then Continue For
                    Dim secs As Double
                    If Not Double.TryParse(ptsEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, secs) Then Continue For
                    list.Add(New SpuPacket With {.Pts = TimeSpan.FromSeconds(secs), .Data = ParseHexDump(dataEl.GetString())})
                Next
            End If
        End Using
        Return list
    End Function

    ' =================================================================
    '  OCR preparation: dark text on white, upscaled, with a margin
    ' =================================================================
    ''' <summary>
    ''' Subtitles are light text with a dark outline on a transparent background.
    ''' OCR engines read best dark text on white: composite on black, invert, scale small images up.
    ''' Returns an 8-bit grayscale image.
    ''' </summary>
    Public Function PrepareForOcr(src As SubBitmap, ByRef outW As Integer, ByRef outH As Integer) As Byte()
        ' Crop to visible pixels.
        Dim minX = src.Width, minY = src.Height, maxX = -1, maxY = -1
        For y = 0 To src.Height - 1
            For x = 0 To src.Width - 1
                If src.Alpha(y * src.Width + x) > 40 Then
                    If x < minX Then minX = x
                    If x > maxX Then maxX = x
                    If y < minY Then minY = y
                    If y > maxY Then maxY = y
                End If
            Next
        Next
        If maxX < 0 Then outW = 0 : outH = 0 : Return Nothing
        Dim cw = maxX - minX + 1, ch = maxY - minY + 1
        Const margin = 16
        Dim scale = If(ch < 60, 3, If(ch < 120, 2, 1))
        ' Windows OCR rejects images larger than ~2600 px.
        While scale > 1 AndAlso (cw * scale + margin * 2 > 2600 OrElse ch * scale + margin * 2 > 2600)
            scale -= 1
        End While
        outW = cw * scale + margin * 2
        outH = ch * scale + margin * 2
        Dim w = outW
        Dim gray(outW * outH - 1) As Byte
        Array.Fill(gray, CByte(255))
        For y = 0 To ch - 1
            For x = 0 To cw - 1
                Dim i = (minY + y) * src.Width + (minX + x)
                Dim v = CInt(src.Luma(i)) * src.Alpha(i) \ 255     ' composite on black
                Dim ink = CByte(255 - If(v > 110, 255, v * 2 \ 3)) ' bright fill → black ink, outline → light
                For sy = 0 To scale - 1
                    Dim row = (margin + y * scale + sy) * w + margin + x * scale
                    For sx = 0 To scale - 1
                        gray(row + sx) = ink
                    Next
                Next
            Next
        Next
        Return gray
    End Function

    ''' <summary>8-bit grayscale → 32-bit BMP file bytes (BGRA). Used to feed Windows OCR and for debugging.</summary>
    Public Function ToBmp(gray As Byte(), w As Integer, h As Integer) As Byte()
        Dim rowBytes = w * 4
        Dim size = 54 + rowBytes * h
        Using ms As New MemoryStream(size)
            Using bw As New BinaryWriter(ms)
                bw.Write(CUShort(&H4D42)) : bw.Write(size) : bw.Write(0) : bw.Write(54)
                bw.Write(40) : bw.Write(w) : bw.Write(-h) : bw.Write(CUShort(1)) : bw.Write(CUShort(32))
                bw.Write(0) : bw.Write(rowBytes * h) : bw.Write(2835) : bw.Write(2835) : bw.Write(0) : bw.Write(0)
                For i = 0 To gray.Length - 1
                    Dim g = gray(i)
                    bw.Write(g) : bw.Write(g) : bw.Write(g) : bw.Write(CByte(255))
                Next
            End Using
            Return ms.ToArray()
        End Using
    End Function

End Module

''' <summary>OCR engine abstraction (the Windows implementation lives in the app project).</summary>
Public Interface IOcrEngine
    ''' <summary>Set when the requested language is not available and another one is used.</summary>
    ReadOnly Property Warning As String
    Function RecognizeAsync(gray As Byte(), width As Integer, height As Integer) As Task(Of String)
End Interface
