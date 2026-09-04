using Lotse.Core.Engine;
using Lotse.Core.Model;

namespace Lotse.Core.Tests;

public class AbilityTests
{
    [Fact]
    public void Success_probability_is_half_at_matching_difficulty()
        => Assert.Equal(0.5, Ability.SuccessProbability(0.4, 0.4), 6);

    [Fact]
    public void Correct_answers_raise_theta_and_wrong_answers_lower_it()
    {
        var up = Ability.Update(0, 0, 0.4, 1.0);
        var down = Ability.Update(0, 0, 0.4, 0.0);
        Assert.True(up > 0);
        Assert.True(down < 0);
        Assert.True(up < 1.0 && down > -1.0);
    }

    [Fact]
    public void Learning_rate_shrinks_with_evidence()
        => Assert.True(Ability.LearningRate(0) > Ability.LearningRate(20));

    [Fact]
    public void Theta_stays_within_bounds()
    {
        var theta = 0.0;
        for (var i = 0; i < 200; i++) theta = Ability.Update(theta, i, -1.2, 1.0);
        Assert.InRange(theta, -3, 3);
    }
}

public class AnswerCheckerTests
{
    private static Exercise Cloze(params string[] answers) => new()
    {
        Id = "t.cloze",
        Type = ExerciseType.Cloze,
        NodeId = "GR.KASUS_PRAEPOSITIONEN",
        Band = CefrBand.B1_2,
        Prompt = "mit ___ Bus",
        Answers = answers,
    };

    [Fact]
    public void Exact_match_is_correct()
        => Assert.Equal(Outcome.Correct, AnswerChecker.Check(Cloze("dem Bus"), " dem Bus. ").Outcome);

    [Fact]
    public void Capitalisation_slip_is_almost_correct_and_tagged()
    {
        var r = AnswerChecker.Check(Cloze("die Lösung"), "die lösung");
        Assert.Equal(Outcome.AlmostCorrect, r.Outcome);
        Assert.Contains(AnswerChecker.SlipCapitalisation, r.SlipCodes);
    }

    [Fact]
    public void Umlaut_written_as_ae_is_tolerated_but_tagged()
    {
        var r = AnswerChecker.Check(Cloze("die Lösung"), "die Loesung");
        Assert.Equal(Outcome.AlmostCorrect, r.Outcome);
        Assert.Contains(AnswerChecker.SlipUmlaut, r.SlipCodes);
    }

    [Fact]
    public void Ss_for_sharp_s_is_tagged_separately()
    {
        var r = AnswerChecker.Check(Cloze("die Straße"), "die Strasse");
        Assert.Equal(Outcome.AlmostCorrect, r.Outcome);
        Assert.Contains(AnswerChecker.SlipSharpS, r.SlipCodes);
    }

    [Fact]
    public void One_typo_in_a_long_word_is_tolerated()
    {
        var r = AnswerChecker.Check(Cloze("Ich habe die Besprechung verschoben"), "Ich habe die Besprechnug verschoben");
        Assert.Equal(Outcome.AlmostCorrect, r.Outcome);
        Assert.Contains(AnswerChecker.SlipTypo, r.SlipCodes);
    }

    [Fact]
    public void Typo_in_a_short_word_is_wrong()
        => Assert.Equal(Outcome.Incorrect, AnswerChecker.Check(Cloze("dem Bus"), "den Bus").Outcome);

    [Fact]
    public void Any_accepted_variant_counts()
        => Assert.Equal(Outcome.Correct, AnswerChecker.Check(Cloze("desto", "umso"), "umso").Outcome);

    [Fact]
    public void Multiple_choice_accepts_index_or_text()
    {
        var mc = new Exercise { Id = "t.mc", Type = ExerciseType.MultipleChoice, NodeId = "GR.ARTIKEL_GENUS", Band = CefrBand.B1_1, Prompt = "___ Problem", Options = ["Der", "Die", "Das"], CorrectIndex = 2 };
        Assert.Equal(Outcome.Correct, AnswerChecker.Check(mc, "2").Outcome);
        Assert.Equal(Outcome.Correct, AnswerChecker.Check(mc, "Das").Outcome);
        Assert.Equal(Outcome.Incorrect, AnswerChecker.Check(mc, "0").Outcome);
    }

    [Fact]
    public void Vocab_without_article_is_the_serbian_slip()
    {
        var v = new Exercise { Id = "t.v", Type = ExerciseType.Vocab, NodeId = "WS.BERUF_BUERO", Band = CefrBand.B1_2, Prompt = "rok", Answers = ["die Frist"], Lemma = "Frist" };
        var r = AnswerChecker.Check(v, "Frist");
        Assert.Equal(Outcome.AlmostCorrect, r.Outcome);
        Assert.Contains(AnswerChecker.SlipMissingArticle, r.SlipCodes);
    }

    [Fact]
    public void Vocab_with_wrong_article_is_wrong_but_tagged_as_genus()
    {
        var v = new Exercise { Id = "t.v", Type = ExerciseType.Vocab, NodeId = "WS.BERUF_BUERO", Band = CefrBand.B1_2, Prompt = "rok", Answers = ["die Frist"], Lemma = "Frist" };
        var r = AnswerChecker.Check(v, "der Frist");
        Assert.Equal(Outcome.Incorrect, r.Outcome);
        Assert.Contains(AnswerChecker.SlipWrongArticle, r.SlipCodes);
    }

    [Fact]
    public void Empty_answer_is_wrong()
        => Assert.Equal(Outcome.Incorrect, AnswerChecker.Check(Cloze("dem Bus"), "").Outcome);

    [Fact]
    public void Levenshtein_basics()
    {
        Assert.Equal(0, AnswerChecker.Levenshtein("Haus", "Haus"));
        Assert.Equal(1, AnswerChecker.Levenshtein("Haus", "Hans"));
        Assert.Equal(2, AnswerChecker.Levenshtein("Haus", "Hase"));
        Assert.Equal(4, AnswerChecker.Levenshtein("", "Haus"));
    }
}

public class ReviewSchedulerTests
{
    private static ReviewState New() => new() { ExerciseId = "x", NodeId = "n", DueUtc = DateTime.UnixEpoch };
    private static readonly DateTime T0 = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Again_comes_back_within_the_session()
    {
        var s = New();
        ReviewScheduler.Apply(s, ReviewGrade.Again, T0);
        Assert.Equal(1, s.Lapses);
        Assert.True(s.DueUtc <= T0.AddMinutes(15));
    }

    [Fact]
    public void Good_answers_grow_the_interval()
    {
        var s = New();
        var last = 0.0;
        for (var i = 0; i < 5; i++)
        {
            ReviewScheduler.Apply(s, ReviewGrade.Good, T0.AddDays(last));
            Assert.True(s.IntervalDays > last);
            last = s.IntervalDays;
        }
        Assert.True(last > 10);
    }

    [Fact]
    public void Easy_grows_faster_than_good()
    {
        var good = New();
        var easy = New();
        for (var i = 0; i < 4; i++)
        {
            ReviewScheduler.Apply(good, ReviewGrade.Good, T0);
            ReviewScheduler.Apply(easy, ReviewGrade.Easy, T0);
        }
        Assert.True(easy.IntervalDays > good.IntervalDays);
    }

    [Fact]
    public void Lapse_after_long_interval_shortens_but_does_not_reset_to_zero()
    {
        var s = New();
        for (var i = 0; i < 6; i++) ReviewScheduler.Apply(s, ReviewGrade.Good, T0);
        var before = s.IntervalDays;
        ReviewScheduler.Apply(s, ReviewGrade.Again, T0);
        Assert.True(s.IntervalDays < before);
        Assert.True(s.IntervalDays >= 1);
    }

    [Fact]
    public void Interval_is_capped()
    {
        var s = New();
        for (var i = 0; i < 30; i++) ReviewScheduler.Apply(s, ReviewGrade.Easy, T0);
        Assert.True(s.IntervalDays <= 120);
    }

    [Fact]
    public void Grade_is_derived_from_outcome_and_effort()
    {
        Assert.Equal(ReviewGrade.Again, ReviewScheduler.GradeFor(Outcome.Incorrect, false, 1000, 25));
        Assert.Equal(ReviewGrade.Hard, ReviewScheduler.GradeFor(Outcome.AlmostCorrect, false, 1000, 25));
        Assert.Equal(ReviewGrade.Hard, ReviewScheduler.GradeFor(Outcome.Correct, true, 1000, 25));
        Assert.Equal(ReviewGrade.Easy, ReviewScheduler.GradeFor(Outcome.Correct, false, 5000, 25));
        Assert.Equal(ReviewGrade.Good, ReviewScheduler.GradeFor(Outcome.Correct, false, 30000, 25));
    }
}

public class RecheckLifecycleTests
{
    private static readonly Exercise B2Item = new() { Id = "i", Type = ExerciseType.Cloze, NodeId = "GR.PASSIV", Band = CefrBand.B2_1, Prompt = "p", Answers = ["a"] };
    private static readonly DateTime T0 = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Weak_then_recovered_node_gets_a_recheck_scheduled()
    {
        var s = new SkillState { NodeId = "GR.PASSIV", Theta = -0.2 };
        for (var i = 0; i < 5; i++) LearnerAnalysis.ApplyAttempt(s, B2Item, 0, T0.AddMinutes(i));
        Assert.True(s.IsWeak);
        Assert.True(s.WasWeak);

        var t = T0.AddDays(1);
        for (var i = 0; i < 25 && s.RecheckDueUtc is null; i++) LearnerAnalysis.ApplyAttempt(s, B2Item, 1, t.AddMinutes(i));
        Assert.NotNull(s.RecheckDueUtc);
        Assert.InRange((s.RecheckDueUtc!.Value - t).TotalDays, 6.9, 7.2);
    }

    [Fact]
    public void Passing_three_rechecks_clears_the_weak_flag()
    {
        var s = new SkillState { NodeId = "GR.PASSIV", Theta = 1.5, Attempts = 20, Correct = 18, WasWeak = true, CurrentStreak = 5, RecheckDueUtc = T0 };
        LearnerAnalysis.ApplyAttempt(s, B2Item, 1, T0.AddHours(1));
        Assert.Equal(1, s.RechecksPassed);
        Assert.NotNull(s.RecheckDueUtc);
        s.RecheckDueUtc = T0;
        LearnerAnalysis.ApplyAttempt(s, B2Item, 1, T0.AddHours(2));
        s.RecheckDueUtc = T0;
        LearnerAnalysis.ApplyAttempt(s, B2Item, 1, T0.AddHours(3));
        Assert.Equal(3, s.RechecksPassed);
        Assert.Null(s.RecheckDueUtc);
        Assert.False(s.WasWeak);
    }

    [Fact]
    public void Failing_a_recheck_resets_the_counter()
    {
        var s = new SkillState { NodeId = "GR.PASSIV", Theta = 1.0, Attempts = 20, Correct = 15, WasWeak = true, RechecksPassed = 1, RecheckDueUtc = T0 };
        LearnerAnalysis.ApplyAttempt(s, B2Item, 0, T0.AddHours(1));
        Assert.Equal(0, s.RechecksPassed);
        Assert.Null(s.RecheckDueUtc);
        Assert.True(s.WasWeak);
    }
}
