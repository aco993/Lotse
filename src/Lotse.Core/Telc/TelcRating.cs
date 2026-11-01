namespace Lotse.Core.Telc;

/// <summary>The four bands telc raters use for every production criterion.</summary>
public enum TelcBand
{
    A,
    B,
    C,
    D,
}

/// <summary>
/// Schriftlicher Ausdruck: three criteria, each A/B/C/D = 5/3/1/0, the sum times three - at most 45 points.
/// </summary>
public sealed record TelcWritingRating(TelcBand Aufgabe, TelcBand Gestaltung, TelcBand Richtigkeit, bool ThemaVerfehlt = false)
{
    public static int BandPoints(TelcBand b) => b switch { TelcBand.A => 5, TelcBand.B => 3, TelcBand.C => 1, _ => 0 };

    /// <summary>"Thema verfehlt" sets every criterion to D, whatever else was rated.</summary>
    public double Points => ThemaVerfehlt ? 0 : 3 * (BandPoints(Aufgabe) + BandPoints(Gestaltung) + BandPoints(Richtigkeit));

    public static readonly IReadOnlyList<(string Title, string Question)> Criteria =
    [
        ("I Aufgabenbewältigung", "Wie viele Leitpunkte sind jeweils mit mehr als einem Satz angemessen bearbeitet? 3 = A, 2 = B, 1 = C, 0 = D."),
        ("II Kommunikative Gestaltung", "Register, Verknüpfung, Wortschatzspektrum. Ohne Betreff, Anrede oder Schlussformel kein A; Leitpunkte nur aneinandergereiht: kein B."),
        ("III Formale Richtigkeit", "A: kaum Fehler. B: wenige systematische Fehler. C: mehrere systematische Fehler, aber verständlich. D: viele elementare Fehler."),
    ];

    /// <summary>
    /// Applies the caps the rules make mechanical: a rating above what the checked text allows comes down to the cap.
    /// Rater judgement (self or AI) decides within the caps, never above them.
    /// </summary>
    public TelcWritingRating Capped(TelcWritingCheck check) => this with
    {
        Aufgabe = Max(Aufgabe, check.MaxAufgabe),
        Gestaltung = Max(Gestaltung, check.MaxGestaltung),
    };

    /// <summary>The worse of the two bands (D is worse than A).</summary>
    private static TelcBand Max(TelcBand rated, TelcBand cap) => rated < cap ? cap : rated;
}

/// <summary>
/// One part of the oral exam: Ausdrucksfähigkeit, Aufgabenbewältigung and Formale Richtigkeit are worth 7/5/3/0,
/// Aussprache und Intonation 4/2/1/0 - 25 points per part, 75 for the three.
/// </summary>
public sealed record TelcSpeakingRating(TelcBand Ausdruck, TelcBand Aufgabe, TelcBand Richtigkeit, TelcBand Aussprache)
{
    public static int MainPoints(TelcBand b) => b switch { TelcBand.A => 7, TelcBand.B => 5, TelcBand.C => 3, _ => 0 };
    public static int PronunciationPoints(TelcBand b) => b switch { TelcBand.A => 4, TelcBand.B => 2, TelcBand.C => 1, _ => 0 };

    public double Points => MainPoints(Ausdruck) + MainPoints(Aufgabe) + MainPoints(Richtigkeit) + PronunciationPoints(Aussprache);

    public static readonly IReadOnlyList<(string Title, string Question)> Criteria =
    [
        ("Ausdrucksfähigkeit", "Passt die Ausdrucksweise zu Inhalt und Rolle, ist sie vielfältig, kommt die Absicht an? A voll · B im Großen und Ganzen · C kaum noch · D nicht."),
        ("Aufgabenbewältigung", "Gesprächsbeteiligung, Strategien (nachfragen, umschreiben, auf den Partner eingehen), Flüssigkeit."),
        ("Formale Richtigkeit", "Satzbau und Formen. A keine/sehr vereinzelte Fehler · B wenige, ohne die Absicht zu stören · C viele, die sie stören · D unverständlich."),
        ("Aussprache und Intonation", "A wie Standardsprache · B Abweichungen ohne Verständnisproblem · C erschwert das Verstehen · D stellenweise unverständlich."),
    ];
}
