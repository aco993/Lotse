using System.Text.RegularExpressions;
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
        foreach (var e in fx.Catalog.Exercises.Where(e => e.Answers.Count > 0 && e.Type is not (ExerciseType.WordOrder or ExerciseType.SpotError)))
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

    /// <summary>
    /// The course used to greet its author by name. It now carries {Vorname}/{Nachname}/{Name}, and this test is
    /// what stops the literal name from creeping back in with the next lesson.
    /// </summary>
    [Fact]
    public void No_content_file_hard_codes_the_authors_name()
    {
        var dir = ContentLoader.ResolveContentDirectory(null);
        var offenders = Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("Aleksandar", StringComparison.Ordinal)
                     || File.ReadAllText(f).Contains("Micić", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(offenders.Count == 0, "Fester Name statt Token in: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Authored keys leaned hard on position (index 1 in 58 % of reading/listening questions, the third
    /// multiple-choice option right only 9 % of the time, four reading tasks with a constant key). KeyShuffle
    /// balances them at load; this keeps the balance from drifting back and makes sure only the text moved.
    /// </summary>
    [Fact]
    public void Choice_keys_are_balanced_and_no_reading_task_has_a_constant_key()
    {
        var mc = fx.Catalog.Exercises.Where(e => e.Type == ExerciseType.MultipleChoice && e.Options.Count == 3).ToList();
        Assert.True(mc.Count >= 50, $"nur {mc.Count} dreioptionige MC-Aufgaben");
        for (var i = 0; i < 3; i++)
        {
            var share = mc.Count(e => e.CorrectIndex == i) / (double)mc.Count;
            Assert.InRange(share, 0.20, 0.47);
        }

        var readings = fx.Catalog.Exercises.Where(e => e.Type == ExerciseType.Reading && e.Questions.Count >= 4).ToList();
        Assert.NotEmpty(readings);
        Assert.All(readings, r => Assert.True(r.Questions.Select(q => q.CorrectIndex).Distinct().Count() > 1, $"{r.Id}: konstanter Schlüssel"));

        var questions = fx.Catalog.Exercises.Where(e => e.Type == ExerciseType.Reading).SelectMany(e => e.Questions).ToList();
        var topShare = questions.GroupBy(q => q.CorrectIndex).Max(g => g.Count()) / (double)questions.Count;
        Assert.True(topShare < 0.45, $"eine Position trägt {topShare:P0} der Schlüssel");
    }

    [Fact]
    public void Key_shuffle_is_deterministic_and_moves_only_the_text()
    {
        var e = new Exercise
        {
            Id = "mc.test.001",
            Type = ExerciseType.MultipleChoice,
            NodeId = "GR.PASSIV",
            Band = CefrBand.B2_1,
            Prompt = "___",
            Options = ["a", "b", "c", "d"],
            CorrectIndex = 1,
        };
        var once = KeyShuffle.Apply(e);
        var twice = KeyShuffle.Apply(e);
        Assert.Equal(once.Options, twice.Options);
        Assert.Equal(once.CorrectIndex, twice.CorrectIndex);
        Assert.Equal("b", once.Options[once.CorrectIndex!.Value]);
        Assert.Equal(e.Options.OrderBy(o => o), once.Options.OrderBy(o => o));

        // Types whose order carries meaning are left alone.
        var spot = e with { Type = ExerciseType.SpotError, Answers = ["x"] };
        Assert.Same(spot, KeyShuffle.Apply(spot));
        var order = e with { Type = ExerciseType.WordOrder, Answers = ["a b c d"] };
        Assert.Same(order, KeyShuffle.Apply(order));
    }

    /// <summary>
    /// The stand-in name a learner gets when they leave the profile empty must not belong to anyone in the course.
    /// The first pool had "Berger" in it, and Lesson 1's team lead is Sabine Berger - so a nameless learner read
    /// "Frau Berger hat mir Ihre Einarbeitung übergeben" as Herr Berger. Surnames are checked against the whole
    /// bank (exercises address people too), first names against the lesson speakers the learner actually talks to.
    /// </summary>
    [Fact]
    public void Fallback_names_belong_to_nobody_in_the_content()
    {
        var dir = ContentLoader.ResolveContentDirectory(null);
        var addressed = new Regex(@"(?:Frau|Herr|Herrn|Familie)\s+(\p{Lu}[\p{Ll}]+)");
        var surnames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            foreach (Match m in addressed.Matches(File.ReadAllText(f)))
                surnames.Add(m.Groups[1].Value);
        foreach (var l in fx.Catalog.Lessons)
            foreach (var parts in l.Story.Select(s => s.Speaker.Split('(')[0].Trim().Split(' ')).Where(p => p.Length >= 2))
                surnames.Add(parts[^1]);

        var speakerFirstNames = fx.Catalog.Lessons.SelectMany(l => l.Story)
            .Select(s => s.Speaker.Split('(')[0].Trim().Split(' ')[0])
            .ToHashSet(StringComparer.Ordinal);

        var takenSurnames = NameFallback.LastNames.Where(surnames.Contains).ToList();
        var takenFirst = NameFallback.FirstNames.Where(speakerFirstNames.Contains).ToList();
        Assert.True(takenSurnames.Count == 0, "Platzhalter-Nachname gehört einer Figur: " + string.Join(", ", takenSurnames));
        Assert.True(takenFirst.Count == 0, "Platzhalter-Vorname gehört einem Sprecher: " + string.Join(", ", takenFirst));
        Assert.DoesNotContain(NameFallback.Neutral.Last, surnames);
    }

    /// <summary>
    /// The repository is public, and an absolute path from the machine it was written on carries a real Windows
    /// account name into it. Three files had one (a prompt doc and the two harness scripts) before this test.
    /// Tools take their paths from their own location or from an argument instead.
    /// </summary>
    [Fact]
    public void No_file_carries_an_absolute_home_path_of_the_machine_it_was_written_on()
    {
        var root = RepositoryRoot();
        Assert.SkipWhen(root is null, "Quellbaum nicht gefunden (Lauf aus einem Paket ohne Repo).");

        var skipDirs = new[] { "bin", "obj", ".git", "node_modules", "TestResults" };
        var pattern = new Regex("[A-Za-z]:[\\\\/]Users[\\\\/]", RegexOptions.IgnoreCase);
        var offenders = Directory.EnumerateFiles(root!, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(skipDirs.Contains))
            .Where(f => TextExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).Equals("ContentAndPlannerTests.cs", StringComparison.Ordinal)) // this file states the pattern
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root!, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Absoluter Benutzerpfad in: " + string.Join(", ", offenders));
    }

    private static readonly string[] TextExtensions =
        [".cs", ".razor", ".json", ".md", ".ps1", ".mjs", ".js", ".css", ".yml", ".yaml", ".props", ".slnx", ".editorconfig"];

    /// <summary>Walks up from the test binary until the solution file appears; null when there is no source tree.</summary>
    private static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lotse.slnx"))) dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>
    /// AnswerChecker compares the learner's input against these strings verbatim, so a token here could never be
    /// matched - it would be an unsolvable exercise.
    /// </summary>
    [Fact]
    public void No_answer_contains_a_name_token()
        => Assert.All(fx.Catalog.Exercises, e => Assert.All(e.Answers, a => Assert.False(NameTemplate.ContainsToken(a), e.Id)));

    /// <summary>
    /// Heute tells the learner what to practise; "Nominalstil ↔ Verbalstil" is a term, not an instruction. Every
    /// grammar node therefore owes a plain-language name - and one that is not just the title again.
    /// </summary>
    [Fact]
    public void Every_grammar_node_has_a_plain_language_name()
    {
        var missing = fx.Catalog.Nodes
            .Where(n => n.Area == SkillArea.Grammatik)
            .Where(n => string.IsNullOrWhiteSpace(n.PlainTitle) || n.PlainTitle == n.Title)
            .Select(n => n.Id)
            .ToList();
        Assert.True(missing.Count == 0, "Ohne plainTitle: " + string.Join(", ", missing));
    }

    [Fact]
    public void Audio_only_exercises_have_text_to_speak()
        => Assert.All(fx.Catalog.Exercises.Where(e => e.AudioOnly || e.Type == ExerciseType.Dictation), e => Assert.False(string.IsNullOrWhiteSpace(e.Text), e.Id));
}

public class PlannerTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    private static readonly DateTime Now = new(2026, 9, 2, 18, 0, 0, DateTimeKind.Utc);

    private PlannerInput Input(int minutes, Dictionary<string, SkillState>? states = null, List<ReviewState>? due = null, int sessions = 0, DateTime? lastProduction = null, TargetLevel target = TargetLevel.B2)
        => new()
        {
            TimeBudgetMinutes = minutes,
            NowUtc = Now,
            Catalog = fx.Catalog,
            SkillStates = states ?? new Dictionary<string, SkillState>(),
            DueReviews = due ?? [],
            SessionsCompleted = sessions,
            LastProductionUtc = lastProduction,
            TargetLevel = target,
            Seed = 7,
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

    /// <summary>Everything mastered except the C1 band, so the focus ranking has exactly one direction to go.</summary>
    private Dictionary<string, SkillState> OnlyC1Weak()
    {
        var states = new Dictionary<string, SkillState>();
        foreach (var n in fx.Catalog.Nodes)
            states[n.Id] = n.Band == CefrBand.C1
                ? new SkillState { NodeId = n.Id, Theta = -1.5, Attempts = 12, Correct = 3, LastPracticedUtc = Now.AddDays(-1), LastErrorUtc = Now.AddDays(-1) }
                : new SkillState { NodeId = n.Id, Theta = 1.5, Attempts = 12, Correct = 12, LastPracticedUtc = Now.AddDays(-1) };
        return states;
    }

    private List<SkillNode> FocusedNodes(SessionPlan plan) => plan.Steps
        .Where(s => s.Kind is StepKind.Focus or StepKind.Explore)
        .Select(s => fx.Catalog.Node(s.Exercise.NodeId)!)
        .ToList();

    [Fact]
    public void C1_target_lets_the_focus_reach_above_B2()
    {
        // Guard: the assertion below is only meaningful while the catalogue actually has drillable C1 material.
        Assert.Contains(fx.Catalog.Nodes, n => n.Band == CefrBand.C1 && fx.Catalog.ForNode(n.Id).Any(e => !e.IsProduction && !e.IsReceptive));

        var plan = new SessionPlanner().Plan(Input(15, OnlyC1Weak(), target: TargetLevel.C1));
        Assert.Contains(FocusedNodes(plan), n => n.Band == CefrBand.C1);
    }

    [Fact]
    public void B2_target_never_puts_a_C1_node_in_focus()
    {
        // Same learner, same weaknesses - only the goal differs. A B2 candidate's minutes belong to B2 material.
        var plan = new SessionPlanner().Plan(Input(15, OnlyC1Weak()));
        Assert.DoesNotContain(FocusedNodes(plan), n => n.Band == CefrBand.C1);
    }

    /// <summary>Every node equally and mildly known, so nothing but the occupation nudge can move the order.</summary>
    private Dictionary<string, SkillState> AllEqual()
    {
        var states = new Dictionary<string, SkillState>();
        foreach (var n in fx.Catalog.Nodes)
            states[n.Id] = new SkillState { NodeId = n.Id, Theta = 0, Attempts = 8, Correct = 5, LastPracticedUtc = Now.AddDays(-3) };
        return states;
    }

    [Fact]
    public void Occupation_lifts_its_own_nodes_and_says_why()
    {
        var states = AllEqual();
        var ohne = new SessionPlanner().RankFocusNodes(Input(15, states)).ToList();
        var mit = new SessionPlanner().RankFocusNodes(Input(15, states) with { Occupation = Occupation.Pflege }).ToList();

        int Rank(List<(string NodeId, double Priority, string Reason)> l) => l.FindIndex(r => r.NodeId == "WS.GESUNDHEIT_KOERPER");
        Assert.True(Rank(mit) < Rank(ohne), $"Pflege: Rang {Rank(mit)} statt besser als {Rank(ohne)}");

        // An adaptation the learner cannot see is indistinguishable from a whim.
        Assert.Contains("weil du in der Pflege arbeitest", mit.First(r => r.NodeId == "WS.GESUNDHEIT_KOERPER").Reason);
    }

    [Fact]
    public void Elektrotechnik_reaches_its_own_node_and_the_neighbouring_IT_one()
    {
        var states = AllEqual();
        var ohne = new SessionPlanner().RankFocusNodes(Input(15, states)).ToList();
        var mit = new SessionPlanner().RankFocusNodes(Input(15, states) with { Occupation = Occupation.Elektrotechnik }).ToList();

        int Rank(List<(string NodeId, double Priority, string Reason)> l, string id) => l.FindIndex(r => r.NodeId == id);
        Assert.True(Rank(mit, "WS.TECHNIK_ELEKTRO") < Rank(ohne, "WS.TECHNIK_ELEKTRO"));
        // The trade reads schematics and PLC code in the same shift, so IT is lifted too - but only as a nudge.
        Assert.True(Rank(mit, "WS.IT_SOFTWARE") < Rank(ohne, "WS.IT_SOFTWARE"));
        Assert.Contains("weil du in der Elektrotechnik arbeitest", mit.First(r => r.NodeId == "WS.TECHNIK_ELEKTRO").Reason);
    }

    [Fact]
    public void Every_occupation_can_name_itself_and_points_only_at_nodes_that_exist()
    {
        // A field added to the enum without a label, a reason or real content would steer the planner into nothing.
        var nodeIds = fx.Catalog.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var o in Enum.GetValues<Occupation>().Where(o => o != Occupation.Unspecified))
        {
            Assert.NotEqual("Keine Angabe", o.Label());
            Assert.False(string.IsNullOrWhiteSpace(o.ReasonTail()), $"{o}: ohne Begründung");
            Assert.NotEmpty(o.PreferredNodes());
            Assert.All(o.PreferredNodes(), id => Assert.Contains(id, nodeIds));
            Assert.All(o.PreferredNodes(), id => Assert.True(fx.Catalog.ForNode(id).Any(), $"{o}: Knoten {id} ohne Übungen"));
            Assert.NotEmpty(o.PreferredTags());
            Assert.All(o.PreferredTags(), t => Assert.Contains(fx.Catalog.Exercises,
                e => e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Without_an_occupation_nothing_is_pulled_forward()
    {
        var ranked = new SessionPlanner().RankFocusNodes(Input(15, AllEqual())).ToList();
        Assert.All(ranked, r => Assert.DoesNotContain("Vorgezogen", r.Reason));
    }

    [Fact]
    public void Occupation_is_only_a_tie_break_never_a_reordering_of_weaknesses()
    {
        // A genuine weakness must still win over a merely well-fitting topic.
        var states = AllEqual();
        states["GR.PASSIV"] = new SkillState { NodeId = "GR.PASSIV", Theta = -2.0, Attempts = 15, Correct = 2, LastPracticedUtc = Now.AddDays(-1), LastErrorUtc = Now.AddDays(-1) };

        var ranked = new SessionPlanner().RankFocusNodes(Input(15, states) with { Occupation = Occupation.Pflege }).ToList();
        Assert.Equal("GR.PASSIV", ranked[0].NodeId);
    }

    [Theory]
    [InlineData(Occupation.Unspecified, 0)]   // an unset profile must score every exercise the same
    [InlineData(Occupation.IT, 5)]            // node + tag + professional context
    public void Occupation_fit_scores_what_it_promises(Occupation occupation, int expected)
    {
        var exercise = new Exercise
        {
            Id = "x",
            Type = ExerciseType.Cloze,
            NodeId = "WS.IT_SOFTWARE",
            Band = CefrBand.B2_1,
            Prompt = "…",
            Answers = ["…"],
            Tags = ["it"],
            Context = ExerciseContext.Beruf,
        };
        Assert.Equal(expected, occupation.Fit(exercise));
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
    public void Readiness_shows_no_number_when_nothing_is_known()
    {
        var r = LearnerAnalysis.Readiness(fx.Catalog, new Dictionary<string, SkillState>());
        Assert.False(r.HasEvidence);
        Assert.Equal(0, r.EvidenceModules);
        Assert.Contains("keine Prüfungsaufgaben", r.Verdict);
        Assert.Equal(4, r.Modules.Count);
        Assert.All(r.Modules, m => Assert.False(m.HasEvidence));
    }

    /// <summary>A node at exactly the given mastery, fully confident: theta is solved from the 1.4-slope logistic
    /// that <see cref="Ability.SuccessProbability"/> uses against the B2.1 difficulty.</summary>
    private static SkillState Mastered(string nodeId, double mastery = 0.85) => new()
    {
        NodeId = nodeId,
        Theta = CefrBand.B2_1.Difficulty() + Math.Log(mastery / (1 - mastery)) / 1.4,
        Attempts = 20,
        Correct = (int)Math.Round(20 * mastery),
        LastPracticedUtc = Now.AddDays(-1),
    };

    /// <summary>
    /// The finding that mattered most in the 2026-09 review: a learner who drilled vocabulary and grammar to 85 %
    /// and never read, listened, wrote or spoke once was shown "Lesen 81 %" and told to start exam simulations.
    /// </summary>
    [Fact]
    public void Grammar_and_vocabulary_alone_never_produce_an_exam_number()
    {
        var states = fx.Catalog.Nodes
            .Where(n => n.Area is SkillArea.Grammatik or SkillArea.Wortschatz or SkillArea.Redemittel)
            .ToDictionary(n => n.Id, n => Mastered(n.Id));

        var r = LearnerAnalysis.Readiness(fx.Catalog, states);

        Assert.False(r.HasEvidence, "Ohne eine einzige Modulaufgabe darf es keine Prüfungszahl geben.");
        Assert.All(r.Modules, m => Assert.False(m.HasEvidence));
        Assert.All(r.Modules, m => Assert.True(m.Foundation > 0.7, $"{m.Module}: das Fundament muss trotzdem sichtbar sein"));
        Assert.Contains("keine Prüfungsaufgaben", r.Verdict);
    }

    [Fact]
    public void A_module_number_rests_on_the_modules_own_tasks_and_the_foundation_lifts_it_by_at_most_a_step()
    {
        var states = fx.Catalog.Nodes
            .Where(n => n.Area is SkillArea.Grammatik or SkillArea.Wortschatz or SkillArea.Redemittel)
            .ToDictionary(n => n.Id, n => Mastered(n.Id, 0.9));
        // Reading practised, but weakly: 40 % - the vocabulary behind it is at 90 %.
        foreach (var n in fx.Catalog.Nodes.Where(n => n.Area == SkillArea.Lesen))
            states[n.Id] = Mastered(n.Id, 0.4);

        var r = LearnerAnalysis.Readiness(fx.Catalog, states);
        var lesen = r.Modules.Single(m => m.Module == "Lesen");

        Assert.True(lesen.HasEvidence);
        Assert.True(r.HasEvidence);
        Assert.Equal(1, r.EvidenceModules);
        // Evidence ~0.4 (shrunk a little towards the prior), foundation ~0.9: the number must stay near the evidence.
        Assert.True(lesen.Readiness <= 0.4 + LearnerAnalysis.FoundationBonus + 0.05, $"Lesen {lesen.Readiness:P0} ist vom Fundament hochgezogen");
        Assert.True(lesen.Readiness < 0.6, "Ein schwach geübtes Modul darf nicht als bestanden erscheinen");
        Assert.Contains("1 von 4 Modulen", r.Verdict);
        Assert.Contains("Hören", r.Verdict); // named as still missing
    }

    [Fact]
    public void Four_measured_modules_give_the_old_verdicts_back()
    {
        var states = fx.Catalog.Nodes.ToDictionary(n => n.Id, n => Mastered(n.Id, 0.85));
        var r = LearnerAnalysis.Readiness(fx.Catalog, states);
        Assert.True(r.HasEvidence);
        Assert.Equal(4, r.EvidenceModules);
        Assert.All(r.Modules, m => Assert.True(m.Readiness >= 0.7, $"{m.Module}: {m.Readiness:P0}"));
        Assert.Contains("Auf Kurs", r.Verdict);
    }
}
