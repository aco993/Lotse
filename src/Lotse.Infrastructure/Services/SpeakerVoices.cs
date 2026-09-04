namespace Lotse.Infrastructure.Services;

/// <summary>
/// Picks a voice for a story line from its speaker label. The labels come from the lesson JSON and often carry an
/// aside - "Sabine (im Stand-up)", "Herr Krüger (Mail)" - so the bracket is stripped before anything is matched.
///
/// Anything not recognised keeps the default voice. That is deliberate: a new character should sound slightly off
/// (one voice for two roles) rather than be guessed wrong from a name, and institutions like "IT-Hotline" or
/// "Radiobeitrag" have no gender to get right in the first place. Adding a character here is a one-line change.
/// </summary>
public static class SpeakerVoices
{
    private static readonly HashSet<string> Female = new(StringComparer.OrdinalIgnoreCase)
    {
        // Recurring cast
        "Sabine", "Lena", "Ana", "Mira",
        // Roles that name themselves in the feminine
        "Apothekerin", "Prüferin", "Sachbearbeiterin", "Deine Frau",
    };

    private static readonly HashSet<string> Male = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jonas", "Tarek", "Yusuf",
        "Du",   // the learner
    };

    public static SpeechVoice For(string? speaker)
    {
        var name = Normalize(speaker);
        if (name.Length == 0) return SpeechVoice.Male;

        // German courtesy titles are the most reliable signal there is, so they win over any name list.
        if (name.StartsWith("Frau ", StringComparison.OrdinalIgnoreCase)) return SpeechVoice.Female;
        if (name.StartsWith("Herr ", StringComparison.OrdinalIgnoreCase)) return SpeechVoice.Male;

        if (Female.Contains(name)) return SpeechVoice.Female;
        if (Male.Contains(name)) return SpeechVoice.Male;

        // "Sabine Berger" - the full label is not listed, the first name is.
        var first = name.Split(' ', 2)[0];
        if (Female.Contains(first)) return SpeechVoice.Female;
        if (Male.Contains(first)) return SpeechVoice.Male;

        return SpeechVoice.Male;
    }

    private static string Normalize(string? speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return "";
        var name = speaker.AsSpan();
        var bracket = name.IndexOf('(');
        if (bracket >= 0) name = name[..bracket];
        // Span.TrimEnd takes a set of characters as a span, not as params - hence ",:-".AsSpan().
        return name.Trim().TrimEnd(",:-".AsSpan()).Trim().ToString();
    }
}
