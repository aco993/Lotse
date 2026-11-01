namespace Lotse.Core.Engine;

/// <summary>
/// The FSRS-6 memory model (Free Spaced Repetition Scheduler, open-spaced-repetition project) - the generation Anki
/// ships since 25.07. Each card is described by two numbers: <b>stability</b> S, the number of days after which the
/// probability of recall has fallen to 90 %, and <b>difficulty</b> D in 1..10. Recall probability ("retrievability")
/// decays with elapsed time t as R = (1 + f · t/S)^-d, where f is chosen so that R(S) = 0.9; every review updates S
/// and D from the grade and from how likely the recall was at that moment.
/// <para>
/// What FSRS-6 changed over FSRS-5, and why it is used here: the decay d, fixed at 0.5 before, is a parameter
/// (default 0.1542, a flatter curve that fits real review logs better), and so it can be fitted to one learner with
/// <see cref="FsrsOptimizer"/>; same-day reviews have their own damping (w19), so re-asking an item inside a session
/// no longer inflates its stability. The published benchmark shows FSRS-6 ahead of FSRS-5 on log loss, by a small
/// margin - which is why the per-learner decay is the part that matters most for one person's data.
/// </para>
/// Pure functions, no state, so the scheduler stays unit-testable.
/// </summary>
public static class Fsrs
{
    /// <summary>FSRS-6 default parameters w0..w20, fitted by the FSRS project on its large review benchmark.</summary>
    public static readonly IReadOnlyList<double> DefaultWeights =
    [
        0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722, 0.1666,
        0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425, 0.0912, 0.0658,
        0.1542,
    ];

    /// <summary>The default decay d = w20. A learner's own value comes from <see cref="FsrsOptimizer.FitDecay"/>.</summary>
    public static double DefaultDecay => W[20];

    public const double MinDecay = 0.1;
    public const double MaxDecay = 0.8;

    private static readonly IReadOnlyList<double> W = DefaultWeights;

    public const double MinDifficulty = 1.0;
    public const double MaxDifficulty = 10.0;

    /// <summary>The factor that pins R(S) to 0.9 for a given decay.</summary>
    private static double Factor(double decay) => Math.Pow(0.9, -1 / decay) - 1;

    private static double Decay(double? decay) => Math.Clamp(decay ?? DefaultDecay, MinDecay, MaxDecay);

    /// <summary>Probability of recalling a card of stability <paramref name="stabilityDays"/> after <paramref name="elapsedDays"/>.</summary>
    public static double Retrievability(double stabilityDays, double elapsedDays, double? decay = null)
    {
        if (stabilityDays <= 0) return 0;
        var d = Decay(decay);
        return Math.Pow(1 + Factor(d) * Math.Max(0, elapsedDays) / stabilityDays, -d);
    }

    /// <summary>Days until recall probability drops to <paramref name="desiredRetention"/> (0.9 gives exactly the stability, whatever the decay).</summary>
    public static double IntervalDays(double stabilityDays, double desiredRetention, double? decay = null)
    {
        var d = Decay(decay);
        return stabilityDays / Factor(d) * (Math.Pow(desiredRetention, -1 / d) - 1);
    }

    /// <summary>Stability after the very first review, by grade (1 = Again .. 4 = Easy).</summary>
    public static double InitialStability(int grade) => Math.Max(0.1, W[Grade(grade) - 1]);

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

    /// <summary>
    /// Stability after a lapse: shorter than before, never longer, never zero - and, new in FSRS-6, never shorter
    /// than what a same-day lapse would leave, so a long-known card does not collapse to the level of a new one.
    /// </summary>
    public static double NextForgetStability(double difficulty, double stability, double retrievability)
    {
        var longTerm = W[11] * Math.Pow(difficulty, -W[12]) * (Math.Pow(stability + 1, W[13]) - 1) * Math.Exp(W[14] * (1 - retrievability));
        var shortTerm = stability / Math.Exp(W[17] * W[18]);
        return Math.Max(0.1, Math.Min(Math.Min(longTerm, shortTerm), stability));
    }

    /// <summary>
    /// Stability after a review on the same day as the previous one (re-asking within a session). FSRS-6 damps the
    /// gain for already-stable cards (w19) and never lets a right answer shrink stability.
    /// </summary>
    public static double NextShortTermStability(double stability, int grade)
    {
        var g = Grade(grade);
        var increase = Math.Exp(W[17] * (g - 3 + W[18])) * Math.Pow(stability, -W[19]);
        if (g >= 3) increase = Math.Max(increase, 1);
        return Math.Max(0.1, stability * increase);
    }

    private static double Clamp(double d) => Math.Clamp(d, MinDifficulty, MaxDifficulty);

    private static int Grade(int grade) => grade is >= 1 and <= 4
        ? grade
        : throw new ArgumentOutOfRangeException(nameof(grade), grade, "FSRS grades are 1 (Again) .. 4 (Easy).");
}

/// <summary>One review in a learner's history, as the optimizer replays it.</summary>
/// <param name="Grade">1 = Again .. 4 = Easy, derived the same way the scheduler derives it.</param>
public sealed record FsrsReview(string CardId, DateTime Utc, int Grade);

/// <summary>The decay fitted to one learner, and how much evidence stood behind it.</summary>
public sealed record FsrsFit(double Decay, int Reviews, double LogLoss, double DefaultLogLoss)
{
    /// <summary>The fitted curve predicts this learner's recalls better than the default by at least this share.</summary>
    public double Improvement => DefaultLogLoss <= 0 ? 0 : 1 - LogLoss / DefaultLogLoss;
}

/// <summary>
/// Fits FSRS-6's decay to one learner. Every card's history is replayed with the scheduler's own update rules; at
/// each review a day or more after the previous one the model's predicted recall is compared with what happened
/// (log loss), and the decay with the lowest total loss wins - a one-parameter grid search, robust with the few
/// hundred reviews one person has, where fitting all 21 weights would overfit. Below
/// <see cref="MinReviews"/> qualifying reviews the default stays: too little data to beat it reliably.
/// </summary>
public static class FsrsOptimizer
{
    public const int MinReviews = 150;
    private const double Step = 0.01;

    public static FsrsFit? FitDecay(IEnumerable<FsrsReview> reviews)
    {
        var cards = reviews.GroupBy(r => r.CardId).Select(g => g.OrderBy(r => r.Utc).ToList()).Where(c => c.Count >= 2).ToList();
        var qualifying = cards.Sum(c => c.Zip(c.Skip(1)).Count(p => (p.Second.Utc - p.First.Utc).TotalDays >= 1));
        if (qualifying < MinReviews) return null;

        var defaultLoss = LogLoss(cards, Fsrs.DefaultDecay);
        var best = (Decay: Fsrs.DefaultDecay, Loss: defaultLoss);
        for (var d = Fsrs.MinDecay; d <= Fsrs.MaxDecay + 1e-9; d += Step)
        {
            var loss = LogLoss(cards, d);
            if (loss < best.Loss - 1e-12) best = (Math.Round(d, 2), loss);
        }
        return new FsrsFit(best.Decay, qualifying, best.Loss / qualifying, defaultLoss / qualifying);
    }

    /// <summary>Total log loss of the predicted recall over all qualifying reviews, replaying each card from its first review.</summary>
    public static double LogLoss(IReadOnlyList<List<FsrsReview>> cards, double decay)
    {
        double loss = 0;
        foreach (var card in cards)
        {
            double s = 0, diff = 0;
            DateTime? last = null;
            foreach (var r in card)
            {
                if (last is null)
                {
                    s = Fsrs.InitialStability(r.Grade);
                    diff = Fsrs.InitialDifficulty(r.Grade);
                    last = r.Utc;
                    continue;
                }
                var elapsed = (r.Utc - last.Value).TotalDays;
                if (elapsed >= 1)
                {
                    var p = Math.Clamp(Fsrs.Retrievability(s, elapsed, decay), 1e-4, 1 - 1e-4);
                    loss -= r.Grade > 1 ? Math.Log(p) : Math.Log(1 - p);
                    s = r.Grade == 1 ? Fsrs.NextForgetStability(diff, s, p) : Fsrs.NextRecallStability(diff, s, p, r.Grade);
                }
                else
                {
                    s = Fsrs.NextShortTermStability(s, r.Grade);
                }
                diff = Fsrs.NextDifficulty(diff, r.Grade);
                last = r.Utc;
            }
        }
        return loss;
    }
}
