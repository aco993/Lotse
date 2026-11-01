namespace Lotse.Core.Training;

/// <summary>How often answers given with one confidence level were actually right.</summary>
public sealed record CalibrationRow(int Confidence, int Answers, int Correct)
{
    public double Accuracy => Answers == 0 ? 0 : Correct / (double)Answers;
}

/// <summary>
/// Confidence calibration: does "sicher" mean right? Well-calibrated learners are right far more often at 3 than at
/// 1; an overconfident one is wrong at 3 nearly as often as at 2 - and those sure-and-wrong answers are the ones that
/// return after a week (see <see cref="LearnedRule.HypercorrectionDays"/>). Showing the gap is itself a small
/// intervention: learners who see their calibration rate their certainty more carefully, which makes the review
/// priorities that depend on it more accurate.
/// </summary>
public sealed record ConfidenceCalibration(IReadOnlyList<CalibrationRow> Rows)
{
    /// <summary>Answers per level below which a percentage would be noise.</summary>
    public const int MinAnswers = 8;
    /// <summary>"Sicher" should be right at least this often.</summary>
    public const double SureTarget = 0.85;

    public static ConfidenceCalibration From(IEnumerable<(int Confidence, bool Correct)> answers)
    {
        var list = answers.Where(a => a.Confidence is >= 1 and <= 3).ToList();
        return new ConfidenceCalibration([.. Enumerable.Range(1, 3).Select(c =>
            new CalibrationRow(c, list.Count(a => a.Confidence == c), list.Count(a => a.Confidence == c && a.Correct)))]);
    }

    public CalibrationRow Of(int confidence) => Rows[confidence - 1];

    public bool HasEvidence => Of(3).Answers >= MinAnswers;

    /// <summary>One sentence in German on what the numbers say, or null while there is too little evidence.</summary>
    public string? Verdict
    {
        get
        {
            if (!HasEvidence) return null;
            var sure = Of(3);
            var unsure = Of(1);
            if (sure.Accuracy < SureTarget - 0.15)
                return $"Bei „sicher“ liegst du nur in {sure.Accuracy:P0} richtig – du überschätzt dich. Wähle 3 nur, wenn du die Regel nennen könntest.";
            if (sure.Accuracy < SureTarget)
                return $"Bei „sicher“ {sure.Accuracy:P0} richtig – fast gut kalibriert. Ziel: über {SureTarget:P0}.";
            if (unsure.Answers >= MinAnswers && unsure.Accuracy > 0.7)
                return $"Bei „geraten“ liegst du in {unsure.Accuracy:P0} richtig – du kannst mehr, als du dir zutraust.";
            return $"Gut kalibriert: „sicher“ ist bei dir in {sure.Accuracy:P0} der Fälle richtig.";
        }
    }
}
