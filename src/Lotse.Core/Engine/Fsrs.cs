namespace Lotse.Core.Engine;

/// <summary>
/// The FSRS-5 memory model (Free Spaced Repetition Scheduler, open-spaced-repetition project, 2024) - the algorithm
/// Anki ships as its default scheduler. Each card is described by two numbers: <b>stability</b> S, the number of days
/// after which the probability of recall has fallen to 90 %, and <b>difficulty</b> D in 1..10. Recall probability
/// ("retrievability") decays with elapsed time t as R = (1 + 19/81 · t/S)^-0.5, and every review updates S and D from
/// the grade and from how likely the recall was at that moment: a hard-won recall of a nearly-forgotten card grows S
/// a lot, an easy recall of a fresh card barely at all, and a lapse shrinks S without throwing it away.
/// The weights are the published FSRS-5 defaults, fitted on ~700 million reviews; per-learner optimisation is a
/// possible later step behind the same API. Pure functions, no state, so the scheduler stays unit-testable.
/// </summary>
public static class Fsrs
{
    /// <summary>FSRS-5 default parameters w0..w18.</summary>
    public static readonly IReadOnlyList<double> DefaultWeights =
    [
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192,
        1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621,
    ];

    private const double Decay = -0.5;
    private const double Factor = 19.0 / 81.0;
    private static readonly IReadOnlyList<double> W = DefaultWeights;

    public const double MinDifficulty = 1.0;
    public const double MaxDifficulty = 10.0;

    /// <summary>Probability of recalling a card of stability <paramref name="stabilityDays"/> after <paramref name="elapsedDays"/>.</summary>
    public static double Retrievability(double stabilityDays, double elapsedDays)
    {
        if (stabilityDays <= 0) return 0;
        return Math.Pow(1 + Factor * Math.Max(0, elapsedDays) / stabilityDays, Decay);
    }

    /// <summary>Days until recall probability drops to <paramref name="desiredRetention"/> (0.9 gives exactly the stability).</summary>
    public static double IntervalDays(double stabilityDays, double desiredRetention)
        => stabilityDays / Factor * (Math.Pow(desiredRetention, 1 / Decay) - 1);

    /// <summary>Stability after the very first review, by grade (1 = Again .. 4 = Easy).</summary>
    public static double InitialStability(int grade) => W[Grade(grade) - 1];

    /// <summary>Difficulty after the very first review, by grade.</summary>
    public static double InitialDifficulty(int grade)
        => Clamp(W[4] - Math.Exp(W[5] * (Grade(grade) - 1)) + 1);

    /// <summary>Difficulty after a review: moves with the grade (linear damping so it saturates at the edges) and reverts slightly toward the difficulty of an "Easy" card.</summary>
    public static double NextDifficulty(double difficulty, int grade)
    {
        var g = Grade(grade);
        var delta = -W[6] * (g - 3);
        var damped = difficulty + delta * (10 - difficulty) / 9;
        return Clamp(W[7] * InitialDifficulty(4) + (1 - W[7]) * damped);
    }

    /// <summary>Stability after a successful recall (Hard/Good/Easy) with retrievability <paramref name="retrievability"/> at review time.</summary>
    public static double NextRecallStability(double difficulty, double stability, double retrievability, int grade)
    {
        var g = Grade(grade);
        var hardPenalty = g == 2 ? W[15] : 1;
        var easyBonus = g == 4 ? W[16] : 1;
        return stability * (Math.Exp(W[8]) * (11 - difficulty) * Math.Pow(stability, -W[9])
            * (Math.Exp(W[10] * (1 - retrievability)) - 1) * hardPenalty * easyBonus + 1);
    }

    /// <summary>Stability after a lapse: shorter than before, never longer, never zero.</summary>
    public static double NextForgetStability(double difficulty, double stability, double retrievability)
    {
        var s = W[11] * Math.Pow(difficulty, -W[12]) * (Math.Pow(stability + 1, W[13]) - 1) * Math.Exp(W[14] * (1 - retrievability));
        return Math.Max(0.1, Math.Min(s, stability));
    }

    /// <summary>Stability after a review on the same day as the previous one (re-asking within a session).</summary>
    public static double NextShortTermStability(double stability, int grade)
        => stability * Math.Exp(W[17] * (Grade(grade) - 3 + W[18]));

    private static double Clamp(double d) => Math.Clamp(d, MinDifficulty, MaxDifficulty);

    private static int Grade(int grade) => grade is >= 1 and <= 4
        ? grade
        : throw new ArgumentOutOfRangeException(nameof(grade), grade, "FSRS grades are 1 (Again) .. 4 (Easy).");
}
