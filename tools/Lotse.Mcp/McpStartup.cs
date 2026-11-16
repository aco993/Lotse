using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lotse.Mcp;

public sealed class McpStartupException(string message) : Exception(message);

internal static class McpStartup
{
    /// <summary>
    /// The checkout this binary was built from. An MCP client starts the server from a working directory of its own
    /// choosing (Claude Desktop: its install folder), so nothing here may depend on the current directory.
    /// </summary>
    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lotse.slnx"))) return dir.FullName;
        return null;
    }

    /// <summary>The web app's own data folder in this checkout, so a local setup needs no --data at all.</summary>
    public static string DefaultDataDirectory()
        => RepoRoot() is { } root ? Path.Combine(root, "src", "Lotse.Web", "data") : Path.Combine(Directory.GetCurrentDirectory(), "data");

    /// <summary>The checkout's content folder unless --content says otherwise.</summary>
    public static string? DefaultContentDirectory() => RepoRoot() is { } root ? Path.Combine(root, "content") : null;

    /// <summary>
    /// The web app owns the schema and migrates it on start. This process only reads and appends, so an older
    /// database is a reason to stop, not to migrate it from here behind the app's back.
    /// </summary>
    public static async Task CheckDatabaseAsync(IServiceProvider services)
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<LotseDbContext>>().CreateDbContextAsync();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
            throw new McpStartupException($"Die Datenbank ist älter als dieses Werkzeug ({pending.Count} Migrationen fehlen). Lotse einmal starten, dann neu verbinden.");
    }

    public static int Fail(string message)
    {
        Console.Error.WriteLine("lotse-mcp: " + message);
        return 1;
    }
}

/// <summary>
/// The one learner this process serves, fixed at start: from --user, or the only account when there is exactly one.
/// The MCP client is the learner's own assistant on the learner's own machine; there is no sign-in to ask.
/// </summary>
public sealed class McpLearner(IDbContextFactory<LotseDbContext> dbFactory, ILogger<McpLearner> logger) : ICurrentUserAccessor
{
    private string? _userId;

    public Task<string> GetUserIdAsync() => Task.FromResult(_userId ?? throw new InvalidOperationException("Lerner noch nicht bestimmt."));

    public async Task ResolveAsync(string? email)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalized = email.Trim().ToUpperInvariant();
            _userId = await db.Users.Where(u => u.NormalizedEmail == normalized).Select(u => u.Id).FirstOrDefaultAsync()
                ?? throw new McpStartupException($"Kein Konto mit der E-Mail {email}.");
        }
        else
        {
            var ids = await db.Users.Select(u => u.Id).Take(2).ToListAsync();
            _userId = ids.Count switch
            {
                1 => ids[0],
                0 => throw new McpStartupException("Die Datenbank hat noch kein Konto. In Lotse registrieren, dann neu verbinden."),
                _ => throw new McpStartupException("Mehrere Konten in der Datenbank - mit --user <E-Mail> angeben, welches."),
            };
        }
        logger.LogInformation("Lotse-MCP bereit für Konto {UserId}.", _userId);
    }
}
