namespace Lotse.Core.Model;

/// <summary>
/// One-parameter logistic (Rasch-style) ability model with an Elo-like online update.
/// Chosen over Bayesian Knowledge Tracing because it puts learners and exercises on one interpretable CEFR scale,
/// needs no offline fitting, and stays explainable ("you solve B2.1 items on this topic 62 % of the time").
/// </summary>
public static class Ability
{
    /// <summary>Expected probability that a learner with ability <paramref name="theta"/> solves an item of difficulty <paramref name="difficulty"/>.</summary>
    public static double SuccessProbability(double theta, double difficulty)
        => 1.0 / (1.0 + Math.Exp(-1.4 * (theta - difficulty)));

    /// <summary>Learning rate: large while we know little about the learner, smaller once the estimate has settled.</summary>
    public static double LearningRate(int attemptsSoFar)
        => Math.Max(0.12, 0.55 / (1.0 + attemptsSoFar / 6.0));

    /// <summary>
    /// Applies one observation. <paramref name="score"/> is 0..1 (1 = solved cleanly, 0.7 = almost, 0 = wrong).
    /// Returns the new theta; the caller persists it.
    /// </summary>
    public static double Update(double theta, int attemptsSoFar, double difficulty, double score)
    {
        var expected = SuccessProbability(theta, difficulty);
        var next = theta + LearningRate(attemptsSoFar) * (score - expected);
        return Math.Clamp(next, -3.0, 3.0);
    }

    /// <summary>Target difficulty for practice: slightly above the current ability so the learner succeeds roughly 70–80 % of the time.</summary>
    public static double TargetDifficulty(double theta) => theta + 0.25;
}
