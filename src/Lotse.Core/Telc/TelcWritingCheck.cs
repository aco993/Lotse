using System.Text.RegularExpressions;

namespace Lotse.Core.Telc;

/// <summary>
/// What can be checked in a telc email without a rater: length, the three features of a (semi-)formal email the
/// rules name (Betreff, Anrede, Schlussformel), whether the Leitpunkte seem to be addressed, and whether the text
/// is linked or just a list. The results become caps on criteria I and II - telc's own rules make them mechanical
/// ("A wird nicht gegeben, wenn … Betreffzeile, Anrede, Schlussformel fehlen"; "B wird nicht gegeben, wenn die
/// Leitpunkte linear ohne logische Verknüpfung aufgelistet sind") - and hints for the learner. Keyword coverage is
/// a hint only: it caps nothing, because mentioning a word is not the same as treating a point.
/// </summary>
public sealed partial record TelcWritingCheck(
    int Words,
    bool HasBetreff,
    bool HasAnrede,
    bool HasSchluss,
    int Connectors,
    IReadOnlyList<string> ConnectorsUsed,
    IReadOnlyList<bool> LeitpunkteMentioned,
    bool InformalSlip)
{
    /// <summary>Distinct linking words below which a 150-word email reads as a list.</summary>
    public const int MinConnectors = 4;

    public bool LongEnough => Words >= TelcBlueprint.WritingMinWords;
    public bool FormComplete => HasBetreff && HasAnrede && HasSchluss;
    public bool Linear => Connectors < MinConnectors;
    public int LeitpunkteHit => LeitpunkteMentioned.Count(m => m);

    /// <summary>Best band criterion II can get: no A without the email features, no B for a linear list.</summary>
    public TelcBand MaxGestaltung => Linear ? TelcBand.C : !FormComplete || InformalSlip ? TelcBand.B : TelcBand.A;

    /// <summary>Criterion I is the rater's call; a text far below the length cannot treat three points properly.</summary>
    public TelcBand MaxAufgabe => Words < TelcBlueprint.WritingMinWords / 2 ? TelcBand.C : TelcBand.A;

    /// <summary>One line per finding, in the order a rater would mention them; empty when nothing stands out.</summary>
    public IReadOnlyList<string> Hints(IReadOnlyList<TelcLeitpunkt>? leitpunkte = null)
    {
        var hints = new List<string>();
        if (!LongEnough) hints.Add($"Nur {Words} Wörter - verlangt sind mindestens {TelcBlueprint.WritingMinWords}.");
        if (!HasBetreff) hints.Add("Keine Betreffzeile („Betreff: …“) - ohne sie gibt es in Kriterium II kein A.");
        if (!HasAnrede) hints.Add("Keine Anrede („Sehr geehrte Damen und Herren,“) - ohne sie gibt es in Kriterium II kein A.");
        if (!HasSchluss) hints.Add("Keine Schlussformel („Mit freundlichen Grüßen“) - ohne sie gibt es in Kriterium II kein A.");
        if (InformalSlip) hints.Add("Register schwankt: „du/dich/dir“ oder „Hallo“ in einer formellen E-Mail kostet in Kriterium II das A.");
        if (Linear) hints.Add($"Nur {Connectors} verschiedene Verknüpfungswörter - eine Liste von Leitpunkten bekommt in Kriterium II höchstens C. Ziel: mindestens {MinConnectors} (deshalb, außerdem, allerdings, da, damit …).");
        if (leitpunkte is not null)
        {
            var missing = leitpunkte.Where((l, i) => i < LeitpunkteMentioned.Count && !LeitpunkteMentioned[i]).Select(l => l.Text).ToList();
            if (LeitpunkteHit < 3 && missing.Count > 0)
                hints.Add($"Nur {LeitpunkteHit} Leitpunkte erkennbar. Nicht gefunden: {string.Join(" · ", missing)} - drei Punkte (oder zwei und ein eigener Aspekt) braucht es für ein A in Kriterium I.");
        }
        return hints;
    }

    // Connectors and linking adverbs that make a B2 text hang together. Multi-word ones are matched as phrases. Bare
    // "statt" and "darüber" are left out: they are mostly the particle of "stattfinden" or a pronominal adverb
    // ("sich darüber freuen"), and counting them let a list of short sentences pass as linked.
    private static readonly string[] LinkWords =
    [
        "deshalb", "deswegen", "daher", "darum", "außerdem", "zudem", "darüber hinaus", "ferner", "allerdings",
        "jedoch", "trotzdem", "dennoch", "obwohl", "obgleich", "weil", "da", "denn", "damit", "um", "sodass", "so dass",
        "falls", "wenn", "sofern", "während", "wohingegen", "nachdem", "bevor", "sobald", "seitdem", "zunächst", "anschließend",
        "schließlich", "abschließend", "erstens", "zweitens", "einerseits", "andererseits", "zum einen", "zum anderen",
        "aus diesem grund", "infolgedessen", "folglich", "stattdessen", "insbesondere", "vor allem", "zum beispiel", "beispielsweise",
        "nicht nur", "sowohl", "weder", "entweder", "dass", "ob", "indem", "ohne dass", "trotz", "wegen", "aufgrund",
        "dabei", "dadurch", "daraufhin", "hinzu kommt", "im gegensatz dazu", "letztlich", "anstatt",
    ];

    [GeneratedRegex(@"^\s*(betreff|betr\.)\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BetreffLine();

    [GeneratedRegex(@"^\s*(sehr geehrte[rs]?|liebe[rs]?|guten tag|hallo|hi)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AnredeLine();

    [GeneratedRegex(@"(mit freundlichen grüßen|mit freundlichem gruß|freundliche grüße|viele grüße|beste grüße|herzliche grüße|liebe grüße|mit besten grüßen|hochachtungsvoll)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchlussFormula();

    [GeneratedRegex(@"\b(du|dich|dir|dein|deine|deinen|deinem|deiner|euch|euer|eure)\b|^\s*hallo\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex InformalMarker();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    public static int CountWords(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : WordPattern().Count(text);

    public static TelcWritingCheck Analyze(string text, TelcWritingTask? task = null)
    {
        text ??= "";
        var lower = " " + string.Join(' ', WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value)) + " ";
        var used = LinkWords.Distinct().Where(w => lower.Contains($" {w} ", StringComparison.Ordinal)).ToList();
        // "um" only counts as a connector when an "zu"-infinitive follows somewhere; "da" is ambiguous but common enough in B2 emails.
        if (used.Contains("um") && !lower.Contains(" zu ", StringComparison.Ordinal)) used.Remove("um");

        var mentioned = task?.Leitpunkte.Select(l => l.Keywords.Any(k => lower.Contains(" " + k.ToLowerInvariant(), StringComparison.Ordinal))).ToList()
                        ?? [];
        var informal = task is { Formal: true } && InformalMarker().IsMatch(text);

        return new TelcWritingCheck(
            CountWords(text),
            BetreffLine().IsMatch(text),
            AnredeLine().IsMatch(text),
            SchlussFormula().IsMatch(text),
            used.Count,
            used,
            mentioned,
            informal);
    }
}
