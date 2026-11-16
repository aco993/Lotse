using System.Globalization;
using Lotse.Core.Model;

namespace Lotse.Core.Engine;

public enum WordHeard
{
    /// <summary>The recogniser wrote this word (or a spelling of it that sounds the same).</summary>
    Heard,
    /// <summary>The recogniser wrote a different word in this place - often the word the learner's sound resembles.</summary>
    Misheard,
    /// <summary>Nothing in the transcript lines up with this word: swallowed, skipped, or too unclear to transcribe.</summary>
    Missing,
}

public sealed record WordResult(string Word, WordHeard Status, string? HeardAs = null);

public sealed record ReadAloudResult(string Target, string Transcript, IReadOnlyList<WordResult> Words, IReadOnlyList<string> Extra)
{
    /// <summary>Share of target words the recogniser wrote as said. Words the learner added do not lower it; they are listed in <see cref="Extra"/>.</summary>
    public double Score => Words.Count == 0 ? 0 : (double)Words.Count(w => w.Status == WordHeard.Heard) / Words.Count;

    public IEnumerable<WordResult> ToPractise => Words.Where(w => w.Status != WordHeard.Heard);
}

/// <summary>
/// Read a sentence aloud, let the browser's speech recogniser transcribe it, and line the transcript up word by word
/// with the sentence. What this measures is intelligibility: whether a general-purpose German recogniser understands
/// each word as the word that was meant. It is not a phonetic analysis - an accent the recogniser copes with scores
/// as heard, and a recogniser's own slip scores as misheard. Within that limit it is the free half of a pronunciation
/// check: it needs no account and no key, and it shows which word to say again.
/// </summary>
public static class ReadAloud
{
    /// <summary>Spoken Redemittel - the phrases the oral exam is built on. E-mail formulas are written, not said.</summary>
    public static readonly IReadOnlyList<string> SpokenNodes = ["RM.MEINUNG_ARGUMENTATION", "RM.DISKUSSION", "RM.VORTRAG_STRUKTUR", "RM.MEETING_BERUF", "RM.BESCHWERDE_BITTE"];

    /// <summary>
    /// Whole sentences to read: each spoken Redemittel cloze with its first accepted answer filled in. A sentence that
    /// trails off ("…") keeps its beginning, which is the phrase being practised.
    /// </summary>
    public static IReadOnlyList<(string Id, string NodeId, string Sentence)> Sentences(ContentCatalog catalog)
        => catalog.Exercises
            .Where(e => e.Type == ExerciseType.Cloze && SpokenNodes.Contains(e.NodeId) && e.Answers.Count > 0 && e.Prompt.Contains("___", StringComparison.Ordinal))
            .Select(e => (e.Id, e.NodeId, Sentence: Fill(e.Prompt, e.Answers[0])))
            .Where(s => Tokens(s.Sentence).Count >= 4)
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    private static string Fill(string prompt, string answer)
    {
        var i = prompt.IndexOf("___", StringComparison.Ordinal);
        var s = prompt[..i] + answer + prompt[(i + 3)..];
        // "(der Bus)" style base-form hints belong to the cloze, not to the sentence that is read.
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\([^)]*\)", "");
        return s.Replace("…", "", StringComparison.Ordinal).Replace("...", "", StringComparison.Ordinal).Trim().TrimEnd(',', ' ');
    }

    public static ReadAloudResult Compare(string target, string transcript)
    {
        var want = Tokens(target);
        var got = Tokens(transcript);
        var keyW = want.Select(Key).ToArray();
        var keyG = got.Select(Key).ToArray();

        // Word-level edit distance. A substitution costs 1 like an insertion or deletion, so "fahre mit dem Bus" heard
        // as "fahre mit den Bus" lines "dem" up with "den" (misheard) instead of calling one missing and one extra.
        var n = want.Count;
        var m = got.Count;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (keyW[i - 1] == keyG[j - 1] ? 0 : 1));

        var words = new List<WordResult>();
        var extra = new List<string>();
        int a = n, b = m;
        while (a > 0 || b > 0)
        {
            if (a > 0 && b > 0 && d[a, b] == d[a - 1, b - 1] + (keyW[a - 1] == keyG[b - 1] ? 0 : 1))
            {
                words.Add(keyW[a - 1] == keyG[b - 1] ? new(want[a - 1], WordHeard.Heard) : new(want[a - 1], WordHeard.Misheard, got[b - 1]));
                a--; b--;
            }
            else if (a > 0 && d[a, b] == d[a - 1, b] + 1)
            {
                words.Add(new(want[a - 1], WordHeard.Missing));
                a--;
            }
            else
            {
                extra.Add(got[b - 1]);
                b--;
            }
        }
        words.Reverse();
        extra.Reverse();
        return new ReadAloudResult(target, transcript, words, extra);
    }

    /// <summary>Words as written, punctuation stripped from their edges; hyphenated compounds stay one word.</summary>
    public static IReadOnlyList<string> Tokens(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ',', ';', ':', '!', '?', '„', '“', '"', '\'', '(', ')', '–', '-', '…'))
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>
    /// What counts as the same word for a recogniser: case, ß/ss and umlaut spellings do not matter, and a few pairs
    /// that sound identical in German are one word here, because no recogniser can tell them apart from sound alone.
    /// </summary>
    private static string Key(string word)
    {
        var w = word.ToLower(CultureInfo.GetCultureInfo("de-DE"))
            .Replace("ß", "ss", StringComparison.Ordinal)
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal).Replace("ü", "ue", StringComparison.Ordinal);
        return Homophones.TryGetValue(w, out var same) ? same : w;
    }

    private static readonly Dictionary<string, string> Homophones = new(StringComparer.Ordinal)
    {
        ["dass"] = "das",
        ["seid"] = "seit",
        ["wider"] = "wieder",
        ["war"] = "wahr",
        ["meer"] = "mehr",
        ["statt"] = "stadt",
        ["fiel"] = "viel",
        ["leeren"] = "lehren",
        ["mahlen"] = "malen",
    };
}
