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

/// <summary>
/// What a chat assistant may read and write through MCP (<c>tools/Lotse.Mcp</c>), against a real SQLite database:
/// it sees the learner's own state and nobody else's, and every write is checked before it lands.
/// </summary>
public sealed class ChatBridgeTests : IAsyncLifetime
{
    private sealed class TestDbFactory(string path) : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<LotseDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private const string Learner = "learner-chat";
    private const string Other = "learner-other";

    private string _dbPath = "";
    private TestDbFactory _factory = default!;
    private ContentCatalogProvider _provider = default!;
    private LearningService _learning = default!;
    private LearningService _otherLearning = default!;
    private ChatBridge _bridge = default!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lotse-chat-{Guid.NewGuid():N}.db");
        _factory = new TestDbFactory(_dbPath);
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = Learner, UserName = Learner }, new ApplicationUser { Id = Other, UserName = Other });
            await db.SaveChangesAsync();
        }
        var contentDir = ContentLoader.ResolveContentDirectory();
        _provider = new ContentCatalogProvider(contentDir, _factory, NullLogger<ContentCatalogProvider>.Instance);
        await _provider.RefreshAsync();
        var telcContent = new TelcContentProvider(TelcContentLoader.Load(contentDir, _provider.Catalog), _provider, _factory, NullLogger<TelcContentProvider>.Instance);

        LearningService Learning(string user) => new(_factory, _provider, new NullTutor(), new SessionPlanner(), TimeProvider.System,
            new FakeCurrentUserAccessor(user), NullLogger<LearningService>.Instance);
        _learning = Learning(Learner);
        _otherLearning = Learning(Other);
        var who = new FakeCurrentUserAccessor(Learner);
        var telc = new TelcService(_factory, _provider, telcContent, new NullTutor(), _learning, TimeProvider.System, who);
        _bridge = new ChatBridge(_factory, _provider, _learning, telc, who, TimeProvider.System, NullLogger<ChatBridge>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureDeletedAsync();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public async Task Status_says_not_measured_instead_of_a_number_for_a_new_learner()
    {
        var status = await _bridge.StatusAsync();
        Assert.Null(status.Readiness);
        Assert.All(status.Modules, m => Assert.Null(m.Readiness));
        Assert.Equal(0, status.TotalAttempts);
    }

    [Fact]
    public async Task Status_carries_telc_points_and_the_exam_date_once_telc_is_the_exam()
    {
        var profile = await _learning.GetProfileAsync();
        profile.Exam = ExamProvider.Telc;
        profile.TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        await _learning.SaveProfileAsync(profile);

        var status = await _bridge.StatusAsync();
        Assert.Equal(30, status.DaysToExam);
        Assert.NotNull(status.Telc);
        Assert.Equal(TelcBlueprint.WrittenPass, status.Telc!.WrittenPass);
        Assert.Null(status.Telc.WrittenPoints); // nothing practised yet
        Assert.Equal(Enum.GetValues<TelcPart>().Length, status.Telc.Parts.Count);
    }

    [Fact]
    public async Task Error_patterns_group_the_learners_own_mistakes_and_not_another_learners()
    {
        var ex = _provider.Catalog.Exercises.First(e => e.Type == ExerciseType.Cloze && e.NodeId == "GR.PASSIV");
        await _otherLearning.SubmitAnswerAsync(null, 0, ex.Id, "falsch", 3000, false);
        var code = _provider.Catalog.ErrorTypes.First(e => e.NodeId == "GR.KASUS_PRAEPOSITIONEN").Code;
        await _bridge.RecordErrorsAsync([new(code, "mit der Bus", "mit dem Bus"), new(code, "seit zwei Monate", "seit zwei Monaten")]);

        var patterns = await _bridge.ErrorPatternsAsync();
        var p = Assert.Single(patterns);
        Assert.Equal(code, p.Code);
        Assert.Equal(2, p.Count);
        Assert.All(p.Examples, e => Assert.True(e.FromAi));
        Assert.Contains(p.Examples, e => e.Wrong == "mit der Bus" && e.Correction == "mit dem Bus");
    }

    [Fact]
    public async Task Recorded_errors_with_an_unknown_code_or_no_wrong_words_are_rejected_with_a_reason()
    {
        var code = _provider.Catalog.ErrorTypes.First().Code;
        var r = await _bridge.RecordErrorsAsync([new("ERFUNDEN", "x", "y"), new(code, " ", "y"), new(code, "ein Fehler", "")]);
        Assert.Single(r.Accepted);
        Assert.Equal(2, r.Rejected.Count);
        Assert.Contains(r.Rejected, m => m.Contains("ERFUNDEN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Valid_exercises_join_the_bank_and_can_be_practised_like_any_other()
    {
        var r = await _bridge.AddExercisesAsync("GR.KASUS_PRAEPOSITIONEN",
        [
            new("cloze", "Ich fahre jeden Tag mit ___ (die U-Bahn) zur Arbeit.", ["der U-Bahn"], Explanation: "mit + Dativ"),
            new("multiplechoice", "Wir warten seit ___ Stunde.", Options: ["eine", "einer", "einen"], CorrectIndex: 1),
        ]);
        Assert.Empty(r.Rejected);
        Assert.Equal(2, r.Accepted.Count);
        Assert.All(r.Accepted, id => Assert.True(TelcContent.IsGenerated(id) || id.StartsWith("gen.", StringComparison.Ordinal)));

        var added = _provider.Catalog.Exercises.Single(e => e.Id == r.Accepted[0]);
        Assert.Equal(ExerciseSource.Generated, added.Source);
        var result = await _learning.SubmitAnswerAsync(null, 0, added.Id, "der U-Bahn", 3000, false);
        Assert.Equal(Outcome.Correct, result.Check.Outcome);
    }

    [Fact]
    public async Task The_app_process_plans_from_exercises_the_mcp_process_added()
    {
        // Two providers over one database: the web app's and the MCP server's, as two processes would have.
        var webCatalog = new ContentCatalogProvider(ContentLoader.ResolveContentDirectory(), _factory, NullLogger<ContentCatalogProvider>.Instance);
        await webCatalog.RefreshAsync();
        var r = await _bridge.AddExercisesAsync("GR.PASSIV", [new("cloze", "Der Vertrag ___ gestern unterschrieben. (werden, Präteritum)", ["wurde"])]);
        Assert.DoesNotContain(webCatalog.Catalog.Exercises, e => e.Id == r.Accepted[0]);

        await webCatalog.RefreshIfChangedAsync();
        Assert.Contains(webCatalog.Catalog.Exercises, e => e.Id == r.Accepted[0]);
        var before = webCatalog.Catalog;
        await webCatalog.RefreshIfChangedAsync();
        Assert.Same(before, webCatalog.Catalog); // nothing new, nothing reloaded
    }

    [Fact]
    public async Task Open_types_bad_clozes_duplicates_and_broken_choices_are_refused()
    {
        var known = _provider.Catalog.ForNode("GR.KASUS_PRAEPOSITIONEN").First().Prompt;
        var r = await _bridge.AddExercisesAsync("GR.KASUS_PRAEPOSITIONEN",
        [
            new("freewrite", "Schreib über deinen Tag."),
            new("cloze", "Ich fahre mit dem Bus.", ["dem"]),
            new("cloze", known, ["x"]),
            new("multiplechoice", "Wir warten seit ___ Stunde.", Options: ["eine", "einer"], CorrectIndex: 5),
            new("translate", "Idem autobusom.", []),
        ]);
        Assert.Empty(r.Accepted);
        Assert.Equal(5, r.Rejected.Count);
        Assert.Contains("freewrite", r.Rejected[0], StringComparison.Ordinal);
        Assert.Contains("___", r.Rejected[1], StringComparison.Ordinal);
        Assert.Contains("schon", r.Rejected[2], StringComparison.Ordinal);
        Assert.Contains("CorrectIndex", r.Rejected[3], StringComparison.Ordinal);
        Assert.Contains("Answers", r.Rejected[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_topic_and_too_many_items_fail_loudly()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _bridge.AddExercisesAsync("GR.GIBT_ES_NICHT", [new("cloze", "a ___", ["b"])]));
        var many = Enumerable.Range(0, ChatBridge.MaxExercisesPerCall + 1).Select(i => new ExerciseDraft("cloze", $"Satz {i} ___", ["x"])).ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => _bridge.AddExercisesAsync("GR.PASSIV", many));
    }

    [Fact]
    public void Topics_list_ids_and_the_error_codes_record_errors_accepts()
    {
        var topics = _bridge.Topics("Grammatik");
        Assert.NotEmpty(topics);
        Assert.All(topics, t => Assert.Equal("Grammatik", t.Area));
        var codes = topics.SelectMany(t => t.ErrorCodes).ToHashSet();
        Assert.Contains(_provider.Catalog.ErrorTypes.First(e => e.NodeId == "GR.KASUS_PRAEPOSITIONEN").Code, codes);
    }
}
