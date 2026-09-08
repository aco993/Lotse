using System.Text.RegularExpressions;

namespace Lotse.Core.Exam;

/// <summary>One stretch of a listening text spoken by one person; <see cref="Speaker"/> is null for a narrator or a monologue.</summary>
public sealed record SpeakerTurn(string? Speaker, string Text);

/// <summary>
/// Splits a listening text into speaker turns so that a conversation can be read with one voice per person and
/// without the labels ("Lena:") being spoken. The content files write conversations inline - "Lena: … – Markus: …" -
/// or one turn per line; the generator is told to use lines. Anything that does not look like a conversation of
/// at least two people comes back as a single unlabelled turn, so a monologue is never chopped up by a stray colon.
/// </summary>
public static partial class SpeakerTurns
{
    // A label sits at the start, after a line break or after a spaced dash: one to three capitalised words (a title
    // dot allowed, "Dr. Keller"), a colon, a space. Lower-case words in between rule out "Information zu Ihrer Reise:".
    [GeneratedRegex(@"(?:^|\n|\s[–—-]\s)[ \t]*(?<name>[A-ZÄÖÜ][\wäöüß.]*(?: [A-ZÄÖÜ][\wäöüß.]*){0,2}):\s", RegexOptions.CultureInvariant)]
    private static partial Regex Label();

    // Colon-headed lines that are structure, not people: an announcement's "Montag: 8 bis 12 Uhr" has several.
    private static readonly HashSet<string> NotSpeakers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag", "Wochenende",
        "Achtung", "Hinweis", "Wichtig", "Information", "Betreff", "Thema", "Ort", "Datum", "Uhrzeit", "Termin", "Zeit",
        "Kosten", "Preis", "Adresse", "Telefon", "Öffnungszeiten", "Sprechzeiten", "Beispiel", "Frage", "Antwort", "Fazit",
        "Erstens", "Zweitens", "Drittens", "Vorteil", "Nachteil", "Vorteile", "Nachteile", "Tipp", "Ergebnis", "Bilanz",
    };

    public static IReadOnlyList<SpeakerTurn> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var whole = new SpeakerTurn(null, text.Trim());

        var labels = Label().Matches(text).Where(m => !NotSpeakers.Contains(m.Groups["name"].Value)).ToList();
        if (labels.Count < 3) return [whole];

        var turns = new List<SpeakerTurn>();
        var lead = text[..labels[0].Index].Trim();
        if (lead.Length > 0) turns.Add(new SpeakerTurn(null, lead));
        for (var i = 0; i < labels.Count; i++)
        {
            var start = labels[i].Index + labels[i].Length;
            var end = i + 1 < labels.Count ? labels[i + 1].Index : text.Length;
            var spoken = text[start..end].Trim();
            if (spoken.Length > 0) turns.Add(new SpeakerTurn(labels[i].Groups["name"].Value, spoken));
        }

        var people = turns.Select(t => t.Speaker).Where(s => s is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return people >= 2 ? turns : [whole];
    }

    /// <summary>True when the text is a conversation between at least two people.</summary>
    public static bool IsDialogue(string? text) => Parse(text).Count(t => t.Speaker is not null) >= 2;
}
