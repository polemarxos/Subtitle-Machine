Imports System.Text.RegularExpressions
Imports Windows.Graphics.Imaging
Imports Windows.Media.Ocr
Imports Windows.Storage.Streams

''' <summary>OCR with the engine built into Windows 10/11 (no download needed).</summary>
Public Class WindowsOcr
    Implements IOcrEngine

    Private ReadOnly _engine As OcrEngine
    Public ReadOnly Property Warning As String Implements IOcrEngine.Warning

    Private Sub New(engine As OcrEngine, warning As String)
        _engine = engine
        Me.Warning = warning
    End Sub

    ''' <summary>Creates an OCR engine for the subtitle language, falling back to the Windows display languages.</summary>
    Public Shared Function Create(lang As LanguageInfo) As IOcrEngine
        Dim warning As String = Nothing
        Dim engine As OcrEngine = Nothing
        If lang IsNot Nothing Then
            Try
                Dim wl As New Windows.Globalization.Language(lang.Code)
                If OcrEngine.IsLanguageSupported(wl) Then engine = OcrEngine.TryCreateFromLanguage(wl)
            Catch ex As Exception
                Logger.Log("OCR language error: " & ex.Message)
            End Try
            If engine Is Nothing Then
                warning = $"The Windows OCR language pack for {lang.Name} is not installed – results may be poor. " &
                          $"Install it in Settings › Time & language › Language & region › Add a language ({lang.Name}), with 'Optical character recognition'."
            End If
        End If
        If engine Is Nothing Then engine = OcrEngine.TryCreateFromUserProfileLanguages()
        If engine Is Nothing Then Throw New Exception("Windows OCR is not available on this PC.")
        Logger.Log($"OCR engine: {engine.RecognizerLanguage.LanguageTag}")
        Return New WindowsOcr(engine, warning)
    End Function

    Public Async Function RecognizeAsync(gray As Byte(), width As Integer, height As Integer) As Task(Of String) Implements IOcrEngine.RecognizeAsync
        Dim bmp = ImageSubtitleDecoders.ToBmp(gray, width, height)
        Using stream As New InMemoryRandomAccessStream()
            Using writer As New DataWriter(stream)
                writer.WriteBytes(bmp)
                Await writer.StoreAsync()
                writer.DetachStream()
            End Using
            stream.Seek(0)
            Dim decoder = Await BitmapDecoder.CreateAsync(stream)
            Using sb = Await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                Dim result = Await _engine.RecognizeAsync(sb)
                Dim lines = result.Lines.Select(Function(l) FixSpacing(l.Text)).Where(Function(t) t.Length > 0)
                Return String.Join(vbLf, lines)
            End Using
        End Using
    End Function

    ''' <summary>Windows OCR puts spaces between CJK characters – remove them.</summary>
    Private Shared Function FixSpacing(text As String) As String
        Dim t = Regex.Replace(text, "(?<=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}])\s+(?=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}])", "")
        Return t.Trim()
    End Function
End Class
