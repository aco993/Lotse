namespace Lotse.Core.Exam;

public sealed record ExamPart(string Name, string TaskFormat, int Items, string Tip);

public sealed record ExamModule(string Name, int Minutes, IReadOnlyList<ExamPart> Parts, string PassRule);

/// <summary>
/// Structure of the Goethe-Zertifikat B2 (format valid since 2019). Encoded as data so the exam simulator, the
/// readiness report and the tips all speak about the same thing. telc Deutsch B2 is close enough that the same
/// preparation transfers; the differences are noted in <see cref="TelcDifferences"/>.
/// </summary>
public static class ExamBlueprint
{
    public const string Name = "Goethe-Zertifikat B2";

    public static readonly IReadOnlyList<ExamModule> Modules =
    [
        new("Lesen", 65,
        [
            new("Teil 1", "Vier Forumsbeiträge – neun Aussagen den Personen zuordnen (Meinungen erkennen).", 9, "Erst die Aussagen lesen, dann die Texte. Achte auf Umschreibungen, nicht auf gleiche Wörter."),
            new("Teil 2", "Lückentext (Zeitungsartikel) – sechs Sätze aus einer Liste einsetzen.", 6, "Verweiswörter beachten: „dieser“, „dabei“, „deshalb“ verraten den Anschluss."),
            new("Teil 3", "Zeitungsbericht – sechs Multiple-Choice-Fragen.", 6, "Die Reihenfolge der Fragen folgt dem Text. Distraktoren sind oft wörtlich aus dem Text, aber falsch bezogen."),
            new("Teil 4", "Meinungen zu einem Thema – sechs Aussagen zuordnen.", 6, "Pro/Contra-Signalwörter markieren."),
            new("Teil 5", "Regelwerk/Vorschriften – vier Überschriften bzw. Abschnitte zuordnen.", 4, "Kurz, formell, oft Nominalstil – Schlüsselwörter der Absätze abgleichen."),
        ], "Mindestens 60 % (18 von 30 Punkten)."),
        new("Hören", 40,
        [
            new("Teil 1", "Fünf kurze Alltagstexte – je eine Richtig/Falsch- und eine Multiple-Choice-Aufgabe (einmal hören).", 10, "Vor dem Hören die Aufgaben lesen und das Szenario antizipieren."),
            new("Teil 2", "Interview/Vortrag – sechs Multiple-Choice-Fragen (zweimal hören).", 6, "Beim ersten Hören grob, beim zweiten gezielt kontrollieren."),
            new("Teil 3", "Diskussion mit drei Personen – wer sagt was? (einmal hören)", 6, "Stimmen früh unterscheiden: Moderator, Pro, Contra."),
            new("Teil 4", "Vortrag/Radiobeitrag – acht Multiple-Choice-Fragen (zweimal hören).", 8, "Zahlen, Ursachen und Folgen sind typische Fragen."),
        ], "Mindestens 60 % (18 von 30 Punkten)."),
        new("Schreiben", 75,
        [
            new("Teil 1", "Forumsbeitrag (ca. 150 Wörter) zu einem gesellschaftlichen Thema mit vier Leitpunkten.", 1, "Alle vier Punkte behandeln, Meinung begründen, Absätze und Konnektoren nutzen. Ca. 50 Minuten."),
            new("Teil 2", "Formelle Nachricht (ca. 100 Wörter) an Vorgesetzte/Kollegen – z. B. Absage, Bitte, Vorschlag.", 1, "Register: Sie-Form, Anrede/Gruß, höfliche Konjunktiv-II-Bitten. Ca. 25 Minuten."),
        ], "Bewertet nach Erfüllung, Kohärenz, Wortschatz, Strukturen. Mindestens 60 %."),
        new("Sprechen", 15,
        [
            new("Teil 1", "Vortrag (ca. 4 Minuten) zu einem von zwei Themen mit drei Leitpunkten, danach Rückfragen.", 1, "Feste Struktur: Einleitung, eigene Erfahrung, Situation im Heimatland, Vor-/Nachteile, Fazit."),
            new("Teil 2", "Diskussion (ca. 5 Minuten) mit Partner zu einer These – Meinung äußern, auf Argumente eingehen.", 1, "Redemittel für Zustimmung, Widerspruch, Nachfrage und Kompromiss parat haben."),
        ], "Bewertet nach Erfüllung, Interaktion, Kohärenz, Wortschatz, Strukturen, Aussprache. Mindestens 60 %."),
    ];

    public const string TelcDifferences =
        "telc Deutsch B2: zusätzlich „Sprachbausteine“ (Grammatik/Wortschatz-Lückentexte), Schreiben ist ein halbformeller Brief zu einer von zwei Aufgaben, "
        + "Sprechen hat drei Teile (Präsentation, Diskussion, gemeinsame Planung). Die B2-Kompetenzen sind identisch – wer Goethe besteht, besteht telc.";

    public static readonly IReadOnlyList<string> WritingCriteria = ["Erfüllung der Aufgabe", "Kohärenz und Aufbau", "Wortschatz", "Strukturen (Grammatik)"];
    public static readonly IReadOnlyList<string> SpeakingCriteria = ["Erfüllung der Aufgabe", "Interaktion", "Kohärenz", "Wortschatz", "Strukturen", "Aussprache"];
}
