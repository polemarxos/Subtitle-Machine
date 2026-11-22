''' <summary>A language known to Whisper / TranslateGemma.</summary>
Public Class LanguageInfo
    Public ReadOnly Property Name As String
    ''' <summary>ISO 639-1 code (used by whisper.cpp, TranslateGemma prompts and output file names).</summary>
    Public ReadOnly Property Code As String
    ''' <summary>ISO 639-2 codes (B and T) as found in MKV/MP4 stream tags.</summary>
    Public ReadOnly Property Code3 As String()

    Public Sub New(name As String, code As String, ParamArray code3 As String())
        Me.Name = name
        Me.Code = code
        Me.Code3 = code3
    End Sub

    ''' <summary>Name in the interface language (Name stays English – it is used in the AI prompt).</summary>
    Public ReadOnly Property DisplayName As String
        Get
            Return Loc.Tr(Name)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return DisplayName
    End Function
End Class

Public Module Languages

    Public ReadOnly All As LanguageInfo() = {
        New LanguageInfo("English", "en", "eng"),
        New LanguageInfo("Greek", "el", "gre", "ell"),
        New LanguageInfo("Spanish", "es", "spa"),
        New LanguageInfo("French", "fr", "fre", "fra"),
        New LanguageInfo("German", "de", "ger", "deu"),
        New LanguageInfo("Italian", "it", "ita"),
        New LanguageInfo("Portuguese", "pt", "por"),
        New LanguageInfo("Russian", "ru", "rus"),
        New LanguageInfo("Chinese", "zh", "chi", "zho"),
        New LanguageInfo("Japanese", "ja", "jpn"),
        New LanguageInfo("Korean", "ko", "kor"),
        New LanguageInfo("Arabic", "ar", "ara"),
        New LanguageInfo("Dutch", "nl", "dut", "nld"),
        New LanguageInfo("Polish", "pl", "pol"),
        New LanguageInfo("Turkish", "tr", "tur"),
        New LanguageInfo("Swedish", "sv", "swe"),
        New LanguageInfo("Danish", "da", "dan"),
        New LanguageInfo("Norwegian", "no", "nor", "nob", "nno"),
        New LanguageInfo("Finnish", "fi", "fin"),
        New LanguageInfo("Czech", "cs", "cze", "ces"),
        New LanguageInfo("Slovak", "sk", "slo", "slk"),
        New LanguageInfo("Hungarian", "hu", "hun"),
        New LanguageInfo("Romanian", "ro", "rum", "ron"),
        New LanguageInfo("Bulgarian", "bg", "bul"),
        New LanguageInfo("Serbian", "sr", "srp"),
        New LanguageInfo("Croatian", "hr", "hrv"),
        New LanguageInfo("Slovenian", "sl", "slv"),
        New LanguageInfo("Ukrainian", "uk", "ukr"),
        New LanguageInfo("Hebrew", "he", "heb"),
        New LanguageInfo("Persian", "fa", "per", "fas"),
        New LanguageInfo("Hindi", "hi", "hin"),
        New LanguageInfo("Bengali", "bn", "ben"),
        New LanguageInfo("Urdu", "ur", "urd"),
        New LanguageInfo("Thai", "th", "tha"),
        New LanguageInfo("Vietnamese", "vi", "vie"),
        New LanguageInfo("Indonesian", "id", "ind"),
        New LanguageInfo("Malay", "ms", "may", "msa"),
        New LanguageInfo("Filipino (Tagalog)", "tl", "fil", "tgl"),
        New LanguageInfo("Catalan", "ca", "cat"),
        New LanguageInfo("Lithuanian", "lt", "lit"),
        New LanguageInfo("Latvian", "lv", "lav"),
        New LanguageInfo("Estonian", "et", "est"),
        New LanguageInfo("Albanian", "sq", "alb", "sqi"),
        New LanguageInfo("Macedonian", "mk", "mac", "mkd"),
        New LanguageInfo("Georgian", "ka", "geo", "kat"),
        New LanguageInfo("Armenian", "hy", "arm", "hye"),
        New LanguageInfo("Icelandic", "is", "ice", "isl"),
        New LanguageInfo("Galician", "gl", "glg"),
        New LanguageInfo("Basque", "eu", "baq", "eus"),
        New LanguageInfo("Swahili", "sw", "swa"),
        New LanguageInfo("Tamil", "ta", "tam"),
        New LanguageInfo("Telugu", "te", "tel")
    }

    ''' <summary>Finds a language by ISO 639-1, ISO 639-2 or English name (case-insensitive). Returns Nothing if unknown.</summary>
    Public Function Find(codeOrName As String) As LanguageInfo
        If String.IsNullOrWhiteSpace(codeOrName) Then Return Nothing
        Dim key = codeOrName.Trim().ToLowerInvariant()
        For Each l In All
            If l.Code = key OrElse l.Name.ToLowerInvariant() = key OrElse l.Code3.Contains(key) Then Return l
        Next
        Return Nothing
    End Function

End Module
