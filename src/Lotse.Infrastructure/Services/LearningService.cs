using System.Text.Json;
using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
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

public sealed record ProductionResult(long ProductionId, ProductionEvaluation? Evaluation, IReadOnlyList<string> Rubric, string? ModelAnswer, int WordCount, int? MinWords, string? TutorError);

public sealed record DashboardModel(
    LearnerProfile Profile,
    ReadinessReport Readiness,
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

public sealed record ErrorJournalEntry(DateTime Utc, string Code, string Title, string NodeTitle, string? Snippet, string? Correction, bool FromAi, int Severity);

/// <summary>
/// Application service: the only place that talks to both the database and the engine. Blazor pages call this,
/// never the DbContext directly. Every public method opens its own short-lived DbContext (safe for Blazor Server).
/// </summary>
public sealed class LearningService(
    IDbContextFactory<LotseDbContext> dbFactory,
    ContentCatalogProvider catalogProvider,
    ITutor tutor,
    SessionPlanner planner,
    TimeProvider clock,
    ILogger<LearningService> logger)
{
    private ContentCatalog Catalog => catalogProvider.Catalog;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public ITutor Tutor => tutor;

    // ------------------------------------------------------------------------------------------
    // Profile & dashboard
    // ------------------------------------------------------------------------------------------

    public async Task<LearnerProfile> GetProfileAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profile = await db.Profiles.FindAsync([1], ct);
        if (profile is not null) return profile;

        profile = new LearnerProfile { Id = 1, CreatedUtc = Now };
        db.Profiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(ct);
            return profile;
        }
        catch (DbUpdateException)
        {
            // Two circuits raced to create the single profile row; the other one won.
            db.Entry(profile).State = EntityState.Detached;
            return await db.Profiles.AsNoTracking().FirstAsync(p => p.Id == 1, ct);
        }
    }

    public async Task SaveProfileAsync(LearnerProfile profile, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Profiles.Update(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DashboardModel> GetDashboardAsync(CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = Now;
        var states = await db.SkillStates.AsNoTracking().ToDictionaryAsync(s => s.NodeId, ct);
        var errors = await RecentErrorsAsync(db, 30, ct);
        var due = await db.ReviewStates.CountAsync(r => r.DueUtc <= now, ct);
        var rechecks = states.Values.Count(s => s.RecheckDueUtc is not null && s.RecheckDueUtc <= now);

        var since7 = now.AddDays(-7);
        var sessions7 = await db.Sessions.CountAsync(s => s.EndedUtc != null && s.EndedUtc >= since7, ct);
        var todayStart = now.Date;
        var todaySessions = await db.Sessions.Where(s => s.StartedUtc >= todayStart).ToListAsync(ct);
        // Only finished sessions count; an open tab left overnight must not inflate the number.
        var minutesToday = (int)todaySessions.Where(s => s.EndedUtc != null).Sum(s => Math.Min(60, (s.EndedUtc!.Value - s.StartedUtc).TotalMinutes));
        var open = await db.Sessions.Where(s => s.EndedUtc == null && s.StartedUtc >= now.AddHours(-12)).OrderByDescending(s => s.StartedUtc).FirstOrDefaultAsync(ct);
        var totalAttempts = await db.Attempts.CountAsync(ct);

        var streak = await ComputeStreakAsync(db, now, ct);
        var readiness = LearnerAnalysis.Readiness(Catalog, states);
        var weak = LearnerAnalysis.WeakAreas(Catalog, states, errors, now);
        var topErrors = LearnerAnalysis.TopErrorCodes(Catalog, errors, now);

        var input = await BuildPlannerInputAsync(db, profile.DailyMinutes, null, ct);
        var preview = planner.Plan(input with { Seed = now.DayOfYear }).Summary;

        return new DashboardModel(profile, readiness, weak, topErrors, streak, due, rechecks, minutesToday, sessions7, totalAttempts, open, preview, tutor.IsAvailable, tutor.Description);
    }

    private static async Task<int> ComputeStreakAsync(LotseDbContext db, DateTime now, CancellationToken ct)
    {
        var days = await db.Sessions.Where(s => s.EndedUtc != null).Select(s => s.EndedUtc!.Value.Date).Distinct().ToListAsync(ct);
        var set = days.ToHashSet();
        var day = now.Date;
        if (!set.Contains(day)) day = day.AddDays(-1); // today not done yet does not break the streak
        var streak = 0;
        while (set.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    private static async Task<List<ErrorEvent>> RecentErrorsAsync(LotseDbContext db, int days, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        return (await db.ErrorEvents.AsNoTracking().Where(e => e.Utc >= since).ToListAsync(ct)).Select(e => e.ToModel()).ToList();
    }

    // ------------------------------------------------------------------------------------------
    // Sessions
    // ------------------------------------------------------------------------------------------

    private async Task<PlannerInput> BuildPlannerInputAsync(LotseDbContext db, int minutes, string? requestedNode, CancellationToken ct)
    {
        var now = Now;
        var states = await db.SkillStates.AsNoTracking().ToDictionaryAsync(s => s.NodeId, ct);
        var due = await db.ReviewStates.AsNoTracking().Where(r => r.DueUtc <= now).ToListAsync(ct);
        var recentSince = now.AddDays(-4);
        var recent = (await db.Attempts.AsNoTracking().Where(a => a.Utc >= recentSince).Select(a => a.ExerciseId).ToListAsync(ct)).ToHashSet();
        var recentProd = (await db.Productions.AsNoTracking().Where(p => p.Utc >= recentSince).Select(p => p.ExerciseId).ToListAsync(ct));
        foreach (var id in recentProd) recent.Add(id);
        var errors = await RecentErrorsAsync(db, 30, ct);
        var sessions = await db.Sessions.CountAsync(s => s.EndedUtc != null && s.Kind == SessionKind.Daily, ct);
        var lastProduction = await db.Productions.OrderByDescending(p => p.Utc).Select(p => (DateTime?)p.Utc).FirstOrDefaultAsync(ct);
        var receptiveIds = Catalog.Exercises.Where(e => e.IsReceptive).Select(e => e.Id).ToList();
        var lastInput = await db.Attempts.Where(a => receptiveIds.Contains(a.ExerciseId))
            .OrderByDescending(a => a.Utc).Select(a => (DateTime?)a.Utc).FirstOrDefaultAsync(ct);

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
            Seed = Random.Shared.Next(),
        };
    }

    public async Task<SessionEntity> StartSessionAsync(int minutes, string? requestedNode = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var input = await BuildPlannerInputAsync(db, minutes, requestedNode, ct);
        var plan = planner.Plan(input);
        var steps = plan.Steps.Select(s => new StepRecord { Kind = s.Kind, ExerciseId = s.Exercise.Id, Reason = s.Reason }).ToList();
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
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

    public async Task<SessionEntity> StartPlacementAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = PlacementTest.Build(Catalog, seed: Random.Shared.Next());
        var steps = items.Select(e => new StepRecord { Kind = StepKind.Explore, ExerciseId = e.Id, Reason = "Einstufung: " + Catalog.NodeTitle(e.NodeId) }).ToList();
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            Kind = SessionKind.Placement,
            PlannedMinutes = 20,
            StartedUtc = Now,
            PlanJson = JsonSerializer.Serialize(steps),
            StepsTotal = steps.Count,
            Summary = $"Einstufung über {PlacementTest.CoreNodeIds.Count} Kernthemen",
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>A free session on one exercise (used by the Schreiben/Sprechen/Prüfung pages).</summary>
    public async Task<SessionEntity> StartSingleAsync(string exerciseId, SessionKind kind, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ex = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var steps = new List<StepRecord> { new() { Kind = ex.IsProduction ? StepKind.Production : StepKind.Focus, ExerciseId = ex.Id, Reason = "Freie Übung" } };
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(), Kind = kind, PlannedMinutes = ex.EstimatedSeconds / 60 + 1, StartedUtc = Now,
            PlanJson = JsonSerializer.Serialize(steps), StepsTotal = 1, Summary = Catalog.NodeTitle(ex.NodeId),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<SessionView?> GetSessionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return null;
        var steps = JsonSerializer.Deserialize<List<StepRecord>>(session.PlanJson) ?? [];
        var exercises = steps.Select(s => Catalog.Exercise(s.ExerciseId)).Where(e => e is not null).Select(e => e!).ToList();
        if (exercises.Count != steps.Count)
        {
            steps = steps.Where(s => Catalog.Exercise(s.ExerciseId) is not null).ToList();
        }
        return new SessionView(session, steps, exercises);
    }

    public async Task<SessionEntity?> GetOpenSessionAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = Now.AddHours(-12);
        return await db.Sessions.AsNoTracking().Where(s => s.EndedUtc == null && s.StartedUtc >= since).OrderByDescending(s => s.StartedUtc).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SessionEntity>> RecentSessionsAsync(int take = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Sessions.AsNoTracking().OrderByDescending(s => s.StartedUtc).Take(take).ToListAsync(ct);
    }

    public async Task<SessionEntity> FinishSessionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FirstAsync(s => s.Id == id, ct);
        if (session.EndedUtc is null)
        {
            session.EndedUtc = Now;
            // An abandoned placement (less than 70 % answered) does not calibrate anything; the banner stays.
            if (session.Kind == SessionKind.Placement && session.StepsDone >= session.StepsTotal * 0.7)
            {
                var profile = await db.Profiles.FindAsync([1], ct);
                if (profile is not null) profile.PlacementCompletedUtc = Now;
            }
            await db.SaveChangesAsync(ct);
        }
        return session;
    }

    public async Task AbandonSessionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);
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

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attempt = new AttemptEntity
        {
            SessionId = sessionId, ExerciseId = exercise.Id, NodeId = exercise.NodeId, Outcome = check.Outcome, Score = check.Score,
            DurationMs = durationMs, HintUsed = hintUsed, Utc = now, AnswerText = answer,
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
                AttemptId = attempt.Id, Code = code, NodeId = et?.NodeId ?? exercise.NodeId, Utc = now,
                Snippet = answer, Correction = check.Expected, FromAi = false,
            });
        }

        // skill state (the exercise's node, plus slip nodes when they differ)
        var state = await GetOrCreateStateAsync(db, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, check.Score, now);
        foreach (var code in check.SlipCodes)
        {
            var slipNode = Catalog.Error(code)?.NodeId;
            if (slipNode is null || slipNode == exercise.NodeId) continue;
            var slipState = await GetOrCreateStateAsync(db, slipNode, ct);
            LearnerAnalysis.ApplyAttempt(slipState, exercise with { Band = CefrBand.B1_2 }, 0.3, now);
        }

        // spaced repetition
        var review = await db.ReviewStates.FindAsync([exercise.Id], ct);
        if (review is null)
        {
            review = new ReviewState { ExerciseId = exercise.Id, NodeId = exercise.NodeId, DueUtc = now };
            db.ReviewStates.Add(review);
        }
        ReviewScheduler.Apply(review, ReviewScheduler.GradeFor(check.Outcome, hintUsed, durationMs, exercise.EstimatedSeconds), now);

        var complete = false;
        if (sessionId is not null)
            complete = await MarkStepDoneAsync(db, sessionId.Value, stepIndex, check.Score, ct);

        await db.SaveChangesAsync(ct);

        var node = Catalog.Node(exercise.NodeId);
        // The contrastive note belongs to real mistakes, not to a capitalisation slip on an otherwise right answer.
        var note = check.Outcome == Outcome.Incorrect
            ? exercise.SerbianNote ?? node?.InterferenceNote
            : check.SlipCodes.Contains(AnswerChecker.SlipMissingArticle) ? Catalog.Node("GR.ARTIKEL_GENUS")?.InterferenceNote : null;
        return new AnswerResult(check, exercise, node, state.Mastery, note, complete);
    }

    /// <summary>Reading / listening comprehension: one score for the whole exercise.</summary>
    public async Task<AnswerResult> SubmitReadingAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosen, int durationMs, CancellationToken ct = default)
    {
        var exercise = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var correct = exercise.Questions.Select((q, i) => i < chosen.Count && chosen[i] == q.CorrectIndex ? 1 : 0).Sum();
        var score = exercise.Questions.Count == 0 ? 0 : (double)correct / exercise.Questions.Count;
        var outcome = score >= 0.999 ? Outcome.Correct : score >= 0.6 ? Outcome.AlmostCorrect : Outcome.Incorrect;
        var now = Now;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Attempts.Add(new AttemptEntity
        {
            SessionId = sessionId, ExerciseId = exercise.Id, NodeId = exercise.NodeId, Outcome = Outcome.Graded, Score = score,
            DurationMs = durationMs, Utc = now, AnswerText = string.Join(",", chosen),
        });
        var state = await GetOrCreateStateAsync(db, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, score, now);
        var complete = sessionId is null ? false : await MarkStepDoneAsync(db, sessionId.Value, stepIndex, score, ct);
        await db.SaveChangesAsync(ct);

        var check = new CheckResult(outcome, $"{correct} von {exercise.Questions.Count} richtig", [], score >= 0.6 ? "Gut gelesen." : "Noch einmal in Ruhe lesen – achte auf Umschreibungen.");
        return new AnswerResult(check, exercise, Catalog.Node(exercise.NodeId), state.Mastery, null, complete);
    }

    private static async Task<SkillState> GetOrCreateStateAsync(LotseDbContext db, string nodeId, CancellationToken ct)
    {
        var state = await db.SkillStates.FindAsync([nodeId], ct);
        if (state is null)
        {
            state = new SkillState { NodeId = nodeId, Theta = -0.2 };
            db.SkillStates.Add(state);
        }
        return state;
    }

    private static async Task<bool> MarkStepDoneAsync(LotseDbContext db, Guid sessionId, int stepIndex, double score, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return false;
        var steps = JsonSerializer.Deserialize<List<StepRecord>>(session.PlanJson) ?? [];
        if (stepIndex >= 0 && stepIndex < steps.Count && !steps[stepIndex].Done)
        {
            steps[stepIndex].Done = true;
            steps[stepIndex].Score = score;
            session.StepsDone = steps.Count(s => s.Done);
            if (score >= 0.7) session.CorrectCount++;
            session.PlanJson = JsonSerializer.Serialize(steps);
        }
        var complete = steps.All(s => s.Done);
        if (complete && session.EndedUtc is null)
        {
            session.EndedUtc = DateTime.UtcNow;
            if (session.Kind == SessionKind.Placement)
            {
                var profile = await db.Profiles.FindAsync([1], ct);
                if (profile is not null) profile.PlacementCompletedUtc = DateTime.UtcNow;
            }
        }
        return complete;
    }

    public async Task SkipStepAsync(Guid sessionId, int stepIndex, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await MarkStepDoneAsync(db, sessionId, stepIndex, 0, ct);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------------------------
    // Production (writing / speaking)
    // ------------------------------------------------------------------------------------------

    public async Task<LearnerContext> BuildLearnerContextAsync(CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var states = await db.SkillStates.AsNoTracking().ToDictionaryAsync(s => s.NodeId, ct);
        var errors = await RecentErrorsAsync(db, 30, ct);
        var weak = LearnerAnalysis.WeakAreas(Catalog, states, errors, Now, take: 5).Select(w => w.Title).ToList();
        var codes = errors.GroupBy(e => e.Code).OrderByDescending(g => g.Count()).Take(8).Select(g => g.Key).ToList();
        return new LearnerContext(profile.NativeLanguage, weak, codes, Catalog.ErrorTypes.ToList(), profile.Occupation);
    }

    public async Task<ProductionResult> SubmitProductionAsync(Guid? sessionId, int stepIndex, string exerciseId, string text, bool speaking, CancellationToken ct = default)
    {
        var exercise = Catalog.Exercise(exerciseId) ?? throw new KeyNotFoundException(exerciseId);
        var words = AnswerChecker.CountWords(text);
        var now = Now;

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
            SessionId = sessionId, ExerciseId = exercise.Id, NodeId = exercise.NodeId, IsSpeaking = speaking, Text = text, WordCount = words, Utc = now,
            Score = evaluation?.OverallScore, EstimatedLevel = evaluation?.EstimatedLevel,
            EvaluationJson = evaluation is null ? null : JsonSerializer.Serialize(evaluation), EvaluatedByAi = evaluation is not null,
        };
        db.Productions.Add(entity);
        await db.SaveChangesAsync(ct);

        if (evaluation is not null)
        {
            await ApplyEvaluationAsync(db, exercise, evaluation, now, ct);
            if (sessionId is not null) await MarkStepDoneAsync(db, sessionId.Value, stepIndex, evaluation.OverallScore, ct);
            await db.SaveChangesAsync(ct);
        }

        return new ProductionResult(entity.Id, evaluation, exercise.Rubric, exercise.ModelAnswer, words, exercise.MinWords, tutorError);
    }

    private async Task ApplyEvaluationAsync(LotseDbContext db, Exercise exercise, ProductionEvaluation evaluation, DateTime now, CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(db, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, evaluation.OverallScore, now);

        db.Attempts.Add(new AttemptEntity
        {
            ExerciseId = exercise.Id, NodeId = exercise.NodeId, Outcome = Outcome.Graded, Score = evaluation.OverallScore, Utc = now,
        });

        // Every tagged error is evidence on its node: a small negative observation at B2.1 difficulty.
        var touched = new Dictionary<string, SkillState>();
        foreach (var err in evaluation.Errors)
        {
            var et = Catalog.Error(err.Code);
            if (et is null) continue;
            db.ErrorEvents.Add(new ErrorEventEntity { Code = err.Code, NodeId = et.NodeId, Utc = now, Snippet = err.Snippet, Correction = err.Correction, FromAi = true });
            if (!touched.TryGetValue(et.NodeId, out var s))
            {
                s = await GetOrCreateStateAsync(db, et.NodeId, ct);
                touched[et.NodeId] = s;
            }
            LearnerAnalysis.ApplyAttempt(s, exercise with { Band = CefrBand.B2_1 }, et.Severity >= 3 ? 0.0 : 0.3, now);
        }
    }

    /// <summary>Self-check when no tutor is configured: the learner ticks the rubric; the fraction becomes the score.</summary>
    public async Task<double> SubmitSelfCheckAsync(long productionId, Guid? sessionId, int stepIndex, IReadOnlyList<bool> checks, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Productions.FirstAsync(p => p.Id == productionId, ct);
        var exercise = Catalog.Exercise(entity.ExerciseId) ?? throw new KeyNotFoundException(entity.ExerciseId);
        var score = checks.Count == 0 ? 0 : (double)checks.Count(c => c) / checks.Count;
        if (exercise.MinWords is { } min && entity.WordCount < min * 0.8) score *= 0.7; // too short cannot be fully "erfüllt"
        entity.Score = score;
        entity.EvaluationJson = JsonSerializer.Serialize(new { selfCheck = checks });
        var now = Now;
        var state = await GetOrCreateStateAsync(db, exercise.NodeId, ct);
        LearnerAnalysis.ApplyAttempt(state, exercise, score, now);
        db.Attempts.Add(new AttemptEntity { SessionId = sessionId, ExerciseId = exercise.Id, NodeId = exercise.NodeId, Outcome = Outcome.Graded, Score = score, Utc = now });
        if (sessionId is not null) await MarkStepDoneAsync(db, sessionId.Value, stepIndex, score, ct);
        await db.SaveChangesAsync(ct);
        return score;
    }

    public async Task<IReadOnlyList<ProductionEntity>> RecentProductionsAsync(int take = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Productions.AsNoTracking().OrderByDescending(p => p.Utc).Take(take).ToListAsync(ct);
    }

    // ------------------------------------------------------------------------------------------
    // Progress, errors, generation
    // ------------------------------------------------------------------------------------------

    public async Task<IReadOnlyDictionary<string, SkillState>> GetSkillStatesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SkillStates.AsNoTracking().ToDictionaryAsync(s => s.NodeId, ct);
    }

    public async Task<IReadOnlyList<ErrorJournalEntry>> GetErrorJournalAsync(int days = 30, int take = 100, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = Now.AddDays(-days);
        var rows = await db.ErrorEvents.AsNoTracking().Where(e => e.Utc >= since).OrderByDescending(e => e.Utc).Take(take).ToListAsync(ct);
        return rows.Select(r =>
        {
            var et = Catalog.Error(r.Code);
            return new ErrorJournalEntry(r.Utc, r.Code, et?.Title ?? r.Code, Catalog.NodeTitle(r.NodeId), r.Snippet, r.Correction, r.FromAi, et?.Severity ?? 1);
        }).ToList();
    }

    public async Task<IReadOnlyList<(DateTime Day, int Attempts, double Accuracy)>> GetDailyHistoryAsync(int days = 30, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = Now.Date.AddDays(-days);
        var rows = await db.Attempts.AsNoTracking().Where(a => a.Utc >= since).Select(a => new { a.Utc, a.Score }).ToListAsync(ct);
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
        var states = await GetSkillStatesAsync(ct);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var errors = await RecentErrorsAsync(db, 30, ct);
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

    public async Task ResetAllDataAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);
    }
}
