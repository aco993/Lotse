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
/// Spaced repetition on the FSRS-5 memory model (<see cref="Fsrs"/>). The scheduler owns the practical rules around the
/// model: grades are derived from behaviour (wrong / slip / hint / speed), never self-rated; a wrong answer comes back
/// within ten minutes so it is retrieved once more before the session ends (relearning step); the next interval is the
/// point where recall probability would fall to <see cref="DesiredRetention"/>; intervals are capped so an exam-bound
/// learner never has an item vanish for half a year; and a small deterministic jitter spreads due dates so reviews
/// do not pile up on one day. States scheduled by the earlier SM-2 scheduler are converted on their next review.
/// </summary>
public static class ReviewScheduler
{
    /// <summary>Target recall probability at the moment an item comes due. 0.9 is the FSRS/Anki default: a good trade-off between workload and retention.</summary>
    public const double DesiredRetention = 0.9;
    public const double MaxIntervalDays = 120;
    private const double RelearnMinutes = 10;

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
        var g = (int)grade + 1; // FSRS grades 1..4
        ConvertLegacyState(state);

        if (state.Stability <= 0)
        {
            state.Stability = Fsrs.InitialStability(g);
            state.Difficulty = Fsrs.InitialDifficulty(g);
        }
        else
        {
            var elapsedDays = state.LastReviewUtc is { } last ? Math.Max(0, (nowUtc - last).TotalDays) : 0;
            state.Difficulty = Fsrs.NextDifficulty(state.Difficulty, g);
            if (elapsedDays < 1)
            {
                state.Stability = Fsrs.NextShortTermStability(state.Stability, g);
            }
            else
            {
                var r = Fsrs.Retrievability(state.Stability, elapsedDays);
                state.Stability = grade == ReviewGrade.Again
                    ? Fsrs.NextForgetStability(state.Difficulty, state.Stability, r)
                    : Fsrs.NextRecallStability(state.Difficulty, state.Stability, r, g);
            }
        }

        if (grade == ReviewGrade.Again)
        {
            state.Lapses++;
            state.Repetitions = 0;
            state.IntervalDays = 0;
        }
        else
        {
            state.Repetitions++;
            state.IntervalDays = Math.Clamp(Math.Round(Fsrs.IntervalDays(state.Stability, DesiredRetention)), 1, MaxIntervalDays);
        }

        state.LastReviewUtc = nowUtc;
        state.DueUtc = state.IntervalDays <= 0
            ? nowUtc.AddMinutes(RelearnMinutes)
            : nowUtc.AddDays(state.IntervalDays * Jitter(state.ExerciseId));
    }

    /// <summary>
    /// A state written by the SM-2 scheduler (before 0.8.0) has an interval and an ease factor but no stability. Its
    /// interval is the best available estimate of stability, and its ease factor (1.3 hard .. 3.0 easy) maps linearly
    /// onto FSRS difficulty (10 .. 1); a state still in relearning (interval 0) simply starts over as new.
    /// </summary>
    private static void ConvertLegacyState(ReviewState state)
    {
        if (state.Stability > 0 || state.LastReviewUtc is null || state.IntervalDays <= 0) return;
        state.Stability = Math.Max(1, state.IntervalDays);
        state.Difficulty = Math.Clamp(1 + 9 * (3.0 - state.Ease) / (3.0 - 1.3), Fsrs.MinDifficulty, Fsrs.MaxDifficulty);
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
