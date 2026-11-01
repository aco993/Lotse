using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Infrastructure.Content;
using Microsoft.EntityFrameworkCore;

namespace Lotse.Core.Tests;

public sealed class TelcTests
{
    private static readonly ContentCatalog Catalog = ContentLoader.Load(ContentLoader.ResolveContentDirectory());
    private static readonly TelcContent Content = TelcContentLoader.Load(ContentLoader.ResolveContentDirectory(), Catalog);

    // ---- Blueprint ---------------------------------------------------------------------------------------------

    [Fact]
    public void Blueprint_adds_up_to_the_official_totals()
    {
        Assert.Equal(225, TelcBlueprint.Parts.Where(p => TelcBlueprint.IsWritten(p.Part)).Sum(p => p.MaxPoints));
        Assert.Equal(75, TelcBlueprint.Parts.Where(p => !TelcBlueprint.IsWritten(p.Part)).Sum(p => p.MaxPoints));
        Assert.Equal(75, TelcBlueprint.SubtestMax(TelcSubtest.Leseverstehen));
        Assert.Equal(30, TelcBlueprint.SubtestMax(TelcSubtest.Sprachbausteine));
        Assert.Equal(75, TelcBlueprint.SubtestMax(TelcSubtest.Hoerverstehen));
        Assert.Equal(45, TelcBlueprint.SubtestMax(TelcSubtest.SchriftlicherAusdruck));
        // Leseverstehen and Sprachbausteine share one 90-minute block.
        Assert.Equal(TelcBlueprint.ReadingAndLanguageMinutes, TelcBlueprint.Parts.Where(p => p.Subtest is TelcSubtest.Leseverstehen or TelcSubtest.Sprachbausteine).Sum(p => p.Minutes));
        Assert.Equal(Enum.GetValues<TelcPart>(), TelcBlueprint.Parts.Select(p => p.Part));
    }

    [Theory]
    [InlineData(TelcPart.Lesen1, 5.0)]
    [InlineData(TelcPart.Lesen3, 2.5)]
    [InlineData(TelcPart.Sprachbausteine2, 1.5)]
    [InlineData(TelcPart.Hoeren2, 2.5)]
    [InlineData(TelcPart.Hoeren3, 5.0)]
    public void Items_are_worth_what_the_part_divides_by(TelcPart part, double perItem)
        => Assert.Equal(perItem, TelcBlueprint.PointsPerItem(part));

    [Fact]
    public void Written_pass_is_aggregate_oral_pass_is_separate()
    {
        // Weak email (9 of 45), strong reading: the written half still passes because it is added up.
        var points = new Dictionary<TelcPart, double>
        {
            [TelcPart.Lesen1] = 25,
            [TelcPart.Lesen2] = 20,
            [TelcPart.Lesen3] = 22.5,
            [TelcPart.Sprachbausteine1] = 10.5,
            [TelcPart.Sprachbausteine2] = 9,
            [TelcPart.Hoeren1] = 20,
            [TelcPart.Hoeren2] = 20,
            [TelcPart.Hoeren3] = 20,
            [TelcPart.Schreiben] = 9,
            [TelcPart.Sprechen1] = 15,
            [TelcPart.Sprechen2] = 15,
            [TelcPart.Sprechen3] = 14,
        };
        var result = new TelcResult(points);
        Assert.Equal(156, result.Written);
        Assert.True(result.WrittenPassed);
        // 44 oral points: one short of 45 - and no surplus in the written half helps.
        Assert.Equal(44, result.Oral);
        Assert.False(result.OralPassed);
        Assert.Equal("nicht bestanden", result.Grade);

        points[TelcPart.Sprechen3] = 15;
        var passed = new TelcResult(points);
        Assert.True(passed.Passed);
        Assert.Equal("ausreichend", passed.Grade);
    }

    [Fact]
    public void A_partial_result_has_no_grade()
    {
        var result = new TelcResult(new Dictionary<TelcPart, double> { [TelcPart.Lesen1] = 25 });
        Assert.Null(result.Grade);
        Assert.False(result.WrittenPassed);
    }

    [Theory]
    [InlineData(270, "sehr gut")]
    [InlineData(269.5, "gut")]
    [InlineData(240, "gut")]
    [InlineData(210, "befriedigend")]
    [InlineData(180, "ausreichend")]
    [InlineData(179.5, "nicht bestanden")]
    public void Grades_follow_the_official_bands(double total, string grade) => Assert.Equal(grade, TelcBlueprint.Grade(total));

    // ---- Ratings -----------------------------------------------------------------------------------------------

    [Fact]
    public void Writing_rating_is_three_times_the_band_sum()
    {
        Assert.Equal(45, new TelcWritingRating(TelcBand.A, TelcBand.A, TelcBand.A).Points);
        Assert.Equal(27, new TelcWritingRating(TelcBand.B, TelcBand.B, TelcBand.B).Points);
        Assert.Equal(18, new TelcWritingRating(TelcBand.A, TelcBand.C, TelcBand.D).Points);
        Assert.Equal(0, new TelcWritingRating(TelcBand.A, TelcBand.A, TelcBand.A, ThemaVerfehlt: true).Points);
    }

    [Fact]
    public void Speaking_rating_is_seven_seven_seven_four()
    {
        Assert.Equal(25, new TelcSpeakingRating(TelcBand.A, TelcBand.A, TelcBand.A, TelcBand.A).Points);
        Assert.Equal(17, new TelcSpeakingRating(TelcBand.B, TelcBand.B, TelcBand.B, TelcBand.B).Points);
        Assert.Equal(0, new TelcSpeakingRating(TelcBand.D, TelcBand.D, TelcBand.D, TelcBand.D).Points);
    }

    // ---- Writing check -----------------------------------------------------------------------------------------

    [Fact]
    public void Email_without_subject_cannot_get_an_A_in_criterion_II()
    {
        var task = Content.FindSchreiben("sa.sprachschule-fitness")!.A;
        var text = task.ModelAnswer!.Replace("Betreff: Anfrage zu Ihren Intensivkursen Deutsch B2\n\n", "");
        var check = TelcWritingCheck.Analyze(text, task);
        Assert.False(check.HasBetreff);
        Assert.Equal(TelcBand.B, check.MaxGestaltung);
        var capped = new TelcWritingRating(TelcBand.A, TelcBand.A, TelcBand.A).Capped(check);
        Assert.Equal(TelcBand.B, capped.Gestaltung);
        Assert.Contains(check.Hints(task.Leitpunkte), h => h.Contains("Betreff"));
    }

    [Fact]
    public void A_list_without_connectors_is_capped_at_C()
    {
        var text = "Betreff: Kurs\n\nSehr geehrte Damen und Herren,\n\nich möchte einen Kurs machen. Ich arbeite viel. Ich habe abends Zeit. Ich will die Prüfung machen. Was kostet der Kurs?\n\nMit freundlichen Grüßen\nA. M.";
        var check = TelcWritingCheck.Analyze(text);
        Assert.True(check.FormComplete);
        Assert.True(check.Linear);
        Assert.Equal(TelcBand.C, check.MaxGestaltung);
        Assert.False(check.LongEnough);
    }

    [Fact]
    public void Every_model_answer_passes_its_own_checks()
    {
        foreach (var task in Content.Schreiben.SelectMany(p => new[] { p.A, p.B }).Where(t => t.ModelAnswer is not null))
        {
            var check = TelcWritingCheck.Analyze(task.ModelAnswer!, task);
            Assert.True(check.LongEnough, $"{task.Id}: {check.Words} Wörter");
            Assert.True(check.FormComplete, $"{task.Id}: Betreff/Anrede/Schluss");
            Assert.False(check.Linear, $"{task.Id}: nur {check.Connectors} Verknüpfungen");
            Assert.False(check.InformalSlip, $"{task.Id}: Register");
            Assert.True(check.LeitpunkteHit >= 3, $"{task.Id}: nur {check.LeitpunkteHit} Leitpunkte erkannt");
            Assert.Equal(TelcBand.A, check.MaxGestaltung);
        }
    }

    [Fact]
    public void Informal_address_in_a_formal_email_is_flagged()
    {
        var task = Content.FindSchreiben("sa.sprachschule-fitness")!.A;
        var check = TelcWritingCheck.Analyze(task.ModelAnswer!.Replace("Könnten Sie mir bitte", "Kannst du mir bitte"), task);
        Assert.True(check.InformalSlip);
        Assert.Equal(TelcBand.B, check.MaxGestaltung);
    }

    // ---- Content -----------------------------------------------------------------------------------------------

    [Fact]
    public void Content_loads_and_has_two_full_exams()
    {
        foreach (var part in Enum.GetValues<TelcPart>())
            Assert.True(Content.TaskIds(part).Count >= 2, $"{part}: weniger als zwei Aufgaben");
        Assert.True(Content.Erfahrungen.Count >= 7, "Teil 1 braucht eine Liste von mindestens sieben Themen wie in der Prüfung");
    }

    [Fact]
    public void Objective_keys_have_one_entry_per_item()
    {
        foreach (var part in Enum.GetValues<TelcPart>().Where(TelcBlueprint.IsObjective))
            foreach (var id in Content.TaskIds(part))
                Assert.Equal(TelcBlueprint.Info(part).Items, Content.Key(part, id)!.Count);
    }

    [Fact]
    public void Answer_positions_are_spread_so_guessing_a_letter_does_not_pass()
    {
        var sb1 = Content.Sprachbausteine.Where(s => s.Teil == 1).SelectMany(s => s.Gaps).Select(g => g.Correct).ToList();
        foreach (var letter in new[] { 0, 1, 2 })
            Assert.InRange(sb1.Count(c => c == letter) / (double)sb1.Count, 0.2, 0.5);
        var lesen2 = Content.Lesen2.SelectMany(t => t.Questions).Select(q => q.Correct).ToList();
        Assert.True(lesen2.Distinct().Count() == 3);
    }

    [Fact]
    public void Validation_catches_broken_tasks()
    {
        var broken = TelcContent.Empty with
        {
            Sprachbausteine = [new TelcSprachbausteine("x", 2, "x", "Nur [1] eine Lücke", [new TelcGap(0, "GR.NOPE")], ["a"])],
            Lesen3 = [new TelcLesen3("y", "y", ["s"], [new TelcAd("a", "a")], [0])],
        };
        var problems = broken.Validate(Catalog.Nodes.Select(n => n.Id).ToHashSet());
        Assert.Contains(problems, p => p.Contains("10 Lücken"));
        Assert.Contains(problems, p => p.Contains("GR.NOPE"));
        Assert.Contains(problems, p => p.Contains("15 Wörtern"));
        Assert.Contains(problems, p => p.Contains("12 Anzeigen"));
    }

    [Fact]
    public void Objective_scoring_counts_only_exact_answers()
    {
        var key = Content.Key(TelcPart.Lesen3, "lv3.freizeit")!;
        var answers = key.Select(k => (int?)k).ToList();
        Assert.Equal(25, TelcObjectiveScoring.Points(TelcPart.Lesen3, key, answers));
        answers[6] = 3; // "x" was right
        answers[0] = null;
        Assert.Equal(20, TelcObjectiveScoring.Points(TelcPart.Lesen3, key, answers));
        Assert.Equal("x", TelcObjectiveScoring.Letter(-1));
        Assert.Equal("o", TelcObjectiveScoring.Letter(14));
    }
}

/// <summary>The telc service end to end against a temporary SQLite file: scoring, the learner model, caps, simulation.</summary>
public sealed class TelcServiceTests : IAsyncLifetime
{
    private const string UserId = "telc-learner";

    private sealed class TestDbFactory(string path) : Microsoft.EntityFrameworkCore.IDbContextFactory<Lotse.Infrastructure.Data.LotseDbContext>
    {
        public Lotse.Infrastructure.Data.LotseDbContext CreateDbContext()
            => new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Lotse.Infrastructure.Data.LotseDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private string _dbPath = "";
    private TestDbFactory _factory = default!;
    private Lotse.Infrastructure.Services.TelcService _svc = default!;
    private TelcContent _content = default!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lotse-telc-{Guid.NewGuid():N}.db");
        _factory = new TestDbFactory(_dbPath);
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new Lotse.Infrastructure.Data.ApplicationUser { Id = UserId, UserName = UserId });
            await db.SaveChangesAsync();
        }
        var provider = new ContentCatalogProvider(ContentLoader.ResolveContentDirectory(), _factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentCatalogProvider>.Instance);
        await provider.RefreshAsync();
        _content = TelcContentLoader.Load(ContentLoader.ResolveContentDirectory(), provider.Catalog);
        var user = new FakeCurrentUserAccessor(UserId);
        var learning = new Lotse.Infrastructure.Services.LearningService(_factory, provider, new Lotse.Core.Tutor.NullTutor(), new Lotse.Core.Engine.SessionPlanner(), TimeProvider.System,
            user, Microsoft.Extensions.Logging.Abstractions.NullLogger<Lotse.Infrastructure.Services.LearningService>.Instance);
        _svc = new Lotse.Infrastructure.Services.TelcService(_factory, provider, _content, new Lotse.Core.Tutor.NullTutor(), learning, TimeProvider.System, user);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureDeletedAsync();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public async Task Sprachbausteine_mistakes_reach_the_error_journal_with_the_gaps_code()
    {
        var task = _content.Sprachbausteine.First(s => s.Teil == 1);
        var answers = task.Gaps.Select(g => (int?)g.Correct).ToList();
        answers[0] = (task.Gaps[0].Correct + 1) % 3;
        answers[1] = null;

        var result = await _svc.SubmitObjectiveAsync(TelcPart.Sprachbausteine1, task.Id, answers);
        Assert.Equal(12, result.Points);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(10, db.Attempts.Count(a => a.UserId == UserId && a.ExerciseId.StartsWith("telc." + task.Id)));
        var errors = db.ErrorEvents.Where(e => e.UserId == UserId).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Code == task.Gaps[0].ErrorCode);
        Assert.Single(db.TelcAttempts.Where(a => a.UserId == UserId));
    }

    [Fact]
    public async Task Email_points_are_capped_by_the_checks_whatever_the_rating_says()
    {
        var task = _content.Schreiben[0].A;
        var text = task.ModelAnswer!.Replace("Betreff: Anfrage zu Ihren Intensivkursen Deutsch B2\n\n", "");
        var result = await _svc.SubmitProductionAsync(TelcPart.Schreiben, task.Id, text, [TelcBand.A, TelcBand.A, TelcBand.A]);
        Assert.Equal(TelcBand.B, result.Bands[1]);
        Assert.Equal(39, result.Points); // 3 · (5 + 3 + 5)
        Assert.False(result.Check!.HasBetreff);
    }

    [Fact]
    public async Task A_simulation_draws_every_part_and_counts_each_part_once()
    {
        var sim = await _svc.StartOrResumeSimulationAsync();
        Assert.Equal(12, sim.Tasks.Count);
        Assert.Equal(TelcPart.Lesen1, sim.Next);
        Assert.Equal(sim.Id, (await _svc.StartOrResumeSimulationAsync()).Id); // resumed, not a second one

        var key = _content.Key(TelcPart.Lesen1, sim.Tasks[TelcPart.Lesen1])!;
        await _svc.SubmitObjectiveAsync(TelcPart.Lesen1, sim.Tasks[TelcPart.Lesen1], key.Select(k => (int?)k).ToList(), simulationId: sim.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.SubmitObjectiveAsync(TelcPart.Lesen1, sim.Tasks[TelcPart.Lesen1], key.Select(k => (int?)k).ToList(), simulationId: sim.Id));

        var view = (await _svc.GetSimulationAsync(sim.Id))!;
        Assert.Equal(25, view.Result.Written);
        Assert.Equal(TelcPart.Lesen2, view.Next);

        await _svc.AbandonSimulationAsync(sim.Id);
        Assert.Null(await _svc.GetSimulationAsync(sim.Id));
        var history = await _svc.HistoryAsync(TelcPart.Lesen1);
        Assert.Single(history);
        Assert.False(history[0].Simulation); // kept as a practice result

        var overview = await _svc.GetOverviewAsync();
        Assert.Equal(25, overview.Readiness.Of(TelcPart.Lesen1).Points);
    }

    [Fact]
    public async Task Practice_rotates_to_the_task_done_least_recently()
    {
        var first = await _svc.NextTaskIdAsync(TelcPart.Lesen2);
        var key = _content.Key(TelcPart.Lesen2, first!)!;
        await _svc.SubmitObjectiveAsync(TelcPart.Lesen2, first!, key.Select(k => (int?)k).ToList());
        Assert.NotEqual(first, await _svc.NextTaskIdAsync(TelcPart.Lesen2));
    }
}
