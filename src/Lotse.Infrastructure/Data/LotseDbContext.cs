using Lotse.Core.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lotse.Infrastructure.Data;

/// <summary>
/// The signed-in learner. Identity's default shape (username/email/password hash, no roles) is all this app needs;
/// a display name and every learner-specific field lives on <see cref="LearnerProfile"/> instead, one row per user.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
}

public enum SessionKind
{
    Daily,
    Placement,
    Exam,
    Free,
    Lesson,
}

/// <summary>Progress through the course: one row per lesson the learner has started.</summary>
public sealed class LessonProgressEntity
{
    public required string UserId { get; set; }
    public required string LessonId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    /// <summary>Share of steps solved (0..1) at completion.</summary>
    public double? Score { get; set; }
    public int TimesCompleted { get; set; }
    public Guid? LastSessionId { get; set; }
}

/// <summary>One learner's profile — exactly one row per <see cref="ApplicationUser"/>.</summary>
public sealed class LearnerProfile
{
    public required string UserId { get; set; }
    /// <summary>Substituted into the content's {Vorname}/{Name} tokens; empty falls back to the author's own name.</summary>
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string NativeLanguage { get; set; } = "Serbisch";
    /// <summary>Free text, purely context for the tutor prompt ("… arbeitet als Softwareentwickler").</summary>
    public string? JobTitle { get; set; } = "Softwareentwickler";
    /// <summary>Structured field; steers which drills the planner prefers. See <see cref="Core.Model.Occupation"/>.</summary>
    public Occupation Occupation { get; set; } = Occupation.Unspecified;
    public int DailyMinutes { get; set; } = 10;
    public DateOnly? TargetExamDate { get; set; }
    /// <summary>B2 by default; C1 lifts the planner's focus cap. See <see cref="Core.Model.TargetLevel"/>.</summary>
    public TargetLevel TargetLevel { get; set; } = TargetLevel.B2;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PlacementCompletedUtc { get; set; }
}

public sealed class AttemptEntity
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public Guid? SessionId { get; set; }
    public required string ExerciseId { get; set; }
    public required string NodeId { get; set; }
    public Outcome Outcome { get; set; }
    public double Score { get; set; }
    public int DurationMs { get; set; }
    public bool HintUsed { get; set; }
    public DateTime Utc { get; set; }
    public string? AnswerText { get; set; }
}

public sealed class ErrorEventEntity
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public long? AttemptId { get; set; }
    public required string Code { get; set; }
    public required string NodeId { get; set; }
    public DateTime Utc { get; set; }
    public string? Snippet { get; set; }
    public string? Correction { get; set; }
    public bool FromAi { get; set; }

    public ErrorEvent ToModel() => new(Code, NodeId, Utc, Snippet, Correction, FromAi);
}

public sealed class SessionEntity
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public SessionKind Kind { get; set; }
    public int PlannedMinutes { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>The plan as JSON (step kinds, exercise ids, reasons) so a session can be resumed after a reload.</summary>
    public string PlanJson { get; set; } = "[]";
    public int StepsTotal { get; set; }
    public int StepsDone { get; set; }
    public int CorrectCount { get; set; }
    public string Summary { get; set; } = "";
    /// <summary>Set for <see cref="SessionKind.Lesson"/> sessions.</summary>
    public string? LessonId { get; set; }
}

/// <summary>A piece of free writing or a speaking transcript with its evaluation.</summary>
public sealed class ProductionEntity
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public Guid? SessionId { get; set; }
    public required string ExerciseId { get; set; }
    public required string NodeId { get; set; }
    public bool IsSpeaking { get; set; }
    public required string Text { get; set; }
    public int WordCount { get; set; }
    public DateTime Utc { get; set; }
    public double? Score { get; set; }
    public string? EstimatedLevel { get; set; }
    /// <summary>Full <c>ProductionEvaluation</c> as JSON when the AI tutor evaluated; self-check results otherwise.</summary>
    public string? EvaluationJson { get; set; }
    public bool EvaluatedByAi { get; set; }
}

/// <summary>
/// Exercises produced by the AI tutor; they join the catalog on startup. Deliberately kept global/shared across
/// every account rather than per-user: <see cref="Content.ContentCatalogProvider"/> is a process-wide singleton
/// that merges every generated exercise into the one shared <c>ContentCatalog</c> served to all circuits. Scoping
/// this per user would mean the catalog itself becoming per-request — a much larger change than "add accounts";
/// see docs/ARCHITEKTUR.md.
/// </summary>
public sealed class GeneratedExerciseEntity
{
    public required string Id { get; set; }
    public required string NodeId { get; set; }
    public required string Json { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>One named setting for one learner — today only <c>"tutor.settings"</c> (the AI tutor configuration,
/// including the API key encrypted at rest). Scoped per user: one learner's key must never be readable, let alone
/// usable, from another account.</summary>
public sealed class SettingEntity
{
    public required string UserId { get; set; }
    public required string Key { get; set; }
    public string Value { get; set; } = "";
}

public sealed class LotseDbContext(DbContextOptions<LotseDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    /// <summary>Name of the EF Core shadow property that scopes <see cref="SkillState"/> and <see cref="ReviewState"/>
    /// (pure Core types, no UserId member of their own) to an account. Every <c>EF.Property&lt;string&gt;</c> /
    /// <c>Entry(x).Property(...)</c> access goes through this constant - the string exists exactly once.</summary>
    public const string UserIdShadow = "UserId";

    /// <summary>Identity store schema this context is built for: v3 adds the passkey (WebAuthn) table. Program.cs
    /// feeds the same value into <c>IdentityOptions.Stores.SchemaVersion</c>.</summary>
    public static readonly Version IdentitySchemaVersion = IdentitySchemaVersions.Version3;

    private static readonly IServiceProvider IdentityStoreDefaults = new ServiceCollection()
        .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersion)
        .BuildServiceProvider();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // IdentityDbContext decides whether the passkey table exists by reading IdentityOptions.Stores.SchemaVersion
        // from the APPLICATION service provider behind the options. A context built outside the app (tests, the EF
        // tools, a script) has none, would silently fall back to schema v1 and disagree with the migrations - so it
        // gets a minimal provider carrying exactly that one setting. The schema is a property of this context, not
        // of whoever happened to build the options.
        if (optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider is null)
            optionsBuilder.UseApplicationServiceProvider(IdentityStoreDefaults);
    }

    public DbSet<LearnerProfile> Profiles => Set<LearnerProfile>();
    public DbSet<SkillState> SkillStates => Set<SkillState>();
    public DbSet<ReviewState> ReviewStates => Set<ReviewState>();
    public DbSet<AttemptEntity> Attempts => Set<AttemptEntity>();
    public DbSet<ErrorEventEntity> ErrorEvents => Set<ErrorEventEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<ProductionEntity> Productions => Set<ProductionEntity>();
    public DbSet<GeneratedExerciseEntity> GeneratedExercises => Set<GeneratedExerciseEntity>();
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();
    public DbSet<LessonProgressEntity> LessonProgress => Set<LessonProgressEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // Identity's own tables: AspNetUsers, AspNetRoles, AspNetUserClaims, ...

        b.Entity<LessonProgressEntity>(e =>
        {
            e.HasKey(l => new { l.UserId, l.LessonId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LearnerProfile>(e =>
        {
            e.HasKey(p => p.UserId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // SkillState/ReviewState are pure Lotse.Core domain objects (used directly by the engine and its unit
        // tests, no DB involved there) reused here as EF entities. They carry no UserId property of their own —
        // the scoping lives purely in this mapping as an EF Core shadow property, so the learning engine never has
        // to know accounts exist. LearningService reads/writes it via EF.Property<string>(x, UserIdShadow).
        b.Entity<SkillState>(e =>
        {
            e.Property<string>(UserIdShadow).IsRequired();
            e.HasKey(UserIdShadow, nameof(SkillState.NodeId));
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(UserIdShadow).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(s => s.Mastery).Ignore(s => s.Confidence).Ignore(s => s.IsWeak).Ignore(s => s.IsStrong);
        });

        b.Entity<ReviewState>(e =>
        {
            e.Property<string>(UserIdShadow).IsRequired();
            e.HasKey(UserIdShadow, nameof(ReviewState.ExerciseId));
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(UserIdShadow).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(UserIdShadow, nameof(ReviewState.DueUtc));
            e.Ignore(r => r.IsNew);
        });

        b.Entity<AttemptEntity>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.Utc);
            e.HasIndex(a => a.ExerciseId);
            e.Property(a => a.Outcome).HasConversion<string>();
        });

        b.Entity<ErrorEventEntity>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Utc);
            e.HasIndex(x => x.NodeId);
        });

        b.Entity<SessionEntity>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.UserId);
            e.HasIndex(s => s.StartedUtc);
            e.Property(s => s.Kind).HasConversion<string>();
        });

        b.Entity<ProductionEntity>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.UserId);
            e.HasIndex(p => p.Utc);
        });

        b.Entity<GeneratedExerciseEntity>().HasKey(g => g.Id);

        b.Entity<SettingEntity>(e =>
        {
            e.HasKey(s => new { s.UserId, s.Key });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
