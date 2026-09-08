using System.Globalization;
using System.Text.RegularExpressions;
using Lotse.Core.Model;

namespace Lotse.Core.Exam;

/// <summary>
/// The exam's procedural rules, which the exercises carry only implicitly: which part an item belongs to (from its
/// prompt, "Hören Teil 3: …"), how often a listening text may be played, and how long a reading or writing part
/// lasts. Applied to items in the exam context only - a practice text may be heard twice and is never timed.
/// The numbers follow <see cref="ExamBlueprint"/>: Hören plays Teil 1 and 3 once, Teil 2 and 4 twice; Lesen has
/// 65 minutes for five parts; Schreiben 50 + 25 minutes; the Vortrag is about four minutes.
/// </summary>
public static partial class ExamTiming
{
    [GeneratedRegex(@"\bTeil\s*(?<n>[1-5])\b", RegexOptions.CultureInvariant)]
    private static partial Regex PartLabel();

    /// <summary>The exam part named in the prompt, or null when the item does not say.</summary>
    public static int? Part(Exercise exercise)
        => PartLabel().Match(exercise.Prompt) is { Success: true } m ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// How often a listening text may be played before it is evaluated. Exam items follow the exam; practice items
    /// keep the two-plays habit the prompts announce ("Hören Sie zweimal"). Afterwards the text is free to replay.
    /// </summary>
    public static int MaxPlays(Exercise exercise)
    {
        if (!exercise.AudioOnly) return int.MaxValue;
        if (exercise.Context != ExerciseContext.Pruefung) return 2;
        return Part(exercise) is 1 or 3 ? 1 : 2;
    }

    /// <summary>Seconds an exam-format task has, or null where nothing is timed: practice items, and listening, where the audio sets the pace.</summary>
    public static int? TimeLimitSeconds(Exercise exercise)
    {
        if (exercise.Context != ExerciseContext.Pruefung) return null;
        return exercise.Type switch
        {
            ExerciseType.FreeWrite => exercise.MinWords >= 150 ? 50 * 60 : 25 * 60,
            ExerciseType.Speak => exercise.TargetSeconds is > 0 ? exercise.TargetSeconds : 4 * 60,
            ExerciseType.Reading when !exercise.AudioOnly => ReadingMinutes(Part(exercise)) * 60,
            _ => null,
        };
    }

    // 65 minutes for five parts, spent unevenly: the nine-item matching part first needs the most, the short rules
    // part last needs the least. 18 + 12 + 12 + 12 + 6 leaves the five minutes the exam gives for the answer sheet.
    private static int ReadingMinutes(int? part) => part switch { 1 => 18, 5 => 6, _ => 12 };
}
