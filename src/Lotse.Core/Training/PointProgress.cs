namespace Lotse.Core.Training;

/// <summary>One answer to a <see cref="TrainingPoint"/>, as the "learned?" rule needs it.</summary>
/// <param name="Session">Number of the training session (1 = diagnostic).</param>
/// <param name="Confidence">The learner's own 1/2/3, given before seeing the solution.</param>
/// <param name="Utc">When it was answered; needed for the week a sure-and-wrong mistake must wait (see <see cref="LearnedRule.HypercorrectionDays"/>).</param>
public sealed record PointAnswer(string PointId, string VariantId, int Session, bool Correct, int Confidence, ErrorKind? Kind = null, DateTime? Utc = null);

public enum PointStatus
{
    /// <summary>Never wrong and never guessed: nothing to repeat.</summary>
    Sicher,
    /// <summary>Wrong (or right by guessing) and not yet answered right with full confidence since.</summary>
    Offen,
    /// <summary>Right with confidence 3 since the last mistake, but not yet in a new context two sessions later - or, after a sure-and-wrong mistake, not yet a week later.</summary>
    AufGutemWeg,
    /// <summary>Right, confidence 3, in a new context, at least two sessions after the last mistake.</summary>
    Gelernt,
}

/// <summary>Where one point stands, and why.</summary>
public sealed record PointProgress(
    string PointId,
    PointStatus Status,
    /// <summary>The last mistake was made with confidence 3 - the learner was sure and wrong, the costliest kind.</summary>
    bool Urgent,
    /// <summary>Session of the last mistake (or guess); null for <see cref="PointStatus.Sicher"/>.</summary>
    int? LastMistakeSession,
    /// <summary>The earliest session in which the point can count as learned.</summary>
    int? LearnableFromSession,
    /// <summary>Variants the point was got wrong (or guessed) in. A review must use another one.</summary>
    IReadOnlySet<string> FailedVariants,
    ErrorKind? LastKind,
    /// <summary>After a sure-and-wrong mistake: the earliest moment the proving answer may come (a week later).</summary>
    DateTime? LearnableFromUtc = null)
{
    /// <summary>The last mistake was sure-and-wrong and its week is not over: the point needs the delayed re-test.</summary>
    public bool AwaitsDelayedCheck(DateTime nowUtc) => LearnableFromUtc is { } t && Status != PointStatus.Gelernt && nowUtc < t;

    /// <summary>Comes back in the next session's Wiederholung.</summary>
    public bool NeedsReview => Status is PointStatus.Offen or PointStatus.AufGutemWeg;
}

/// <summary>
/// The rule that decides when a point counts as learned. A right answer straight after the correction proves little:
/// the solution is still in short-term memory, and the sentence is the one the learner has just seen. So it counts
/// only if it was right with confidence 3 (no guessing), in a sentence the learner never got wrong (a new context),
/// and at least two sessions after the last mistake. Until then the point keeps coming back.
/// <para>
/// A mistake made with confidence 3 waits longer: at least <see cref="HypercorrectionDays"/> days. That is the
/// hypercorrection effect - errors made with high confidence are corrected well right after feedback, but research
/// (Butler, Fazio &amp; Marsh 2011; Metcalfe 2017) finds that after about a week they come back more often than
/// low-confidence errors. Two sessions on two consecutive evenings would declare such a point learned exactly in
/// the window where it is most likely to return.
/// </para>
/// </summary>
public static class LearnedRule
{
    /// <summary>Sessions that must lie between the last mistake and the answer that proves it learned.</summary>
    public const int MinSessionGap = 2;

    /// <summary>Days a sure-and-wrong mistake must lie behind the answer that proves it learned.</summary>
    public const int HypercorrectionDays = 7;

    /// <summary>
    /// A right answer given with confidence 1 is a guess and is treated like a mistake: it starts (or restarts) the
    /// review cycle. Confidence 2 neither starts nor finishes one.
    /// </summary>
    public static bool IsMistake(PointAnswer a) => !a.Correct || a.Confidence <= 1;

    public static PointProgress Evaluate(string pointId, IEnumerable<PointAnswer> answers)
    {
        var history = answers.Where(a => a.PointId == pointId).OrderBy(a => a.Session).ToList();
        var mistakes = history.Where(IsMistake).ToList();
        if (mistakes.Count == 0)
            return new PointProgress(pointId, PointStatus.Sicher, false, null, null, new HashSet<string>(), null);

        var last = mistakes[^1];
        var failed = mistakes.Select(m => m.VariantId).ToHashSet();
        var learnableFrom = last.Session + MinSessionGap;
        // Two answers in the same session as the last mistake are ordered by list position, not by session; only
        // answers from LATER sessions can prove anything, which also keeps that ambiguity out of the rule.
        var after = history.Where(a => a.Session > last.Session).ToList();
        var lastWrong = mistakes.LastOrDefault(m => !m.Correct);
        var urgent = !last.Correct && last.Confidence >= 3;
        // The week only applies when the mistake's time is known; old rows without one keep the session rule.
        var learnableFromUtc = urgent && last.Utc is { } t ? t.AddDays(HypercorrectionDays) : (DateTime?)null;

        PointStatus status;
        if (after.Any(a => a.Correct && a.Confidence >= 3 && a.Session >= learnableFrom && !failed.Contains(a.VariantId)
                           && (learnableFromUtc is null || a.Utc is null || a.Utc >= learnableFromUtc)))
            status = PointStatus.Gelernt;
        else if (after.Any(a => a.Correct && a.Confidence >= 3))
            status = PointStatus.AufGutemWeg;
        else
            status = PointStatus.Offen;

        return new PointProgress(pointId, status, status != PointStatus.Gelernt && urgent, last.Session, learnableFrom, failed, lastWrong?.Kind, learnableFromUtc);
    }

    public static IReadOnlyList<PointProgress> EvaluateAll(IEnumerable<string> pointIds, IReadOnlyList<PointAnswer> answers)
        => pointIds.Select(id => Evaluate(id, answers)).ToList();
}
