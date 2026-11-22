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
# Tip: only add words that are always meant the same way – a word with two meanings (Will, Mark, Rose) can confuse the translation.

# ==== All languages: keep these as they are ====
# -- Agencies and organisations --
FBI =
CIA =
NSA =
DEA =
ATF =
SWAT =
NYPD =
LAPD =
NCIS =
CSI =
MI5 =
MI6 =
KGB =
Interpol =
Europol =
Scotland Yard =
# -- Places --
Wall Street =
Times Square =
Central Park =
Broadway =
Hollywood =
Silicon Valley =
# -- Technology and brands --
DNA =
GPS =
Wi-Fi =
Bluetooth =
USB =
iPhone =
iPad =
MacBook =
Android =
Windows =
PlayStation =
Xbox =
Facebook =
Instagram =
WhatsApp =
TikTok =
Snapchat =
YouTube =
Google =
Netflix =
Spotify =
Wikipedia =
Tinder =
PayPal =
eBay =
FaceTime =
Skype =
Uber =
McDonald's =
Starbucks =
Coca-Cola =
Ferrari =
Porsche =
BMW =
Harley-Davidson =

[el]
# ==== English → Greek ====
# -- Forms of address --
Mr. = κ.
Mrs. = κα
Ms. = κα
Dr. = Δρ
Ma'am = Κυρία
Your Honor = Κύριε Πρόεδρε
Okay = Εντάξει
OK = Εντάξει
Oh, my God = Θεέ μου
Oh my God = Θεέ μου
Dude = φίλε
Bro = φίλε
Buddy = φιλαράκο
Cops = μπάτσοι
# -- Ranks and jobs --
Lieutenant = Υπολοχαγός
Sergeant = Λοχίας
Corporal = Δεκανέας
Colonel = Συνταγματάρχης
Admiral = Ναύαρχος
Commander = Διοικητής
Detective = Ντετέκτιβ
Agent = Πράκτορας
Sheriff = Σερίφης
Deputy = Βοηθός σερίφη
U.S. Marshal = Ομοσπονδιακός σερίφης
Coroner = Ιατροδικαστής
Medical Examiner = Ιατροδικαστής
Paramedics = Διασώστες
Senator = Γερουσιαστής
Congressman = Βουλευτής
Mayor = Δήμαρχος
Governor = Κυβερνήτης
Prosecutor = Εισαγγελέας
District Attorney = Εισαγγελέας
Attorney General = Γενικός Εισαγγελέας
Defense attorney = Συνήγορος υπεράσπισης
Public defender = Αυτεπάγγελτος συνήγορος
# -- Courtroom and police --
Objection = Ένσταση
Sustained = Δεκτή
Overruled = Απορρίπτεται
Jury = Ένορκοι
Grand jury = Σώμα ενόρκων
Felony = Κακούργημα
Misdemeanor = Πλημμέλημα
Warrant = Ένταλμα
Search warrant = Ένταλμα έρευνας
Subpoena = Κλήτευση
Bail = Εγγύηση
Parole = Αποφυλάκιση υπό όρους
Plea deal = Συμφωνία με την εισαγγελία
Precinct = Τμήμα
# -- Hospital --
Emergency room = Επείγοντα
ICU = Εντατική
CPR = ΚΑΡΠΑ
# -- Institutions and countries --
White House = Λευκός Οίκος
Pentagon = Πεντάγωνο
Congress = Κογκρέσο
Senate = Γερουσία
Supreme Court = Ανώτατο Δικαστήριο
Secret Service = Μυστική Υπηρεσία
Homeland Security = Εσωτερική Ασφάλεια
United Nations = ΟΗΕ
UN = ΟΗΕ
European Union = Ευρωπαϊκή Ένωση
EU = ΕΕ
NATO = ΝΑΤΟ
USA = ΗΠΑ
United States = Ηνωμένες Πολιτείες
UK = Ηνωμένο Βασίλειο
New York = Νέα Υόρκη
Los Angeles = Λος Άντζελες
San Francisco = Σαν Φρανσίσκο
Las Vegas = Λας Βέγκας
New Orleans = Νέα Ορλεάνη
London = Λονδίνο
# -- School and holidays --
High school = Λύκειο
Middle school = Γυμνάσιο
Freshman = Πρωτοετής
Prom = Χορός αποφοίτησης
Spring break = Ανοιξιάτικες διακοπές
Thanksgiving = Ημέρα των Ευχαριστιών
Halloween = Χάλογουιν
Christmas Eve = Παραμονή Χριστουγέννων
New Year's Eve = Παραμονή Πρωτοχρονιάς
Fourth of July = 4η Ιουλίου
Independence Day = Ημέρα Ανεξαρτησίας
Valentine's Day = Ημέρα του Αγίου Βαλεντίνου
Super Bowl = Σούπερ Μπόουλ
# -- Money and units --
Bucks = δολάρια
Dollars = δολάρια
Miles = μίλια
# -- Common first names (spelled the way Greek subtitles write them) --
John = Τζον
Michael = Μάικλ
Mike = Μάικ
David = Ντέιβιντ
James = Τζέιμς
Robert = Ρόμπερτ
William = Γουίλιαμ
Richard = Ρίτσαρντ
Joseph = Τζόζεφ
Thomas = Τόμας
Tom = Τομ
Charles = Τσαρλς
Charlie = Τσάρλι
Christopher = Κρίστοφερ
Daniel = Ντάνιελ
Matthew = Μάθιου
Anthony = Άντονι
Paul = Πολ
Steven = Στίβεν
Andrew = Άντριου
Kevin = Κέβιν
Brian = Μπράιαν
George = Τζορτζ
Edward = Έντουαρντ
Peter = Πίτερ
Harry = Χάρι
Sam = Σαμ
Ben = Μπεν
Nick = Νικ
Mary = Μέρι
Jennifer = Τζένιφερ
Elizabeth = Ελίζαμπεθ
Sarah = Σάρα
Emily = Έμιλι
Jessica = Τζέσικα
Emma = Έμα
Olivia = Ολίβια
Rachel = Ρέιτσελ
Laura = Λόρα
Kate = Κέιτ
Anna = Άνα
Lucy = Λούσι
Susan = Σούζαν
Linda = Λίντα
Karen = Κάρεν
Nancy = Νάνσι
Lisa = Λίζα
Amy = Έιμι

[en]
# ==== Greek → English ====
κ. = Mr.
κα = Mrs.
Δρ = Dr.
Εντάξει = Okay
Κύριε Πρόεδρε = Your Honor
Θεέ μου = Oh, my God
# -- Places --
Αθήνα = Athens
Θεσσαλονίκη = Thessaloniki
Πειραιάς = Piraeus
Κρήτη = Crete
Ρόδος = Rhodes
Κέρκυρα = Corfu
# -- Institutions --
ΕΛ.ΑΣ. = Hellenic Police
ΕΚΑΒ = ambulance service
Βουλή = Parliament
ευρώ = euros
# -- Food and drink: keep the Greek names --
ταβέρνα = taverna
σουβλάκι = souvlaki
μουσακάς = moussaka
τζατζίκι = tzatziki
ούζο = ouzo
τσίπουρο = tsipouro
φέτα = feta
# -- Family --
γιαγιά = Grandma
παππούς = Grandpa
παππού = Grandpa
# -- Common first names (nominative and vocative forms) --
Γιάννης = Yannis
Γιάννη = Yannis
Γιώργος = Giorgos
Γιώργο = Giorgos
Νίκος = Nikos
Νίκο = Nikos
Κώστας = Kostas
Κώστα = Kostas
Δημήτρης = Dimitris
Δημήτρη = Dimitris
Παναγιώτης = Panagiotis
Παναγιώτη = Panagiotis
Βασίλης = Vasilis
Βασίλη = Vasilis
Χρήστος = Christos
Χρήστο = Christos
Μανώλης = Manolis
Μανώλη = Manolis
Μαρία = Maria
Ελένη = Eleni
Κατερίνα = Katerina
Σοφία = Sofia
Δέσποινα = Despina
Βασιλική = Vasiliki
"

    ''' <summary>SHA-256 of earlier starter glossaries; an unedited old copy is replaced by the new one.</summary>
    Private Shared ReadOnly OldTemplateHashes As String() = {"0cdd1af48f76e558b213fa68cea3975ef0ee03cd7207f5b742c0948946cba983"}

    Public Shared Function Load() As Glossary
        Try
            ' First start: create the starter glossary so the user can see and edit it.
            ' A starter glossary of an older version that the user never edited is replaced by the new one.
            If Not File.Exists(FilePath) OrElse IsUneditedOldTemplate(File.ReadAllText(FilePath, Encoding.UTF8)) Then
                File.WriteAllText(FilePath, Template.Replace(vbLf, vbCrLf).Replace(vbCr & vbCr, vbCr), New UTF8Encoding(False))
            End If
            If File.Exists(FilePath) Then Return Parse(File.ReadAllText(FilePath, Encoding.UTF8))
        Catch ex As Exception
            Logger.Log("Glossary unreadable: " & ex.Message)
        End Try
        Return Parse(Template)
    End Function

    Friend Shared Function IsUneditedOldTemplate(text As String) As Boolean
        Dim norm = If(text, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Trim()
        Dim hash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(norm))).ToLowerInvariant()
        Return OldTemplateHashes.Contains(hash)
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
