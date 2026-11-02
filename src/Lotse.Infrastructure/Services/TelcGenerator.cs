using System.Diagnostics;
using System.Text;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Services;

/// <summary>One draft as the review page shows it: the task with its key, and what the checks found.</summary>
public sealed record TelcDraftView(
    string Id, TelcPart Part, TelcDraftStatus Status, string Title, string Text, IReadOnlyList<string> Problems,
    bool CriticOk, string? Topic, string? Model, int LatencyMs, DateTime CreatedUtc, string? RejectReason);

/// <summary>
/// How the pipeline performs, measured on everything it ever produced - the honest numbers behind "the AI writes
/// exam tasks". <see cref="CriticAgreement"/> is the share of human decisions the critic model predicted
/// (critic ok ↔ approved); it says how far the second model can be trusted to pre-sort.
/// </summary>
public sealed record TelcPipelineStats(int Invalid, int Pending, int Approved, int Rejected, double? AcceptanceRate, double? CriticAgreement, int? MedianLatencyMs)
{
    public int Total => Invalid + Pending + Approved + Rejected;
}

public interface ITelcGenerator
{
    bool Available { get; }
    /// <summary>Writes one task for <paramref name="part"/>, checks it and stores it as a draft. Throws when no tutor is configured.</summary>
    Task<TelcDraftView> GenerateAsync(TelcPart part, string? topic = null, CancellationToken ct = default);
    Task<IReadOnlyList<TelcDraftView>> DraftsAsync(TelcDraftStatus? status = null, int take = 50, CancellationToken ct = default);
    Task ReviewAsync(string id, bool approve, string? reason = null, CancellationToken ct = default);
    Task<TelcPipelineStats> StatsAsync(CancellationToken ct = default);
}

/// <summary>
/// The content pipeline for telc tasks: the tutor writes a task in the format of a seed example → the format rules
/// check it (<see cref="TelcContent.Validate"/>, plus a test that it did not copy the example) → a second call
/// reviews it as a critic, solution key included → a human approves or rejects it on the review page. Only approved
/// tasks reach practice and simulations. Every draft is kept, failed ones too, so the statistics count all of them.
/// </summary>
public sealed class TelcGenerator(
    IDbContextFactory<LotseDbContext> dbFactory,
    TelcContentProvider telc,
    ContentCatalogProvider catalog,
    ITutor tutor,
    ILearningService learning,
    TimeProvider clock,
    ICurrentUserAccessor currentUser,
    ILogger<TelcGenerator> logger) : ITelcGenerator
{
    /// <summary>B2 topics of everyday and working life in Germany; a draft takes the one used least so far.</summary>
    public static readonly IReadOnlyList<string> Topics =
    [
        "Arbeitswelt und Weiterbildung", "Wohnen und Nachbarschaft", "Gesundheit und Vorsorge", "Umwelt und Klima im Alltag",
        "Digitalisierung und Datenschutz", "Mobilität und Verkehr", "Freizeit und Ehrenamt", "Familie und Kinderbetreuung",
        "Konsum und Verbraucherschutz", "Behörden und Bürgerservice", "Ernährung und Lebensmittel", "Kultur und Medien",
        "Reisen und Urlaub", "Sprachenlernen und Integration", "Geld, Banken und Versicherungen", "Sport und Vereinsleben",
    ];

    /// <summary>Six-word sequences a draft may share with its example before it counts as a copy.</summary>
    public const double MaxShingleOverlap = 0.15;

    public bool Available => tutor.IsAvailable;

    public async Task<TelcDraftView> GenerateAsync(TelcPart part, string? topic = null, CancellationToken ct = default)
    {
        if (!TelcContent.GeneratableParts.Contains(part)) throw new ArgumentException($"{part} wird nicht generiert.", nameof(part));
        if (tutor is ILearnerBound bound) await bound.EnsureCurrentAsync(ct);
        if (!tutor.IsAvailable) throw new InvalidOperationException("Kein KI-Tutor konfiguriert.");
        var userId = await currentUser.GetUserIdAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        topic ??= await LeastUsedTopicAsync(db, ct);
        var seed = telc.Seed;
        var exampleId = seed.TaskIds(part)[Random.Shared.Next(seed.TaskIds(part).Count)];
        var example = TelcDraftCodec.Example(seed, part, exampleId);
        var context = await learning.BuildLearnerContextAsync(ct);

        var id = $"gen.{part.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}"[..32];
        var draft = new TelcDraftEntity { Id = id, Part = part, Json = "", Topic = topic, Model = tutor.Description, CreatedByUserId = userId, CreatedUtc = clock.GetUtcNow().UtcDateTime };
        var watch = Stopwatch.StartNew();
        try
        {
            var raw = await tutor.GenerateTelcAsync(part, Brief(part, topic), example, context, ct);
            draft.Json = raw;
            var single = TelcDraftCodec.TryRead(part, raw, id);
            var problems = single is null ? ["Antwort passt nicht zum Format (Feld fehlt oder hat den falschen Typ)."] : FormatProblems(single, part);
            if (problems.Count > 0)
            {
                draft.Status = TelcDraftStatus.Invalid;
                draft.Problems = string.Join("\n", problems);
            }
            else
            {
                draft.Json = TelcDraftCodec.Write(single!, part);
                var critique = await tutor.CritiqueTelcAsync(part, TelcTaskText.WithKey(single!, part, id), context, ct);
                draft.Status = TelcDraftStatus.Pending;
                draft.CriticOk = critique.Ok;
                draft.Problems = critique.Problems.Count == 0 ? null : string.Join("\n", critique.Problems);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "telc-Generierung für {Part} fehlgeschlagen.", part);
            draft.Status = TelcDraftStatus.Invalid;
            draft.Problems = "Der Tutor lieferte keine lesbare Aufgabe: " + e.Message;
        }
        draft.LatencyMs = (int)watch.ElapsedMilliseconds;
        db.TelcDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("telc-Entwurf {Id} ({Part}): {Status} in {Ms} ms.", id, part, draft.Status, draft.LatencyMs);
        return View(draft);
    }

    /// <summary>What the prompt adds to the example: the topic, the mix of answers, and for Sprachbausteine the nodes a gap may test.</summary>
    private string Brief(TelcPart part, string topic)
    {
        var sb = new StringBuilder().AppendLine($"THEMA: {topic}. Ein neuer Text mit neuem Inhalt, anderen Personen, anderen Orten als im Beispiel.");
        switch (part)
        {
            case TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2:
                sb.AppendLine(part == TelcPart.Sprachbausteine1
                        ? "Teil 1: jede Lücke hat drei Optionen; die falschen sind typische Lernerfehler (falscher Kasus, falsche Präposition, falscher Konnektor), keine Unsinnswörter."
                        : "Teil 2: ein Kasten mit 15 verschiedenen Wörtern, davon genau 10 Lösungen und 5 plausible Ablenker; options ist bei jeder Lücke null.")
                  .AppendLine("Jede Lücke testet GENAU EINEN dieser Knoten (nodeId) und, wenn passend, einen Fehlercode dieses Knotens:");
                var codes = catalog.Catalog.ErrorTypes.ToLookup(e => e.NodeId);
                foreach (var n in catalog.Catalog.Nodes.Where(n => n.Area == SkillArea.Grammatik))
                    sb.AppendLine($"- {n.Id} ({n.Title}){(codes[n.Id].Any() ? ": " + string.Join(", ", codes[n.Id].Select(c => c.Code)) : "")}");
                break;
            case TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3:
                sb.AppendLine("Mische richtige und falsche Aussagen etwa zur Hälfte. Eine falsche Aussage ist am Text klar widerlegbar, nicht bloß nicht erwähnt.");
                break;
            case TelcPart.Lesen3:
                sb.AppendLine("Ein bis zwei Situationen haben keine passende Anzeige (Lösung -1). Die Anzeigen stehen in gemischter Reihenfolge.");
                break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// The format rules, plus: the draft must not reuse the text of any task of its part - the example it was shown
    /// or one already in the bank. A near-copy trains nothing new, and the seed is only ever an example of format.
    /// </summary>
    private List<string> FormatProblems(TelcContent single, TelcPart part)
    {
        var problems = telc.Validate(single).ToList();
        var text = TelcTaskText.WithKey(single, part, single.TaskIds(part)[0]);
        var bank = telc.Content;
        var (overlap, closest) = bank.TaskIds(part).Select(id => (ShingleOverlap(text, TelcTaskText.WithKey(bank, part, id)), id)).MaxBy(x => x.Item1);
        if (overlap > MaxShingleOverlap) problems.Add($"Zu nah am Beispiel: {overlap:P0} der Sechs-Wort-Folgen stehen schon in „{bank.TaskTitle(part, closest)}“.");
        return problems;
    }

    /// <summary>Share of <paramref name="text"/>'s six-word sequences that also occur in <paramref name="other"/>.</summary>
    public static double ShingleOverlap(string text, string other)
    {
        static HashSet<string> Shingles(string s)
        {
            var w = s.ToLowerInvariant().Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
            return Enumerable.Range(0, Math.Max(0, w.Length - 5)).Select(i => string.Join(' ', w, i, 6)).ToHashSet();
        }
        var a = Shingles(text);
        if (a.Count == 0) return 0;
        var b = Shingles(other);
        return a.Count(b.Contains) / (double)a.Count;
    }

    private static async Task<string> LeastUsedTopicAsync(LotseDbContext db, CancellationToken ct)
    {
        var used = await db.TelcDrafts.AsNoTracking().Where(d => d.Topic != null).GroupBy(d => d.Topic!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var counts = used.ToDictionary(u => u.Key, u => u.Count);
        return Topics.OrderBy(t => counts.GetValueOrDefault(t)).ThenBy(_ => Random.Shared.Next()).First();
    }

    public async Task<IReadOnlyList<TelcDraftView>> DraftsAsync(TelcDraftStatus? status = null, int take = 50, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.TelcDrafts.AsNoTracking();
        if (status is { } s) q = q.Where(d => d.Status == s);
        var rows = await q.OrderByDescending(d => d.CreatedUtc).Take(take).ToListAsync(ct);
        return rows.Select(View).ToList();
    }

    public async Task ReviewAsync(string id, bool approve, string? reason = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var draft = await db.TelcDrafts.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new KeyNotFoundException(id);
        if (draft.Status != TelcDraftStatus.Pending) throw new InvalidOperationException("Dieser Entwurf wurde schon entschieden.");
        draft.Status = approve ? TelcDraftStatus.Approved : TelcDraftStatus.Rejected;
        draft.ReviewedUtc = clock.GetUtcNow().UtcDateTime;
        draft.RejectReason = approve ? null : string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await db.SaveChangesAsync(ct);
        if (approve) await telc.RefreshAsync(ct);
    }

    public async Task<TelcPipelineStats> StatsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.TelcDrafts.AsNoTracking().Select(d => new { d.Status, d.CriticOk, d.LatencyMs }).ToListAsync(ct);
        int Count(TelcDraftStatus s) => rows.Count(r => r.Status == s);
        var decided = rows.Where(r => r.Status is TelcDraftStatus.Approved or TelcDraftStatus.Rejected).ToList();
        var finished = decided.Count + Count(TelcDraftStatus.Invalid);
        var latencies = rows.Select(r => r.LatencyMs).Order().ToList();
        return new TelcPipelineStats(
            Count(TelcDraftStatus.Invalid), Count(TelcDraftStatus.Pending), Count(TelcDraftStatus.Approved), Count(TelcDraftStatus.Rejected),
            finished == 0 ? null : Count(TelcDraftStatus.Approved) / (double)finished,
            decided.Count == 0 ? null : decided.Count(r => r.CriticOk == (r.Status == TelcDraftStatus.Approved)) / (double)decided.Count,
            latencies.Count == 0 ? null : latencies[latencies.Count / 2]);
    }

    private TelcDraftView View(TelcDraftEntity d)
    {
        var single = d.Status == TelcDraftStatus.Invalid ? null : TelcDraftCodec.TryRead(d.Part, d.Json, d.Id);
        var title = single?.TaskTitle(d.Part, d.Id) ?? $"{TelcBlueprint.Info(d.Part).Title} (unlesbar)";
        var text = single is null ? d.Json : TelcTaskText.WithKey(single, d.Part, d.Id);
        var problems = d.Problems?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return new TelcDraftView(d.Id, d.Part, d.Status, title, text, problems, d.CriticOk, d.Topic, d.Model, d.LatencyMs, d.CreatedUtc, d.RejectReason);
    }
}
