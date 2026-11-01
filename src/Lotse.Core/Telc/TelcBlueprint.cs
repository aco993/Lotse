namespace Lotse.Core.Telc;

/// <summary>The twelve scored parts of telc Deutsch B2, in exam order.</summary>
public enum TelcPart
{
    Lesen1,
    Lesen2,
    Lesen3,
    Sprachbausteine1,
    Sprachbausteine2,
    Hoeren1,
    Hoeren2,
    Hoeren3,
    Schreiben,
    Sprechen1,
    Sprechen2,
    Sprechen3,
}

public enum TelcSubtest
{
    Leseverstehen,
    Sprachbausteine,
    Hoerverstehen,
    SchriftlicherAusdruck,
    MuendlicherAusdruck,
}

/// <param name="Items">Scored items; production parts count as one.</param>
/// <param name="MaxPoints">What the part is worth in the 300-point total.</param>
/// <param name="Minutes">Time the part gets on its own when practised; null where it shares a block (see <see cref="TelcBlueprint.ReadingAndLanguageMinutes"/>).</param>
public sealed record TelcPartInfo(TelcPart Part, TelcSubtest Subtest, string Title, int Items, double MaxPoints, int? Minutes, string Format, string Tip);

/// <summary>
/// telc Deutsch B2 as the official Übungstest 1 (telc gGmbH, 2021 edition, the published model test) describes it:
/// twelve scored parts, a written exam of 225 points and an oral exam of 75. The two pass marks are separate and
/// each is 60 %; inside the written exam the subtests are added up, so a strong Leseverstehen carries a weak
/// Schriftlicher Ausdruck. Encoded once so the practice pages, the simulation, the readiness estimate and the plan
/// all count the same way.
/// </summary>
public static class TelcBlueprint
{
    public const string Name = "telc Deutsch B2";
    public const string Source = "telc Deutsch B2, Übungstest 1 (Modelltest), telc gGmbH 2021";

    public const double WrittenMax = 225;
    public const double WrittenPass = 135;
    public const double OralMax = 75;
    public const double OralPass = 45;
    public const double TotalMax = WrittenMax + OralMax;

    /// <summary>Leseverstehen and Sprachbausteine are one block of 90 minutes; the candidate splits it.</summary>
    public const int ReadingAndLanguageMinutes = 90;
    public const int ListeningMinutes = 20;
    public const int WritingMinutes = 30;
    public const int OralPreparationMinutes = 20;
    public const int WritingMinWords = 150;
    /// <summary>Teil 1 of the oral exam: each candidate talks for about one and a half minutes.</summary>
    public const int ExperienceSeconds = 90;
    /// <summary>Teil 2: the summary of the text before the discussion, at most one minute.</summary>
    public const int TextSummarySeconds = 60;

    public static readonly IReadOnlyList<TelcPartInfo> Parts =
    [
        new(TelcPart.Lesen1, TelcSubtest.Leseverstehen, "Leseverstehen Teil 1", 5, 25, 20,
            "Zehn Überschriften, fünf kurze Texte: Welche Überschrift passt zu welchem Text? Fünf Überschriften bleiben übrig.",
            "Erst alle Texte überfliegen und jeweils den Kern in drei Wörtern notieren, dann die Überschriften. Überschriften mit einem Wort aus dem Text sind oft die Falle."),
        new(TelcPart.Lesen2, TelcSubtest.Leseverstehen, "Leseverstehen Teil 2", 5, 25, 25,
            "Ein längerer Zeitungs- oder Magazintext, fünf Multiple-Choice-Aufgaben (a, b, c).",
            "Die Aufgaben folgen dem Text. Die richtige Lösung sagt dasselbe mit anderen Worten; wörtlich übernommene Ausdrücke stehen gern in der falschen Lösung."),
        new(TelcPart.Lesen3, TelcSubtest.Leseverstehen, "Leseverstehen Teil 3", 10, 25, 25,
            "Zehn Situationen, zwölf Anzeigen: Welche Anzeige passt? Jede Anzeige höchstens einmal, manchmal passt keine (x).",
            "Pro Situation die zwei oder drei harten Bedingungen unterstreichen (Ort, Zeit, Preis, Zielgruppe). Eine Anzeige, die nur eine davon verletzt, passt nicht - dann ist x richtig."),
        new(TelcPart.Sprachbausteine1, TelcSubtest.Sprachbausteine, "Sprachbausteine Teil 1", 10, 15, 10,
            "Ein Brief oder eine E-Mail mit zehn Lücken, je drei Möglichkeiten (a, b, c): vor allem Grammatik.",
            "Vor der Lücke auf das Verb oder die Präposition achten: Sie entscheidet über Kasus, Präposition und Konnektor. Den ganzen Satz lesen, nicht nur die Lücke."),
        new(TelcPart.Sprachbausteine2, TelcSubtest.Sprachbausteine, "Sprachbausteine Teil 2", 10, 15, 10,
            "Ein Text mit zehn Lücken und ein Kasten mit fünfzehn Wörtern (a–o): vor allem Wortschatz, Präpositionen, Konnektoren. Jedes Wort höchstens einmal.",
            "Erst die Wortart der Lücke bestimmen (Präposition? Konnektor? Nomen?), dann nur unter den passenden Kästchenwörtern suchen. Sichere Lücken zuerst - das verkleinert die Auswahl."),
        new(TelcPart.Hoeren1, TelcSubtest.Hoerverstehen, "Hörverstehen Teil 1", 5, 25, null,
            "Eine kurze Nachrichtensendung mit fünf Meldungen, zu jeder eine Aussage: richtig oder falsch? Nur einmal hören.",
            "Die fünf Aussagen vorher lesen und die Schlüsselwörter markieren - Zahl, Ort, wer was tut. Während des Hörens sofort entscheiden."),
        new(TelcPart.Hoeren2, TelcSubtest.Hoerverstehen, "Hörverstehen Teil 2", 10, 25, null,
            "Ein Radiointerview, zehn Aussagen: richtig oder falsch? Nur einmal hören.",
            "Die Aussagen folgen dem Gespräch. Achte darauf, WER etwas sagt und ob es eine Tatsache oder ein Wunsch ist - genau dort steckt meist das „falsch“."),
        new(TelcPart.Hoeren3, TelcSubtest.Hoerverstehen, "Hörverstehen Teil 3", 5, 25, null,
            "Fünf kurze Ansagen (Telefon, Radio, Lautsprecher), zu jeder eine Aussage: richtig oder falsch? Nur einmal hören.",
            "Ansagen enthalten oft eine Korrektur („nicht um 9, sondern um 10 Uhr“). Die zweite Angabe zählt."),
        new(TelcPart.Schreiben, TelcSubtest.SchriftlicherAusdruck, "Schriftlicher Ausdruck", 1, 45, WritingMinutes,
            "Aufgabe A oder B wählen, eine halbformelle oder formelle E-Mail schreiben: drei von vier Leitpunkten oder zwei und ein eigener Aspekt, mindestens 150 Wörter.",
            "Betreff, Anrede, Einleitung und Schlussformel sind Pflicht für ein A. Die Leitpunkte nicht nur abhaken: jeder braucht mehr als einen Satz und eine Verbindung zum nächsten."),
        new(TelcPart.Sprechen1, TelcSubtest.MuendlicherAusdruck, "Mündlicher Ausdruck Teil 1", 1, 25, null,
            "Über Erfahrungen sprechen: Du erzählst etwa 1½ Minuten zu einem vorbereiteten Thema, dann stellt dein Partner Fragen - und du ihm.",
            "Die Themen kennst du vorher: Bereite alle sieben vor. Ein roter Faden (was, wann, warum wichtig) plus ein konkretes Detail wirkt mehr als eine Liste. Stell deinem Partner danach echte Fragen."),
        new(TelcPart.Sprechen2, TelcSubtest.MuendlicherAusdruck, "Mündlicher Ausdruck Teil 2", 1, 25, null,
            "Diskussion: Einen kurzen Zeitungstext hast du in der Vorbereitung gelesen. Du fasst ihn kurz zusammen, dann diskutiert ihr - Meinung, Begründung, eigene Erfahrung, mögliche Lösungen.",
            "Nicht auf Sieg diskutieren: Auf den Partner eingehen („Da haben Sie recht, aber …“), nachfragen und am Ende eine Lösung vorschlagen bringt die Punkte für Aufgabenbewältigung."),
        new(TelcPart.Sprechen3, TelcSubtest.MuendlicherAusdruck, "Mündlicher Ausdruck Teil 3", 1, 25, null,
            "Gemeinsam etwas planen: Ihr bekommt eine Situation (eine Feier, eine Reise, ein Projekt) und plant zusammen - Vorschläge machen, annehmen, ablehnen, sich einigen.",
            "Vorschlag → Reaktion → Begründung → nächster Punkt. Wer nur Vorschläge macht und nicht reagiert, verliert Punkte. Zum Schluss das Ergebnis zusammenfassen."),
    ];

    public static TelcPartInfo Info(TelcPart part) => Parts[(int)part];

    public static bool IsWritten(TelcPart part) => part < TelcPart.Sprechen1;

    public static bool IsObjective(TelcPart part) => part < TelcPart.Schreiben;

    public static bool IsListening(TelcPart part) => part is TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3;

    /// <summary>Points one right answer is worth: 25 points over five items is 5, over ten 2.5; a Sprachbaustein 1.5.</summary>
    public static double PointsPerItem(TelcPart part) => IsObjective(part) ? Info(part).MaxPoints / Info(part).Items : 0;

    /// <summary>Points of an objective part with <paramref name="correct"/> right answers.</summary>
    public static double ObjectivePoints(TelcPart part, int correct)
        => Math.Clamp(correct, 0, Info(part).Items) * PointsPerItem(part);

    public static string SubtestTitle(TelcSubtest s) => s switch
    {
        TelcSubtest.Leseverstehen => "Leseverstehen",
        TelcSubtest.Sprachbausteine => "Sprachbausteine",
        TelcSubtest.Hoerverstehen => "Hörverstehen",
        TelcSubtest.SchriftlicherAusdruck => "Schriftlicher Ausdruck",
        _ => "Mündlicher Ausdruck",
    };

    public static double SubtestMax(TelcSubtest s) => Parts.Where(p => p.Subtest == s).Sum(p => p.MaxPoints);

    /// <summary>The final grade; only meaningful once both pass marks are reached, which the caller checks.</summary>
    public static string Grade(double total) => total switch
    {
        >= 270 => "sehr gut",
        >= 240 => "gut",
        >= 210 => "befriedigend",
        >= 180 => "ausreichend",
        _ => "nicht bestanden",
    };
}

/// <summary>A full or partial result in telc points. Missing parts count as not taken, never as zero.</summary>
public sealed record TelcResult(IReadOnlyDictionary<TelcPart, double> Points)
{
    public bool Has(TelcPart part) => Points.ContainsKey(part);

    public double Written => Points.Where(p => TelcBlueprint.IsWritten(p.Key)).Sum(p => p.Value);
    public double Oral => Points.Where(p => !TelcBlueprint.IsWritten(p.Key)).Sum(p => p.Value);
    public double Total => Written + Oral;

    public bool WrittenComplete => Enum.GetValues<TelcPart>().Where(TelcBlueprint.IsWritten).All(Has);
    public bool OralComplete => Enum.GetValues<TelcPart>().Where(p => !TelcBlueprint.IsWritten(p)).All(Has);

    public bool WrittenPassed => WrittenComplete && Written >= TelcBlueprint.WrittenPass;
    public bool OralPassed => OralComplete && Oral >= TelcBlueprint.OralPass;
    public bool Passed => WrittenPassed && OralPassed;

    /// <summary>The certificate grade, or "nicht bestanden" when either pass mark is missed, or null while a part is missing.</summary>
    public string? Grade => !WrittenComplete || !OralComplete ? null : Passed ? TelcBlueprint.Grade(Total) : "nicht bestanden";

    public double Subtest(TelcSubtest s) => Points.Where(p => TelcBlueprint.Info(p.Key).Subtest == s).Sum(p => p.Value);
}
