using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Core.Tests;

/// <summary>Every other test builds its database with <c>EnsureCreated</c>; production goes through
/// <see cref="DatabaseInitializer"/> and the migrations. This is the one test that runs the real path on a fresh
/// file and then asks EF Core whether the migrations still describe the model - the drift a stale migration (or a
/// migrations folder that was never committed, as happened before 0.7.0) would otherwise hide until first start.</summary>
public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task Fresh_file_is_created_by_migrations_and_the_model_has_no_pending_changes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lotse-init-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LotseDbContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            await using var db = new LotseDbContext(options);
            var log = await DatabaseInitializer.InitializeAsync(db, NullLogger.Instance);

            Assert.Contains("Datenbank neu angelegt.", log);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges(), "the model changed without a migration - run `dotnet ef migrations add`");

            // The scoped tables exist with their UserId column: a smoke query per shadow-keyed set must not throw.
            Assert.Empty(await db.SkillStates.Select(s => EF.Property<string>(s, LotseDbContext.UserIdShadow)).ToListAsync());
            Assert.Empty(await db.ReviewStates.Select(r => EF.Property<string>(r, LotseDbContext.UserIdShadow)).ToListAsync());

            // Second start on the same file: nothing to do, nothing logged.
            await using var again = new LotseDbContext(options);
            Assert.Empty(await DatabaseInitializer.InitializeAsync(again, NullLogger.Instance));
        }
        finally
        {
            await using (var db = new LotseDbContext(options)) await db.Database.EnsureDeletedAsync();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
