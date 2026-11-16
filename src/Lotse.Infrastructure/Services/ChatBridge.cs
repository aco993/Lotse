using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Services;

public sealed record LearnerStatus(
    string Name,
    string TargetLevel,
    string Exam,
    DateOnly? ExamDate,
    int? DaysToExam,
    int StreakDays,
    int DueReviews,
    int MinutesThisWeek,
    int WeeklyGoalMinutes,
    int SessionsLast7Days,
    int TotalAttempts,
    /// <summary>Null while no exam module has been practised enough to be measured - never a guessed number.</summary>
    double? Readiness,
    string Verdict,
    IReadOnlyList<ModuleStatus> Modules,
    IReadOnlyList<WeakTopic> WeakTopics,
    TelcStatus? Telc,
    string NextSession);

public sealed record ModuleStatus(string Module, double? Readiness, double Coverage, IReadOnlyList<string> Blockers);

public sealed record WeakTopic(string NodeId, string Title, string Area, double Mastery, double Confidence, int Errors30d,
    IReadOnlyList<string> TopErrors, string? InterferenceNote);

public sealed record TelcStatus(double? WrittenPoints, double WrittenPass, double? OralPoints, double OralPass, string? ExpectedGrade,
    IReadOnlyList<TelcPartStatus> Parts);

public sealed record TelcPartStatus(string Part, double? Points, double Max, int Evidence);

public sealed record ErrorPattern(string Code, string Title, string Topic, int Count, IReadOnlyList<ErrorExample> Examples);

public sealed record ErrorExample(DateTime Utc, string? Wrong, string? Correction, bool FromAi);

public sealed record TopicInfo(string NodeId, string Title, string Area, string Band, int Exercises, IReadOnlyList<string> ErrorCodes);

/// <summary>An exercise as a chat model writes it: the closed types only, so the app can check the answer itself.</summary>
public sealed record ExerciseDraft(
    string Type,
    string Prompt,
    IReadOnlyList<string>? Answers = null,
    string? Instruction = null,
    IReadOnlyList<string>? Options = null,
    int? CorrectIndex = null,
    string? Explanation = null,
    string? SerbianNote = null,
    string? Band = null);

public sealed record ImportResult(IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected);

/// <summary>A mistake a chat found in the learner's German: the error code from the app's catalogue, the wrong words, the fix.</summary>
public sealed record ExternalError(string Code, string Wrong, string Correction);

/// <summary>
/// What a chat assistant (over MCP, see <c>tools/Lotse.Mcp</c>) may read from and write to one learner's data.
///
/// Reading is the learner's state as the app itself shows it. Writing is deliberately narrow and always checked:
/// closed exercises the app can mark by itself, through the same <see cref="ExerciseValidator"/> as the tutor's own,
/// and mistakes that carry a known error code. Nothing here changes scores, reviews or settings - those come only
/// from practice in the app.
/// </summary>
public sealed class ChatBridge(
    IDbContextFactory<LotseDbContext> dbFactory,
    ContentCatalogProvider catalogProvider,
    ILearningService learning,
    ITelcService telc,
    ICurrentUserAccessor who,
    TimeProvider clock,
    ILogger<ChatBridge> logger)
{
    public const int MaxExercisesPerCall = 10;
    public const int MaxErrorsPerCall = 20;
    public const int MaxSnippetLength = 200;

    /// <summary>Closed types only: their answer is a string or an index the app compares against.</summary>
    public static readonly IReadOnlySet<ExerciseType> ImportableTypes = new HashSet<ExerciseType>
    {
        ExerciseType.Cloze, ExerciseType.Transform, ExerciseType.Translate, ExerciseType.MultipleChoice, ExerciseType.WordOrder,
    };

    private ContentCatalog Catalog => catalogProvider.Catalog;

    public async Task<LearnerStatus> StatusAsync(CancellationToken ct = default)
    {
        var d = await learning.GetDashboardAsync(ct);
        var p = d.Profile;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        int? daysLeft = p.TargetExamDate is { } exam ? exam.DayNumber - today.DayNumber : null;
        var name = $"{p.FirstName} {p.LastName}".Trim();

        TelcStatus? telcStatus = null;
        if (p.Exam == ExamProvider.Telc)
        {
            var r = (await telc.GetOverviewAsync(ct)).Readiness;
            telcStatus = new TelcStatus(
                r.HasWrittenEvidence ? Math.Round(r.Written.Points) : null, TelcBlueprint.WrittenPass,
                r.HasOralEvidence ? Math.Round(r.Oral.Points) : null, TelcBlueprint.OralPass,
                r.ExpectedGrade,
                r.Parts.Select(e => new TelcPartStatus(e.Part.ToString(), e.Points is { } pts ? Math.Round(pts, 1) : null, e.Max, e.Evidence)).ToList());
        }

        return new LearnerStatus(
            name.Length == 0 ? "(ohne Namen)" : name,
            p.TargetLevel.ToString(),
            p.Exam.ToString(),
            p.TargetExamDate,
            daysLeft,
            d.StreakDays,
            d.DueReviews,
            d.MinutesThisWeek,
            p.WeeklyGoalMinutes,
            d.SessionsLast7Days,
            d.TotalAttempts,
            d.Readiness.HasEvidence ? Math.Round(d.Readiness.Overall, 2) : null,
            d.Readiness.Verdict,
            d.Readiness.Modules.Select(m => new ModuleStatus(m.Module, m.HasEvidence ? Math.Round(m.Readiness, 2) : null, Math.Round(m.Coverage, 2), m.Blockers)).ToList(),
            d.WeakAreas.Select(w => new WeakTopic(w.NodeId, w.Title, w.Area.ToString(), Math.Round(w.Mastery, 2), Math.Round(w.Confidence, 2), w.Errors30d,
                w.TopErrors.Select(e => $"{e.Code}: {e.Title} ({e.Count}×)").ToList(), w.InterferenceNote)).ToList(),
            telcStatus,
            d.NextSessionPreview);
    }

    /// <summary>The error journal grouped by error type, most frequent first, with the newest examples of each.</summary>
    public async Task<IReadOnlyList<ErrorPattern>> ErrorPatternsAsync(int days = 30, CancellationToken ct = default)
    {
        var journal = await learning.GetErrorJournalAsync(Math.Clamp(days, 1, 365), take: 500, ct);
        return journal
            .GroupBy(e => e.Code)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(e => e.Utc))
            .Select(g => new ErrorPattern(g.Key, g.First().Title, g.First().NodeTitle, g.Count(),
                g.OrderByDescending(e => e.Utc).Take(3).Select(e => new ErrorExample(e.Utc, e.Snippet, e.Correction, e.FromAi)).ToList()))
            .ToList();
    }

    /// <summary>Every topic of the course with its id - the id <see cref="AddExercisesAsync"/> needs.</summary>
    public IReadOnlyList<TopicInfo> Topics(string? area = null)
    {
        var nodes = Catalog.Nodes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(area))
            nodes = nodes.Where(n => n.Area.ToString().Equals(area, StringComparison.OrdinalIgnoreCase));
        return nodes.Select(n => new TopicInfo(n.Id, n.Title, n.Area.ToString(), n.Band.Label(), Catalog.ForNode(n.Id).Count(),
            Catalog.ErrorTypes.Where(e => e.NodeId == n.Id).Select(e => e.Code).ToList())).ToList();
    }

    /// <summary>
    /// Adds exercises a chat wrote to one topic. Each one is checked like the tutor's own and rejected with its reason
    /// otherwise; accepted ones join the shared exercise bank and come up in the learner's sessions like any other.
    /// </summary>
    public async Task<ImportResult> AddExercisesAsync(string nodeId, IReadOnlyList<ExerciseDraft> drafts, CancellationToken ct = default)
    {
        var node = Catalog.Node(nodeId) ?? throw new ArgumentException($"Unbekanntes Thema '{nodeId}'. Die Ids liefert list_topics.", nameof(nodeId));
        if (drafts.Count > MaxExercisesPerCall) throw new ArgumentException($"Höchstens {MaxExercisesPerCall} Übungen pro Aufruf.", nameof(drafts));

        var existing = Catalog.ForNode(nodeId).Select(e => Normalize(e.Prompt)).ToHashSet(StringComparer.Ordinal);
        var accepted = new List<Exercise>();
        var rejected = new List<string>();
        for (var i = 0; i < drafts.Count; i++)
        {
            var d = drafts[i];
            var label = $"#{i + 1}";
            if (!Enum.TryParse<ExerciseType>(d.Type, ignoreCase: true, out var type) || !ImportableTypes.Contains(type))
            {
                rejected.Add($"{label}: Typ '{d.Type}' geht nicht - erlaubt sind {string.Join(", ", ImportableTypes.Select(t => t.ToString().ToLowerInvariant()))}.");
                continue;
            }
            if (type == ExerciseType.Cloze && !d.Prompt.Contains("___", StringComparison.Ordinal))
            {
                rejected.Add($"{label}: Bei cloze steht die Lücke als ___ im Prompt.");
                continue;
            }
            if (!existing.Add(Normalize(d.Prompt)))
            {
                rejected.Add($"{label}: Diesen Prompt gibt es in diesem Thema schon.");
                continue;
            }
            var band = d.Band is not null && Enum.TryParse<CefrBand>(d.Band, ignoreCase: true, out var b) ? b : node.Band;
            var ex = new Exercise
            {
                Id = $"gen.chat.{node.Id.ToLowerInvariant()}.{Guid.NewGuid():N}"[..40],
                Type = type,
                NodeId = node.Id,
                Band = band,
                Context = ExerciseContext.Beruf,
                Source = ExerciseSource.Generated,
                Prompt = d.Prompt.Trim(),
                Instruction = string.IsNullOrWhiteSpace(d.Instruction) ? null : d.Instruction.Trim(),
                Answers = d.Answers?.Select(a => a.Trim()).ToList() ?? [],
                Options = d.Options?.Select(o => o.Trim()).ToList() ?? [],
                CorrectIndex = type == ExerciseType.MultipleChoice ? d.CorrectIndex : null,
                Explanation = string.IsNullOrWhiteSpace(d.Explanation) ? null : d.Explanation.Trim(),
                SerbianNote = string.IsNullOrWhiteSpace(d.SerbianNote) ? null : d.SerbianNote.Trim(),
            };
            var problems = ExerciseValidator.Validate(ex, Catalog.Nodes.Select(n => n.Id).ToHashSet());
            if (problems.Count > 0)
            {
                rejected.Add($"{label}: {string.Join("; ", problems.Select(p => p.Replace(ex.Id + ": ", "", StringComparison.Ordinal)))}");
                continue;
            }
            accepted.Add(ex);
        }

        if (accepted.Count > 0)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var ex in accepted)
                db.GeneratedExercises.Add(new GeneratedExerciseEntity { Id = ex.Id, NodeId = ex.NodeId, Json = ContentLoader.Serialize(ex), CreatedUtc = now });
            await db.SaveChangesAsync(ct);
            await catalogProvider.RefreshAsync(ct);
        }
        logger.LogInformation("Chat: {Ok}/{Total} Übungen für {Node} übernommen.", accepted.Count, drafts.Count, node.Id);
        return new ImportResult(accepted.Select(e => e.Id).ToList(), rejected);
    }

    /// <summary>
    /// Writes mistakes a chat found into the learner's error journal, so the next sessions in the app aim at them.
    /// Marked as AI findings, like the tutor's; an unknown code is rejected rather than guessed.
    /// </summary>
    public async Task<ImportResult> RecordErrorsAsync(IReadOnlyList<ExternalError> errors, CancellationToken ct = default)
    {
        if (errors.Count > MaxErrorsPerCall) throw new ArgumentException($"Höchstens {MaxErrorsPerCall} Fehler pro Aufruf.", nameof(errors));
        var userId = await who.GetUserIdAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        var accepted = new List<string>();
        var rejected = new List<string>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        for (var i = 0; i < errors.Count; i++)
        {
            var e = errors[i];
            var et = Catalog.Error(e.Code);
            if (et is null)
            {
                rejected.Add($"#{i + 1}: Fehlercode '{e.Code}' gibt es nicht. Die Codes liefert list_topics.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(e.Wrong))
            {
                rejected.Add($"#{i + 1}: Ohne die falsche Stelle ist der Eintrag nicht nachvollziehbar.");
                continue;
            }
            db.ErrorEvents.Add(new ErrorEventEntity
            {
                UserId = userId,
                Code = et.Code,
                NodeId = et.NodeId,
                Utc = now,
                FromAi = true,
                Snippet = Clip(e.Wrong),
                Correction = string.IsNullOrWhiteSpace(e.Correction) ? null : Clip(e.Correction),
            });
            accepted.Add($"{et.Code}: {Clip(e.Wrong)}");
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Chat: {Ok}/{Total} Fehler ins Journal übernommen.", accepted.Count, errors.Count);
        return new ImportResult(accepted, rejected);
    }

    private static string Clip(string s)
    {
        s = s.Trim();
        return s.Length <= MaxSnippetLength ? s : s[..MaxSnippetLength] + "…";
    }

    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
