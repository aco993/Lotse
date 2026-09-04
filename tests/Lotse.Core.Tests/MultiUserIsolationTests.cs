using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Ai;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Core.Tests;

/// <summary>
/// Two learners share one database, one <see cref="ContentCatalogProvider"/> and (for <see cref="TutorRegistry"/>)
/// one <see cref="IDataProtectionProvider"/> - only their <see cref="FakeCurrentUserAccessor"/> id differs, exactly
/// as two real Blazor Server circuits would. Every assertion here proves a concrete leak does NOT happen, rather
/// than trusting the scoping code by reading it: this is the one place account isolation is actually exercised
/// end to end, most importantly for the AI tutor's API key (<see cref="TutorRegistry"/>/<c>SettingEntity</c>) -
/// the one piece of data that must never be readable, let alone usable, from another account.
/// </summary>
public sealed class MultiUserIsolationTests : IAsyncLifetime
{
    private sealed class TestDbFactory(string path) : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<LotseDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private const string UserA = "learner-a";
    private const string UserB = "learner-b";

    private string _dbPath = "";
    private TestDbFactory _factory = default!;
    private ContentCatalogProvider _provider = default!;
    private LearningService _svcA = default!;
    private LearningService _svcB = default!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lotse-isolation-{Guid.NewGuid():N}.db");
        _factory = new TestDbFactory(_dbPath);
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            // Every per-learner row has a real FK to AspNetUsers – seed both identities this test uses.
            db.Users.AddRange(new ApplicationUser { Id = UserA, UserName = UserA }, new ApplicationUser { Id = UserB, UserName = UserB });
            await db.SaveChangesAsync();
        }
        _provider = new ContentCatalogProvider(ContentLoader.ResolveContentDirectory(), _factory, NullLogger<ContentCatalogProvider>.Instance);
        await _provider.RefreshAsync();

        LearningService Build(string userId) => new(_factory, _provider, new NullTutor(), new SessionPlanner(), TimeProvider.System,
            new FakeCurrentUserAccessor(userId), NullLogger<LearningService>.Instance);
        _svcA = Build(UserA);
        _svcB = Build(UserB);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureDeletedAsync();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public async Task Skill_state_from_one_learner_is_invisible_to_the_other()
    {
        var ex = _provider.Catalog.Exercises.First(e => e.Type == ExerciseType.Cloze && e.NodeId == "GR.PASSIV");
        await _svcA.SubmitAnswerAsync(null, 0, ex.Id, ex.Answers[0], 3000, false);

        var statesA = await _svcA.GetSkillStatesAsync();
        var statesB = await _svcB.GetSkillStatesAsync();
        Assert.Contains("GR.PASSIV", statesA.Keys);
        Assert.Empty(statesB);
    }

    [Fact]
    public async Task Sessions_and_dashboards_do_not_cross_accounts()
    {
        var sessionA = await _svcA.StartSessionAsync(10);
        await _svcB.StartSessionAsync(10);

        var recentA = await _svcA.RecentSessionsAsync();
        var recentB = await _svcB.RecentSessionsAsync();
        Assert.Single(recentA);
        Assert.Single(recentB);
        Assert.NotEqual(recentA[0].Id, recentB[0].Id);

        // B must not be able to reach A's session by id, even though ids are just guessed/known GUIDs.
        var bSeesAsSession = await _svcB.GetSessionAsync(sessionA.Id);
        Assert.Null(bSeesAsSession);

        var dashA = await _svcA.GetDashboardAsync();
        var dashB = await _svcB.GetDashboardAsync();
        Assert.NotNull(dashA.OpenSession);
        Assert.NotNull(dashB.OpenSession);
        Assert.NotEqual(dashA.OpenSession!.Id, dashB.OpenSession!.Id);
    }

    /// <summary>
    /// Reset is eight scoped bulk deletes; this checks every one of them, not just the first. Each learner gets a
    /// session, a wrong answer (attempt + error event + skill/review state) and a profile row; A additionally
    /// stores tutor settings, which a reset must NOT touch. After A's reset: A has no learning rows left in any
    /// table, B has lost nothing, and A's tutor row is still there.
    /// </summary>
    [Fact]
    public async Task Reset_empties_every_table_of_the_caller_and_none_of_the_other()
    {
        var ex = _provider.Catalog.Exercises.First(e => e.Type == ExerciseType.Cloze && e.NodeId == "GR.PASSIV");
        foreach (var svc in new[] { _svcA, _svcB })
        {
            await svc.StartSessionAsync(10);
            await svc.SubmitAnswerAsync(null, 0, ex.Id, "völlig falsch", 1000, false);
            await svc.GetProfileAsync();
        }
        var dp = new EphemeralDataProtectionProvider();
        await new TutorRegistry(_factory, dp, NullLoggerFactory.Instance, new FakeCurrentUserAccessor(UserA), env: _ => null)
            .SaveAsync(new TutorSettings("groq", null, "llama-3.3-70b-versatile", "gsk_A_secret"));

        await _svcA.ResetAllDataAsync();

        await using var db = _factory.CreateDbContext();
        var skillOwners = await db.SkillStates.Select(s => EF.Property<string>(s, LotseDbContext.UserIdShadow)).ToListAsync();
        var reviewOwners = await db.ReviewStates.Select(r => EF.Property<string>(r, LotseDbContext.UserIdShadow)).ToListAsync();
        var owners = new Dictionary<string, List<string>>
        {
            ["SkillStates"] = skillOwners,
            ["ReviewStates"] = reviewOwners,
            ["Sessions"] = await db.Sessions.Select(s => s.UserId).ToListAsync(),
            ["Attempts"] = await db.Attempts.Select(a => a.UserId).ToListAsync(),
            ["ErrorEvents"] = await db.ErrorEvents.Select(e => e.UserId).ToListAsync(),
            ["Profiles"] = await db.Profiles.Select(p => p.UserId).ToListAsync(),
        };
        foreach (var (table, ids) in owners)
        {
            Assert.DoesNotContain(UserA, ids); // A: gone from every table
            Assert.Contains(UserB, ids);       // B: untouched in every table
        }
        Assert.Single(await db.Settings.Where(s => s.UserId == UserA).ToListAsync()); // tutor setup survives a reset
    }

    /// <summary>
    /// <see cref="TutorRegistry"/> caches a built tutor per instance. If the learner behind its accessor ever
    /// changes (the "circuit outlives a login" scenario the guard exists for), the next check must reload - the
    /// previous learner's API key must not be used for the next one.
    /// </summary>
    [Fact]
    public async Task Tutor_registry_follows_the_signed_in_learner_when_the_accessor_switches()
    {
        var dp = new EphemeralDataProtectionProvider();
        TutorRegistry Build(ICurrentUserAccessor accessor) => new(_factory, dp, NullLoggerFactory.Instance, accessor, env: _ => null);
        await Build(new FakeCurrentUserAccessor(UserA)).SaveAsync(new TutorSettings("groq", null, "llama-3.3-70b-versatile", "gsk_A_secret"));
        await Build(new FakeCurrentUserAccessor(UserB)).SaveAsync(new TutorSettings("groq", null, "llama-3.3-70b-versatile", "gsk_B_secret"));

        var accessor = new FakeCurrentUserAccessor(UserA);
        var registry = Build(accessor);
        Assert.Null(registry.Settings.ApiKey); // nothing loaded yet - defaults, never someone else's row

        await registry.EnsureCurrentAsync();
        Assert.Equal("gsk_A_secret", registry.Settings.ApiKey);
        Assert.True(registry.IsAvailable);

        accessor.UserId = UserB;
        await registry.EnsureCurrentAsync();
        Assert.Equal("gsk_B_secret", registry.Settings.ApiKey);

        accessor.UserId = UserA;
        await registry.EnsureCurrentAsync();
        Assert.Equal("gsk_A_secret", registry.Settings.ApiKey);
    }

    [Fact]
    public async Task Error_journal_and_session_errors_stay_within_one_account()
    {
        var ex = _provider.Catalog.Exercises.First(e => e.Type == ExerciseType.Cloze && e.NodeId == "GR.PASSIV");
        await _svcA.SubmitAnswerAsync(null, 0, ex.Id, "völlig falsch", 3000, false);

        var journalA = await _svcA.GetErrorJournalAsync();
        var journalB = await _svcB.GetErrorJournalAsync();
        Assert.Single(journalA);
        Assert.Empty(journalB);
    }

    /// <summary>
    /// The one piece of data that must never leak: the AI tutor's API key. Two <see cref="TutorRegistry"/>
    /// instances share the same DB and Data Protection keyring (as two Blazor Server circuits would); each saves
    /// a different key, and each must see only its own after a fresh reload - never the other's.
    /// </summary>
    [Fact]
    public async Task Tutor_api_key_is_isolated_per_account()
    {
        var dp = new EphemeralDataProtectionProvider();
        TutorRegistry Build(string userId) => new(_factory, dp, NullLoggerFactory.Instance, new FakeCurrentUserAccessor(userId), env: _ => null);

        var tutorA = Build(UserA);
        var tutorB = Build(UserB);
        await tutorA.SaveAsync(new TutorSettings("groq", null, "llama-3.3-70b-versatile", "gsk_A_secret"));
        await tutorB.SaveAsync(new TutorSettings("groq", null, "llama-3.3-70b-versatile", "gsk_B_secret"));

        // Fresh instances, as a new circuit would create: each must load its own key, never the other's.
        var reloadedA = Build(UserA);
        var reloadedB = Build(UserB);
        await reloadedA.InitializeAsync();
        await reloadedB.InitializeAsync();

        Assert.Equal("gsk_A_secret", reloadedA.Settings.ApiKey);
        Assert.Equal("gsk_B_secret", reloadedB.Settings.ApiKey);
        Assert.NotEqual(reloadedA.Settings.ApiKey, reloadedB.Settings.ApiKey);

        await using var db = _factory.CreateDbContext();
        var rows = await db.Settings.ToListAsync();
        Assert.Equal(2, rows.Count); // one row per account, not one shared row
        Assert.All(rows, r => Assert.DoesNotContain("secret", r.Value)); // never stored in clear text
    }
}
