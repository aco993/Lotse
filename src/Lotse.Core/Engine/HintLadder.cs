using Lotse.Core.Model;

namespace Lotse.Core.Engine;

/// <summary>One rung of the hint ladder: what it is called on the button and what it says.</summary>
public sealed record HintStep(int Level, string Label, string Text);

/// <summary>
/// Hints that lead to the answer without giving it away - the guardrail that separates a tutor from an answer
/// machine. In a field experiment with nearly a thousand students, unrestricted GPT-4 access during practice
/// lowered later exam scores, and a tutor that gave hints but never solutions removed most of that harm (Bastani et
/// al., PNAS 2025). So help comes in rungs, each one costing the item its "Good" grade (any hint makes the review
/// <see cref="ReviewGrade.Hard"/>), and no rung ever contains an accepted answer:
/// <list type="number">
/// <item><b>Thema</b> - which rule is being tested, in plain words.</item>
/// <item><b>Regel</b> - the contrastive note or the author's hint: why a Serbian (or English) speaker gets it wrong.</item>
/// <item><b>Anfang</b> - the shape of the answer: first letters and length, one wrong option struck out, the first chunk.</item>
/// </list>
/// The AI tutor may add a Socratic question on top; it passes through <see cref="Leaks"/> like every rung here.
/// </summary>
public static class HintLadder
{
    public const int MaxLevel = 3;

    /// <summary>The rungs available for this exercise, lowest first. Empty for types where a hint makes no sense.</summary>
    public static IReadOnlyList<HintStep> For(Exercise exercise, SkillNode? node, HelperLanguage lang = HelperLanguage.Serbian)
    {
        if (exercise.Type is ExerciseType.FreeWrite or ExerciseType.Speak or ExerciseType.Reading or ExerciseType.Dialogue or ExerciseType.Match)
            return [];

        var steps = new List<HintStep>();
        if (node is not null)
            steps.Add(new HintStep(1, "Thema", $"Hier geht es um: {node.PlainTitle ?? node.Title}."));

        var rule = exercise.Hint ?? exercise.NoteFor(lang) ?? node?.InterferenceNoteFor(lang);
        if (!string.IsNullOrWhiteSpace(rule))
            steps.Add(new HintStep(2, "Regel", rule));

        if (Shape(exercise) is { } shape)
            steps.Add(new HintStep(3, "Anfang", shape));

        // Whatever a content author wrote, a rung that spells out an accepted answer is not a hint.
        return steps.Where(s => !Leaks(s.Text, exercise)).Select((s, i) => s with { Level = i + 1 }).ToList();
    }

    /// <summary>The form of the answer without the answer: first letters and length, a struck-out option, the first chunk.</summary>
    public static string? Shape(Exercise exercise)
    {
        switch (exercise.Type)
        {
            case ExerciseType.MultipleChoice when exercise.CorrectIndex is { } right && exercise.Options.Count >= 3:
                // Strike out the last wrong option - stable for one item, so asking twice gives the same hint.
                var wrong = Enumerable.Range(0, exercise.Options.Count).Last(i => i != right);
                return $"„{exercise.Options[wrong]}“ ist es nicht.";
            case ExerciseType.WordOrder when exercise.Options.Count >= 3 && exercise.Answers.Count > 0:
                var first = exercise.Answers[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return first is null ? null : $"Der Satz beginnt mit „{first}“.";
            case ExerciseType.SpotError when exercise.CorrectIndex is { } wrongWord && exercise.Options.Count >= 4:
                return wrongWord < exercise.Options.Count / 2 ? "Der Fehler steckt in der ersten Satzhälfte." : "Der Fehler steckt in der zweiten Satzhälfte.";
            case ExerciseType.Cloze or ExerciseType.Vocab or ExerciseType.Dictation when exercise.Answers.Count > 0:
                return "So sieht die Lösung aus: " + Mask(exercise.Answers[0]);
            case ExerciseType.Transform or ExerciseType.Translate when exercise.Answers.Count > 0:
                var words = exercise.Answers[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return words.Length < 4 ? "So sieht die Lösung aus: " + Mask(exercise.Answers[0]) : $"Der Satz beginnt mit „{words[0]} …“ und hat {words.Length} Wörter.";
            default:
                return null;
        }
    }

    /// <summary>
    /// Keeps the first letter of each word and blanks the rest: "aufgestanden" → "a___________". A leading article
    /// goes completely ("dem Bus" → "___ B__"): the case ending is usually what the item tests, and its first
    /// letter would give it away.
    /// </summary>
    public static string Mask(string answer)
        => string.Join(" ", answer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select((w, i) =>
            IsArticle(w) && i == 0 && answer.Contains(' ') ? "___" : w.Length <= 1 ? "_" : w[0] + new string('_', w.Length - 1)));

    private static bool IsArticle(string w) => w.ToLowerInvariant() is "der" or "die" or "das" or "den" or "dem" or "des" or "ein" or "eine" or "einen" or "einem" or "einer";

    /// <summary>
    /// True when <paramref name="hint"/> contains an accepted answer (case, umlaut spelling and punctuation folded,
    /// whole words only). Answers of one or two letters are ignored: "am" in a hint about "am Montag" is not a leak.
    /// </summary>
    public static bool Leaks(string hint, Exercise exercise)
    {
        // A choice item's answer is its right option; spotting an error is given away by naming the wrong word too.
        IEnumerable<string> accepted = exercise.Type switch
        {
            ExerciseType.MultipleChoice when exercise.CorrectIndex is { } c && c >= 0 && c < exercise.Options.Count => [exercise.Options[c]],
            ExerciseType.SpotError when exercise.CorrectIndex is { } w && w >= 0 && w < exercise.Options.Count => [.. exercise.Answers, exercise.Options[w]],
            _ => exercise.Answers,
        };
        return Leaks(hint, accepted);
    }

    public static bool Leaks(string hint, IEnumerable<string> accepted)
    {
        var text = " " + Fold(hint) + " ";
        return accepted.Select(Fold).Where(a => a.Length > 2).Any(a => text.Contains(" " + a + " ", StringComparison.Ordinal));
    }

    private static string Fold(string s)
    {
        var folded = AnswerChecker.FoldUmlauts(AnswerChecker.Normalize(s, keepCase: false));
        var chars = folded.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
