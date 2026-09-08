namespace Lotse.Core.Model;

/// <summary>
/// Shuffles the answer options of choice exercises once, deterministically, when they enter the catalogue.
///
/// Authored content leans on position: across the bank the correct multiple-choice option sat at index 1 in 58 %
/// of the reading and listening questions and the third option was right only 9 % of the time, and four reading
/// tasks had the same key on every question. A learner picks that up long before they notice it - "B, never C" -
/// and the exam does not reward it. Rather than ask authors to balance keys by hand, the order is permuted here
/// from a stable hash of the exercise id, so it is the same on every start and every machine (a learner's saved
/// session must not see the options re-ordered after a restart), and the correct <em>text</em> is what moves,
/// never what is correct.
///
/// Applied at the two load boundaries only (seed files, generated rows) - never in the catalogue constructor,
/// which would re-permute on every <c>With()</c>. Types whose option order carries meaning stay untouched:
/// <see cref="ExerciseType.SpotError"/> (the options are the sentence), <see cref="ExerciseType.WordOrder"/> (the
/// options are chunks the learner orders), dialogues (each option has its own feedback) and Match (the UI
/// shuffles the right column itself).
/// </summary>
public static class KeyShuffle
{
    public static Exercise Apply(Exercise e) => e.Type switch
    {
        ExerciseType.MultipleChoice when e.Options.Count > 1 && e.CorrectIndex is { } ci && ci >= 0 && ci < e.Options.Count
            => Permute(e, ci),
        ExerciseType.Reading when e.Questions.Count > 0
            => e with { Questions = [.. e.Questions.Select((q, i) => Permute(q, e.Id + "#" + i))] },
        _ => e,
    };

    private static Exercise Permute(Exercise e, int correct)
    {
        var order = Permutation(e.Options.Count, e.Id);
        return e with
        {
            Options = [.. order.Select(i => e.Options[i])],
            CorrectIndex = Array.IndexOf(order, correct),
        };
    }

    private static ReadingQuestion Permute(ReadingQuestion q, string seed)
    {
        if (q.Options.Count < 2 || q.CorrectIndex < 0 || q.CorrectIndex >= q.Options.Count) return q;
        var order = Permutation(q.Options.Count, seed);
        return q with { Options = [.. order.Select(i => q.Options[i])], CorrectIndex = Array.IndexOf(order, q.CorrectIndex) };
    }

    /// <summary>Fisher–Yates over a seeded <see cref="Random"/>; the seeded generator is stable across runs.</summary>
    private static int[] Permutation(int n, string seed)
    {
        var order = Enumerable.Range(0, n).ToArray();
        var rng = new Random(StableHash.Seed("keys:" + seed));
        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }
}
