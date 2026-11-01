using Lotse.Core.Telc;
using Lotse.Core.Training;
using Lotse.Core.Tutor;

namespace Lotse.Core.Tests;

/// <summary>The research-led rules: the week after a sure-and-wrong mistake, calibration, quoted AI errors, the telc forecast and plan.</summary>
public sealed class LearningScienceTests
{
    private static readonly DateTime Day0 = new(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);

    // ---- Hypercorrection --------------------------------------------------------------------------------------

    [Fact]
    public void Sure_and_wrong_needs_a_week_even_after_two_sessions()
    {
        var history = new List<PointAnswer>
        {
            new("p", "v1", 1, false, 3, Utc: Day0),
            new("p", "v2", 2, true, 3, Utc: Day0.AddDays(1)),
            new("p", "v3", 3, true, 3, Utc: Day0.AddDays(2)),
        };
        var progress = LearnedRule.Evaluate("p", history);
        Assert.Equal(PointStatus.AufGutemWeg, progress.Status);
        Assert.Equal(Day0.AddDays(LearnedRule.HypercorrectionDays), progress.LearnableFromUtc);
        Assert.True(progress.AwaitsDelayedCheck(Day0.AddDays(3)));

        history.Add(new("p", "v2", 4, true, 3, Utc: Day0.AddDays(8)));
        Assert.Equal(PointStatus.Gelernt, LearnedRule.Evaluate("p", history).Status);
    }

    [Fact]
    public void An_unsure_mistake_keeps_the_session_rule_only()
    {
        var history = new List<PointAnswer>
        {
            new("p", "v1", 1, false, 2, Utc: Day0),
            new("p", "v2", 3, true, 3, Utc: Day0.AddDays(1)),
        };
        var progress = LearnedRule.Evaluate("p", history);
        Assert.Equal(PointStatus.Gelernt, progress.Status);
        Assert.Null(progress.LearnableFromUtc);
    }

    [Fact]
    public void Answers_without_a_time_keep_the_old_rule()
    {
        var history = new List<PointAnswer> { new("p", "v1", 1, false, 3), new("p", "v2", 3, true, 3) };
        Assert.Equal(PointStatus.Gelernt, LearnedRule.Evaluate("p", history).Status);
    }

    // ---- Calibration ------------------------------------------------------------------------------------------

    [Fact]
    public void Calibration_flags_an_overconfident_learner()
    {
        var answers = Enumerable.Range(0, 10).Select(i => (3, i < 6))       // "sicher": 60 % right
            .Concat(Enumerable.Range(0, 10).Select(i => (1, i < 3)))           // "geraten": 30 % right
            .ToList();
        var c = ConfidenceCalibration.From(answers);
        Assert.Equal(0.6, c.Of(3).Accuracy, 3);
        Assert.Contains("überschätzt", c.Verdict);
    }

    [Fact]
    public void Calibration_stays_silent_with_little_evidence()
    {
        var c = ConfidenceCalibration.From([(3, false), (3, true), (1, true)]);
        Assert.False(c.HasEvidence);
        Assert.Null(c.Verdict);
    }

    // ---- Quoted errors ----------------------------------------------------------------------------------------

    [Fact]
    public void Only_errors_that_quote_the_text_survive()
    {
        var text = "Ich freue mich über  Ihre Antwort. Ich warte auf dem Bus.";
        var errors = new[]
        {
            new TutorError("VERB_PRAEP", "freue mich über Ihre Antwort", "freue mich auf Ihre Antwort", "Zukunft: auf"),
            new TutorError("KASUS_PRAEP", "„auf dem Bus“", "auf den Bus", "warten auf + Akk."),
            new TutorError("ADJ_ENDUNG", "mit gute Grüßen", "mit guten Grüßen", "erfunden"),
        };
        var kept = QuotedErrors.Filter(errors, text, out var dropped);
        Assert.Equal(2, kept.Count);
        Assert.Equal(1, dropped);
        Assert.DoesNotContain(kept, e => e.Code == "ADJ_ENDUNG");
    }

    // ---- telc forecast and plan -------------------------------------------------------------------------------

    [Fact]
    public void Readiness_weights_recent_results_and_simulations_higher()
    {
        var r = TelcReadiness.From(
        [
            new TelcPartResult(TelcPart.Schreiben, 9, Day0),
            new TelcPartResult(TelcPart.Schreiben, 27, Day0.AddDays(5), Simulation: true),
        ]);
        var est = r.Of(TelcPart.Schreiben);
        // Newest (simulation, weight 3·2 = 6) against older practice (weight 2): (6·27 + 2·9) / 8 = 22.5.
        Assert.Equal(22.5, est.Points!.Value, 3);
        Assert.Equal(2, est.Evidence);
    }

    [Fact]
    public void Projection_fills_untested_parts_from_the_same_half_only()
    {
        var r = TelcReadiness.From([new TelcPartResult(TelcPart.Lesen1, 20, Day0)]);
        var (written, projected) = r.Written;
        Assert.True(projected);
        Assert.Equal(225 * 0.8, written, 3);
        Assert.False(r.HasOralEvidence);
        Assert.Null(r.ExpectedGrade);
        // Untested parts lead the priorities: nothing is known about them.
        Assert.NotEqual(TelcPart.Lesen1, r.Priorities()[0].Part);
    }

    [Fact]
    public void Plan_runs_back_from_the_exam_date_with_simulations_and_a_quiet_end()
    {
        var today = new DateOnly(2026, 11, 2);
        var exam = today.AddDays(60);
        var plan = TelcStudyPlan.Build(today, exam, TelcReadiness.From([]));
        Assert.NotEmpty(plan);
        Assert.Equal(TelcPhase.Aufbau, plan[0].Phase);
        Assert.True(plan[0].Simulation); // baseline at the start
        Assert.Contains(plan, w => w.Phase == TelcPhase.Pruefungsformat && w.Simulation);
        Assert.Contains(plan, w => w.Phase == TelcPhase.Endspurt && w.Simulation);
        Assert.Equal(exam, plan[^1].End);
        Assert.True(plan.Zip(plan.Skip(1)).All(p => p.First.Phase <= p.Second.Phase), "Phasen laufen nur vorwärts");

        Assert.Empty(TelcStudyPlan.Build(today, null, TelcReadiness.From([])));
        Assert.Empty(TelcStudyPlan.Build(today, today.AddDays(-1), TelcReadiness.From([])));
    }

    [Fact]
    public void The_last_two_days_are_rest()
    {
        var today = new DateOnly(2026, 11, 2);
        var plan = TelcStudyPlan.Build(today, today.AddDays(2), TelcReadiness.From([]));
        Assert.Single(plan);
        Assert.Equal(TelcPhase.Ruhe, plan[0].Phase);
    }
}
