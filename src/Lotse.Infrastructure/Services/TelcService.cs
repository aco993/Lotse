using System.Text;
using System.Text.Json;
using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lotse.Infrastructure.Services;

/// <summary>A checked objective part: every item right or wrong, and the telc points.</summary>
public sealed record TelcObjectiveResult(TelcPart Part, string TaskId, double Points, double Max, IReadOnlyList<int> Key, IReadOnlyList<int?> Answers, IReadOnlyList<bool> Correct);

/// <summary>A stored production part. <see cref="Check"/> is set for the email only.</summary>
public sealed record TelcProductionResult(TelcPart Part, string TaskId, double Points, double Max, IReadOnlyList<TelcBand> Bands, TelcAiRating? Ai, TelcWritingCheck? Check);

public sealed record TelcAttemptRow(long Id, TelcPart Part, string TaskId, string Title, double Points, double Max, DateTime Utc, bool Simulation);

public sealed record TelcSimulationRow(Guid Id, DateTime StartedUtc, DateTime? CompletedUtc, TelcResult Result);

public sealed record TelcOverview(
    ExamProvider Exam,
    DateOnly? ExamDate,
    TelcReadiness Readiness,
    IReadOnlyList<TelcPlanWeek> Plan,
    IReadOnlyList<TelcAttemptRow> Recent,
    IReadOnlyList<TelcSimulationRow> Simulations,
    Guid? OpenSimulationId);

/// <summary>A simulation in progress or done. <see cref="Next"/> is the first part without a result.</summary>
public sealed record TelcSimulationView(Guid Id, DateTime StartedUtc, DateTime? CompletedUtc, IReadOnlyDictionary<TelcPart, string> Tasks, IReadOnlyDictionary<TelcPart, TelcAttemptRow> Done)
{
    public TelcResult Result => new(Done.ToDictionary(d => d.Key, d => d.Value.Points));
    public TelcPart? Next => Enum.GetValues<TelcPart>().Where(p => !Done.ContainsKey(p)).Cast<TelcPart?>().FirstOrDefault();
    public bool AllDone => Next is null;
}

public interface ITelcService
{
    TelcContent Content { get; }
    bool TutorAvailable { get; }
    Task<TelcOverview> GetOverviewAsync(CancellationToken ct = default);
    /// <summary>The task of this part the learner has done least recently (never done first).</summary>
    Task<string?> NextTaskIdAsync(TelcPart part, CancellationToken ct = default);
    Task<IReadOnlyList<TelcAttemptRow>> HistoryAsync(TelcPart? part = null, int take = 30, CancellationToken ct = default);
    Task<TelcObjectiveResult> SubmitObjectiveAsync(TelcPart part, string taskId, IReadOnlyList<int?> answers, int? durationSeconds = null, Guid? simulationId = null, CancellationToken ct = default);
    /// <summary>The tutor's rating on telc criteria. Throws when no tutor is configured or the call fails.</summary>
    Task<TelcAiRating> RateWithAiAsync(TelcPart part, string taskId, string learnerText, string? conversation = null, CancellationToken ct = default);
    /// <summary>Stores an email or oral part with its bands (self-rated, or taken from <paramref name="ai"/>). The email's bands are capped by its checks.</summary>
    Task<TelcProductionResult> SubmitProductionAsync(TelcPart part, string taskId, string text, IReadOnlyList<TelcBand> bands, bool themaVerfehlt = false, TelcAiRating? ai = null, int? durationSeconds = null, Guid? simulationId = null, CancellationToken ct = default);
    Task<TelcSimulationView> StartOrResumeSimulationAsync(CancellationToken ct = default);
    Task<TelcSimulationView?> GetSimulationAsync(Guid id, CancellationToken ct = default);
    Task<TelcSimulationView> CompleteSimulationAsync(Guid id, CancellationToken ct = default);
    Task AbandonSimulationAsync(Guid id, CancellationToken ct = default);
    Task<LearnerContext> LearnerContextAsync(CancellationToken ct = default);
    /// <summary>One turn of the AI exam partner in an oral part. Throws when no tutor is configured.</summary>
    Task<string> PartnerAsync(TelcPart part, string taskId, IReadOnlyList<ChatTurn> history, CancellationToken ct = default);
    /// <summary>What the deterministic analyzer finds in a text - the corrections between 4/3/2 rounds without a tutor.</summary>
    IReadOnlyList<TextFinding> QuickFindings(string text, bool spoken);
    /// <summary>The task as the candidate reads it, in plain text - what the tutor gets as context.</summary>
    string TaskText(TelcPart part, string taskId);
    /// <summary>Fills the {Name}/{Vorname}/{Nachname} tokens of a model answer with the learner's own name.</summary>
    Task<string> PersonalizeAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// telc Deutsch B2 practice and simulation. Results are stored in telc points per part and feed
/// <see cref="TelcReadiness"/>; each answer also reaches the shared learner model - a Sprachbaustein gap as an
/// attempt (and, when wrong, an error event) on the node it tests, a reading or listening part as one attempt on its
/// skill, an email or oral part as an attempt with the tutor's quoted errors - so the daily planner and the
/// Fehlerjournal learn from exam practice too. Never the FSRS scheduler: these are exam tasks, not review cards.
/// </summary>
public sealed class TelcService(
    IDbContextFactory<LotseDbContext> dbFactory,
    ContentCatalogProvider catalogProvider,
    TelcContent content,
    ITutor tutor,
    ILearningService learning,
    TimeProvider clock,
    ICurrentUserAccessor currentUser) : ITelcService
{
    private ContentCatalog Catalog => catalogProvider.Catalog;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public TelcContent Content => content;
    public bool TutorAvailable => tutor.IsAvailable;

    public async Task<TelcOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profile = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => new { p.Exam, p.TargetExamDate }).FirstOrDefaultAsync(ct);
        var attempts = await db.TelcAttempts.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.Utc).ToListAsync(ct);
        var readiness = TelcReadiness.From(attempts.Select(a => new TelcPartResult(a.Part, a.Points, a.Utc, a.SimulationId is not null)));
        var today = DateOnly.FromDateTime(Now);
        var plan = TelcStudyPlan.Build(today, profile?.TargetExamDate, readiness);

        var sims = await db.TelcSimulations.AsNoTracking().Where(s => s.UserId == userId).OrderByDescending(s => s.StartedUtc).ToListAsync(ct);
        var simRows = sims.Where(s => s.CompletedUtc is not null)
            .Select(s => new TelcSimulationRow(s.Id, s.StartedUtc, s.CompletedUtc,
                new TelcResult(attempts.Where(a => a.SimulationId == s.Id).GroupBy(a => a.Part).ToDictionary(g => g.Key, g => g.First().Points))))
            .ToList();
        var open = sims.FirstOrDefault(s => s.CompletedUtc is null);

        return new TelcOverview(
            profile?.Exam ?? ExamProvider.Goethe,
            profile?.TargetExamDate,
            readiness,
            plan,
            attempts.Take(12).Select(Row).ToList(),
            simRows,
            open?.Id);
    }

    public async Task<string?> NextTaskIdAsync(TelcPart part, CancellationToken ct = default)
    {
        var ids = content.TaskIds(part);
        if (ids.Count == 0) return null;
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var last = await LastDoneAsync(db, userId, part, ct);
        return ids.OrderBy(id => last.TryGetValue(id, out var t) ? t : DateTime.MinValue).ThenBy(ids.ToList().IndexOf).First();
    }

    /// <summary>When each task of a part was last done; the email counts per pair, since the pair is what is drawn.</summary>
    private async Task<Dictionary<string, DateTime>> LastDoneAsync(LotseDbContext db, string userId, TelcPart part, CancellationToken ct)
    {
        var rows = await db.TelcAttempts.AsNoTracking().Where(a => a.UserId == userId && a.Part == part)
            .GroupBy(a => a.TaskId).Select(g => new { g.Key, Last = g.Max(a => a.Utc) }).ToListAsync(ct);
        var result = new Dictionary<string, DateTime>();
        foreach (var r in rows)
        {
            var key = part == TelcPart.Schreiben && content.FindWritingTask(r.Key) is { } w ? w.Pair.Id : r.Key;
            if (!result.TryGetValue(key, out var t) || r.Last > t) result[key] = r.Last;
        }
        return result;
    }

    public async Task<IReadOnlyList<TelcAttemptRow>> HistoryAsync(TelcPart? part = null, int take = 30, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.TelcAttempts.AsNoTracking().Where(a => a.UserId == userId);
        if (part is { } p) q = q.Where(a => a.Part == p);
        var rows = await q.OrderByDescending(a => a.Utc).Take(take).ToListAsync(ct);
        return rows.Select(Row).ToList();
    }

    private TelcAttemptRow Row(TelcAttemptEntity a) => new(a.Id, a.Part, a.TaskId, Title(a.Part, a.TaskId), a.Points, a.MaxPoints, a.Utc, a.SimulationId is not null);

    private string Title(TelcPart part, string taskId)
        => part == TelcPart.Schreiben && content.FindWritingTask(taskId) is { } w
            ? $"{w.Task.Kind} ({(w.Pair.A.Id == taskId ? "A" : "B")})"
            : content.TaskTitle(part, taskId) ?? taskId;

    // ---- Objective parts ---------------------------------------------------------------------------------------

    public async Task<TelcObjectiveResult> SubmitObjectiveAsync(TelcPart part, string taskId, IReadOnlyList<int?> answers, int? durationSeconds = null, Guid? simulationId = null, CancellationToken ct = default)
    {
        if (!TelcBlueprint.IsObjective(part)) throw new ArgumentException($"{part} ist kein geschlossener Teil.", nameof(part));
        var key = content.Key(part, taskId) ?? throw new KeyNotFoundException(taskId);
        var correct = TelcObjectiveScoring.Check(key, answers);
        var points = TelcBlueprint.ObjectivePoints(part, correct.Count(c => c));
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSimulationAsync(db, userId, simulationId, part, ct);

        var now = Now;
        db.TelcAttempts.Add(new TelcAttemptEntity
        {
            UserId = userId,
            Part = part,
            TaskId = taskId,
            Points = points,
            MaxPoints = TelcBlueprint.Info(part).MaxPoints,
            Utc = now,
            DurationSeconds = durationSeconds,
            SimulationId = simulationId,
            AnswersJson = JsonSerializer.Serialize(answers),
        });

        if (part is TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2)
        {
            var task = content.FindSprachbausteine(taskId)!;
            for (var i = 0; i < task.Gaps.Count; i++)
            {
                var gap = task.Gaps[i];
                var answerText = answers.ElementAtOrDefault(i) is { } a ? GapOption(task, gap, a) : "";
                await FeedAsync(db, userId, $"telc.{taskId}.{i + 1}", gap.NodeId, correct[i] ? 1 : 0, answerText,
                    correct[i] ? [] : [(gap.ErrorCode ?? Catalog.ErrorTypes.FirstOrDefault(e => e.NodeId == gap.NodeId)?.Code, answerText, GapOption(task, gap, gap.Correct))], now, ct);
            }
        }
        else
        {
            await FeedAsync(db, userId, $"telc.{taskId}", ReceptiveNode(part), correct.Count(c => c) / (double)correct.Count, null, [], now, ct);
        }

        await db.SaveChangesAsync(ct);
        return new TelcObjectiveResult(part, taskId, points, TelcBlueprint.Info(part).MaxPoints, key, answers, correct);
    }

    private static string GapOption(TelcSprachbausteine task, TelcGap gap, int index)
        => (task.Teil == 1 ? gap.Options : task.Box) is { } list && index >= 0 && index < list.Count ? list[index] : "";

    /// <summary>The skill node a reading or listening part trains: skimming/scanning, detail, everyday or longer listening.</summary>
    public static string ReceptiveNode(TelcPart part) => part switch
    {
        TelcPart.Lesen2 => "LS.LESEN_DETAIL",
        TelcPart.Lesen1 or TelcPart.Lesen3 => "LS.LESEN_GLOBAL_SELEKTIV",
        TelcPart.Hoeren2 => "HV.HOEREN_VORTRAG",
        _ => "HV.HOEREN_ALLTAG",
    };

    // ---- Production parts --------------------------------------------------------------------------------------

    public async Task<TelcAiRating> RateWithAiAsync(TelcPart part, string taskId, string learnerText, string? conversation = null, CancellationToken ct = default)
    {
        if (tutor is ILearnerBound bound) await bound.EnsureCurrentAsync(ct);
        if (!tutor.IsAvailable) throw new InvalidOperationException("Kein KI-Tutor konfiguriert.");
        var context = await learning.BuildLearnerContextAsync(ct);
        return await tutor.RateTelcAsync(new TelcRatingRequest(part, TaskText(part, taskId), learnerText, conversation), context, ct);
    }

    public Task<LearnerContext> LearnerContextAsync(CancellationToken ct = default) => learning.BuildLearnerContextAsync(ct);

    public async Task<string> PartnerAsync(TelcPart part, string taskId, IReadOnlyList<ChatTurn> history, CancellationToken ct = default)
    {
        if (tutor is ILearnerBound bound) await bound.EnsureCurrentAsync(ct);
        if (!tutor.IsAvailable) throw new InvalidOperationException("Kein KI-Tutor konfiguriert.");
        var context = await learning.BuildLearnerContextAsync(ct);
        return await tutor.PartnerAsync(part, TaskText(part, taskId), history, context, ct);
    }

    public IReadOnlyList<TextFinding> QuickFindings(string text, bool spoken)
    {
        var task = new Exercise { Id = "telc.quick", Type = spoken ? ExerciseType.Speak : ExerciseType.FreeWrite, NodeId = "SP.VORTRAG", Band = CefrBand.B2_1, Prompt = "telc" };
        return FreeTextAnalyzer.Analyze(text, task, catalogProvider.Lexicon, spoken).Findings;
    }

    public async Task<TelcProductionResult> SubmitProductionAsync(TelcPart part, string taskId, string text, IReadOnlyList<TelcBand> bands, bool themaVerfehlt = false, TelcAiRating? ai = null, int? durationSeconds = null, Guid? simulationId = null, CancellationToken ct = default)
    {
        if (TelcBlueprint.IsObjective(part)) throw new ArgumentException($"{part} ist ein geschlossener Teil.", nameof(part));
        var speaking = !TelcBlueprint.IsWritten(part);
        if (bands.Count != (speaking ? 4 : 3)) throw new ArgumentException("Für jedes Kriterium braucht es genau eine Stufe.", nameof(bands));

        TelcWritingCheck? check = null;
        double points;
        IReadOnlyList<TelcBand> stored = bands;
        string nodeId;
        if (!speaking)
        {
            var writing = content.FindWritingTask(taskId) ?? throw new KeyNotFoundException(taskId);
            check = TelcWritingCheck.Analyze(text, writing.Task);
            var rating = new TelcWritingRating(bands[0], bands[1], bands[2], themaVerfehlt).Capped(check);
            points = rating.Points;
            stored = [rating.Aufgabe, rating.Gestaltung, rating.Richtigkeit];
            nodeId = writing.Task.NodeId;
        }
        else
        {
            if (content.TaskTitle(part, taskId) is null) throw new KeyNotFoundException(taskId);
            points = new TelcSpeakingRating(bands[0], bands[1], bands[2], bands[3]).Points;
            nodeId = part == TelcPart.Sprechen1 ? "SP.VORTRAG" : "SP.DISKUSSION";
        }

        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSimulationAsync(db, userId, simulationId, part, ct);
        var now = Now;
        var max = TelcBlueprint.Info(part).MaxPoints;
        db.TelcAttempts.Add(new TelcAttemptEntity
        {
            UserId = userId,
            Part = part,
            TaskId = taskId,
            Points = points,
            MaxPoints = max,
            Utc = now,
            DurationSeconds = durationSeconds,
            SimulationId = simulationId,
            Text = text,
            RatingJson = JsonSerializer.Serialize(new StoredRating(stored, themaVerfehlt, ai), ContentLoader.Options),
            RatedByAi = ai is not null,
        });
        var errors = (ai?.Errors ?? []).Select(e => ((string?)e.Code, e.Snippet, e.Correction)).ToList();
        await FeedAsync(db, userId, $"telc.{taskId}", nodeId, points / max, Trim(text), errors, now, ct, fromAi: ai is not null);
        await db.SaveChangesAsync(ct);
        return new TelcProductionResult(part, taskId, points, max, stored, ai, check);
    }

    private sealed record StoredRating(IReadOnlyList<TelcBand> Bands, bool ThemaVerfehlt, TelcAiRating? Ai);

    // ---- Simulation --------------------------------------------------------------------------------------------

    public async Task<TelcSimulationView> StartOrResumeSimulationAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var open = await db.TelcSimulations.AsNoTracking().Where(s => s.UserId == userId && s.CompletedUtc == null).OrderByDescending(s => s.StartedUtc).FirstOrDefaultAsync(ct);
        if (open is not null) return await ViewAsync(db, open, ct);

        // One task per part, the least recently practised - a simulation should not replay last week's text.
        var tasks = new Dictionary<TelcPart, string>();
        foreach (var part in Enum.GetValues<TelcPart>())
        {
            var ids = content.TaskIds(part);
            if (ids.Count == 0) throw new InvalidOperationException($"Für {TelcBlueprint.Info(part).Title} gibt es keine Aufgabe.");
            var last = await LastDoneAsync(db, userId, part, ct);
            tasks[part] = ids.OrderBy(id => last.TryGetValue(id, out var t) ? t : DateTime.MinValue).ThenBy(ids.ToList().IndexOf).First();
        }
        var sim = new TelcSimulationEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StartedUtc = Now,
            TasksJson = JsonSerializer.Serialize(tasks.ToDictionary(t => t.Key.ToString(), t => t.Value)),
        };
        db.TelcSimulations.Add(sim);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(db, sim, ct);
    }

    public async Task<TelcSimulationView?> GetSimulationAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sim = await db.TelcSimulations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        return sim is null ? null : await ViewAsync(db, sim, ct);
    }

    public async Task<TelcSimulationView> CompleteSimulationAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sim = await db.TelcSimulations.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct) ?? throw new KeyNotFoundException(id.ToString());
        sim.CompletedUtc ??= Now;
        await db.SaveChangesAsync(ct);
        return await ViewAsync(db, sim, ct);
    }

    public async Task AbandonSimulationAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The parts already done stay as practice results; only the frame goes.
        await db.TelcAttempts.Where(a => a.UserId == userId && a.SimulationId == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.SimulationId, (Guid?)null), ct);
        await db.TelcSimulations.Where(s => s.Id == id && s.UserId == userId && s.CompletedUtc == null).ExecuteDeleteAsync(ct);
    }

    private async Task<TelcSimulationView> ViewAsync(LotseDbContext db, TelcSimulationEntity sim, CancellationToken ct)
    {
        var tasks = (JsonSerializer.Deserialize<Dictionary<string, string>>(sim.TasksJson) ?? [])
            .Where(t => Enum.TryParse<TelcPart>(t.Key, out _))
            .ToDictionary(t => Enum.Parse<TelcPart>(t.Key), t => t.Value);
        var rows = await db.TelcAttempts.AsNoTracking().Where(a => a.SimulationId == sim.Id).OrderBy(a => a.Utc).ToListAsync(ct);
        var done = rows.GroupBy(a => a.Part).ToDictionary(g => g.Key, g => Row(g.First()));
        return new TelcSimulationView(sim.Id, sim.StartedUtc, sim.CompletedUtc, tasks, done);
    }

    /// <summary>A result may join a simulation only once per part, and only the learner's own, still open one.</summary>
    private static async Task EnsureSimulationAsync(LotseDbContext db, string userId, Guid? simulationId, TelcPart part, CancellationToken ct)
    {
        if (simulationId is not { } id) return;
        var ok = await db.TelcSimulations.AnyAsync(s => s.Id == id && s.UserId == userId && s.CompletedUtc == null, ct);
        if (!ok) throw new InvalidOperationException("Diese Simulation ist nicht (mehr) offen.");
        if (await db.TelcAttempts.AnyAsync(a => a.SimulationId == id && a.Part == part, ct))
            throw new InvalidOperationException($"{TelcBlueprint.Info(part).Title} ist in dieser Simulation schon abgegeben.");
    }

    // ---- Learner model -----------------------------------------------------------------------------------------

    private async Task FeedAsync(LotseDbContext db, string userId, string exerciseId, string nodeId, double score, string? answer,
        IReadOnlyList<(string? Code, string Snippet, string Correction)> errors, DateTime now, CancellationToken ct, bool fromAi = false)
    {
        if (Catalog.Node(nodeId) is null) return;
        var attempt = new AttemptEntity
        {
            UserId = userId,
            ExerciseId = exerciseId,
            NodeId = nodeId,
            Outcome = score >= 1 ? Outcome.Correct : score <= 0 ? Outcome.Incorrect : Outcome.Graded,
            Score = Math.Clamp(score, 0, 1),
            Utc = now,
            AnswerText = answer,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        foreach (var (code, snippet, correction) in errors)
        {
            if (code is null || Catalog.Error(code) is not { } type) continue;
            db.ErrorEvents.Add(new ErrorEventEntity
            {
                UserId = userId,
                AttemptId = attempt.Id,
                Code = code,
                NodeId = type.NodeId,
                Utc = now,
                Snippet = Trim(snippet),
                Correction = Trim(correction),
                FromAi = fromAi,
            });
        }

        var state = await db.SkillStates.FindAsync([userId, nodeId], ct);
        if (state is null)
        {
            state = new SkillState { NodeId = nodeId, Theta = -0.2 };
            var entry = db.Entry(state);
            entry.Property(LotseDbContext.UserIdShadow).CurrentValue = userId;
            entry.State = EntityState.Added;
        }
        var exercise = new Exercise { Id = exerciseId, Type = ExerciseType.Reading, NodeId = nodeId, Band = CefrBand.B2_1, Prompt = exerciseId };
        LearnerAnalysis.ApplyAttempt(state, exercise, Math.Clamp(score, 0, 1), now);
    }

    private static string? Trim(string? s) => s is { Length: > 300 } ? s[..300] + "…" : s;

    public async Task<string> PersonalizeAsync(string text, CancellationToken ct = default)
    {
        if (!NameTemplate.ContainsToken(text)) return text;
        var userId = await currentUser.GetUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new { p.FirstName, p.LastName, p.HelperLanguage }).FirstOrDefaultAsync(ct);
        var view = (row is null ? LearnerView.Default : new LearnerView(row.FirstName ?? "", row.LastName ?? "", row.HelperLanguage)).WithFallbackFor(userId);
        return NameTemplate.Render(text, view.FirstName, view.LastName);
    }

    // ---- Task text for the tutor -------------------------------------------------------------------------------

    public string TaskText(TelcPart part, string taskId)
    {
        var sb = new StringBuilder();
        switch (part)
        {
            case TelcPart.Schreiben when content.FindWritingTask(taskId) is { } w:
                sb.AppendLine($"Aufgabe: {w.Task.Kind}").AppendLine(w.Task.Situation);
                if (w.Task.Stimulus is { } stim) sb.AppendLine().AppendLine("Anzeige/Anlass:").AppendLine(stim);
                sb.AppendLine().AppendLine("Behandeln Sie entweder drei der folgenden Punkte oder zwei und einen weiteren Aspekt Ihrer Wahl:");
                foreach (var l in w.Task.Leitpunkte) sb.AppendLine("- " + l.Text);
                sb.AppendLine($"Register: {(w.Task.Formal ? "formell" : "halbformell")}. Mindestens {TelcBlueprint.WritingMinWords} Wörter, Betreff, Anrede, Einleitung, Schluss.");
                break;
            case TelcPart.Sprechen1 when content.FindErfahrung(taskId) is { } e:
                sb.AppendLine($"Thema: {e.Title} ({string.Join(", ", e.Stichpunkte)})").AppendLine("Der Kandidat spricht ca. 1,5 Minuten und beantwortet danach Fragen des Partners.");
                break;
            case TelcPart.Sprechen2 when content.FindDiskussion(taskId) is { } d:
                sb.AppendLine($"Zeitungstext: {d.Title}").AppendLine(d.Text).AppendLine()
                  .AppendLine("Diskutieren Sie über den Inhalt des Textes, bringen Sie Ihre Erfahrungen ein und äußern Sie Ihre Meinung. Begründen Sie Ihre Argumente. Sprechen Sie über mögliche Lösungen.");
                break;
            case TelcPart.Sprechen3 when content.FindPlanung(taskId) is { } p:
                sb.AppendLine(p.Title).AppendLine(p.Situation).AppendLine("Mögliche Planungspunkte: " + string.Join(", ", p.Aspects));
                break;
            default:
                sb.Append(content.TaskTitle(part, taskId) ?? taskId);
                break;
        }
        return sb.ToString().Trim();
    }
}
