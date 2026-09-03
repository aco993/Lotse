using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Data;

/// <summary>
/// Brings the database to the current schema with EF Core migrations. Databases created by earlier versions with
/// <c>EnsureCreated</c> (no migration history) are baselined first: their tables match the InitialCreate migration,
/// so that migration is recorded as applied and only the newer ones run. No data is touched.
/// </summary>
public static class DatabaseInitializer
{
    public const string BaselineMigrationId = "20260903074802_InitialCreate";

    public static async Task<IReadOnlyList<string>> InitializeAsync(LotseDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var log = new List<string>();
        var historyExists = await TableExistsAsync(db, "__EFMigrationsHistory", ct);
        var legacySchema = !historyExists && await TableExistsAsync(db, "Profiles", ct);

        if (legacySchema)
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)", ct);
            await db.Database.ExecuteSqlRawAsync(
                $"INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('{BaselineMigrationId}', '10.0.11')", ct);
            log.Add("Bestehende Datenbank auf Migrationen umgestellt (Baseline).");
            logger.LogInformation("Datenbank ohne Migrationshistorie erkannt – Baseline {Migration} eingetragen.", BaselineMigrationId);
        }

        // A fresh file has every migration pending (creates the schema); a baselined or current file only the newer ones.
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            await db.Database.MigrateAsync(ct);
            log.Add(legacySchema || historyExists
                ? $"{pending.Count} Migration(en) angewendet: {string.Join(", ", pending.Select(Short))}."
                : "Datenbank neu angelegt.");
            logger.LogInformation("Migrationen angewendet: {Migrations}", string.Join(", ", pending));
        }
        return log;
    }

    private static async Task<bool> TableExistsAsync(LotseDbContext db, string table, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name = {0}", table).ToListAsync(ct);
        return rows.Count > 0;
    }

    private static string Short(string migrationId) => migrationId.Length > 15 && migrationId[14] == '_' ? migrationId[15..] : migrationId;
}
