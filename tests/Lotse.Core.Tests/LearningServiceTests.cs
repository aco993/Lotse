using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Core.Tests;

/// <summary>Integration tests of the application service against a real (temporary) SQLite file.</summary>
public sealed class LearningServiceTests : IAsyncLifetime
{
    private sealed class TestDbFactory(string path) : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<LotseDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A tutor that returns a fixed evaluation and a fixed generated drill, so the ingestion path is testable offline.</summary>
    private sealed class FakeTutor : ITutor
    {
        public bool IsAvailable => true;
        public string Description => "Fake";
        public int Evaluations;

        public Task<ProductionEvaluation> EvaluateAsync(Exercise exercise, string learnerText, ProductionMode mode, LearnerContext context, CancellationToken ct = default)
        {
            Evaluations++;
            return Task.FromResult(new ProductionEvaluation(0.55, "B1.2",
                [new RubricScore("Erfüllung", 3, "ok"), new RubricScore("Strukturen", 2, "Fehler")],
                [new TutorError("KASUS_PRAEP", "mit der Auto", "mit dem Auto", "mit + Dativ"), new TutorError("UNBEKANNT_XYZ", "x", "y", "z")],
                "korrigiert", "Feedback", ["GR.KASUS_PRAEPOSITIONEN"], ["Upgrade"]));
        }

        public Task<IReadOnlyList<Exercise>> GenerateExercisesAsync(SkillNode node, CefrBand band, int count, ExerciseContext exerciseContext, LearnerContext context, IReadOnlyList<Exercise> examples, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Exercise>>(
            [
                new Exercise { Id = $"gen.{node.Id}.t1", Type = ExerciseType.Cloze, NodeId = node.Id, Band = band, Prompt = "___ Test", Answers = ["der"], Source = ExerciseSource.Generated },
            ]);

        public Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default) => Task.FromResult("Antwort");

        public Task<Exercise?> GenerateReadingAsync(SkillNode node, CefrBand band, bool audioOnly, LearnerContext context, CancellationToken ct = default)
            => Task.FromResult<Exercise?>(new Exercise
            {
                Id = $"gen.{node.Id}.r1", Type = ExerciseType.Reading, NodeId = node.Id, Band = band, Prompt = "Lesen", Text = "Text", AudioOnly = audioOnly,
                Questions = [new ReadingQuestion("F?", ["a", "b"], 1)], Source = ExerciseSource.Generated,
            });
    }

    private string _dbPath = "";
    private TestDbFactory _factory = default!;
    private ContentCatalogProvider _provider = default!;
    private readonly ManualClock _clock = new();
    private readonly FakeTutor _tutor = new();
    private LearningService _svc = default!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lotse-test-{Guid.NewGuid():N}.db");
        _factory = new TestDbFactory(_dbPath);
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        _provider = new ContentCatalogProvider(ContentLoader.ResolveContentDirectory(), _factory, NullLogger<ContentCatalogProvider>.Instance);
        await _provider.RefreshAsync();
        _svc = new LearningService(_factory, _provider, _tutor, new SessionPlanner(), _clock, NullLogger<LearningService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureDeletedAsync();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private Exercise First(Func<Exercise, bool> pred) => _provider.Catalog.Exercises.First(pred);

    [Fact]
    public async Task Profile_is_created_once_and_reused()
    {
        var a = await _svc.GetProfileAsync();
        var b = await _svc.GetProfileAsync();
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(10, a.DailyMinutes);
    }

    [Fact]
    public async Task Starting_a_session_stores_a_plan_with_reasons()
    {
        var session = await _svc.StartSessionAsync(10);
        var view = await _svc.GetSessionAsync(session.Id);
        Assert.NotNull(view);
        Assert.True(view!.Steps.Count >= 6);
        Assert.Equal(view.Steps.Count, view.Exercises.Count);
        Assert.All(view.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.Equal(0, view.NextIndex);
    }

    [Fact]
    public async Task Correct_answer_updates_skill_state_review_state_and_session_progress()
    {
        var session = await _svc.StartSessionAsync(5);
        var view = (await _svc.GetSessionAsync(session.Id))!;
        var ex = view.Exercises.First(e => e.Answers.Count > 0 && e.Type != ExerciseType.WordOrder && !e.IsProduction);
        var index = view.Exercises.ToList().IndexOf(ex);

        var result = await _svc.SubmitAnswerAsync(session.Id, index, ex.Id, ex.Answers[0], 4000, false);
        Assert.Equal(Outcome.Correct, result.Check.Outcome);

        var states = await _svc.GetSkillStatesAsync();
        Assert.True(states[ex.NodeId].Attempts == 1 && states[ex.NodeId].Correct == 1);
        Assert.True(states[ex.NodeId].Theta > -0.2);

        await using var db = _factory.CreateDbContext();
        var review = await db.ReviewStates.FindAsync(ex.Id);
        Assert.NotNull(review);
        Assert.True(review!.DueUtc > _clock.Now.UtcDateTime.AddHours(12));
        var updated = await db.Sessions.FirstAsync(s => s.Id == session.Id);
        Assert.Equal(1, updated.StepsDone);
        Assert.Equal(1, updated.CorrectCount);
    }

    [Fact]
    public async Task Wrong_answer_logs_an_error_event_and_lowers_theta()
    {
        var ex = First(e => e.Type == ExerciseType.Cloze && e.NodeId == "GR.PASSIV");
        var result = await _svc.SubmitAnswerAsync(null, 0, ex.Id, "völlig falsch", 3000, false);
        Assert.Equal(Outcome.Incorrect, result.Check.Outcome);
        Assert.NotNull(result.InterferenceNote);

        var journal = await _svc.GetErrorJournalAsync();
        Assert.Single(journal);
        Assert.Equal("GR.PASSIV", _provider.Catalog.Error(journal[0].Code)!.NodeId);

        var states = await _svc.GetSkillStatesAsync();
        Assert.True(states["GR.PASSIV"].Theta < -0.2);
    }

    [Fact]
    public async Task Capitalisation_slip_is_logged_on_the_orthography_node_not_the_exercise_node()
    {
        var ex = First(e => e.Id == "gr.art.008"); // answer "Das"
        var result = await _svc.SubmitAnswerAsync(null, 0, ex.Id, "das", 3000, false);
        Assert.Equal(Outcome.AlmostCorrect, result.Check.Outcome);
        Assert.Null(result.InterferenceNote);
        var journal = await _svc.GetErrorJournalAsync();
        Assert.Single(journal);
        Assert.Equal(AnswerChecker.SlipCapitalisation, journal[0].Code);
        var states = await _svc.GetSkillStatesAsync();
        Assert.Contains("GR.ORTHOGRAFIE", states.Keys);
    }

    [Fact]
    public async Task Answering_every_step_completes_the_session_and_builds_a_streak()
    {
        var session = await _svc.StartSessionAsync(5);
        var view = (await _svc.GetSessionAsync(session.Id))!;
        for (var i = 0; i < view.Steps.Count; i++)
        {
            var ex = view.Exercises[i];
            if (ex.IsProduction) await _svc.SkipStepAsync(session.Id, i);
            else if (ex.Type == ExerciseType.Reading) await _svc.SubmitReadingAsync(session.Id, i, ex.Id, ex.Questions.Select(q => q.CorrectIndex).ToList(), 1000);
            else await _svc.SubmitAnswerAsync(session.Id, i, ex.Id, ex.Type == ExerciseType.MultipleChoice ? ex.CorrectIndex.ToString() : ex.Answers[0], 2000, false);
        }
        var done = (await _svc.GetSessionAsync(session.Id))!;
        Assert.True(done.IsComplete);
        Assert.NotNull(done.Session.EndedUtc);

        var dash = await _svc.GetDashboardAsync();
        Assert.Equal(1, dash.StreakDays);
        Assert.Null(dash.OpenSession);
    }

    [Fact]
    public async Task Abandoned_placement_does_not_count_as_completed()
    {
        var session = await _svc.StartPlacementAsync();
        var view = (await _svc.GetSessionAsync(session.Id))!;
        Assert.Equal(PlacementTest.CoreNodeIds.Count * 2, view.Steps.Count);
        await _svc.SubmitAnswerAsync(session.Id, 0, view.Exercises[0].Id, "", 1000, false);
        await _svc.FinishSessionAsync(session.Id);
        var profile = await _svc.GetProfileAsync();
        Assert.Null(profile.PlacementCompletedUtc);
    }

    [Fact]
    public async Task Completed_placement_marks_the_profile()
    {
        var session = await _svc.StartPlacementAsync();
        var view = (await _svc.GetSessionAsync(session.Id))!;
        for (var i = 0; i < view.Steps.Count; i++)
        {
            var ex = view.Exercises[i];
            await _svc.SubmitAnswerAsync(session.Id, i, ex.Id, ex.Type == ExerciseType.MultipleChoice ? ex.CorrectIndex.ToString() : ex.Answers[0], 2000, false);
        }
        var profile = await _svc.GetProfileAsync();
        Assert.NotNull(profile.PlacementCompletedUtc);
        var states = await _svc.GetSkillStatesAsync();
        Assert.True(PlacementTest.CoreNodeIds.All(states.ContainsKey));
    }

    [Fact]
    public async Task Tutor_evaluation_is_persisted_and_its_known_errors_feed_the_model()
    {
        var ex = First(e => e.Type == ExerciseType.FreeWrite);
        var result = await _svc.SubmitProductionAsync(null, 0, ex.Id, "Ich fahre mit der Auto zur Arbeit und das ist gut.", speaking: false);
        Assert.NotNull(result.Evaluation);
        Assert.Equal(1, _tutor.Evaluations);

        var journal = await _svc.GetErrorJournalAsync();
        Assert.Single(journal); // the unknown code was dropped
        Assert.Equal("KASUS_PRAEP", journal[0].Code);
        Assert.True(journal[0].FromAi);

        var states = await _svc.GetSkillStatesAsync();
        Assert.Contains(ex.NodeId, states.Keys);
        Assert.Contains("GR.KASUS_PRAEPOSITIONEN", states.Keys);

        var recent = await _svc.RecentProductionsAsync();
        Assert.Single(recent);
        Assert.True(recent[0].EvaluatedByAi);
        Assert.Equal("B1.2", recent[0].EstimatedLevel);
    }

    [Fact]
    public async Task Self_check_scores_the_rubric_fraction_and_penalises_short_texts()
    {
        var quiet = new LearningService(_factory, _provider, new NullTutor(), new SessionPlanner(), _clock, NullLogger<LearningService>.Instance);
        var ex = First(e => e.Type == ExerciseType.FreeWrite && e.MinWords >= 100);
        var result = await quiet.SubmitProductionAsync(null, 0, ex.Id, "Nur zehn Wörter sind hier, das ist viel zu wenig Text.", speaking: false);
        Assert.Null(result.Evaluation);
        var score = await quiet.SubmitSelfCheckAsync(result.ProductionId, null, 0, [true, true, true, true]);
        Assert.InRange(score, 0.69, 0.71); // 1.0 * 0.7 because far below MinWords
    }

    [Fact]
    public async Task Generated_exercises_join_the_catalog_and_survive_a_refresh()
    {
        var made = await _svc.GenerateForNodeAsync("GR.PASSIV", 1);
        Assert.Single(made);
        Assert.NotNull(_provider.Catalog.Exercise(made[0].Id));
        Assert.Equal(1, _provider.GeneratedExerciseCount);

        var reading = await _svc.GenerateReadingForNodeAsync("HV.HOEREN_ALLTAG");
        Assert.NotNull(reading);
        Assert.True(reading!.AudioOnly);

        await _provider.RefreshAsync();
        Assert.Equal(2, _provider.GeneratedExerciseCount);
    }

    [Fact]
    public async Task Reading_answers_are_scored_as_a_fraction()
    {
        var ex = First(e => e.Type == ExerciseType.Reading && !e.AudioOnly);
        var chosen = ex.Questions.Select((q, i) => i == 0 ? (q.CorrectIndex + 1) % q.Options.Count : q.CorrectIndex).ToList();
        var result = await _svc.SubmitReadingAsync(null, 0, ex.Id, chosen, 5000);
        Assert.Contains($"{ex.Questions.Count - 1} von {ex.Questions.Count}", result.Check.Expected);
    }

    [Fact]
    public async Task Dashboard_preview_and_readiness_work_without_data()
    {
        var dash = await _svc.GetDashboardAsync();
        Assert.Equal(0, dash.TotalAttempts);
        Assert.False(string.IsNullOrWhiteSpace(dash.NextSessionPreview));
        Assert.Equal(4, dash.Readiness.Modules.Count);
        Assert.True(dash.TutorAvailable);
    }

    [Fact]
    public async Task Reset_wipes_learning_data_but_keeps_seed_content()
    {
        var ex = First(e => e.Type == ExerciseType.Cloze);
        await _svc.SubmitAnswerAsync(null, 0, ex.Id, "x", 1000, false);
        await _svc.ResetAllDataAsync();
        await _provider.RefreshAsync();
        var states = await _svc.GetSkillStatesAsync();
        Assert.Empty(states);
        Assert.True(_provider.Catalog.Exercises.Count >= 700);
    }

    [Fact]
    public async Task Session_error_summary_lists_codes_made_in_that_session()
    {
        var session = await _svc.StartSessionAsync(5);
        var view = (await _svc.GetSessionAsync(session.Id))!;
        var idx = view.Exercises.ToList().FindIndex(e => e.Type is ExerciseType.Cloze or ExerciseType.Transform or ExerciseType.Translate);
        Assert.True(idx >= 0);
        await _svc.SubmitAnswerAsync(session.Id, idx, view.Exercises[idx].Id, "", 1000, false);
        var errors = await _svc.GetSessionErrorsAsync(session.Id);
        Assert.Single(errors);
    }
}
