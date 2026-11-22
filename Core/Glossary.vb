Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' Names and terms that must always be translated the same way (or kept untranslated).
''' Stored as plain text in glossary.txt:
'''
'''   Nick = Νίκος                 (all target languages)
'''   Winterfell =                 (empty → keep the word as it is)
'''   [el]                         (entries below apply only to Greek)
'''   Ministry of Magic = Υπουργείο Μαγείας
''' </summary>
Public Class Glossary

    Public Class Entry
        Public Property Source As String
        Public Property Target As String
        ''' <summary>ISO 639-1 code, or Nothing for all languages.</summary>
        Public Property Language As String
        Friend Property Pattern As Regex
    End Class

    Public ReadOnly Entries As New List(Of Entry)

    Public Shared ReadOnly Property FilePath As String
        Get
            Return Path.Combine(AppPaths.DataDir, "glossary.txt")
        End Get
    End Property

    ''' <summary>Starter glossary for English ↔ Greek (written once to glossary.txt; the user can edit it).</summary>
    Public Const Template As String =
"# Glossary – names and terms that must always be translated the same way.
# One entry per line:   original = translation
# Leave the right side empty to keep a word untranslated:   Winterfell =
# Entries under a [language] header apply only when translating INTO that language: [el] = Greek, [en] = English.
# Lines starting with # are ignored. Add the names of characters and places of your series below.

# ---- All languages: keep these as they are ----
FBI =
CIA =
NSA =
NASA =
NYPD =
LAPD =
MI6 =
DNA =
GPS =
Wi-Fi =
iPhone =
Facebook =
Instagram =
YouTube =
Google =

[el]
# ---- English → Greek ----
Mr. = κ.
Mrs. = κα
Ms. = κα
Dr. = Δρ
Okay = Εντάξει
OK = Εντάξει
Ma'am = Κυρία
Lieutenant = Υπολοχαγός
Sergeant = Λοχίας
Detective = Ντετέκτιβ
Sheriff = Σερίφης
Deputy = Βοηθός σερίφη
Senator = Γερουσιαστής
Mayor = Δήμαρχος
Prosecutor = Εισαγγελέας
District Attorney = Εισαγγελέας
Your Honor = Κύριε Πρόεδρε
Thanksgiving = Ημέρα των Ευχαριστιών
Dude = φίλε

[en]
# ---- Greek → English ----
κ. = Mr.
κα = Mrs.
Δρ = Dr.
Εντάξει = Okay
Κύριε Πρόεδρε = Your Honor
"

    Public Shared Function Load() As Glossary
        Try
            ' First start: create the starter glossary so the user can see and edit it.
            If Not File.Exists(FilePath) Then File.WriteAllText(FilePath, Template.Replace(vbLf, vbCrLf).Replace(vbCr & vbCr, vbCr), New UTF8Encoding(False))
            If File.Exists(FilePath) Then Return Parse(File.ReadAllText(FilePath, Encoding.UTF8))
        Catch ex As Exception
            Logger.Log("Glossary unreadable: " & ex.Message)
        End Try
        Return Parse(Template)
    End Function

    Public Shared Function Parse(text As String) As Glossary
        Dim g As New Glossary()
        Dim lang As String = Nothing
        For Each raw In If(text, "").Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim line = raw.Trim()
            If line.Length = 0 OrElse line.StartsWith("#") Then Continue For
            Dim header = Regex.Match(line, "^\[\s*([^\]]*)\s*\]$")
            If header.Success Then
                Dim l = Languages.Find(header.Groups(1).Value)
                lang = If(l IsNot Nothing, l.Code, If(header.Groups(1).Value.Trim() = "*" OrElse header.Groups(1).Value.Trim() = "", Nothing, header.Groups(1).Value.Trim().ToLowerInvariant()))
                Continue For
            End If
            Dim eq = line.IndexOf("="c)
            Dim src = If(eq < 0, line, line.Substring(0, eq)).Trim()
            Dim tgt = If(eq < 0, "", line.Substring(eq + 1)).Trim()
            If src.Length = 0 Then Continue For
            If tgt.Length = 0 Then tgt = src
            g.Entries.Add(New Entry With {
                .Source = src, .Target = tgt, .Language = lang,
                .Pattern = New Regex("(?<!\w)" & Regex.Escape(src) & "(?!\w)", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
            })
        Next
        Return g
    End Function

    ''' <summary>Entries that occur in this text and apply to the target language (language-specific ones win).</summary>
    Public Function ForText(text As String, target As LanguageInfo) As List(Of Entry)
        Dim result As New List(Of Entry)
        If String.IsNullOrEmpty(text) OrElse Entries.Count = 0 Then Return result
        For Each e In Entries.Where(Function(x) x.Language = target.Code).Concat(Entries.Where(Function(x) x.Language Is Nothing))
            If result.Any(Function(r) String.Equals(r.Source, e.Source, StringComparison.OrdinalIgnoreCase)) Then Continue For
            If e.Pattern.IsMatch(text) Then result.Add(e)
        Next
        Return result
    End Function

End Class
