using System.Globalization;
using System.Text;
using Lotse.Core.Model;

namespace Lotse.Core.Engine;

/// <summary>Result of checking a closed (typed / chosen) answer.</summary>
public sealed record CheckResult(
    Outcome Outcome,
    /// <summary>The accepted answer the learner's input was matched against (or the model answer when wrong).</summary>
    string Expected,
    /// <summary>Error codes for slips detected even when the answer was accepted (capitalisation, umlaut spelling, typo).</summary>
    IReadOnlyList<string> SlipCodes,
    string Feedback)
{
    public double Score => Outcome switch
    {
        Outcome.Correct => 1.0,
        Outcome.AlmostCorrect => 0.7,
        _ => 0.0,
    };
}

/// <summary>
/// Deterministic answer checking for closed exercise types. Tolerant where a human teacher would be tolerant
/// (punctuation, double spaces, ae/oe/ue for umlauts, one typo in a long word) but every tolerance is *recorded*
/// as a slip so the learner model still sees it.
/// </summary>
public static class AnswerChecker
{
    public const string SlipCapitalisation = "ORTH_GROSSSCHREIBUNG";
    public const string SlipUmlaut = "ORTH_UMLAUT";
    public const string SlipSharpS = "ORTH_SZ";
    public const string SlipTypo = "ORTH_TIPPFEHLER";
    public const string SlipMissingArticle = "ART_FEHLT";
    public const string SlipWrongArticle = "ART_GENUS";

    private static readonly string[] Articles = ["der", "die", "das"];

    /// <summary>Splits "die Frist" into ("die", "Frist"); returns (null, input) when there is no leading article.</summary>
    public static (string? Article, string Noun) SplitArticle(string s)
    {
        var idx = s.IndexOf(' ');
        if (idx <= 0) return (null, s);
        var head = s[..idx].ToLowerInvariant();
        return Articles.Contains(head) ? (head, s[(idx + 1)..]) : (null, s);
    }

    public static CheckResult Check(Exercise exercise, string? rawAnswer)
    {
        var answer = (rawAnswer ?? string.Empty).Trim();
        return exercise.Type switch
        {
            ExerciseType.MultipleChoice => CheckChoice(exercise, answer),
            ExerciseType.SpotError => CheckSpotError(exercise, answer),
            ExerciseType.WordOrder => CheckTyped(exercise, answer, allowTypo: false),
            ExerciseType.Vocab => CheckTyped(exercise, answer, allowTypo: true),
            ExerciseType.Cloze or ExerciseType.Transform or ExerciseType.Translate or ExerciseType.Dictation
                => CheckTyped(exercise, answer, allowTypo: true),
            _ => throw new InvalidOperationException($"{exercise.Type} wird nicht deterministisch geprüft."),
        };
    }

    private static CheckResult CheckChoice(Exercise exercise, string answer)
    {
        var expected = exercise.Options[exercise.CorrectIndex!.Value];
        var ok = int.TryParse(answer, out var idx) ? idx == exercise.CorrectIndex : string.Equals(answer, expected, StringComparison.Ordinal);
        return ok
            ? new CheckResult(Outcome.Correct, expected, [], "Richtig.")
            : new CheckResult(Outcome.Incorrect, expected, [], $"Richtig wäre: {expected}");
    }

    /// <summary>The learner tapped word <paramref name="answer"/> (index); the expected answer shows the corrected word.</summary>
    private static CheckResult CheckSpotError(Exercise exercise, string answer)
    {
        var wrongWord = exercise.Options[exercise.CorrectIndex!.Value];
        var expected = $"{wrongWord} → {exercise.Answers[0]}";
        var ok = int.TryParse(answer, out var idx) && idx == exercise.CorrectIndex;
        return ok
            ? new CheckResult(Outcome.Correct, expected, [], "Gefunden.")
            : new CheckResult(Outcome.Incorrect, expected, [], $"Der Fehler steckt in „{wrongWord}“ – richtig: {exercise.Answers[0]}");
    }

    private static CheckResult CheckTyped(Exercise exercise, string answer, bool allowTypo)
    {
        if (answer.Length == 0)
            return new CheckResult(Outcome.Incorrect, exercise.Answers[0], [], $"Keine Antwort. Richtig wäre: {exercise.Answers[0]}");

        var given = Normalize(answer, keepCase: true);

        // 1. exact (after whitespace/punctuation normalisation)
        foreach (var accepted in exercise.Answers)
        {
            if (string.Equals(given, Normalize(accepted, keepCase: true), StringComparison.Ordinal))
                return new CheckResult(Outcome.Correct, accepted, [], "Richtig.");
        }

        // 1b. vocabulary: the article is part of the answer. Missing article = the classic Serbian-speaker slip,
        //     wrong article = genuinely wrong but tagged precisely so the learner model sees a genus problem.
        if (exercise.Type == ExerciseType.Vocab)
        {
            foreach (var accepted in exercise.Answers)
            {
                var (art, noun) = SplitArticle(Normalize(accepted, keepCase: true));
                if (art is null) continue;
                var (givenArt, givenNoun) = SplitArticle(given);
                if (!string.Equals(noun, givenNoun, StringComparison.OrdinalIgnoreCase)) continue;
                if (givenArt is null)
                    return new CheckResult(Outcome.AlmostCorrect, accepted, [SlipMissingArticle], $"Richtig – aber immer mit Artikel lernen: {accepted}");
                return new CheckResult(Outcome.Incorrect, accepted, [SlipWrongArticle], $"Falscher Artikel: {accepted}");
            }
        }

        // 2. tolerant matches – accepted, but the slip is recorded
        foreach (var accepted in exercise.Answers)
        {
            var slips = new List<string>();
            var a = given;
            var b = Normalize(accepted, keepCase: true);

            if (!string.Equals(a, b, StringComparison.Ordinal) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                slips.Add(SlipCapitalisation);

            var aFold = FoldUmlauts(a).ToLowerInvariant();
            var bFold = FoldUmlauts(b).ToLowerInvariant();
            if (slips.Count == 0 && aFold == bFold)
            {
                if (a.Contains('ß') != b.Contains('ß')) slips.Add(SlipSharpS);
                else slips.Add(SlipUmlaut);
                if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase) && FoldUmlauts(a) != FoldUmlauts(b)) slips.Add(SlipCapitalisation);
            }

            if (slips.Count > 0)
                return new CheckResult(Outcome.AlmostCorrect, accepted, slips, $"Fast richtig – korrekt geschrieben: {accepted}");

            if (allowTypo && IsTypoOf(aFold, bFold))
                return new CheckResult(Outcome.AlmostCorrect, accepted, [SlipTypo], $"Fast richtig (Tippfehler?): {accepted}");
        }

        return new CheckResult(Outcome.Incorrect, exercise.Answers[0], [], $"Richtig wäre: {exercise.Answers[0]}");
    }

    /// <summary>Collapses whitespace, strips surrounding punctuation of each token and trailing sentence punctuation.</summary>
    public static string Normalize(string s, bool keepCase)
    {
        var tokens = s.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ',', '!', '?', ';', ':', '"', '„', '“', '”', '\'', '(', ')', '…', '–', '-'))
            .Where(t => t.Length > 0);
        var joined = string.Join(' ', tokens);
        return keepCase ? joined : joined.ToLowerInvariant();
    }

    public static string FoldUmlauts(string s) => s
        .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue")
        .Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue")
        .Replace("ß", "ss");

    /// <summary>One edit in a word of six or more letters counts as a typo; a whole sentence may contain exactly one such word.</summary>
    private static bool IsTypoOf(string given, string expected)
    {
        var g = given.Split(' ');
        var e = expected.Split(' ');
        if (g.Length != e.Length) return false;
        var typos = 0;
        for (var i = 0; i < g.Length; i++)
        {
            if (g[i] == e[i]) continue;
            if (e[i].Length < 6) return false;
            if (Levenshtein(g[i], e[i]) != 1) return false;
            typos++;
        }
        return typos == 1;
    }

    /// <summary>Optimal-string-alignment distance: insert, delete, substitute or swap two adjacent letters ("Besprechnug") each cost 1.</summary>
    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }

    /// <summary>Word count used for production tasks (min-words check).</summary>
    public static int CountWords(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

    public static string RemoveDiacritics(string s)
    {
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
