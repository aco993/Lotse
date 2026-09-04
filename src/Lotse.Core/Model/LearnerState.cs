namespace Lotse.Core.Model;

/// <summary>
/// The learner's estimated ability on one skill node. <see cref="Theta"/> lives on the same scale as
/// <see cref="CefrBandExtensions.Difficulty"/>; 0 is the B1/B2 boundary.
/// </summary>
public sealed class SkillState
{
    public required string NodeId { get; init; }
    public double Theta { get; set; }
    public int Attempts { get; set; }
    public int Correct { get; set; }
    public int CurrentStreak { get; set; }
    public DateTime? LastPracticedUtc { get; set; }
    public DateTime? LastErrorUtc { get; set; }

    /// <summary>Set when the node was weak at some point; enables the "did you actually learn it?" re-check cycle.</summary>
    public bool WasWeak { get; set; }
    /// <summary>Next scheduled re-check for a formerly weak node (null = none pending).</summary>
    public DateTime? RecheckDueUtc { get; set; }
    /// <summary>How many re-checks have been passed in a row (0, 1, 2 → intervals 7, 21, 60 days).</summary>
    public int RechecksPassed { get; set; }

    /// <summary>Probability of solving a B2.1 item on this node — what the UI shows as "Beherrschung".</summary>
    public double Mastery => Ability.SuccessProbability(Theta, CefrBand.B2_1.Difficulty());

    /// <summary>How much evidence we have. 0 = nothing known, 1 = enough attempts to trust the estimate.</summary>
    public double Confidence => Math.Min(1.0, Attempts / 12.0);

    public bool IsWeak => Attempts >= 3 && Mastery < 0.5;
    public bool IsStrong => Attempts >= 5 && Mastery >= 0.8;
}

/// <summary>Spaced-repetition state of one exercise ("card"), scheduled by <see cref="Engine.ReviewScheduler"/> on the FSRS model.</summary>
public sealed class ReviewState
{
    public required string ExerciseId { get; init; }
    public required string NodeId { get; init; }
    /// <summary>FSRS memory stability in days: the time after which recall probability has dropped to 90 %. 0 = not scheduled yet.</summary>
    public double Stability { get; set; }
    /// <summary>FSRS difficulty 1 (easy) .. 10 (hard); 0 = not scheduled yet.</summary>
    public double Difficulty { get; set; }
    /// <summary>Days between the last review and the due date; 0 while the item is in relearning after a lapse.</summary>
    public double IntervalDays { get; set; }
    /// <summary>SM-2 ease factor written by the scheduler before 0.8.0. Read once, to seed <see cref="Difficulty"/> when such a state is next reviewed.</summary>
    public double Ease { get; set; } = 2.5;
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public DateTime DueUtc { get; set; }
    public DateTime? LastReviewUtc { get; set; }

    public bool IsNew => Repetitions == 0 && LastReviewUtc is null;

    /// <summary>Current probability of recalling this item, from the FSRS forgetting curve.</summary>
    public double RetrievabilityAt(DateTime nowUtc)
        => LastReviewUtc is { } last ? Engine.Fsrs.Retrievability(Stability, (nowUtc - last).TotalDays) : 0;
}

public enum Outcome
{
    Correct,
    /// <summary>Right idea, small slip (capitalisation, typo, umlaut spelling). Counts as mostly right but logs the slip.</summary>
    AlmostCorrect,
    Incorrect,
    /// <summary>Production tasks graded on a rubric rather than right/wrong.</summary>
    Graded,
}

/// <summary>One answered exercise.</summary>
public sealed record Attempt(
    string ExerciseId,
    string NodeId,
    Outcome Outcome,
    /// <summary>0..1; production tasks get the rubric score, closed tasks 1 / 0.7 / 0.</summary>
    double Score,
    int DurationMs,
    bool HintUsed,
    DateTime Utc,
    string? AnswerText = null);

/// <summary>A tagged mistake. Aggregating these over time is how the system finds the learner's weak spots.</summary>
public sealed record ErrorEvent(string Code, string NodeId, DateTime Utc, string? Snippet = null, string? Correction = null, bool FromAi = false);
