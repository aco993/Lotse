using System.Diagnostics;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Services;

public enum HealthState
{
    Ok,
    Warning,
    Error,
}

public sealed record HealthItem(string Name, HealthState State, string Detail, string? Fix = null);

public sealed record HealthReport(IReadOnlyList<HealthItem> Items, DateTime CheckedUtc)
{
    public HealthState Overall => Items.Any(i => i.State == HealthState.Error) ? HealthState.Error : Items.Any(i => i.State == HealthState.Warning) ? HealthState.Warning : HealthState.Ok;
}

/// <summary>
/// Self-diagnosis and self-repair. The report tells the learner in plain words what works; <see cref="RepairAsync"/>
/// fixes what can be fixed automatically (schema, integrity, stale generated content) and says what it did.
/// </summary>
public sealed class HealthService(
    IDbContextFactory<LotseDbContext> dbFactory,
    ContentCatalogProvider catalog,
    ITutor tutor,
    ILogger<HealthService> logger)
{
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var items = new List<HealthItem>();

        // Content
        try
        {
            var problems = catalog.Catalog.Validate();
            items.Add(problems.Count == 0
                ? new HealthItem("Inhalte", HealthState.Ok, $"{catalog.Catalog.Nodes.Count} Themen, {catalog.SeedExerciseCount} Übungen, {catalog.GeneratedExerciseCount} generiert")
                : new HealthItem("Inhalte", HealthState.Warning, $"{problems.Count} Problem(e) in Übungsdaten", problems[0]));
        }
        catch (Exception e) { items.Add(new HealthItem("Inhalte", HealthState.Error, e.Message)); }

        // Database
        try
        {
            var sw = Stopwatch.StartNew();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var canConnect = await db.Database.CanConnectAsync(ct);
            if (!canConnect) items.Add(new HealthItem("Datenbank", HealthState.Error, "Keine Verbindung", "Reparieren legt die Datenbank neu an."));
            else
            {
                var integrity = await db.Database.SqlQueryRaw<string>("PRAGMA integrity_check").ToListAsync(ct);
                var ok = integrity.Count == 1 && integrity[0] == "ok";
                var attempts = await db.Attempts.CountAsync(ct);
                var sessions = await db.Sessions.CountAsync(ct);
                items.Add(ok
                    ? new HealthItem("Datenbank", HealthState.Ok, $"{sessions} Sessions, {attempts} Antworten, Prüfung {sw.ElapsedMilliseconds} ms")
                    : new HealthItem("Datenbank", HealthState.Error, "Integritätsprüfung fehlgeschlagen", "Reparieren versucht VACUUM; Backup der Datei empfohlen."));
            }
        }
        catch (Exception e) { items.Add(new HealthItem("Datenbank", HealthState.Error, e.Message, "Reparieren legt fehlende Tabellen an.")); }

        // Tutor
        items.Add(tutor.IsAvailable
            ? new HealthItem("KI-Tutor", HealthState.Ok, tutor.Description)
            : new HealthItem("KI-Tutor", HealthState.Warning, tutor.Description, "Unter „KI-Tutor“ einen Anbieter wählen – Groq ist kostenlos."));

        return new HealthReport(items, DateTime.UtcNow);
    }

    /// <summary>Applies every safe automatic fix and returns what was done.</summary>
    public async Task<IReadOnlyList<string>> RepairAsync(CancellationToken ct = default)
    {
        var done = new List<string>();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var created = await db.Database.EnsureCreatedAsync(ct);
            done.Add(created ? "Datenbank neu angelegt." : "Datenbankschema vorhanden.");
            var integrity = await db.Database.SqlQueryRaw<string>("PRAGMA integrity_check").ToListAsync(ct);
            if (integrity.Count != 1 || integrity[0] != "ok")
            {
                await db.Database.ExecuteSqlRawAsync("VACUUM", ct);
                done.Add("VACUUM ausgeführt.");
            }
            // Orphans: sessions that were never finished and are older than a day are closed so the dashboard stays clean.
            var stale = DateTime.UtcNow.AddDays(-1);
            var orphans = await db.Sessions.Where(s => s.EndedUtc == null && s.StartedUtc < stale).ToListAsync(ct);
            foreach (var s in orphans)
            {
                if (s.StepsDone == 0) db.Sessions.Remove(s); else s.EndedUtc = s.StartedUtc.AddMinutes(s.PlannedMinutes);
            }
            if (orphans.Count > 0) { await db.SaveChangesAsync(ct); done.Add($"{orphans.Count} verwaiste Session(s) aufgeräumt."); }

            // Generated exercises that no longer validate are removed (content contract may have tightened).
            var rows = await db.GeneratedExercises.ToListAsync(ct);
            var nodeIds = catalog.Catalog.Nodes.Select(n => n.Id).ToHashSet();
            var removed = 0;
            foreach (var row in rows)
            {
                var ex = SafeDeserialize(row.Json);
                if (ex is null || Core.Model.ExerciseValidator.Validate(ex, nodeIds).Count > 0) { db.GeneratedExercises.Remove(row); removed++; }
            }
            if (removed > 0) { await db.SaveChangesAsync(ct); done.Add($"{removed} ungültige generierte Übung(en) entfernt."); }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Reparatur fehlgeschlagen");
            done.Add("Reparatur fehlgeschlagen: " + e.Message);
        }

        await catalog.RefreshAsync(ct);
        done.Add("Inhalte neu geladen.");
        return done;
    }

    private static Core.Model.Exercise? SafeDeserialize(string json)
    {
        try { return ContentLoader.DeserializeExercise(json); }
        catch (Exception) { return null; }
    }
}
