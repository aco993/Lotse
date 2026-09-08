using System.Text.Json;
using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Services;

/// <summary>One step of a stored session plan.</summary>
public sealed class StepRecord
{
    public StepKind Kind { get; set; }
    public required string ExerciseId { get; set; }
    public string Reason { get; set; } = "";
    public bool Done { get; set; }
    public double? Score { get; set; }
}

public sealed record SessionView(SessionEntity Session, IReadOnlyList<StepRecord> Steps, IReadOnlyList<Exercise> Exercises)
{
    public int NextIndex => Steps.ToList().FindIndex(s => !s.Done);
    public bool IsComplete => Steps.All(s => s.Done);
}

public sealed record AnswerResult(CheckResult Check, Exercise Exercise, SkillNode? Node, double MasteryAfter, string? InterferenceNote, bool SessionComplete);

public sealed record ProductionResult(long ProductionId, ProductionEvaluation? Evaluation, IReadOnlyList<string> Rubric, string? ModelAnswer, int WordCount, int? MinWords, string? TutorError, string Text = "")
{
    /// <summary>Rule-based findings when no tutor evaluated the text (see <see cref="FreeTextAnalyzer"/>); empty with an AI evaluation.</summary>
    public IReadOnlyList<TextFinding> Findings { get; init; } = [];
    public IReadOnlyList<string> Hints { get; init; } = [];
}

public sealed record DashboardModel(
    LearnerProfile Profile,
    ReadinessReport Readiness,
    /// <summary>Mastery of the C1 and B2.2 nodes; null unless the learner aims at C1.</summary>
    ModuleReadiness? C1Proximity,
    /// <summary>Change in readiness against roughly a week ago; null while there is no comparable snapshot.</summary>
    double? ReadinessTrend,
    /// <summary>Finished minutes since Monday, against <see cref="LearnerProfile.WeeklyGoalMinutes"/>.</summary>
    int MinutesThisWeek,
    /// <summary>Two lines about the week that just ended - Mondays only, and only with data.</summary>
    WeeklyReview? WeeklyReview,
    IReadOnlyList<WeakArea> WeakAreas,
    IReadOnlyList<(string Code, string Title, int Count, string NodeTitle)> TopErrors,
    int StreakDays,
    int DueReviews,
    int PendingRechecks,
    int MinutesToday,
    int SessionsLast7Days,
    int TotalAttempts,
    SessionEntity? OpenSession,
    string NextSessionPreview,
    bool TutorAvailable,
    string TutorDescription);

/// <summary>Deterministic Monday summary of the previous week - no tutor, no cloud, just what the tables say.</summary>
public sealed record WeeklyReview(int Attempts, double Accuracy, int Stuck, string? WeakestNodeTitle);

public sealed record LessonCard(Lesson Lesson, LessonProgressEntity? Progress, bool IsRecommended)
{
    public bool IsCompleted => Progress?.CompletedUtc is not null;
    public bool IsStarted => Progress is not null && !IsCompleted;
}

public sealed record CourseOverview(IReadOnlyList<LessonCard> Lessons, int Completed, LessonCard? Next);

public sealed record ErrorJournalEntry(DateTime Utc, string Code, string Title, string NodeTitle, string? Snippet, string? Correction, bool FromAi, int Severity);

/// <summary>
/// Application service: the only place that talks to both the database and the engine. Blazor pages call this,
/// never the DbContext directly. Every public method opens its own short-lived DbContext (safe for Blazor Server).
///
/// Every method scopes its queries and inserts to <see cref="ICurrentUserAccessor"/>'s learner — that is the one
/// seam accounts enter the persistence layer through, so <see cref="ILearningService"/>'s public shape stays
/// exactly what it was before accounts existed (no <c>userId</c> parameter anywhere in the interface; Blazor
/// Server's DI scope is one circuit = one signed-in learner, so a Scoped accessor is enough). <see cref="SkillState"/>
/// and <see cref="ReviewState"/> stay pure Core domain objects with no UserId property of their own; their scoping
/// lives as an EF Core shadow property on <see cref="LotseDbContext"/> instead, read/written here via
/// <c>EF.Property&lt;string&gt;</c> — the learning engine and its unit tests never need to know accounts exist.
/// </summary>
public sealed class LearningService(
    IDbContextFactory<LotseDbContext> dbFactory,
    ContentCatalogProvider catalogProvider,
    ITutor tutor,
    SessionPlanner planner,
    TimeProvider clock,
    ICurrentUserAccessor currentUser,
    ILogger<LearningService> logger) : ILearningService
{
    private ContentCatalog Catalog => catalogProvider.Catalog;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public ITutor Tutor => tutor;

    private Task<string> UserIdAsync() => currentUser.GetUserIdAsync();

    // ------------------------------------------------------------------------------------------
    // Profile & dashboard
    // ------------------------------------------------------------------------------------------

    public async Task<LearnerProfile> GetProfileAsync(CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profile = await db.Profiles.FindAsync([userId], ct);
        if (profile is not null) return profile;

        profile = new LearnerProfile { UserId = userId, CreatedUtc = Now };
        db.Profiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(ct);
            return profile;
        }
        catch (DbUpdateException)
        {
            // Two circuits raced to create this learner's profile row; the other one won.
            db.Entry(profile).State = EntityState.Detached;
            return await db.Profiles.AsNoTracking().FirstAsync(p => p.UserId == userId, ct);
        }
    }

    public async Task SaveProfileAsync(LearnerProfile profile, CancellationToken ct = default)
    {
        // The caller hands in the object it got from GetProfileAsync, but the row written is always the signed-in
        // learner's - this method must not be the one way to overwrite somebody else's profile.
        profile.UserId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Profiles.Update(profile);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The tutor, re-checked against the signed-in learner first (see <see cref="ILearnerBound"/>) - the
    /// cached <see cref="ITutor.IsAvailable"/>/<see cref="ITutor.Description"/> would otherwise be stale on the
    /// first read of a fresh circuit (dashboard after login) or after a logout/login on a surviving one.</summary>
    private async Task<ITutor> TutorAsync(CancellationToken ct)
    {
        if (tutor is ILearnerBound bound) await bound.EnsureCurrentAsync(ct);
        return tutor;
    }

    public async Task<DashboardModel> GetDashboardAsync(CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(ct);
        var userId = profile.UserId;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = Now;
        var states = await db.SkillStates.AsNoTracking().Where(s => EF.Property<string>(s, LotseDbContext.UserIdShadow) == userId).ToDictionaryAsync(s => s.NodeId, ct);
        var errors = await RecentErrorsAsync(db, userId, 30, ct);
        var due = await db.ReviewStates.CountAsync(r => EF.Property<string>(r, LotseDbContext.UserIdShadow) == userId && r.DueUtc <= now, ct);
        var rechecks = states.Values.Count(s => s.RecheckDueUtc is not null && s.RecheckDueUtc <= now);

        var since7 = now.AddDays(-7);
        var sessions7 = await db.Sessions.CountAsync(s => s.UserId == userId && s.EndedUtc != null && s.EndedUtc >= since7, ct);
        var todayStart = now.Date;
        var todaySessions = await db.Sessions.Where(s => s.UserId == userId && s.StartedUtc >= todayStart).ToListAsync(ct);
        // Only finished sessions count; an open tab left overnight must not inflate the number.
        var minutesToday = (int)todaySessions.Where(s => s.EndedUtc != null).Sum(s => Math.Min(60, (s.EndedUtc!.Value - s.StartedUtc).TotalMinutes));
        // The week starts on Monday (German calendar), and only finished sessions count - same rule as the day.
        var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        var weekSessions = await db.Sessions.Where(s => s.UserId == userId && s.EndedUtc != null && s.StartedUtc >= weekStart).ToListAsync(ct);
        var minutesThisWeek = (int)weekSessions.Sum(s => Math.Min(60, (s.EndedUtc!.Value - s.StartedUtc).TotalMinutes));
        var weeklyReview = await WeeklyReviewAsync(db, userId, now, ct);
        var open = await db.Sessions.Where(s => s.UserId == userId && s.EndedUtc == null && s.StartedUtc >= now.AddHours(-12)).OrderByDescending(s => s.StartedUtc).FirstOrDefaultAsync(ct);
        var totalAttempts = await db.Attempts.CountAsync(a => a.UserId == userId, ct);

        var streak = await ComputeStreakAsync(db, userId, now, ct);
        var readiness = LearnerAnalysis.Readiness(Catalog, states);
        // No snapshot and no trend before there is a measured number: a trend of the prior against the prior is noise.
        var readinessTrend = readiness.HasEvidence ? await ReadinessTrendAsync(db, userId, readiness.Overall, readiness.Modules, now, ct) : null;
        // Only computed for a C1 learner: a B2 learner has no use for a number about material they are not aiming at.
        var c1 = profile.TargetLevel == TargetLevel.C1 ? LearnerAnalysis.C1Proximity(Catalog, states) : null;
        var weak = LearnerAnalysis.WeakAreas(Catalog, states, errors, now);
        var topErrors = LearnerAnalysis.TopErrorCodes(Catalog, errors, now);

        var input = await BuildPlannerInputAsync(db, userId, profile.DailyMinutes, null, ct);
        var preview = planner.Plan(input with { Seed = now.DayOfYear }).Summary;

        var t = await TutorAsync(ct);
        return new DashboardModel(profile, readiness, c1, readinessTrend, minutesThisWeek, weeklyReview, weak, topErrors, streak, due, rechecks, minutesToday, sessions7, totalAttempts, open, preview, t.IsAvailable, t.Description);
    }

    private static async Task<int> ComputeStreakAsync(LotseDbContext db, string userId, DateTime now, CancellationToken ct)
    {
        var days = await db.Sessions.Where(s => s.UserId == userId && s.EndedUtc != null).Select(s => s.EndedUtc!.Value.Date).Distinct().ToListAsync(ct);
        var set = days.ToHashSet();
        var day = now.Date;
        if (!set.Contains(day)) day = day.AddDays(-1); // today not done yet does not break the streak
        var streak = 0;
        while (set.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    private async Task<List<ErrorEvent>> RecentErrorsAsync(LotseDbContext db, string userId, int days, CancellationToken ct)
    {
        var since = Now.AddDays(-days);
        return (await db.ErrorEvents.AsNoTracking().Where(e => e.UserId == userId && e.Utc >= since).ToListAsync(ct)).Select(e => e.ToModel()).ToList();
    }

    // ------------------------------------------------------------------------------------------
    // Sessions
    // ------------------------------------------------------------------------------------------

    private async Task<PlannerInput> BuildPlannerInputAsync(LotseDbContext db, string userId, int minutes, string? requestedNode, CancellationToken ct)
    {
        var now = Now;
        var states = await db.SkillStates.AsNoTracking().Where(s => EF.Property<string>(s, LotseDbContext.UserIdShadow) == userId).ToDictionaryAsync(s => s.NodeId, ct);
        var due = await db.ReviewStates.AsNoTracking().Where(r => EF.Property<string>(r, LotseDbContext.UserIdShadow) == userId && r.DueUtc <= now).ToListAsync(ct);
        var recentSince = now.AddDays(-4);
        var recent = (await db.Attempts.AsNoTracking().Where(a => a.UserId == userId && a.Utc >= recentSince).Select(a => a.ExerciseId).ToListAsync(ct)).ToHashSet();
        var recentProd = (await db.Productions.AsNoTracking().Where(p => p.UserId == userId && p.Utc >= recentSince).Select(p => p.ExerciseId).ToListAsync(ct));
        foreach (var id in recentProd) recent.Add(id);
        var errors = await RecentErrorsAsync(db, userId, 30, ct);
        var sessions = await db.Sessions.CountAsync(s => s.UserId == userId && s.EndedUtc != null && s.Kind == SessionKind.Daily, ct);
        var lastProduction = await db.Productions.Where(p => p.UserId == userId).OrderByDescending(p => p.Utc).Select(p => (DateTime?)p.Utc).FirstOrDefaultAsync(ct);
        var receptiveIds = Catalog.Exercises.Where(e => e.IsReceptive).Select(e => e.Id).ToList();
        var lastInput = await db.Attempts.Where(a => a.UserId == userId && receptiveIds.Contains(a.ExerciseId))
            .OrderByDescending(a => a.Utc).Select(a => (DateTime?)a.Utc).FirstOrDefaultAsync(ct);
        // Read straight from the row: the profile may not be in this DbContext's change tracker, and a session must
        // be planned for the level the learner has saved, not for a stale copy.
        var plannerProfile = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new { p.TargetLevel, p.Occupation }).FirstOrDefaultAsync(ct);
        var targetLevel = plannerProfile?.TargetLevel ?? TargetLevel.B2;
        var occupation = plannerProfile?.Occupation ?? Occupation.Unspecified;

        return new PlannerInput
        {
            TimeBudgetMinutes = minutes,
            NowUtc = now,
            Catalog = Catalog,
            SkillStates = states,
            DueReviews = due,
            RecentExerciseIds = recent,
            RecentErrors = errors,
            SessionsCompleted = sessions,
            LastProductionUtc = lastProduction,
            LastInputUtc = lastInput,
            RequestedNodeId = requestedNode,
            TargetLevel = targetLevel,
            Occupation = occupation,
            Seed = Random.Shared.Next(),
        };
    }

    public async Task<SessionEntity> StartSessionAsync(int minutes, string? requestedNode = null, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Starting is idempotent: a second click (or a reload that replays the click) resumes the session that is
        // already open instead of stacking a new one on top of it - which is what used to leave "Session läuft noch"
        // on the dashboard after the learner had visibly finished. A requested topic is a deliberate new start.
        if (requestedNode is null && await OpenSessionOfKindAsync(db, userId, SessionKind.Daily, ct) is { } open)
            return open;
        var input = await BuildPlannerInputAsync(db, userId, minutes, requestedNode, ct);
        var plan = planner.Plan(input);
        var steps = plan.Steps.Select(s => new StepRecord { Kind = s.Kind, ExerciseId = s.Exercise.Id, Reason = s.Reason }).ToList();
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = SessionKind.Daily,
            PlannedMinutes = minutes,
            StartedUtc = Now,
            PlanJson = JsonSerializer.Serialize(steps),
            StepsTotal = steps.Count,
            Summary = plan.Summary,
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Session {Id} geplant: {Steps} Schritte, ~{Sec}s – {Summary}", session.Id, steps.Count, plan.EstimatedSeconds, plan.Summary);
        return session;
    }

    public async Task<SessionEntity> StartPlacementAsync(bool quick = false, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await OpenSessionOfKindAsync(db, userId, SessionKind.Placement, ct) is { } open)
            return open; // an interrupted placement is resumed, never duplicated
        var items = PlacementTest.Build(Catalog, seed: Random.Shared.Next(), quick: quick);
        var steps = items.Select(e => new StepRecord { Kind = StepKind.Explore, ExerciseId = e.Id, Reason = "Einstufung: " + Catalog.NodeTitle(e.NodeId) }).ToList();
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = SessionKind.Placement,
            PlannedMinutes = quick ? 8 : 20,
            StartedUtc = Now,
            PlanJson = JsonSerializer.Serialize(steps),
            StepsTotal = steps.Count,
            Summary = quick
                ? $"Kurze Einstufung über {PlacementTest.CoreNodeIds.Count} Kernthemen"
                : $"Einstufung über {PlacementTest.CoreNodeIds.Count} Kernthemen",
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    private DateTime OpenSessionHorizon => Now.AddHours(-12);

    private async Task<SessionEntity?> OpenSessionOfKindAsync(LotseDbContext db, string userId, SessionKind kind, CancellationToken ct)
    {
        var since = OpenSessionHorizon;
        return await db.Sessions.Where(s => s.UserId == userId && s.Kind == kind && s.EndedUtc == null && s.StartedUtc >= since)
            .OrderByDescending(s => s.StartedUtc).FirstOrDefaultAsync(ct);
    }

    // ------------------------------------------------------------------------------------------
    // Course (lessons)
    // ------------------------------------------------------------------------------------------

    public async Task<CourseOverview> GetCourseAsync(CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var progress = await db.LessonProgress.AsNoTracking().Where(p => p.UserId == userId).ToDictionaryAsync(p => p.LessonId, ct);
        var lessons = Catalog.Lessons;
        // Recommended: the first unfinished lesson in order (a started one first, then the next new one).
        var next = lessons.FirstOrDefault(l => progress.TryGetValue(l.Id, out var p) && p.CompletedUtc is null)
                   ?? lessons.FirstOrDefault(l => !progress.ContainsKey(l.Id));
        var cards = lessons.Select(l => new LessonCard(l, progress.GetValueOrDefault(l.Id), l.Id == next?.Id)).ToList();
        return new CourseOverview(cards, cards.Count(c => c.IsCompleted), cards.FirstOrDefault(c => c.IsRecommended));
    }

    /// <summary>Starts (or restarts) a lesson as a session with the lesson's steps in order and the production task last.</summary>
    public async Task<SessionEntity> StartLessonAsync(string lessonId, CancellationToken ct = default)
    {
        var lesson = Catalog.Lesson(lessonId) ?? throw new KeyNotFoundException(lessonId);
        var steps = lesson.AllExerciseIds
            .Select(id => Catalog.Exercise(id))
            .Where(e => e is not null)
            .Select(e => new StepRecord
            {
                Kind = e!.IsProduction ? StepKind.Production : StepKind.Focus,
                ExerciseId = e.Id,
                Reason = e.IsProduction ? $"Zum Abschluss der Lektion „{lesson.Title}“: Jetzt selbst formulieren." : $"Lektion {lesson.Order}: {lesson.Title}",
            }).ToList();

        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = SessionKind.Lesson,
            LessonId = lesson.Id,
            PlannedMinutes = lesson.Minutes,
            StartedUtc = Now,
            PlanJson = JsonSerializer.Serialize(steps),
            StepsTotal = steps.Count,
            Summary = $"Lektion {lesson.Order}: {lesson.Title}",
        };
        db.Sessions.Add(session);
        var progress = await db.LessonProgress.FindAsync([userId, lesson.Id], ct);
        if (progress is null) db.LessonProgress.Add(new LessonProgressEntity { UserId = userId, LessonId = lesson.Id, StartedUtc = Now, LastSessionId = session.Id });
        else { progress.LastSessionId = session.Id; if (progress.CompletedUtc is not null) { progress.CompletedUtc = null; } }
        await db.SaveChangesAsync(ct);
        return session;
    }

    private async Task CompleteLessonAsync(LotseDbContext db, SessionEntity session, IReadOnlyList<StepRecord> steps, CancellationToken ct)
    {
        if (session.LessonId is null) return;
        var progress = await db.LessonProgress.FindAsync([session.UserId, session.LessonId], ct)
                       ?? db.LessonProgress.Add(new LessonProgressEntity { UserId = session.UserId, LessonId = session.LessonId, StartedUtc = session.StartedUtc }).Entity;
        progress.CompletedUtc = Now;
        progress.Score = steps.Count == 0 ? 0 : steps.Average(s => s.Score ?? 0);
        progress.TimesCompleted++;
        progress.LastSessionId = session.Id;
    }

    /// <summary>Dialogue: one choice per learner turn (in order). Score = share of best replies.</summary>
    public async Task<AnswerResult> SubmitDialogueAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosen, int durationMs, CancellationToken ct = default)
    {
        var exercise = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var turns = exercise.Lines.Where(l => l.IsLearnerTurn).ToList();
        var correct = turns.Select((t, i) => i < chosen.Count && chosen[i] == t.CorrectIndex ? 1 : 0).Sum();
        return await SubmitCompositeAsync(sessionId, stepIndex, exercise, correct, turns.Count, durationMs, string.Join(",", chosen),
            correct == turns.Count ? "Jede Antwort saß." : "Lies die Begründungen – dort steckt die Lektion.", ct);
    }

    /// <summary>Match: for each left item the index of the chosen right item. Score = share of correct pairs.</summary>
    public async Task<AnswerResult> SubmitMatchAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosenRight, int durationMs, CancellationToken ct = default)
    {
        var exercise = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var correct = exercise.Pairs.Select((p, i) => i < chosenRight.Count && chosenRight[i] == i ? 1 : 0).Sum();
        return await SubmitCompositeAsync(sessionId, stepIndex, exercise, correct, exercise.Pairs.Count, durationMs, string.Join(",", chosenRight),
            correct == exercise.Pairs.Count ? "Alle Paare richtig." : "Die falschen Paare sind markiert.", ct);
    }

    private async Task<AnswerResult> SubmitCompositeAsync(Guid? sessionId, int stepIndex, Exercise exercise, int correct, int total, int durationMs, string answerText, string feedback, CancellationToken ct)
    {
        var score = total == 0 ? 0 : (double)correct / total;
        var outcome = score >= 0.999 ? Outcome.Correct : score >= 0.6 ? Outcome.AlmostCorrect : Outcome.Incorrect;
        var now = Now;
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attempt = new AttemptEntity { UserId = userId, SessionId = sessionId, ExerciseId = exercise.Id, NodeId = exercise.NodeId, Outcome = Outcome.Graded, Score = score, DurationMs = durationMs, Utc = now, AnswerText = answerText };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        if (score < 0.6)
        {
            var primary = Catalog.ErrorTypes.FirstOrDefault(e => e.NodeId == exercise.NodeId)?.Code;
            if (primary is not null) db.ErrorEvents.Add(new ErrorEventEntity { UserId = userId, AttemptId = attempt.Id, Code = primary, NodeId = exercise.NodeId, Utc = now, Snippet = exercise.Prompt, FromAi = false });
        }
        var state = await GetOrCreateStateAsync(db, userId, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, score, now);
        var complete = sessionId is null ? false : await MarkStepDoneAsync(db, userId, sessionId.Value, stepIndex, score, ct);
        await db.SaveChangesAsync(ct);
        var check = new CheckResult(outcome, $"{correct} von {total} richtig", [], feedback);
        // Checked against the raw answers above; only the copy the learner gets to read carries their name.
        var view = await LearnerViewAsync(db, userId, ct);
        return new AnswerResult(check, NameTemplate.Render(exercise, view), Catalog.Node(exercise.NodeId), state.Mastery, null, complete);
    }

    /// <summary>A free session on one exercise (used by the Schreiben/Sprechen/Prüfung pages).</summary>
    public async Task<SessionEntity> StartSingleAsync(string exerciseId, SessionKind kind, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ex = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var steps = new List<StepRecord> { new() { Kind = ex.IsProduction ? StepKind.Production : StepKind.Focus, ExerciseId = ex.Id, Reason = "Freie Übung" } };
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            PlannedMinutes = ex.EstimatedSeconds / 60 + 1,
            StartedUtc = Now,
            PlanJson = JsonSerializer.Serialize(steps),
            StepsTotal = 1,
            Summary = Catalog.NodeTitle(ex.NodeId),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<ProgressStats> GetProgressStatsAsync(CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reviews = await db.ReviewStates.AsNoTracking()
            .Where(r => EF.Property<string>(r, LotseDbContext.UserIdShadow) == userId).ToListAsync(ct);
        return ProgressStatsCalculator.Compute(reviews, Now);
    }

    /// <summary>
    /// Records today's readiness once and returns the change against a snapshot from six to eight days ago.
    /// The window is a range, not exactly seven days: the learner does not open the app on a fixed schedule, and a
    /// strict equality would show a trend only to someone who never misses a day. Null when nothing comparable exists.
    /// </summary>
    private async Task<double?> ReadinessTrendAsync(LotseDbContext db, string userId, double overall, IReadOnlyList<ModuleReadiness> modules, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        if (!await db.ReadinessSnapshots.AnyAsync(s => s.UserId == userId && s.Day == today, ct))
        {
            db.ReadinessSnapshots.Add(new ReadinessSnapshotEntity
            {
                UserId = userId,
                Day = today,
                Overall = overall,
                ModulesJson = JsonSerializer.Serialize(modules.ToDictionary(m => m.Module, m => m.Readiness)),
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two tabs opened the dashboard at once; the unique index did its job and today is already recorded.
                db.ChangeTracker.Clear();
            }
        }

        var from = today.AddDays(-8);
        var to = today.AddDays(-6);
        var earlier = await db.ReadinessSnapshots.AsNoTracking()
            .Where(s => s.UserId == userId && s.Day >= from && s.Day <= to)
            .OrderByDescending(s => s.Day)
            .Select(s => (double?)s.Overall)
            .FirstOrDefaultAsync(ct);
        return earlier is null ? null : overall - earlier.Value;
    }

    /// <summary>Monday morning's two lines about the week that just ended; null on every other day.</summary>
    private async Task<WeeklyReview?> WeeklyReviewAsync(LotseDbContext db, string userId, DateTime now, CancellationToken ct)
    {
        if (now.DayOfWeek != DayOfWeek.Monday) return null;
        var from = now.Date.AddDays(-7);
        var to = now.Date;
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Utc >= from && a.Utc < to)
            .Select(a => new { a.Score, a.NodeId }).ToListAsync(ct);
        if (attempts.Count == 0) return null;

        var weakest = attempts.GroupBy(a => a.NodeId)
            .Where(g => g.Count() >= 3)
            .OrderBy(g => g.Average(a => a.Score))
            .Select(g => g.Key).FirstOrDefault();
        var reviews = await db.ReviewStates.AsNoTracking()
            .Where(r => EF.Property<string>(r, LotseDbContext.UserIdShadow) == userId).ToListAsync(ct);

        return new WeeklyReview(
            attempts.Count,
            attempts.Average(a => a.Score),
            ProgressStatsCalculator.Compute(reviews, now).Stuck,
            weakest is null ? null : Catalog.NodePlainTitle(weakest));
    }

    /// <summary>The learner's own name for the content tokens; empty fields fall back inside <see cref="NameTemplate"/>.</summary>
    /// <summary>
    /// Name and helper language in one read - everything the render boundary needs to turn shared catalogue
    /// content into this learner's copy. A learner without a profile row gets the defaults.
    /// </summary>
    private static async Task<LearnerView> LearnerViewAsync(LotseDbContext db, string userId, CancellationToken ct)
    {
        var row = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new { p.FirstName, p.LastName, p.HelperLanguage }).FirstOrDefaultAsync(ct);
        var view = row is null ? LearnerView.Default : new LearnerView(row.FirstName ?? "", row.LastName ?? "", row.HelperLanguage);
        return view.WithFallbackFor(userId);
    }

    public async Task<SessionView?> GetSessionAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null) return null;
        var steps = JsonSerializer.Deserialize<List<StepRecord>>(session.PlanJson) ?? [];
        // The catalogue is a singleton shared by every account, so the learner's name is substituted into copies
        // here - the one boundary where content stops being shared and belongs to one person.
        var view = await LearnerViewAsync(db, userId, ct);
        var exercises = steps.Select(s => Catalog.Exercise(s.ExerciseId)).Where(e => e is not null)
            .Select(e => NameTemplate.Render(e!, view)).ToList();
        if (exercises.Count != steps.Count)
        {
            steps = steps.Where(s => Catalog.Exercise(s.ExerciseId) is not null).ToList();
        }
        return new SessionView(session, steps, exercises);
    }

    public async Task<SessionEntity?> GetOpenSessionAsync(CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = OpenSessionHorizon;
        return await db.Sessions.AsNoTracking().Where(s => s.UserId == userId && s.EndedUtc == null && s.StartedUtc >= since).OrderByDescending(s => s.StartedUtc).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SessionEntity>> RecentSessionsAsync(int take = 20, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Sessions.AsNoTracking().Where(s => s.UserId == userId).OrderByDescending(s => s.StartedUtc).Take(take).ToListAsync(ct);
    }

    public async Task<SessionEntity> FinishSessionAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FirstAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session.EndedUtc is null)
        {
            session.EndedUtc = Now;
            // An abandoned placement (less than 70 % answered) does not calibrate anything; the banner stays.
            if (session.Kind == SessionKind.Placement && session.StepsDone >= session.StepsTotal * 0.7)
                await MarkPlacementCompletedAsync(db, userId, ct);
            await db.SaveChangesAsync(ct);
        }
        return session;
    }

    private async Task MarkPlacementCompletedAsync(LotseDbContext db, string userId, CancellationToken ct)
    {
        var profile = await db.Profiles.FindAsync([userId], ct);
        if (profile is null)
        {
            profile = new LearnerProfile { UserId = userId, CreatedUtc = Now };
            db.Profiles.Add(profile);
        }
        profile.PlacementCompletedUtc = Now;
        // One calibration is enough: any other placement still open (from before starts became idempotent, or
        // from a second device) is closed too, so the dashboard cannot keep offering it.
        await db.Sessions
            .Where(s => s.UserId == userId && s.Kind == SessionKind.Placement && s.EndedUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedUtc, Now), ct);
    }

    public async Task AbandonSessionAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null) return;
        if (session.StepsDone == 0) db.Sessions.Remove(session);
        else session.EndedUtc ??= Now;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------------------------
    // Answers
    // ------------------------------------------------------------------------------------------

    public async Task<AnswerResult> SubmitAnswerAsync(Guid? sessionId, int stepIndex, string exerciseId, string? answer, int durationMs, bool hintUsed, CancellationToken ct = default)
    {
        var exercise = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var check = AnswerChecker.Check(exercise, answer);
        var now = Now;
        var userId = await UserIdAsync();
        // Choice tasks submit the option index; the journal should show the learner what they actually picked.
        var answerText = exercise.Type is ExerciseType.MultipleChoice or ExerciseType.SpotError
            && int.TryParse(answer, out var chosen) && chosen >= 0 && chosen < exercise.Options.Count
            ? exercise.Options[chosen]
            : answer;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attempt = new AttemptEntity
        {
            UserId = userId,
            SessionId = sessionId,
            ExerciseId = exercise.Id,
            NodeId = exercise.NodeId,
            Outcome = check.Outcome,
            Score = check.Score,
            DurationMs = durationMs,
            HintUsed = hintUsed,
            Utc = now,
            AnswerText = answerText,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        // error events: slips always; a node-level error when wrong
        var codes = new List<string>(check.SlipCodes);
        if (check.Outcome == Outcome.Incorrect && codes.Count == 0)
        {
            var primary = Catalog.ErrorTypes.FirstOrDefault(e => e.NodeId == exercise.NodeId)?.Code;
            if (primary is not null) codes.Add(primary);
        }
        foreach (var code in codes.Distinct())
        {
            var et = Catalog.Error(code);
            db.ErrorEvents.Add(new ErrorEventEntity
            {
                UserId = userId,
                AttemptId = attempt.Id,
                Code = code,
                NodeId = et?.NodeId ?? exercise.NodeId,
                Utc = now,
                Snippet = answerText,
                Correction = check.Expected,
                FromAi = false,
            });
        }

        // skill state (the exercise's node, plus slip nodes when they differ)
        var state = await GetOrCreateStateAsync(db, userId, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, check.Score, now);
        foreach (var code in check.SlipCodes)
        {
            var slipNode = Catalog.Error(code)?.NodeId;
            if (slipNode is null || slipNode == exercise.NodeId) continue;
            var slipState = await GetOrCreateStateAsync(db, userId, slipNode, ct);
            LearnerAnalysis.ApplyAttempt(slipState, exercise with { Band = CefrBand.B1_2 }, 0.3, now);
        }

        // spaced repetition
        var review = await db.ReviewStates.FindAsync([userId, exercise.Id], ct);
        if (review is null)
        {
            review = new ReviewState { ExerciseId = exercise.Id, NodeId = exercise.NodeId, DueUtc = now };
            // Same shadow-key ordering as GetOrCreateStateAsync above.
            var entry = db.Entry(review);
            entry.Property(LotseDbContext.UserIdShadow).CurrentValue = userId;
            entry.State = EntityState.Added;
        }
        ReviewScheduler.Apply(review, ReviewScheduler.GradeFor(check.Outcome, hintUsed, durationMs, exercise.EstimatedSeconds), now);

        var complete = false;
        if (sessionId is not null)
            complete = await MarkStepDoneAsync(db, userId, sessionId.Value, stepIndex, check.Score, ct);

        await db.SaveChangesAsync(ct);

        var node = Catalog.Node(exercise.NodeId);
        var view = await LearnerViewAsync(db, userId, ct);
        var lang = view.HelperLanguage;
        // The contrastive note belongs to real mistakes, not to a capitalisation slip on an otherwise right answer.
        // It is also the one place where "no note in your language" simply means no note - see Exercise.NoteFor.
        var note = check.Outcome == Outcome.Incorrect
            ? exercise.NoteFor(lang) ?? node?.InterferenceNoteFor(lang)
            : check.SlipCodes.Contains(AnswerChecker.SlipMissingArticle) ? Catalog.Node("GR.ARTIKEL_GENUS")?.InterferenceNoteFor(lang) : null;
        return new AnswerResult(check, NameTemplate.Render(exercise, view), node, state.Mastery, note, complete);
    }

    /// <summary>Reading / listening comprehension: one score for the whole exercise.</summary>
    public async Task<AnswerResult> SubmitReadingAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosen, int durationMs, CancellationToken ct = default)
    {
        var exercise = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var correct = exercise.Questions.Select((q, i) => i < chosen.Count && chosen[i] == q.CorrectIndex ? 1 : 0).Sum();
        var score = exercise.Questions.Count == 0 ? 0 : (double)correct / exercise.Questions.Count;
        var outcome = score >= 0.999 ? Outcome.Correct : score >= 0.6 ? Outcome.AlmostCorrect : Outcome.Incorrect;
        var now = Now;
        var userId = await UserIdAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Attempts.Add(new AttemptEntity
        {
            UserId = userId,
            SessionId = sessionId,
            ExerciseId = exercise.Id,
            NodeId = exercise.NodeId,
            Outcome = Outcome.Graded,
            Score = score,
            DurationMs = durationMs,
            Utc = now,
            AnswerText = string.Join(",", chosen),
        });
        var state = await GetOrCreateStateAsync(db, userId, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, score, now);
        var complete = sessionId is null ? false : await MarkStepDoneAsync(db, userId, sessionId.Value, stepIndex, score, ct);
        await db.SaveChangesAsync(ct);

        var check = new CheckResult(outcome, $"{correct} von {exercise.Questions.Count} richtig", [], score >= 0.6 ? "Gut gelesen." : "Noch einmal in Ruhe lesen – achte auf Umschreibungen.");
        return new AnswerResult(check, exercise, Catalog.Node(exercise.NodeId), state.Mastery, null, complete);
    }

    private static async Task<SkillState> GetOrCreateStateAsync(LotseDbContext db, string userId, string nodeId, CancellationToken ct)
    {
        var state = await db.SkillStates.FindAsync([userId, nodeId], ct);
        if (state is null)
        {
            state = new SkillState { NodeId = nodeId, Theta = -0.2 };
            // UserId is a shadow property AND part of the key, so it must be set before the entity is marked
            // Added (Add() itself would try to build the identity-map key immediately, while it is still null).
            var entry = db.Entry(state);
            entry.Property(LotseDbContext.UserIdShadow).CurrentValue = userId;
            entry.State = EntityState.Added;
        }
        return state;
    }

    /// <summary>
    /// The placement runs an easy pass over every core topic and then a harder one. Failing the easy item of a topic
    /// already answers the harder question, so asking it anyway costs the learner time and tells us nothing - Stefan
    /// called it "kein echtes Überspringen". The plan is built up front, so the skip happens here: the sibling is
    /// marked done with score 0 and a reason the summary can show.
    /// </summary>
    private void SkipHarderSibling(List<StepRecord> steps, int stepIndex)
    {
        var nodeId = Catalog.Exercise(steps[stepIndex].ExerciseId)?.NodeId;
        if (nodeId is null) return;

        for (var i = stepIndex + 1; i < steps.Count; i++)
        {
            if (steps[i].Done || Catalog.Exercise(steps[i].ExerciseId)?.NodeId != nodeId) continue;
            steps[i].Done = true;
            steps[i].Score = 0;
            steps[i].Reason = "übersprungen – Grundlage fehlt";
            return;   // exactly one harder item per topic
        }
    }

    private async Task<bool> MarkStepDoneAsync(LotseDbContext db, string userId, Guid sessionId, int stepIndex, double score, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return false;
        var steps = JsonSerializer.Deserialize<List<StepRecord>>(session.PlanJson) ?? [];
        if (stepIndex >= 0 && stepIndex < steps.Count && !steps[stepIndex].Done)
        {
            steps[stepIndex].Done = true;
            steps[stepIndex].Score = score;
            if (score >= 0.7) session.CorrectCount++;
            if (session.Kind == SessionKind.Placement && score < 0.7) SkipHarderSibling(steps, stepIndex);
            session.StepsDone = steps.Count(s => s.Done);
            session.PlanJson = JsonSerializer.Serialize(steps);
        }
        var complete = steps.All(s => s.Done);
        if (complete && session.EndedUtc is null)
        {
            session.EndedUtc = Now;
            if (session.Kind == SessionKind.Placement) await MarkPlacementCompletedAsync(db, userId, ct);
            if (session.Kind == SessionKind.Lesson) await CompleteLessonAsync(db, session, steps, ct);
        }
        return complete;
    }

    public async Task SkipStepAsync(Guid sessionId, int stepIndex, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await MarkStepDoneAsync(db, userId, sessionId, stepIndex, 0, ct);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------------------------
    // Production (writing / speaking)
    // ------------------------------------------------------------------------------------------

    public async Task<LearnerContext> BuildLearnerContextAsync(CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var states = await db.SkillStates.AsNoTracking().Where(s => EF.Property<string>(s, LotseDbContext.UserIdShadow) == profile.UserId).ToDictionaryAsync(s => s.NodeId, ct);
        var errors = await RecentErrorsAsync(db, profile.UserId, 30, ct);
        var weak = LearnerAnalysis.WeakAreas(Catalog, states, errors, Now, take: 5).Select(w => w.Title).ToList();
        var codes = errors.GroupBy(e => e.Code).OrderByDescending(g => g.Count()).Take(8).Select(g => g.Key).ToList();
        // The free text is the richer prompt ("Softwareentwickler"); the structured field is the fallback so the
        // tutor still knows the field when only the dropdown was set.
        var job = string.IsNullOrWhiteSpace(profile.JobTitle)
            ? profile.Occupation == Occupation.Unspecified ? null : profile.Occupation.Label()
            : profile.JobTitle;
        return new LearnerContext(profile.NativeLanguage, weak, codes, Catalog.ErrorTypes.ToList(), job);
    }

    public async Task<ProductionResult> SubmitProductionAsync(Guid? sessionId, int stepIndex, string exerciseId, string text, bool speaking, CancellationToken ct = default)
    {
        var exercise = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var words = AnswerChecker.CountWords(text);
        var now = Now;
        var userId = await UserIdAsync();

        ProductionEvaluation? evaluation = null;
        string? tutorError = null;
        if (tutor.IsAvailable)
        {
            try
            {
                var context = await BuildLearnerContextAsync(ct);
                evaluation = await tutor.EvaluateAsync(exercise, text, speaking ? ProductionMode.Speaking : ProductionMode.Writing, context, ct);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "KI-Bewertung fehlgeschlagen, Selbstcheck als Fallback.");
                tutorError = e.Message;
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = new ProductionEntity
        {
            UserId = userId,
            SessionId = sessionId,
            ExerciseId = exercise.Id,
            NodeId = exercise.NodeId,
            IsSpeaking = speaking,
            Text = text,
            WordCount = words,
            Utc = now,
            Score = evaluation?.OverallScore,
            EstimatedLevel = evaluation?.EstimatedLevel,
            EvaluationJson = evaluation is null ? null : JsonSerializer.Serialize(evaluation),
            EvaluatedByAi = evaluation is not null,
        };
        db.Productions.Add(entity);
        await db.SaveChangesAsync(ct);

        if (evaluation is not null)
        {
            await ApplyEvaluationAsync(db, userId, exercise, evaluation, now, ct);
            if (sessionId is not null) await MarkStepDoneAsync(db, userId, sessionId.Value, stepIndex, evaluation.OverallScore, ct);
            await db.SaveChangesAsync(ct);
            return new ProductionResult(entity.Id, evaluation, exercise.Rubric, exercise.ModelAnswer, words, exercise.MinWords, tutorError, text);
        }

        // No tutor: the rule-based analyzer still finds the mistakes a Serbian speaker's text typically carries, and
        // they enter the journal and the learner model exactly like AI-tagged ones (marked as not from the AI).
        var report = FreeTextAnalyzer.Analyze(text, exercise, catalogProvider.Lexicon, spoken: speaking);
        if (report.Findings.Count > 0)
        {
            var touched = new Dictionary<string, SkillState>();
            foreach (var f in report.Findings)
            {
                var et = Catalog.Error(f.Code);
                if (et is null) continue;
                db.ErrorEvents.Add(new ErrorEventEntity { UserId = userId, Code = f.Code, NodeId = et.NodeId, Utc = now, Snippet = f.Snippet, Correction = f.Correction, FromAi = false });
                if (!touched.TryGetValue(et.NodeId, out var s))
                {
                    s = await GetOrCreateStateAsync(db, userId, et.NodeId, ct);
                    touched[et.NodeId] = s;
                }
                LearnerAnalysis.ApplyAttempt(s, exercise with { Band = CefrBand.B2_1 }, et.Severity >= 3 ? 0.0 : 0.3, now);
            }
            await db.SaveChangesAsync(ct);
        }
        return new ProductionResult(entity.Id, null, exercise.Rubric, exercise.ModelAnswer, words, exercise.MinWords, tutorError, text)
        {
            Findings = report.Findings,
            Hints = report.Hints,
        };
    }

    /// <summary>
    /// A text that was submitted for self-check but whose self-check was never saved (reload, lost connection,
    /// laptop went to sleep). The step is still open, the text is safe in the database - hand it back so the
    /// learner continues where they were instead of facing an empty editor.
    /// </summary>
    public async Task<ProductionResult?> GetPendingProductionAsync(Guid sessionId, string exerciseId, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        var exercise = Catalog.Exercise(exerciseId);
        if (exercise is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Productions.AsNoTracking()
            .Where(p => p.UserId == userId && p.SessionId == sessionId && p.ExerciseId == exerciseId && p.Score == null && !p.EvaluatedByAi)
            .OrderByDescending(p => p.Utc)
            .FirstOrDefaultAsync(ct);
        if (entity is null) return null;
        // The analysis is deterministic, so it is simply run again instead of being stored.
        var report = FreeTextAnalyzer.Analyze(entity.Text, exercise, catalogProvider.Lexicon, spoken: entity.IsSpeaking);
        return new ProductionResult(entity.Id, null, exercise.Rubric, exercise.ModelAnswer, entity.WordCount, exercise.MinWords, null, entity.Text)
        {
            Findings = report.Findings,
            Hints = report.Hints,
        };
    }

    private async Task ApplyEvaluationAsync(LotseDbContext db, string userId, Exercise exercise, ProductionEvaluation evaluation, DateTime now, CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(db, userId, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, evaluation.OverallScore, now);

        db.Attempts.Add(new AttemptEntity
        {
            UserId = userId,
            ExerciseId = exercise.Id,
            NodeId = exercise.NodeId,
            Outcome = Outcome.Graded,
            Score = evaluation.OverallScore,
            Utc = now,
        });

        // Every tagged error is evidence on its node: a small negative observation at B2.1 difficulty.
        var touched = new Dictionary<string, SkillState>();
        foreach (var err in evaluation.Errors)
        {
            var et = Catalog.Error(err.Code);
            if (et is null) continue;
            db.ErrorEvents.Add(new ErrorEventEntity { UserId = userId, Code = err.Code, NodeId = et.NodeId, Utc = now, Snippet = err.Snippet, Correction = err.Correction, FromAi = true });
            if (!touched.TryGetValue(et.NodeId, out var s))
            {
                s = await GetOrCreateStateAsync(db, userId, et.NodeId, ct);
                touched[et.NodeId] = s;
            }
            LearnerAnalysis.ApplyAttempt(s, exercise with { Band = CefrBand.B2_1 }, et.Severity >= 3 ? 0.0 : 0.3, now);
        }
    }

    /// <summary>Self-check when no tutor is configured: the learner ticks the rubric; the fraction becomes the score.</summary>
    public async Task<double> SubmitSelfCheckAsync(long productionId, Guid? sessionId, int stepIndex, IReadOnlyList<bool> checks, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Productions.FirstAsync(p => p.Id == productionId && p.UserId == userId, ct);
        var exercise = Catalog.Exercise(entity.ExerciseId) ?? throw new KeyNotFoundException(entity.ExerciseId);
        var score = checks.Count == 0 ? 0 : (double)checks.Count(c => c) / checks.Count;
        if (exercise.MinWords is { } min && entity.WordCount < min * 0.8) score *= 0.7; // too short cannot be fully "erfüllt"
        entity.Score = score;
        entity.EvaluationJson = JsonSerializer.Serialize(new { selfCheck = checks });
        var now = Now;
        var state = await GetOrCreateStateAsync(db, userId, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, score, now);
        db.Attempts.Add(new AttemptEntity { UserId = userId, SessionId = sessionId, ExerciseId = exercise.Id, NodeId = exercise.NodeId, Outcome = Outcome.Graded, Score = score, Utc = now });
        if (sessionId is not null) await MarkStepDoneAsync(db, userId, sessionId.Value, stepIndex, score, ct);
        await db.SaveChangesAsync(ct);
        return score;
    }

    public async Task<IReadOnlyList<ProductionEntity>> RecentProductionsAsync(int take = 20, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Productions.AsNoTracking().Where(p => p.UserId == userId).OrderByDescending(p => p.Utc).Take(take).ToListAsync(ct);
    }

    // ------------------------------------------------------------------------------------------
    // Progress, errors, generation
    // ------------------------------------------------------------------------------------------

    public async Task<IReadOnlyDictionary<string, SkillState>> GetSkillStatesAsync(CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SkillStates.AsNoTracking().Where(s => EF.Property<string>(s, LotseDbContext.UserIdShadow) == userId).ToDictionaryAsync(s => s.NodeId, ct);
    }

    /// <summary>Errors made during one session (deterministic ones via attempts, AI ones via productions), grouped for the summary screen.</summary>
    public async Task<IReadOnlyList<(string Code, string Title, string NodeTitle, int Count)>> GetSessionErrorsAsync(Guid sessionId, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attemptIds = await db.Attempts.Where(a => a.UserId == userId && a.SessionId == sessionId).Select(a => a.Id).ToListAsync(ct);
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        var from = session?.StartedUtc ?? DateTime.MinValue;
        var to = session?.EndedUtc ?? Now;
        var rows = await db.ErrorEvents.AsNoTracking()
            .Where(e => e.UserId == userId && ((e.AttemptId != null && attemptIds.Contains(e.AttemptId.Value)) || (e.FromAi && e.Utc >= from && e.Utc <= to)))
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Code)
            .Select(g => (g.Key, Catalog.Error(g.Key)?.Title ?? g.Key, Catalog.NodeTitle(Catalog.Error(g.Key)?.NodeId ?? g.First().NodeId), g.Count()))
            .OrderByDescending(t => t.Item4).ToList();
    }

    public async Task<IReadOnlyList<ErrorJournalEntry>> GetErrorJournalAsync(int days = 30, int take = 100, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = Now.AddDays(-days);
        var rows = await db.ErrorEvents.AsNoTracking().Where(e => e.UserId == userId && e.Utc >= since).OrderByDescending(e => e.Utc).Take(take).ToListAsync(ct);
        return rows.Select(r =>
        {
            var et = Catalog.Error(r.Code);
            return new ErrorJournalEntry(r.Utc, r.Code, et?.Title ?? r.Code, Catalog.NodeTitle(r.NodeId), r.Snippet, r.Correction, r.FromAi, et?.Severity ?? 1);
        }).ToList();
    }

    public async Task<IReadOnlyList<(DateTime Day, int Attempts, double Accuracy)>> GetDailyHistoryAsync(int days = 30, CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = Now.Date.AddDays(-days);
        var rows = await db.Attempts.AsNoTracking().Where(a => a.UserId == userId && a.Utc >= since).Select(a => new { a.Utc, a.Score }).ToListAsync(ct);
        return rows.GroupBy(r => r.Utc.Date).OrderBy(g => g.Key).Select(g => (g.Key, g.Count(), g.Average(r => r.Score))).ToList();
    }

    public async Task<IReadOnlyList<Exercise>> GenerateForNodeAsync(string nodeId, int count = 6, CancellationToken ct = default)
    {
        var node = Catalog.Node(nodeId) ?? throw new KeyNotFoundException(nodeId);
        if (!tutor.IsAvailable) return [];
        var context = await BuildLearnerContextAsync(ct);
        var states = await GetSkillStatesAsync(ct);
        var theta = states.GetValueOrDefault(nodeId)?.Theta ?? -0.2;
        var band = CefrBandExtensions.FromDifficulty(Ability.TargetDifficulty(theta));
        if (band < CefrBand.B1_2) band = CefrBand.B1_2;
        var examples = Catalog.ForNode(nodeId).Where(e => !e.IsProduction && !e.IsReceptive).Take(3).ToList();
        var generated = await tutor.GenerateExercisesAsync(node, band, count, ExerciseContext.Beruf, context, examples, ct);
        if (generated.Count == 0) return [];

        // Shared/global bank (see GeneratedExerciseEntity) – not scoped to this learner.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var ex in generated)
            db.GeneratedExercises.Add(new GeneratedExerciseEntity { Id = ex.Id, NodeId = ex.NodeId, Json = ContentLoader.Serialize(ex), CreatedUtc = Now });
        await db.SaveChangesAsync(ct);
        await catalogProvider.RefreshAsync(ct);
        return generated;
    }

    /// <summary>Generates one reading (Lesen node) or listening (Hören node) task in exam format and adds it to the bank.</summary>
    public async Task<Exercise?> GenerateReadingForNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var node = Catalog.Node(nodeId) ?? throw new KeyNotFoundException(nodeId);
        if (!tutor.IsAvailable) return null;
        var context = await BuildLearnerContextAsync(ct);
        var states = await GetSkillStatesAsync(ct);
        var theta = states.GetValueOrDefault(nodeId)?.Theta ?? -0.2;
        var band = CefrBandExtensions.FromDifficulty(Ability.TargetDifficulty(theta));
        if (band < CefrBand.B1_2) band = CefrBand.B1_2;
        if (band > CefrBand.B2_2) band = CefrBand.B2_2;
        var ex = await tutor.GenerateReadingAsync(node, band, node.Area == SkillArea.Hoeren, context, ct);
        if (ex is null) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.GeneratedExercises.Add(new GeneratedExerciseEntity { Id = ex.Id, NodeId = ex.NodeId, Json = ContentLoader.Serialize(ex), CreatedUtc = Now });
        await db.SaveChangesAsync(ct);
        await catalogProvider.RefreshAsync(ct);
        return ex;
    }

    /// <summary>Tops up the bank for the weakest drill nodes. Returns (node title, generated count) per node.</summary>
    public async Task<IReadOnlyList<(string NodeTitle, int Count)>> FillWeakestAsync(int nodes = 3, int perNode = 6, CancellationToken ct = default)
    {
        if (!tutor.IsAvailable) return [];
        var userId = await UserIdAsync();
        var states = await GetSkillStatesAsync(ct);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var errors = await RecentErrorsAsync(db, userId, 30, ct);
            var weak = LearnerAnalysis.WeakAreas(Catalog, states, errors, Now, take: 20)
                .Where(w => w.Area is SkillArea.Grammatik or SkillArea.Wortschatz or SkillArea.Redemittel)
                .Take(nodes).ToList();
            var result = new List<(string, int)>();
            foreach (var w in weak)
            {
                var made = await GenerateForNodeAsync(w.NodeId, perNode, ct);
                result.Add((w.Title, made.Count));
            }
            return result;
        }
    }

    /// <summary>
    /// Wipes only the signed-in learner's own data — never the database file, never any other account's rows.
    /// The tutor configuration (API key) is deliberately left untouched: a reset should not cost the learner
    /// their provider setup, and it already lives in its own per-user <see cref="SettingEntity"/> row.
    /// </summary>
    public async Task ResetAllDataAsync(CancellationToken ct = default)
    {
        var userId = await UserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // One transaction: eight bulk deletes that stop halfway (SQLite lock, cancellation) must not leave a
        // learner with their attempts gone but their sessions and streak still counting them.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SkillStates.Where(s => EF.Property<string>(s, LotseDbContext.UserIdShadow) == userId).ExecuteDeleteAsync(ct);
        await db.ReviewStates.Where(r => EF.Property<string>(r, LotseDbContext.UserIdShadow) == userId).ExecuteDeleteAsync(ct);
        await db.ErrorEvents.Where(e => e.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Attempts.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Productions.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await db.LessonProgress.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Profiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }
}
