using System.Text.Json;
using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Training;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lotse.Infrastructure.Services;

/// <summary>An answer as the learner typed it, with their 1/2/3. Confidence 0 = not chosen yet.</summary>
public sealed record TrainingAnswerInput(string Key, string Answer, int Confidence);

/// <summary>The learner's own diagnosis of one item after seeing the solution.</summary>
/// <param name="Correct">Only read for free items; closed items keep the checker's verdict.</param>
public sealed record TrainingVerdict(string Key, bool? Correct, ErrorKind? Kind);

/// <summary>One checked item. <see cref="Correct"/> is null for a free item the learner has not judged yet.</summary>
public sealed record TrainingItemResult(
    TrainingItem Item,
    string Answer,
    int Confidence,
    bool? Correct,
    bool AlmostCorrect,
    string? Expected,
    string? Feedback,
    ErrorKind? SuggestedKind,
    ErrorKind? Kind,
    IReadOnlyList<TextFinding> Findings)
{
    /// <summary>Wrong although the learner was sure: the costliest kind of mistake, flagged first.</summary>
    public bool Urgent => Correct == false && Confidence >= 3;
}

public enum TrainingSessionState { Open, Submitted, Completed }

public sealed record TrainingSessionView(
    Guid Id,
    int Number,
    TrainingSessionState State,
    string? TopicTitle,
    IReadOnlyList<TrainingItem> Items,
    IReadOnlyDictionary<string, TrainingAnswerInput> Draft,
    IReadOnlyList<TrainingItemResult> Results);

public sealed record TrainingPointRow(TrainingPoint Point, string TopicTitle, PointProgress Progress, int Answers);

public sealed record TrainingOverview(
    int CompletedSessions,
    int NextNumber,
    Guid? OpenSessionId,
    TrainingSessionState? OpenState,
    IReadOnlyList<TrainingPointRow> Points);

public interface ITrainingService
{
    TrainingContent Content { get; }
    Task<TrainingOverview> GetOverviewAsync(CancellationToken ct = default);
    /// <summary>The open session, or a new one planned from everything answered so far.</summary>
    Task<TrainingSessionView> StartOrResumeAsync(CancellationToken ct = default);
    Task<TrainingSessionView?> GetSessionAsync(Guid id, CancellationToken ct = default);
    Task SaveDraftAsync(Guid id, IReadOnlyList<TrainingAnswerInput> answers, CancellationToken ct = default);
    /// <summary>Checks and stores the answers. Refuses unless every item has an answer and a confidence.</summary>
    Task<TrainingSessionView> SubmitAsync(Guid id, IReadOnlyList<TrainingAnswerInput> answers, CancellationToken ct = default);
    /// <summary>Stores the diagnosis and feeds the learner model. Refuses unless every mistake has a kind and every free item a verdict.</summary>
    Task<TrainingSessionView> CompleteAsync(Guid id, IReadOnlyList<TrainingVerdict> verdicts, CancellationToken ct = default);
}

/// <summary>
/// The B2-Training loop: a whole session answered as one form with a confidence per item, then corrected, diagnosed
/// (A/B/K/S) and tracked per point until <see cref="LearnedRule"/> says it is learned. Feeds the same learner model
/// as the rest of the app (attempts, error events, skill states), so the Fehlerjournal and the daily planner see
/// training mistakes too - but never the FSRS scheduler: confidence is the learner's self-report, and the
/// scheduler's grades come from behaviour only.
/// </summary>
public sealed class TrainingService(
    IDbContextFactory<LotseDbContext> dbFactory,
    ContentCatalogProvider catalogProvider,
    TrainingContent content,
    TimeProvider clock,
    ICurrentUserAccessor currentUser) : ITrainingService
{
    private ContentCatalog Catalog => catalogProvider.Catalog;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public TrainingContent Content => content;

    public async Task<TrainingOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var completed = await db.TrainingSessions.CountAsync(s => s.UserId == userId && s.CompletedUtc != null, ct);
        var open = await OpenSessionAsync(db, userId, ct);
        var history = await HistoryAsync(db, userId, ct);
        var rows = content.Points
            .Select(p => new TrainingPointRow(p, content.Topic(p.TopicId)?.Title ?? p.TopicId, LearnedRule.Evaluate(p.Id, history), history.Count(a => a.PointId == p.Id)))
            .ToList();
        return new TrainingOverview(completed, completed + 1, open?.Id, open is null ? null : StateOf(open), rows);
    }

    public async Task<TrainingSessionView> StartOrResumeAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var open = await OpenSessionAsync(db, userId, ct);
        if (open is not null) return await BuildViewAsync(db, open, ct);

        var number = await db.TrainingSessions.CountAsync(s => s.UserId == userId, ct) + 1;
        var history = await HistoryAsync(db, userId, ct);
        var since = Now.AddDays(-14);
        var recent = (await db.Attempts.AsNoTracking().Where(a => a.UserId == userId && a.Utc >= since).Select(a => a.ExerciseId).ToListAsync(ct)).ToHashSet();
        var view = await LearnerViewAsync(db, userId, ct);
        var plan = TrainingPlanner.Plan(number, content, Catalog, history, recent, seed: StableHash.Seed($"{userId}|{number}"), render: e => NameTemplate.Render(e, view));

        var session = new TrainingSessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Number = number,
            TopicId = plan.TopicId,
            StartedUtc = Now,
            ItemsJson = JsonSerializer.Serialize(plan.Items, ContentLoader.Options),
        };
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return await BuildViewAsync(db, session, ct);
    }

    public async Task<TrainingSessionView?> GetSessionAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.TrainingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        return session is null ? null : await BuildViewAsync(db, session, ct);
    }

    public async Task SaveDraftAsync(Guid id, IReadOnlyList<TrainingAnswerInput> answers, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null || session.SubmittedUtc is not null) return;
        session.DraftJson = JsonSerializer.Serialize(answers.ToDictionary(a => a.Key), ContentLoader.Options);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Keys of items that still lack an answer or a confidence - what blocks the submit button.</summary>
    public static IReadOnlyList<string> Missing(IReadOnlyList<TrainingItem> items, IReadOnlyList<TrainingAnswerInput> answers)
    {
        var byKey = answers.ToDictionary(a => a.Key);
        return items.Where(i => !byKey.TryGetValue(i.Key, out var a) || string.IsNullOrWhiteSpace(a.Answer) || a.Confidence is < 1 or > 3)
            .Select(i => i.Key).ToList();
    }

    public async Task<TrainingSessionView> SubmitAsync(Guid id, IReadOnlyList<TrainingAnswerInput> answers, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct)
                      ?? throw new KeyNotFoundException(id.ToString());
        // A second click, or a second tab, must not store the answers twice.
        if (session.SubmittedUtc is not null) return await BuildViewAsync(db, session, ct);

        var items = Items(session);
        var missing = Missing(items, answers);
        if (missing.Count > 0)
            throw new InvalidOperationException($"Noch nicht vollständig: {string.Join(", ", missing)}");

        var now = Now;
        var byKey = answers.ToDictionary(a => a.Key);
        foreach (var item in items)
        {
            var a = byKey[item.Key];
            db.TrainingAnswers.Add(new TrainingAnswerEntity
            {
                UserId = userId,
                SessionId = session.Id,
                SessionNumber = session.Number,
                ItemKey = item.Key,
                NodeId = item.NodeId,
                PointId = item.PointId,
                VariantId = item.VariantId,
                Answer = a.Answer.Trim(),
                Confidence = a.Confidence,
                // A slip (capitalisation, umlaut, one typo) counts as right, as everywhere else in the app.
                Correct = item.IsFree ? null : AnswerChecker.Check(item.ToExercise(), a.Answer).Outcome != Outcome.Incorrect,
                Utc = now,
            });
        }
        session.SubmittedUtc = now;
        session.DraftJson = "{}";
        await db.SaveChangesAsync(ct);
        return await BuildViewAsync(db, session, ct);
    }

    public async Task<TrainingSessionView> CompleteAsync(Guid id, IReadOnlyList<TrainingVerdict> verdicts, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct)
                      ?? throw new KeyNotFoundException(id.ToString());
        if (session.CompletedUtc is not null) return await BuildViewAsync(db, session, ct);
        if (session.SubmittedUtc is null) throw new InvalidOperationException("Die Sitzung wurde noch nicht abgegeben.");

        var items = Items(session).ToDictionary(i => i.Key);
        var rows = await db.TrainingAnswers.Where(a => a.SessionId == session.Id).ToListAsync(ct);
        var byKey = verdicts.ToDictionary(v => v.Key);
        var undecided = new List<string>();
        foreach (var row in rows)
        {
            var item = items[row.ItemKey];
            byKey.TryGetValue(row.ItemKey, out var v);
            if (item.IsFree) row.Correct = v?.Correct;
            if (row.Correct is null || (row.Correct == false && v?.Kind is null)) undecided.Add(row.ItemKey);
            row.Kind = row.Correct == false ? v?.Kind : null;
        }
        if (undecided.Count > 0)
            throw new InvalidOperationException($"Noch ohne Einordnung: {string.Join(", ", undecided)}");

        var now = Now;
        foreach (var row in rows)
            await FeedLearnerModelAsync(db, userId, session, items[row.ItemKey], row, now, ct);
        session.CompletedUtc = now;
        await db.SaveChangesAsync(ct);
        return await BuildViewAsync(db, session, ct);
    }

    /// <summary>
    /// Writes the item into the shared learner model exactly like a normal answer would: an attempt, an error event
    /// with the point's catalogue code when wrong, and the node's ability. No review state - see the class summary.
    /// </summary>
    private async Task FeedLearnerModelAsync(LotseDbContext db, string userId, TrainingSessionEntity session, TrainingItem item, TrainingAnswerEntity row, DateTime now, CancellationToken ct)
    {
        var correct = row.Correct == true;
        var check = item.IsFree ? null : AnswerChecker.Check(item.ToExercise(), row.Answer);
        var outcome = item.IsFree ? Outcome.Graded : check!.Outcome;
        var score = item.IsFree ? (correct ? 1.0 : 0.0) : check!.Score;
        var attempt = new AttemptEntity
        {
            UserId = userId,
            SessionId = session.Id,
            ExerciseId = item.ExerciseId ?? (item.PointId is not null ? $"tr.{item.PointId}.{item.VariantId}" : $"tr.{item.TaskId}"),
            NodeId = item.NodeId,
            Outcome = outcome,
            Score = score,
            Utc = now,
            AnswerText = row.Answer,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        var codes = new List<string>(check?.SlipCodes ?? []);
        if (!correct)
        {
            var code = item.ErrorCode ?? Catalog.ErrorTypes.FirstOrDefault(e => e.NodeId == item.NodeId)?.Code;
            if (code is not null) codes.Add(code);
        }
        foreach (var code in codes.Distinct())
        {
            db.ErrorEvents.Add(new ErrorEventEntity
            {
                UserId = userId,
                AttemptId = attempt.Id,
                Code = code,
                NodeId = Catalog.Error(code)?.NodeId ?? item.NodeId,
                Utc = now,
                Snippet = Trim(row.Answer),
                Correction = Trim(item.IsFree ? item.ModelAnswer : check!.Expected),
            });
        }

        var state = await db.SkillStates.FindAsync([userId, item.NodeId], ct);
        if (state is null)
        {
            state = new SkillState { NodeId = item.NodeId, Theta = -0.2 };
            var entry = db.Entry(state);
            entry.Property(LotseDbContext.UserIdShadow).CurrentValue = userId;
            entry.State = EntityState.Added;
        }
        var exercise = item.ExerciseId is not null ? Catalog.Exercise(item.ExerciseId) ?? item.ToExercise() : item.ToExercise();
        LearnerAnalysis.ApplyAttempt(state, exercise, score, now);
    }

    private static string? Trim(string? s) => s is { Length: > 300 } ? s[..300] + "…" : s;

    private async Task<TrainingSessionView> BuildViewAsync(LotseDbContext db, TrainingSessionEntity session, CancellationToken ct)
    {
        var items = Items(session);
        var draft = JsonSerializer.Deserialize<Dictionary<string, TrainingAnswerInput>>(session.DraftJson, ContentLoader.Options) ?? [];
        var results = new List<TrainingItemResult>();
        if (session.SubmittedUtc is not null)
        {
            var rows = (await db.TrainingAnswers.AsNoTracking().Where(a => a.SessionId == session.Id).ToListAsync(ct)).ToDictionary(a => a.ItemKey);
            foreach (var item in items)
            {
                if (!rows.TryGetValue(item.Key, out var row)) continue;
                if (item.IsFree)
                {
                    var task = new Exercise
                    {
                        Id = item.TaskId ?? item.Key,
                        Type = item.Speaking ? ExerciseType.Speak : ExerciseType.FreeWrite,
                        NodeId = item.NodeId,
                        Band = CefrBand.B2_1,
                        Prompt = item.Prompt,
                        Rubric = item.Checks,
                    };
                    var report = FreeTextAnalyzer.Analyze(row.Answer, task, catalogProvider.Lexicon, spoken: item.Speaking);
                    var suggested = report.Findings.Count > 0 ? ErrorKind.B : (ErrorKind?)null;
                    results.Add(new TrainingItemResult(item, row.Answer, row.Confidence, row.Correct, false, item.ModelAnswer, null, suggested, row.Kind, report.Findings));
                }
                else
                {
                    var check = AnswerChecker.Check(item.ToExercise(), row.Answer);
                    var almost = check.Outcome == Outcome.AlmostCorrect;
                    // The default diagnosis: the point's own (K for collocations); otherwise B, because the profile
                    // says most gaps are automation, not ignorance. The learner overrides it with one tap.
                    var suggested = item.SuggestedKind ?? ErrorKind.B;
                    results.Add(new TrainingItemResult(item, row.Answer, row.Confidence, row.Correct, almost, check.Expected, check.Feedback, suggested, row.Kind, []));
                }
            }
        }
        var topicTitle = session.TopicId is null ? null : content.Topic(session.TopicId)?.Title;
        return new TrainingSessionView(session.Id, session.Number, StateOf(session), topicTitle, items, draft, results);
    }

    private static TrainingSessionState StateOf(TrainingSessionEntity s)
        => s.CompletedUtc is not null ? TrainingSessionState.Completed : s.SubmittedUtc is not null ? TrainingSessionState.Submitted : TrainingSessionState.Open;

    private static IReadOnlyList<TrainingItem> Items(TrainingSessionEntity s)
        => JsonSerializer.Deserialize<List<TrainingItem>>(s.ItemsJson, ContentLoader.Options) ?? [];

    private static Task<TrainingSessionEntity?> OpenSessionAsync(LotseDbContext db, string userId, CancellationToken ct)
        => db.TrainingSessions.AsNoTracking().Where(s => s.UserId == userId && s.CompletedUtc == null).OrderByDescending(s => s.Number).FirstOrDefaultAsync(ct);

    /// <summary>Every diagnosed point answer from completed sessions - the input of <see cref="LearnedRule"/>.</summary>
    private static async Task<IReadOnlyList<PointAnswer>> HistoryAsync(LotseDbContext db, string userId, CancellationToken ct)
    {
        var completed = db.TrainingSessions.Where(s => s.UserId == userId && s.CompletedUtc != null).Select(s => s.Id);
        var rows = await db.TrainingAnswers.AsNoTracking()
            .Where(a => a.UserId == userId && a.PointId != null && a.Correct != null && completed.Contains(a.SessionId))
            .ToListAsync(ct);
        return rows.Select(a => new PointAnswer(a.PointId!, a.VariantId ?? "", a.SessionNumber, a.Correct!.Value, a.Confidence, a.Kind)).ToList();
    }

    private static async Task<LearnerView> LearnerViewAsync(LotseDbContext db, string userId, CancellationToken ct)
    {
        var row = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new { p.FirstName, p.LastName, p.HelperLanguage }).FirstOrDefaultAsync(ct);
        var view = row is null ? LearnerView.Default : new LearnerView(row.FirstName ?? "", row.LastName ?? "", row.HelperLanguage);
        return view.WithFallbackFor(userId);
    }
}
