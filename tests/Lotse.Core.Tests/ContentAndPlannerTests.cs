using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Infrastructure.Content;

namespace Lotse.Core.Tests;

public sealed class CatalogFixture
{
    public ContentCatalog Catalog { get; } = ContentLoader.Load(ContentLoader.ResolveContentDirectory());
}

public class ContentTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    [Fact]
    public void Catalog_is_structurally_valid()
        => Assert.Empty(fx.Catalog.Validate());

    [Fact]
    public void Catalog_has_substantial_content()
    {
        Assert.True(fx.Catalog.Nodes.Count >= 40, $"nur {fx.Catalog.Nodes.Count} Knoten");
        Assert.True(fx.Catalog.Exercises.Count >= 400, $"nur {fx.Catalog.Exercises.Count} Übungen");
        Assert.True(fx.Catalog.ErrorTypes.Count >= 40);
    }

    [Fact]
    public void Every_placement_node_has_items_below_and_above_the_boundary()
    {
        foreach (var nodeId in PlacementTest.CoreNodeIds)
        {
            var items = fx.Catalog.ForNode(nodeId).Where(e => !e.IsProduction && !e.IsReceptive).ToList();
            Assert.True(items.Any(e => e.Band <= CefrBand.B1_2), $"{nodeId}: kein B1-Item");
            Assert.True(items.Any(e => e.Band >= CefrBand.B2_1), $"{nodeId}: kein B2-Item");
        }
    }

    [Fact]
    public void Every_grammar_and_vocab_node_has_at_least_four_drills()
    {
        foreach (var node in fx.Catalog.Nodes.Where(n => n.Area is SkillArea.Grammatik or SkillArea.Wortschatz or SkillArea.Redemittel))
        {
            var count = fx.Catalog.ForNode(node.Id).Count(e => !e.IsProduction && !e.IsReceptive);
            Assert.True(count >= 3, $"{node.Id} hat nur {count} Drills");
        }
    }

    [Fact]
    public void Every_error_code_points_to_an_existing_node_and_every_node_area_is_covered()
    {
        var nodeIds = fx.Catalog.Nodes.Select(n => n.Id).ToHashSet();
        Assert.All(fx.Catalog.ErrorTypes, e => Assert.Contains(e.NodeId, nodeIds));
        foreach (var area in Enum.GetValues<SkillArea>())
            Assert.Contains(fx.Catalog.Nodes, n => n.Area == area);
    }

    [Fact]
    public void Production_tasks_have_rubrics_and_model_answers()
    {
        var prod = fx.Catalog.Exercises.Where(e => e.IsProduction).ToList();
        Assert.True(prod.Count >= 20);
        Assert.All(prod, e => Assert.NotEmpty(e.Rubric));
        Assert.All(prod, e => Assert.False(string.IsNullOrWhiteSpace(e.ModelAnswer), e.Id));
    }

    [Fact]
    public void Exam_format_writing_tasks_exist_for_both_parts()
    {
        Assert.Contains(fx.Catalog.Exercises, e => e.Type == ExerciseType.FreeWrite && e.MinWords >= 150);
        Assert.Contains(fx.Catalog.Exercises, e => e.Type == ExerciseType.FreeWrite && e.MinWords is >= 100 and < 150);
        Assert.Contains(fx.Catalog.Exercises, e => e.Type == ExerciseType.Speak && e.TargetSeconds >= 180);
    }

    [Fact]
    public void Seed_answers_check_as_correct_against_themselves()
    {
        foreach (var e in fx.Catalog.Exercises.Where(e => e.Answers.Count > 0 && e.Type != ExerciseType.WordOrder))
            foreach (var a in e.Answers)
                Assert.Equal(Outcome.Correct, AnswerChecker.Check(e, a).Outcome);
    }

    [Fact]
    public void Word_order_answers_use_exactly_the_given_chunks()
    {
        foreach (var e in fx.Catalog.Exercises.Where(e => e.Type == ExerciseType.WordOrder))
        {
            var chunkWords = e.Options.SelectMany(o => o.Split(' ')).Select(w => w.ToLowerInvariant()).OrderBy(w => w).ToList();
            var answerWords = AnswerChecker.Normalize(e.Answers[0], keepCase: false).Split(' ').OrderBy(w => w).ToList();
            Assert.Equal(chunkWords, answerWords);
        }
    }

    [Fact]
    public void Audio_only_exercises_have_text_to_speak()
        => Assert.All(fx.Catalog.Exercises.Where(e => e.AudioOnly || e.Type == ExerciseType.Dictation), e => Assert.False(string.IsNullOrWhiteSpace(e.Text), e.Id));
}

public class PlannerTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    private static readonly DateTime Now = new(2026, 9, 2, 18, 0, 0, DateTimeKind.Utc);

    private PlannerInput Input(int minutes, Dictionary<string, SkillState>? states = null, List<ReviewState>? due = null, int sessions = 0, DateTime? lastProduction = null)
        => new()
        {
            TimeBudgetMinutes = minutes, NowUtc = Now, Catalog = fx.Catalog,
            SkillStates = states ?? new Dictionary<string, SkillState>(), DueReviews = due ?? [], SessionsCompleted = sessions,
            LastProductionUtc = lastProduction, Seed = 7,
        };

    [Fact]
    public void New_learner_gets_a_full_session_with_no_history()
    {
        var plan = new SessionPlanner().Plan(Input(10));
        Assert.True(plan.Steps.Count >= 6, $"nur {plan.Steps.Count} Schritte");
        Assert.InRange(plan.EstimatedSeconds, 6 * 60, 13 * 60);
        Assert.All(plan.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
    }

    [Fact]
    public void Five_minute_session_stays_short()
    {
        var plan = new SessionPlanner().Plan(Input(5));
        Assert.InRange(plan.EstimatedSeconds, 2 * 60, 7 * 60);
    }

    [Fact]
    public void Weak_node_is_prioritised_and_explained()
    {
        var states = new Dictionary<string, SkillState>();
        foreach (var n in fx.Catalog.Nodes)
            states[n.Id] = new SkillState { NodeId = n.Id, Theta = 1.5, Attempts = 12, Correct = 11, LastPracticedUtc = Now.AddDays(-1) };
        states["GR.PASSIV"] = new SkillState { NodeId = "GR.PASSIV", Theta = -1.5, Attempts = 12, Correct = 3, LastPracticedUtc = Now.AddDays(-1), LastErrorUtc = Now.AddDays(-1) };

        var plan = new SessionPlanner().Plan(Input(10, states));
        var focus = plan.Steps.Where(s => s.Kind == StepKind.Focus).ToList();
        Assert.NotEmpty(focus);
        Assert.Equal("GR.PASSIV", focus[0].Exercise.NodeId);
        Assert.Contains("Passiv", focus[0].Reason);
    }

    [Fact]
    public void Due_reviews_come_first_but_are_capped()
    {
        var due = fx.Catalog.Exercises.Where(e => e.Type == ExerciseType.Cloze).Take(40)
            .Select(e => new ReviewState { ExerciseId = e.Id, NodeId = e.NodeId, DueUtc = Now.AddDays(-2), Repetitions = 2 }).ToList();
        var plan = new SessionPlanner().Plan(Input(10, due: due));
        var reviews = plan.Steps.Count(s => s.Kind == StepKind.Review);
        Assert.InRange(reviews, 3, 12);
        Assert.Equal(StepKind.Review, plan.Steps[0].Kind);
    }

    [Fact]
    public void Recheck_is_served_when_due()
    {
        var states = new Dictionary<string, SkillState>
        {
            ["GR.KONJUNKTIV2"] = new() { NodeId = "GR.KONJUNKTIV2", Theta = 1.0, Attempts = 15, Correct = 12, WasWeak = true, RecheckDueUtc = Now.AddDays(-1) },
        };
        var plan = new SessionPlanner().Plan(Input(10, states));
        var recheck = plan.Steps.FirstOrDefault(s => s.Kind == StepKind.Recheck);
        Assert.NotNull(recheck);
        Assert.Equal("GR.KONJUNKTIV2", recheck!.Exercise.NodeId);
    }

    [Fact]
    public void Ten_minute_session_includes_one_production_task()
    {
        var plan = new SessionPlanner().Plan(Input(10));
        Assert.Equal(1, plan.Steps.Count(s => s.Kind == StepKind.Production));
    }

    [Fact]
    public void Production_alternates_between_writing_and_speaking()
    {
        var a = new SessionPlanner().Plan(Input(10, sessions: 0)).Steps.First(s => s.Kind == StepKind.Production).Exercise.Type;
        var b = new SessionPlanner().Plan(Input(10, sessions: 1)).Steps.First(s => s.Kind == StepKind.Production).Exercise.Type;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void No_exercise_is_planned_twice()
    {
        var plan = new SessionPlanner().Plan(Input(15));
        Assert.Equal(plan.Steps.Count, plan.Steps.Select(s => s.Exercise.Id).Distinct().Count());
    }

    [Fact]
    public void Same_seed_gives_same_plan()
    {
        var a = new SessionPlanner().Plan(Input(10)).Steps.Select(s => s.Exercise.Id).ToList();
        var b = new SessionPlanner().Plan(Input(10)).Steps.Select(s => s.Exercise.Id).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Placement_covers_every_core_node_with_two_items()
    {
        var items = PlacementTest.Build(fx.Catalog);
        Assert.Equal(PlacementTest.CoreNodeIds.Count * 2, items.Count);
        Assert.All(items, i => Assert.False(i.IsProduction));
    }

    [Fact]
    public void Readiness_verdict_asks_for_data_when_nothing_is_known()
    {
        var r = LearnerAnalysis.Readiness(fx.Catalog, new Dictionary<string, SkillState>());
        Assert.Contains("wenig Daten", r.Verdict);
        Assert.Equal(4, r.Modules.Count);
    }
}
