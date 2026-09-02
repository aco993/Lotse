using Lotse.Core.Model;

namespace Lotse.Core.Engine;

public enum ReviewGrade
{
    Again,
    Hard,
    Good,
    Easy,
}

/// <summary>
/// Spaced repetition in the SM-2 family with a few practical changes: lapses shorten the interval instead of resetting
/// it to zero, easy answers are capped so a lucky guess cannot push a card out for months, and a small deterministic
/// jitter spreads due dates so reviews do not pile up on one day.
/// </summary>
public static class ReviewScheduler
{
    private const double MinEase = 1.3;
    private const double MaxIntervalDays = 120;

    /// <summary>Derives a grade from how the exercise was answered so the learner never has to self-rate.</summary>
    public static ReviewGrade GradeFor(Outcome outcome, bool hintUsed, int durationMs, int estimatedSeconds)
    {
        if (outcome == Outcome.Incorrect) return ReviewGrade.Again;
        if (outcome == Outcome.AlmostCorrect || hintUsed) return ReviewGrade.Hard;
        // Answered clean and quickly: the item is easy for this learner.
        return durationMs < estimatedSeconds * 600 ? ReviewGrade.Easy : ReviewGrade.Good;
    }

    public static void Apply(ReviewState state, ReviewGrade grade, DateTime nowUtc)
    {
        switch (grade)
        {
            case ReviewGrade.Again:
                state.Lapses++;
                state.Repetitions = 0;
                state.Ease = Math.Max(MinEase, state.Ease - 0.2);
                state.IntervalDays = state.IntervalDays > 7 ? Math.Max(1, state.IntervalDays * 0.25) : 0;
                break;
            case ReviewGrade.Hard:
                state.Repetitions++;
                state.Ease = Math.Max(MinEase, state.Ease - 0.15);
                state.IntervalDays = state.Repetitions switch
                {
                    1 => 1,
                    2 => 3,
                    _ => Math.Max(state.IntervalDays * 1.2, state.IntervalDays + 1),
                };
                break;
            case ReviewGrade.Good:
                state.Repetitions++;
                state.IntervalDays = state.Repetitions switch
                {
                    1 => 1,
                    2 => 4,
                    _ => Math.Max(state.IntervalDays * state.Ease, state.IntervalDays + 2),
                };
                break;
            case ReviewGrade.Easy:
                state.Repetitions++;
                state.Ease = Math.Min(3.0, state.Ease + 0.1);
                state.IntervalDays = state.Repetitions switch
                {
                    1 => 3,
                    2 => 7,
                    _ => Math.Max(state.IntervalDays * state.Ease * 1.3, state.IntervalDays + 4),
                };
                break;
        }

        state.IntervalDays = Math.Min(MaxIntervalDays, state.IntervalDays);
        state.LastReviewUtc = nowUtc;

        // "Again" comes back within the same day (10 minutes) so it is seen once more before the session ends.
        state.DueUtc = state.IntervalDays <= 0
            ? nowUtc.AddMinutes(10)
            : nowUtc.AddDays(state.IntervalDays * Jitter(state.ExerciseId));
    }

    /// <summary>±8 % deterministic jitter derived from the id, so tests stay reproducible.</summary>
    private static double Jitter(string id)
    {
        var h = 17;
        foreach (var c in id) h = unchecked(h * 31 + c);
        var unit = (Math.Abs(h) % 1000) / 1000.0; // 0..1
        return 0.92 + unit * 0.16;
    }
}
