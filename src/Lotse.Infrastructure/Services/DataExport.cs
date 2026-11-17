using System.Data.Common;
using System.Text.Json;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lotse.Infrastructure.Services;

/// <summary>
/// One learner's data as a single JSON file: every row of every per-account table, read from EF's model rather than
/// from a hand-written list. A list is what let a table slip through the reset once (see
/// <c>MultiUserIsolationTests</c>); a table added later is exported without anyone remembering to add it here.
/// </summary>
/// <remarks>
/// What is left out, and why:
/// <list type="bullet">
/// <item>The account itself (<c>AspNet*</c>): password hash, security stamp and passkey material are credentials,
/// not learning data.</item>
/// <item>The tutor setup (<see cref="TutorSettingsKey"/>): it holds the API key, and the encryption around it is tied
/// to this machine, so the copy would be useless elsewhere and a liability anywhere.</item>
/// <item>Tables without an owner column (telc drafts, for one): they belong to every account on the machine.</item>
/// </list>
/// </remarks>
public sealed class DataExport(IDbContextFactory<LotseDbContext> dbFactory, TimeProvider clock)
{
    public const string Format = "lotse-export";
    public const int Version = 1;
    public const string TutorSettingsKey = "tutor.settings";

    /// <summary>Every table with a per-account column, as (table, user-id column), in a stable order.</summary>
    public static IReadOnlyList<(string Table, string Column)> PerUserTables(LotseDbContext db) =>
        [.. db.Model.GetEntityTypes()
            .Select(t => (Table: t.GetTableName(), Column: (t.FindProperty("UserId") ?? t.FindProperty(LotseDbContext.UserIdShadow))?.GetColumnName()))
            .Where(t => t.Table is not null && t.Column is not null && !t.Table.StartsWith("AspNet", StringComparison.Ordinal))
            .Select(t => (t.Table!, t.Column!))
            .Distinct()
            .OrderBy(t => t.Item1, StringComparer.Ordinal)];

    /// <summary>Suggested file name, e.g. <c>lotse-2026-11-17.json</c>.</summary>
    public string FileName() => $"lotse-{clock.GetUtcNow():yyyy-MM-dd}.json";

    public async Task WriteAsync(string userId, string? email, Stream output, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });

        json.WriteStartObject();
        json.WriteString("format", Format);
        json.WriteNumber("version", Version);
        json.WriteString("exportedUtc", clock.GetUtcNow());
        if (email is not null) json.WriteString("account", email);
        json.WriteString("note", "Dein Lernstand in Lotse. Nicht enthalten: Passwort, Passkeys und der Tutor-Schlüssel.");
        json.WriteStartObject("tables");

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var (table, column) in PerUserTables(db))
            {
                await using var cmd = connection.CreateCommand();
                // Table and column names come from EF's own model, not from input; the user id is a real parameter.
                cmd.CommandText = $"SELECT * FROM \"{table}\" WHERE \"{column}\" = $user";
                var p = cmd.CreateParameter();
                p.ParameterName = "$user";
                p.Value = userId;
                cmd.Parameters.Add(p);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                json.WriteStartArray(table);
                while (await reader.ReadAsync(ct))
                {
                    if (table == "Settings" && IsTutorSettings(reader)) continue;
                    WriteRow(json, reader, column);
                }
                json.WriteEndArray();
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        json.WriteEndObject();
        json.WriteEndObject();
        await json.FlushAsync(ct);
    }

    private static bool IsTutorSettings(DbDataReader reader)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (reader.GetName(i) == "Key" && !reader.IsDBNull(i) && reader.GetString(i) == TutorSettingsKey) return true;
        return false;
    }

    // The user id is the same on every row and already identifies the file's owner, so it is left out of each row.
    private static void WriteRow(Utf8JsonWriter json, DbDataReader reader, string userColumn)
    {
        json.WriteStartObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (name == userColumn) continue;
            if (reader.IsDBNull(i)) { json.WriteNull(name); continue; }
            switch (reader.GetValue(i))
            {
                case long l: json.WriteNumber(name, l); break;
                case int n: json.WriteNumber(name, n); break;
                case double d: json.WriteNumber(name, d); break;
                case bool b: json.WriteBoolean(name, b); break;
                case byte[] bytes: json.WriteBase64String(name, bytes); break;
                case var other: json.WriteString(name, Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture)); break;
            }
        }
        json.WriteEndObject();
    }
}
