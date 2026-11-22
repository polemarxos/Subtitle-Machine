Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports WeCantSpell.Hunspell

''' <summary>
''' Spell checking with the Hunspell dictionaries of LibreOffice (downloaded once, on first use).
''' Words the user adds are kept in dictionaries\user-xx.txt.
''' </summary>
Public Class SpellChecker

    ''' <summary>Language code → path of the dictionary in the LibreOffice dictionaries repository (without .dic / .aff).</summary>
    Public Shared ReadOnly Sources As New Dictionary(Of String, String) From {
        {"el", "el_GR/el_GR"}, {"en", "en/en_US"}, {"es", "es/es_ES"}, {"de", "de/de_DE_frami"}, {"it", "it_IT/it_IT"},
        {"pt", "pt_PT/pt_PT"}, {"nl", "nl_NL/nl_NL"}, {"ru", "ru_RU/ru_RU"}, {"pl", "pl_PL/pl_PL"}, {"tr", "tr_TR/tr_TR"},
        {"ro", "ro/ro_RO"}, {"bg", "bg_BG/bg_BG"}, {"da", "da_DK/da_DK"}, {"hr", "hr_HR/hr_HR"}, {"cs", "cs_CZ/cs_CZ"},
        {"hu", "hu_HU/hu_HU"}, {"uk", "uk_UA/uk_UA"}, {"sr", "sr/sr"}, {"sl", "sl_SI/sl_SI"}, {"sk", "sk_SK/sk_SK"}
    }
    Private Const BaseUrl As String = "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/"

    Private Shared ReadOnly WordPattern As New Regex("\p{L}[\p{L}\p{M}]*(?:['’]\p{L}[\p{L}\p{M}]*)*", RegexOptions.Compiled)
    Private Shared _providerRegistered As Boolean

    Private ReadOnly _words As WordList
    Private ReadOnly _userWords As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _userFile As String
    Private ReadOnly _cache As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
    Private ReadOnly _extraWords As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Public ReadOnly Property Language As LanguageInfo

    Public Shared Function IsSupported(lang As LanguageInfo) As Boolean
        Return lang IsNot Nothing AndAlso Sources.ContainsKey(lang.Code)
    End Function

    Public Shared ReadOnly Property Dir As String
        Get
            Return Path.Combine(AppPaths.DataDir, "dictionaries")
        End Get
    End Property

    Public Shared Function IsDownloaded(lang As LanguageInfo) As Boolean
        Return IsSupported(lang) AndAlso File.Exists(Path.Combine(Dir, lang.Code & ".dic")) AndAlso File.Exists(Path.Combine(Dir, lang.Code & ".aff"))
    End Function

    Private Sub New(lang As LanguageInfo, words As WordList, userFile As String)
        Language = lang
        _words = words
        _userFile = userFile
        If File.Exists(userFile) Then
            For Each w In File.ReadAllLines(userFile, Encoding.UTF8)
                If w.Trim().Length > 0 Then _userWords.Add(w.Trim())
            Next
        End If
    End Sub

    ''' <summary>Creates a checker from .dic/.aff files (used by the tests).</summary>
    Public Shared Function FromFiles(lang As LanguageInfo, dicFile As String, affFile As String, Optional userFile As String = Nothing) As SpellChecker
        RegisterCodePages()
        Return New SpellChecker(lang, WordList.CreateFromFiles(dicFile, affFile), If(userFile, Path.Combine(Path.GetTempPath(), "user-" & lang.Code & ".txt")))
    End Function

    ''' <summary>Downloads the dictionary when needed and loads it.</summary>
    Public Shared Async Function LoadAsync(lang As LanguageInfo, progress As IProgress(Of DownloadProgress), ct As CancellationToken) As Task(Of SpellChecker)
        If Not IsSupported(lang) Then Return Nothing
        AppPaths.EnsureDir(Dir)
        Dim dic = Path.Combine(Dir, lang.Code & ".dic")
        Dim aff = Path.Combine(Dir, lang.Code & ".aff")
        If Not File.Exists(aff) Then Await WebHelper.DownloadFileAsync(BaseUrl & Sources(lang.Code) & ".aff", aff, progress, ct)
        If Not File.Exists(dic) Then Await WebHelper.DownloadFileAsync(BaseUrl & Sources(lang.Code) & ".dic", dic, progress, ct)
        RegisterCodePages()
        Dim words = Await Task.Run(Function() WordList.CreateFromFiles(dic, aff), ct)
        Return New SpellChecker(lang, words, Path.Combine(Dir, "user-" & lang.Code & ".txt"))
    End Function

    Private Shared Sub RegisterCodePages()
        ' Many dictionaries are not UTF-8 (Greek: ISO-8859-7).
        If _providerRegistered Then Return
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        _providerRegistered = True
    End Sub

    ''' <summary>Words that are never marked (e.g. names from the glossary).</summary>
    Public Sub AddKnownWords(words As IEnumerable(Of String))
        For Each w In words
            For Each m As Match In WordPattern.Matches(If(w, ""))
                _extraWords.Add(m.Value)
            Next
        Next
        _cache.Clear()
    End Sub

    Public Function IsCorrect(word As String) As Boolean
        Dim ok As Boolean
        If _cache.TryGetValue(word, ok) Then Return ok
        ok = Skip(word) OrElse _userWords.Contains(word) OrElse _extraWords.Contains(word) OrElse _words.Check(word) OrElse
             (word.Contains("’"c) AndAlso _words.Check(word.Replace("’"c, "'"c)))
        _cache(word) = ok
        Return ok
    End Function

    ''' <summary>One-letter words and words in capitals (Greek capitals are written without accents) are not checked.</summary>
    Private Shared Function Skip(word As String) As Boolean
        If word.Length < 2 Then Return True
        Dim letters = word.Where(Function(c) Char.IsLetter(c)).ToList()
        Return letters.Count > 0 AndAlso letters.All(Function(c) Char.IsUpper(c))
    End Function

    ''' <summary>Misspelled words of a text: start position and length.</summary>
    Public Function FindMistakes(text As String) As List(Of (Start As Integer, Length As Integer, Word As String))
        Dim result As New List(Of (Start As Integer, Length As Integer, Word As String))
        If String.IsNullOrEmpty(text) Then Return result
        ' ignore tags like <i> and {\an8}
        Dim clean = Regex.Replace(text, "<[^>]*>|\{[^}]*\}", Function(m) New String(" "c, m.Length))
        For Each m As Match In WordPattern.Matches(clean)
            If Not IsCorrect(m.Value) Then result.Add((m.Index, m.Length, m.Value))
        Next
        Return result
    End Function

    Public Function Suggest(word As String, Optional max As Integer = 6) As List(Of String)
        Try
            Return _words.Suggest(word).Take(max).ToList()
        Catch ex As Exception
            Return New List(Of String)
        End Try
    End Function

    Public Sub AddToDictionary(word As String)
        If String.IsNullOrWhiteSpace(word) OrElse Not _userWords.Add(word.Trim()) Then Return
        _cache.Clear()
        Try
            AppPaths.EnsureDir(Path.GetDirectoryName(_userFile))
            File.AppendAllText(_userFile, word.Trim() & Environment.NewLine, New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Log("Could not save the user dictionary: " & ex.Message)
        End Try
    End Sub

End Class
