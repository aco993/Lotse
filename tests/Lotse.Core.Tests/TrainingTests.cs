using Lotse.Core.Model;
using Lotse.Core.Training;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Core.Tests;

/// <summary>The "learned?" rule on its own: pure data in, status out.</summary>
public sealed class LearnedRuleTests
{
    private static PointAnswer A(int session, string variant, bool correct, int confidence) => new("p", variant, session, correct, confidence);

    [Fact]
    public void A_point_never_wrong_and_never_guessed_needs_no_review()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", true, 3)]);
        Assert.Equal(PointStatus.Sicher, p.Status);
        Assert.False(p.NeedsReview);
    }

    [Fact]
    public void Right_but_guessed_counts_as_a_mistake()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", true, 1)]);
        Assert.Equal(PointStatus.Offen, p.Status);
        Assert.False(p.Urgent);
    }

    [Fact]
    public void Wrong_with_confidence_three_is_urgent_until_learned()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", false, 3)]);
        Assert.True(p.Urgent);
        Assert.Equal(3, p.LearnableFromSession);
    }

    [Fact]
    public void Right_with_three_in_the_very_next_session_is_not_yet_learned()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", false, 2), A(2, "v2", true, 3)]);
        Assert.Equal(PointStatus.AufGutemWeg, p.Status);
        Assert.True(p.NeedsReview);
    }

    [Fact]
    public void Right_with_three_in_a_new_context_two_sessions_later_is_learned()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", false, 2), A(2, "v2", true, 3), A(3, "v3", true, 3)]);
        Assert.Equal(PointStatus.Gelernt, p.Status);
        Assert.False(p.NeedsReview);
        Assert.False(p.Urgent);
    }

    [Fact]
    public void The_same_sentence_that_was_wrong_never_proves_it()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", false, 2), A(3, "v1", true, 3)]);
        Assert.Equal(PointStatus.AufGutemWeg, p.Status);
    }

    [Fact]
    public void Confidence_two_does_not_prove_it()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", false, 2), A(3, "v2", true, 2)]);
        Assert.Equal(PointStatus.Offen, p.Status);
    }

    [Fact]
    public void A_new_mistake_restarts_the_count()
    {
        var p = LearnedRule.Evaluate("p", [A(1, "v1", false, 2), A(3, "v2", true, 3), A(4, "v3", false, 3)]);
        Assert.Equal(PointStatus.Offen, p.Status);
        Assert.Equal(6, p.LearnableFromSession);
        Assert.True(p.Urgent);
    }
}

/// <summary>The training content and the planner against the real content files.</summary>
public sealed class TrainingContentTests
{
    private static readonly ContentCatalog Catalog = ContentLoader.Load(ContentLoader.ResolveContentDirectory());
    private static readonly TrainingContent Content = TrainingContentLoader.Load(ContentLoader.ResolveContentDirectory(), Catalog);

    [Fact]
    public void Content_is_valid_and_every_point_has_two_new_contexts()
    {
        Assert.Empty(Content.Validate(Catalog));
        Assert.All(Content.Points, p => Assert.True(p.Variants.Count >= 3, p.Id));
    }

    [Fact]
    public void Every_model_answer_passes_its_own_checker()
    {
        foreach (var (p, v) in Content.Points.SelectMany(p => p.Variants.Select(v => (p, v))))
        {
            var item = new TrainingItem { Key = "k", Block = TrainingBlock.Diagnose, NodeId = p.NodeId, Prompt = v.Prompt, Answers = v.Answers, PointId = p.Id, VariantId = v.Id };
            foreach (var answer in v.Answers)
                Assert.Equal(Outcome.Correct, Engine.AnswerChecker.Check(item.ToExercise(), answer).Outcome);
        }
    }

    [Fact]
    public void Session_one_is_the_21_item_diagnostic_in_order()
    {
        var plan = TrainingPlanner.Plan(1, Content, Catalog, []);
        Assert.Equal(21, plan.Items.Count);
        Assert.All(plan.Items, i => Assert.Equal(TrainingBlock.Diagnose, i.Block));
        Assert.Equal("vp.abhaengen", plan.Items[0].PointId);
        Assert.Equal("v1", plan.Items[0].VariantId);
        Assert.True(plan.Items[^1].IsFree && plan.Items[^1].Speaking);
    }

    [Fact]
    public void Later_sessions_review_mistakes_in_a_new_context_then_focus_then_speak()
    {
        var history = new List<PointAnswer>
        {
            new("gen.wegen", "v1", 1, false, 3),
            new("gen.trotz-plural", "v1", 1, false, 2),
            new("nvv.bezug", "v1", 1, false, 1),
            new("vp.abhaengen", "v1", 1, true, 3),
        };
        var plan = TrainingPlanner.Plan(2, Content, Catalog, history, seed: 7);

        var review = plan.Items.Where(i => i.Block == TrainingBlock.Wiederholung).ToList();
        Assert.Equal(3, review.Count);
        Assert.Equal("gen.wegen", review[0].PointId);                 // sure-and-wrong first
        Assert.All(review, i => Assert.NotEqual("v1", i.VariantId)); // never the sentence that was wrong
        Assert.Equal("genitiv", plan.TopicId);                        // two open points + urgent weight

        var stages = plan.Items.Where(i => i.Block == TrainingBlock.Schwerpunkt).Select(i => i.Stage).ToList();
        Assert.Equal([TrainingStage.Lueckentext, TrainingStage.Umformung, TrainingStage.EigenerSatz, TrainingStage.KurzerText], stages);
        Assert.Equal(TrainingBlock.Sprechen, plan.Items[^1].Block);
        Assert.True(plan.Items[^1].Speaking);

        // Block order is fixed: review, then focus, then speaking.
        var blocks = plan.Items.Select(i => i.Block).ToList();
        Assert.Equal(blocks.OrderBy(b => b).ToList(), blocks);
    }
}

/// <summary>The service end to end against a temporary SQLite file: draft, submit, diagnose, review, learned.</summary>
public sealed class TrainingServiceTests : IAsyncLifetime
{
    private const string UserId = "trainee";

    private sealed class TestDbFactory(string path) : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<LotseDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private string _dbPath = "";
    private TestDbFactory _factory = default!;
    private TrainingService _svc = default!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lotse-training-{Guid.NewGuid():N}.db");
        _factory = new TestDbFactory(_dbPath);
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserId });
            await db.SaveChangesAsync();
        }
        var provider = new ContentCatalogProvider(ContentLoader.ResolveContentDirectory(), _factory, NullLogger<ContentCatalogProvider>.Instance);
        await provider.RefreshAsync();
        var content = TrainingContentLoader.Load(ContentLoader.ResolveContentDirectory(), provider.Catalog);
        _svc = new TrainingService(_factory, provider, content, TimeProvider.System, new FakeCurrentUserAccessor(UserId));
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureDeletedAsync();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    /// <summary>Answers every item: the key for closed items unless listed in <paramref name="wrong"/>, a sentence for free ones.</summary>
    private static List<TrainingAnswerInput> Answers(TrainingSessionView view, int confidence, params string[] wrong)
        => view.Items.Select(i => new TrainingAnswerInput(i.Key,
            i.IsFree ? "Das Projekt hätte früher geplant werden müssen." : wrong.Contains(i.PointId) ? "falsch" : i.Answers[0],
            confidence)).ToList();

    private async Task<TrainingSessionView> RunSessionAsync(int confidence, params string[] wrong)
    {
        var view = await _svc.StartOrResumeAsync();
        view = await _svc.SubmitAsync(view.Id, Answers(view, confidence, wrong));
        var verdicts = view.Results.Select(r => new TrainingVerdict(r.Item.Key, r.Item.IsFree ? true : null, r.Correct == false ? r.SuggestedKind : null)).ToList();
        return await _svc.CompleteAsync(view.Id, verdicts);
    }

    [Fact]
    public async Task Submit_is_refused_until_every_item_has_an_answer_and_a_confidence()
    {
        var view = await _svc.StartOrResumeAsync();
        var answers = Answers(view, 2);
        answers[4] = answers[4] with { Confidence = 0 };
        answers[7] = answers[7] with { Answer = " " };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SubmitAsync(view.Id, answers));
        Assert.Contains("d5", ex.Message);
        Assert.Contains("d8", ex.Message);
        Assert.Equal(TrainingSessionState.Open, (await _svc.GetSessionAsync(view.Id))!.State);
    }

    [Fact]
    public async Task Draft_survives_a_reload_and_solutions_stay_hidden_before_submit()
    {
        var view = await _svc.StartOrResumeAsync();
        await _svc.SaveDraftAsync(view.Id, [new TrainingAnswerInput("d1", "davon ab", 3)]);

        var again = await _svc.StartOrResumeAsync();
        Assert.Equal(view.Id, again.Id);
        Assert.Equal("davon ab", again.Draft["d1"].Answer);
        Assert.Empty(again.Results);
    }

    [Fact]
    public async Task Completing_needs_a_kind_for_every_mistake_and_flags_sure_mistakes()
    {
        var view = await _svc.StartOrResumeAsync();
        view = await _svc.SubmitAsync(view.Id, Answers(view, 3, "gen.wegen", "nvv.bezug"));

        var wrong = view.Results.Where(r => r.Correct == false).ToList();
        Assert.Equal(2, wrong.Count);
        Assert.All(wrong, r => Assert.True(r.Urgent));
        Assert.Equal(ErrorKind.K, wrong.Single(r => r.Item.PointId == "nvv.bezug").SuggestedKind);

        var freeOnly = view.Results.Where(r => r.Item.IsFree).Select(r => new TrainingVerdict(r.Item.Key, true, null)).ToList();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CompleteAsync(view.Id, freeOnly));

        var done = await _svc.CompleteAsync(view.Id, [.. freeOnly, .. wrong.Select(r => new TrainingVerdict(r.Item.Key, null, ErrorKind.A))]);
        Assert.Equal(TrainingSessionState.Completed, done.State);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.TrainingAnswers.CountAsync(a => a.Kind == ErrorKind.A));
        Assert.Contains(await db.ErrorEvents.Select(e => e.Code).ToListAsync(), c => c == "GENITIV");
        Assert.Empty(await db.ReviewStates.ToListAsync()); // confidence never reaches FSRS
    }

    [Fact]
    public async Task A_mistake_comes_back_in_new_contexts_until_learned_two_sessions_later()
    {
        await RunSessionAsync(3, "gen.wegen");

        var s2 = await _svc.StartOrResumeAsync();
        var review = s2.Items.Single(i => i.Block == TrainingBlock.Wiederholung);
        Assert.Equal("gen.wegen", review.PointId);
        Assert.NotEqual("v1", review.VariantId);
        await _svc.CompleteAsync((await _svc.SubmitAsync(s2.Id, Answers(s2, 3))).Id,
            s2.Items.Where(i => i.IsFree).Select(i => new TrainingVerdict(i.Key, true, null)).ToList());

        var row = (await _svc.GetOverviewAsync()).Points.Single(p => p.Point.Id == "gen.wegen");
        Assert.Equal(PointStatus.AufGutemWeg, row.Progress.Status);

        var s3 = await _svc.StartOrResumeAsync();
        var review3 = s3.Items.Single(i => i.Block == TrainingBlock.Wiederholung);
        Assert.NotEqual("v1", review3.VariantId);
        await _svc.CompleteAsync((await _svc.SubmitAsync(s3.Id, Answers(s3, 3))).Id,
            s3.Items.Where(i => i.IsFree).Select(i => new TrainingVerdict(i.Key, true, null)).ToList());

        var overview = await _svc.GetOverviewAsync();
        Assert.Equal(PointStatus.Gelernt, overview.Points.Single(p => p.Point.Id == "gen.wegen").Progress.Status);
        Assert.Equal(3, overview.CompletedSessions);
        Assert.DoesNotContain((await _svc.StartOrResumeAsync()).Items, i => i.Block == TrainingBlock.Wiederholung);
    }
}
