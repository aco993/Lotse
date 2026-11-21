namespace Lotse.Core.Telc;

public enum MaterialKind
{
    /// <summary>A complete official practice exam: do it on paper, under exam conditions, and enter the points.</summary>
    MockExam,
    /// <summary>Official documents about the exam: format, learning objectives, rating criteria, rated samples.</summary>
    Guide,
    /// <summary>Free material that is not an exam but trains one part of it.</summary>
    Practice,
}

/// <param name="Id">Stable key; for mock exams it is stored with the entered result.</param>
/// <param name="Exam">The exam the material belongs to, as the learner reads it ("telc Deutsch B2").</param>
/// <param name="Free">False for material that has to be bought; listed so the learner knows what exists.</param>
/// <param name="Contents">What is inside, in one line.</param>
/// <param name="Use">How to use it together with Lotse.</param>
/// <param name="Links">Label and address of each official download or page.</param>
public sealed record TelcMaterial(
    string Id,
    MaterialKind Kind,
    string Exam,
    string Title,
    string Publisher,
    bool Free,
    string Contents,
    string Use,
    IReadOnlyList<(string Label, string Url)> Links);

/// <summary>
/// Official free preparation material for telc Deutsch B2 (and B1 as a warm-up), as published by its owners.
/// Lotse only links to it: the tests are protected and stay on the publishers' servers. Every address was checked
/// by hand against the publisher's own page in November 2026. A test keeps the list to the publishers' hosts.
/// </summary>
public static class TelcMaterials
{
    public const string CheckedOn = "November 2026";

    /// <summary>The only hosts a link may point to: the exam owners and the public broadcaster.</summary>
    public static readonly IReadOnlySet<string> OfficialHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "www.telc.net", "shop.telc.net", "www.goethe.de", "learngerman.dw.com",
    };

    public static readonly IReadOnlyList<TelcMaterial> All =
    [
        new("telc-b2-ut1", MaterialKind.MockExam, "telc Deutsch B2", "Übungstest 1 (Modelltest)", "telc gGmbH", true,
            "Die komplette schriftliche und mündliche Prüfung mit Lösungsschlüssel, Bewertungskriterien und den Hörtexten als MP3. Auf dieser Ausgabe beruhen Format und Punkte in Lotse.",
            "Einmal gleich am Anfang als ehrliche Standortbestimmung (90 Minuten am Stück, dann 20 Minuten Hören), die Punkte unten eintragen – dann rechnet die Prognose mit echtem Prüfungsmaterial.",
            [
                ("Prüfung und Audio als ZIP (telc.net)", "https://www.telc.net/fileadmin/user_upload/mock_exams/Deutsch/telc_deutsch_b2.zip"),
                ("Testheft als PDF (telc-Shop, kostenlos)", "https://shop.telc.net/media/catalog/product/file/2/0/20201223_5023-b00-010201_web.pdf"),
                ("Hörtexte als MP3 (telc-Shop, kostenlos)", "https://shop.telc.net/media/catalog/product/file/d/e/deutschb2_uebungstest1.mp3"),
                ("Seite der Prüfung bei telc", "https://www.telc.net/en/language-examinations/certificate-exams/german/telc-german-b2/"),
            ]),
        new("telc-b2-ut2", MaterialKind.MockExam, "telc Deutsch B2", "Übungstest Version 2", "telc gGmbH", false,
            "Ein zweiter vollständiger Prüfungssatz als gedrucktes Heft (12,50 € im telc-Shop).",
            "Etwa drei Wochen vor der Prüfung als Generalprobe: zeigt, ob die Arbeit seit Übungstest 1 in Punkten ankommt.",
            [("Im telc-Shop", "https://shop.telc.net/de_DE/mock-examination-2-telc-deutsch-b2.html")]),
        new("telc-b2-ut3", MaterialKind.MockExam, "telc Deutsch B2", "Übungstest Version 3", "telc gGmbH", false,
            "Ein dritter vollständiger Prüfungssatz als gedrucktes Heft (12,50 € im telc-Shop).",
            "In der letzten Woche vor der Prüfung, wenn du noch einen unverbrauchten Satz unter Prüfungsbedingungen willst.",
            [("Im telc-Shop", "https://shop.telc.net/de_DE/mock-examination-3-telc-deutsch-b2.html")]),
        new("telc-b1-ut1", MaterialKind.MockExam, "Zertifikat Deutsch / telc Deutsch B1", "Übungstest 1", "telc gGmbH", true,
            "Die komplette B1-Prüfung mit Lösungen und Audio. Gleicher Aufbau wie B2 (Lesen 3 Teile, Sprachbausteine 2, Hören 3, Schreiben, Sprechen 3), nur leichter.",
            "Zum Aufwärmen, wenn B2 noch zu schwer wirkt: Wer hier sicher über 80 % liegt, ist bereit für B2-Material. Die Punkte zählen nicht in die B2-Prognose.",
            [
                ("Prüfung und Audio als ZIP (telc.net)", "https://www.telc.net/fileadmin/user_upload/mock_exams/Deutsch/telc_deutsch_b1.zip"),
                ("Seite der Prüfung bei telc", "https://www.telc.net/en/language-examinations/certificate-exams/german/certificate-german-telc-german-b1/"),
            ]),
        new("telc-b2-handbuch", MaterialKind.Guide, "telc Deutsch B2", "Handbuch und Lernziele", "telc gGmbH", true,
            "Was die Prüfung misst: Lernziele, Themen, Textsorten, Aufbau jedes Teils.",
            "Einmal querlesen und die Themenliste mit deinen schwächsten Themen in Lotse vergleichen.",
            [("Handbuch als PDF", "https://www.telc.net/fileadmin/user_upload/pdfs/Handbuch_und_Tipps_fuer_Pruefungsvorbereitung/Deutsch_B2_Handbuch.pdf")]),
        new("telc-b2-tipps", MaterialKind.Guide, "telc Deutsch B2", "Tipps zur Prüfungsvorbereitung", "telc gGmbH", true,
            "Anleitung zu allen Aufgabentypen und bewertete Beispieltexte für den Schriftlichen Ausdruck mit Kommentar der Bewerter.",
            "Vor der ersten eigenen E-Mail lesen: Die bewerteten Beispiele zeigen, was für A, B und C pro Kriterium nötig ist – genau die Kriterien, nach denen Lotse und der Tutor bewerten.",
            [("Tipps als PDF", "https://www.telc.net/fileadmin/user_upload/pdfs/Handbuch_und_Tipps_fuer_Pruefungsvorbereitung/Deutsch_B2_tipps_zur_Pruefungsvorbereitung.pdf")]),
        new("goethe-b2", MaterialKind.Practice, "Goethe-Zertifikat B2", "Übungsmaterial", "Goethe-Institut", true,
            "Kostenlose Übungssätze für Lesen, Hören, Schreiben und Sprechen im Goethe-Format.",
            "Anderes Format, gleiches Niveau: gutes Zusatztraining für Lesen und Hören, wenn die telc-Sätze verbraucht sind. Nicht als telc-Ergebnis eintragen.",
            [("Seite beim Goethe-Institut", "https://www.goethe.de/de/spr/prf/ueb/pb2.html")]),
        new("dw-nachrichten", MaterialKind.Practice, "Hörverstehen", "Langsam gesprochene Nachrichten", "Deutsche Welle", true,
            "Täglich aktuelle Nachrichten, langsam gesprochen, mit vollständigem Text – Niveau B2.",
            "Genau das Format von Hörverstehen Teil 1. Täglich eine Folge: einmal ohne Text hören, Kern jeder Meldung notieren, dann mit dem Text vergleichen.",
            [("Bei DW Deutsch lernen", "https://learngerman.dw.com/de/langsam-gesprochene-nachrichten/s-60040332")]),
    ];

    public static IEnumerable<TelcMaterial> MockExams => All.Where(m => m.Kind == MaterialKind.MockExam);

    /// <summary>Mock exams whose points may enter the telc B2 forecast: B2 only, never the B1 warm-up.</summary>
    public static IEnumerable<TelcMaterial> B2MockExams => MockExams.Where(m => m.Exam == TelcBlueprint.Name);

    public static TelcMaterial? Find(string id) => All.FirstOrDefault(m => m.Id == id);

    /// <summary>The task id under which a result entered from a paper mock exam is stored.</summary>
    public const string ExternalPrefix = "extern:";

    public static string ExternalTaskId(string materialId) => ExternalPrefix + materialId;

    public static bool IsExternal(string taskId) => taskId.StartsWith(ExternalPrefix, StringComparison.Ordinal);

    /// <summary>The material behind a stored external task id, or null for Lotse's own tasks.</summary>
    public static TelcMaterial? FromTaskId(string taskId) => IsExternal(taskId) ? Find(taskId[ExternalPrefix.Length..]) : null;
}
