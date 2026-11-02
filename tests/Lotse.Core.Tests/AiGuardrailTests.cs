using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Core.Tests;

/// <summary>The hint ladder never gives the answer away, over the whole exercise bank.</summary>
public sealed class HintLadderTests
{
    private static readonly ContentCatalog Catalog = ContentLoader.Load(ContentLoader.ResolveContentDirectory());

    [Fact]
    public void No_rung_of_any_seed_exercise_contains_an_accepted_answer()
    {
        var leaking = Catalog.Exercises
            .SelectMany(e => HintLadder.For(e, Catalog.Node(e.NodeId)).Select(h => (e, h)))
            .Where(x => HintLadder.Leaks(x.h.Text, x.e))
            .Select(x => $"{x.e.Id}: {x.h.Text}")
            .ToList();
        Assert.Empty(leaking);
    }

    [Fact]
    public void Almost_every_drill_gets_a_ladder_of_more_than_one_rung()
    {
        var drills = Catalog.Exercises.Where(e => e.Type is ExerciseType.Cloze or ExerciseType.MultipleChoice or ExerciseType.Transform or ExerciseType.Translate or ExerciseType.WordOrder or ExerciseType.Vocab).ToList();
        var withTwo = drills.Count(e => HintLadder.For(e, Catalog.Node(e.NodeId)).Count >= 2);
        Assert.True(withTwo >= drills.Count * 0.95, $"nur {withTwo} von {drills.Count} Übungen haben zwei Stufen");
    }

    [Fact]
    public void Free_production_gets_no_ladder()
    {
        var writing = Catalog.Exercises.First(e => e.Type == ExerciseType.FreeWrite);
        Assert.Empty(HintLadder.For(writing, Catalog.Node(writing.NodeId)));
    }

    [Fact]
    public void The_shape_keeps_first_letters_and_hides_a_leading_article()
    {
        Assert.Equal("___ B__", HintLadder.Mask("dem Bus"));
        Assert.Equal("a___________", HintLadder.Mask("aufgestanden"));
        Assert.Equal("w____ i__", HintLadder.Mask("würde ich"));
    }

    [Fact]
    public void Leaks_folds_case_umlauts_and_punctuation_but_needs_whole_words()
    {
        Assert.True(HintLadder.Leaks("Denk an „Würde“!", ["wuerde"]));
        Assert.False(HintLadder.Leaks("Denk an den Bus", ["am"]), "Antworten unter drei Buchstaben zählen nicht");
        Assert.False(HintLadder.Leaks("Denk an Busfahrer", ["Bus"]), "nur ganze Wörter");
    }

    [Fact]
    public void Multiple_choice_strikes_out_a_wrong_option_never_the_right_one()
    {
        var mc = new Exercise { Id = "t", Type = ExerciseType.MultipleChoice, NodeId = "GR.KONNEKTOREN", Band = CefrBand.B2_1, Prompt = "…", Options = ["obwohl", "weil", "trotzdem"], CorrectIndex = 2 };
        var shape = HintLadder.Shape(mc)!;
        Assert.Contains("weil", shape);
        Assert.False(HintLadder.Leaks(shape, mc));
    }
}

/// <summary>The telc content pipeline and the AI hint guard, end to end on a real SQLite file with a scripted tutor.</summary>
public sealed class TelcPipelineTests : IAsyncLifetime
{
    private const string UserId = "pipeline-learner";

    private sealed class TestDbFactory(string path) : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext() => new(new DbContextOptionsBuilder<LotseDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private string _dbPath = "";
    private TestDbFactory _factory = default!;
    private ContentCatalogProvider _catalog = default!;
    private TelcContentProvider _telc = default!;
    private LearningService _learning = default!;
    private readonly ScriptedTutor _tutor = new();
    private TelcGenerator _generator = default!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lotse-pipeline-{Guid.NewGuid():N}.db");
        _factory = new TestDbFactory(_dbPath);
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserId });
            await db.SaveChangesAsync();
        }
        _catalog = new ContentCatalogProvider(ContentLoader.ResolveContentDirectory(), _factory, NullLogger<ContentCatalogProvider>.Instance);
        await _catalog.RefreshAsync();
        _telc = new TelcContentProvider(TelcContentLoader.Load(ContentLoader.ResolveContentDirectory(), _catalog.Catalog), _catalog, _factory, NullLogger<TelcContentProvider>.Instance);
        var user = new FakeCurrentUserAccessor(UserId);
        _learning = new LearningService(_factory, _catalog, _tutor, new SessionPlanner(), TimeProvider.System, user, NullLogger<LearningService>.Instance);
        _generator = new TelcGenerator(_factory, _telc, _catalog, _tutor, _learning, TimeProvider.System, user, NullLogger<TelcGenerator>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureDeletedAsync();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private int _novel;

    /// <summary>A Sprachbausteine-1 task with the seed's nodes but a text and options no other task contains - a new one per call.</summary>
    private string NovelSprachbausteine()
    {
        var n = _novel++;
        var seed = _telc.Seed.Sprachbausteine.First(s => s.Teil == 1);
        var words = Enumerable.Range(0, 120).Select(i => $"wort{n}x{i}").ToList();
        for (var g = 1; g <= 10; g++) words.Insert(g * 11, $"[{g}]");
        var gaps = seed.Gaps.Select((g, i) => g with { Options = [$"a{n}x{i}", $"b{n}x{i}", $"c{n}x{i}"] }).ToList();
        var task = seed with { Id = "x", Title = $"Ein neuer Brief {n}", Text = string.Join(' ', words), Gaps = gaps };
        return TelcDraftCodec.Example(TelcContent.Empty with { Sprachbausteine = [task] }, TelcPart.Sprachbausteine1, "x");
    }

    [Fact]
    public async Task A_valid_new_task_waits_for_review_and_joins_practice_once_approved()
    {
        _tutor.Telc = _ => NovelSprachbausteine();
        var draft = await _generator.GenerateAsync(TelcPart.Sprachbausteine1, "Wohnen");
        Assert.Equal(TelcDraftStatus.Pending, draft.Status);
        Assert.True(draft.CriticOk);
        Assert.Contains("Lösung:", draft.Text);
        Assert.DoesNotContain(draft.Id, _telc.Content.TaskIds(TelcPart.Sprachbausteine1));

        await _generator.ReviewAsync(draft.Id, approve: true);
        Assert.Contains(draft.Id, _telc.Content.TaskIds(TelcPart.Sprachbausteine1));
        Assert.Equal(1, _telc.Content.FindSprachbausteine(draft.Id)!.Teil);

        // A fresh provider (an app restart) reads the approved task back from the database.
        var restarted = new TelcContentProvider(_telc.Seed, _catalog, _factory, NullLogger<TelcContentProvider>.Instance);
        await restarted.RefreshAsync();
        Assert.Contains(draft.Id, restarted.Content.TaskIds(TelcPart.Sprachbausteine1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _generator.ReviewAsync(draft.Id, approve: false));
    }

    [Fact]
    public async Task Copying_the_example_and_breaking_the_format_both_fail_before_any_human_sees_them()
    {
        var seed = _telc.Seed.Sprachbausteine.First(s => s.Teil == 1);
        _tutor.Telc = _ => TelcDraftCodec.Example(_telc.Seed, TelcPart.Sprachbausteine1, seed.Id);
        var copy = await _generator.GenerateAsync(TelcPart.Sprachbausteine1);
        Assert.Equal(TelcDraftStatus.Invalid, copy.Status);
        Assert.Contains(copy.Problems, p => p.StartsWith("Zu nah am Beispiel", StringComparison.Ordinal));

        _tutor.Telc = _ => """{ "title": "kaputt", "text": "[1]", "gaps": null, "box": null }""";
        var broken = await _generator.GenerateAsync(TelcPart.Sprachbausteine1);
        Assert.Equal(TelcDraftStatus.Invalid, broken.Status);

        _tutor.Telc = _ => NovelSprachbausteine().Replace("[10]", "", StringComparison.Ordinal);
        var gapMissing = await _generator.GenerateAsync(TelcPart.Sprachbausteine1);
        Assert.Contains(gapMissing.Problems, p => p.Contains("[10] fehlt", StringComparison.Ordinal));

        Assert.Empty(await _generator.DraftsAsync(TelcDraftStatus.Pending));
    }

    [Fact]
    public async Task Statistics_count_failures_and_measure_how_often_the_critic_agrees_with_the_human()
    {
        _tutor.Telc = _ => NovelSprachbausteine();
        var flagged = 0;
        _tutor.Critique = _ => flagged++ == 0 ? new TaskCritique(false, ["Aufgabe 3: zwei Optionen passen."]) : new TaskCritique(true, []);
        var a = await _generator.GenerateAsync(TelcPart.Sprachbausteine1);   // critic objects, human rejects: agreement
        var b = await _generator.GenerateAsync(TelcPart.Sprachbausteine1);   // critic ok, human approves: agreement
        var c = await _generator.GenerateAsync(TelcPart.Sprachbausteine1);   // critic ok, human rejects: disagreement
        _tutor.Telc = _ => "kein json";
        await _generator.GenerateAsync(TelcPart.Sprachbausteine1);

        Assert.False(a.CriticOk);
        Assert.Single(a.Problems);
        await _generator.ReviewAsync(a.Id, false, "zweideutig");
        await _generator.ReviewAsync(b.Id, true);
        await _generator.ReviewAsync(c.Id, false);

        var stats = await _generator.StatsAsync();
        Assert.Equal((1, 0, 1, 2), (stats.Invalid, stats.Pending, stats.Approved, stats.Rejected));
        Assert.Equal(0.25, stats.AcceptanceRate!.Value, 3);
        Assert.Equal(2 / 3.0, stats.CriticAgreement!.Value, 3);
        Assert.Equal("zweideutig", (await _generator.DraftsAsync(TelcDraftStatus.Rejected)).Single(d => d.Id == a.Id).RejectReason);
    }

    [Fact]
    public async Task Every_objective_part_round_trips_through_the_codec()
    {
        foreach (var part in TelcContent.GeneratableParts)
        {
            var id = _telc.Seed.TaskIds(part)[0];
            var json = TelcDraftCodec.Example(_telc.Seed, part, id);
            var back = TelcDraftCodec.TryRead(part, json, "gen.test")!;
            Assert.Empty(_telc.Validate(back));
            Assert.Equal(TelcTaskText.WithKey(_telc.Seed, part, id).Replace(_telc.Seed.TaskTitle(part, id)!, ""), TelcTaskText.WithKey(back, part, "gen.test").Replace(back.TaskTitle(part, "gen.test")!, ""));
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task An_ai_hint_that_gives_the_answer_away_is_discarded()
    {
        var cloze = _catalog.Catalog.Exercises.First(e => e.Type == ExerciseType.Cloze && e.Answers[0].Length > 3);
        _tutor.Hint = e => $"Probier es mit „{e.Answers[0]}“.";
        Assert.Null(await _learning.GetAiHintAsync(cloze.Id, null));

        _tutor.Hint = _ => "Welcher Fall folgt auf diese Präposition?";
        Assert.Equal("Welcher Fall folgt auf diese Präposition?", await _learning.GetAiHintAsync(cloze.Id, "der"));
    }
}
