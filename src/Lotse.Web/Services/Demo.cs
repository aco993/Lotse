using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lotse.Web.Services;

/// <summary>
/// How this instance is hosted (<c>Lotse:Hosting</c>). The defaults are the local, single-machine app; a public
/// deployment closes registration, trusts its reverse proxy and may offer a demo account.
/// </summary>
public sealed class HostingOptions
{
    /// <summary>False on a personal server once your own account exists: nobody else can register and spend its resources.</summary>
    public bool RegistrationOpen { get; set; } = true;
    /// <summary>Behind a TLS-terminating proxy (Caddy, nginx, a platform's ingress): honour X-Forwarded-For/-Proto so cookies, redirects and passkeys see https.</summary>
    public bool BehindProxy { get; set; }
    public DemoOptions Demo { get; set; } = new();
}

/// <summary>
/// The demo account for a public portfolio instance: one click on the login page, no password, two weeks of
/// history already in it, reset every <see cref="ResetHours"/> hours. Everyone who clicks shares it, so nothing
/// personal belongs in it - account settings and the tutor configuration are closed for it.
/// </summary>
public sealed class DemoOptions
{
    public bool Enabled { get; set; }
    public string Email { get; set; } = "demo@lotse.example";
    public int ResetHours { get; set; } = 24;
}

/// <summary>A clock the seeder moves through the past two weeks, so the history has real dates and due reviews.</summary>
internal sealed class SeedClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Writes the demo learner's history through the real services - the same rows a real learner produces, nothing hand-made.</summary>
public sealed class DemoSeeder(
    IServiceProvider services,
    IDbContextFactory<LotseDbContext> dbFactory,
    ContentCatalogProvider catalog,
    TelcContentProvider telc,
    SessionPlanner planner,
    HostingOptions hosting,
    TimeProvider clock,
    ILoggerFactory loggers)
{
    public const int Days = 14;

    public async Task<string> EnsureUserAsync(CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = hosting.Demo.Email;
        var user = await users.FindByEmailAsync(email);
        if (user is not null) return user.Id;
        user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await users.CreateAsync(user); // no password: the demo signs in by button only
        if (!created.Succeeded) throw new InvalidOperationException("Demo-Konto: " + string.Join(", ", created.Errors.Select(e => e.Description)));
        return user.Id;
    }

    /// <summary>Empties the demo account and fills it again: profile, two weeks of sessions, a few telc parts.</summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        var userId = await EnsureUserAsync(ct);
        var now = clock.GetUtcNow();
        var seedClock = new SeedClock(now.AddDays(-Days));
        var who = new FixedCurrentUserAccessor(userId);
        var learning = new LearningService(dbFactory, catalog, new NullTutor(), planner, seedClock, who, loggers.CreateLogger<LearningService>());
        await learning.ResetAllDataAsync(ct);

        var profile = await learning.GetProfileAsync(ct);
        profile.FirstName = "Mira";
        profile.LastName = "Demo";
        profile.JobTitle = "Pflegefachkraft";
        profile.Occupation = Occupation.Pflege;
        profile.Exam = ExamProvider.Telc;
        profile.TargetExamDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(56);
        profile.PlacementCompletedUtc = seedClock.Now.UtcDateTime;
        await learning.SaveProfileAsync(profile, ct);

        // Deterministic, so every reset tells the same story: unsure at first, steadily better.
        var rng = new Random(2026);
        for (var day = Days; day >= 1; day--)
        {
            if (day % 5 == 0) continue; // a skipped day now and then, like anyone's
            seedClock.Now = now.AddDays(-day).Date.AddHours(17.5);
            var rightShare = 0.5 + 0.3 * (Days - day) / Days;
            await SessionAsync(learning, seedClock, rng, rightShare, ct);
        }

        var telcService = new TelcService(dbFactory, catalog, telc, new NullTutor(), learning, seedClock, who);
        await TelcAsync(telcService, seedClock, now, rng, ct);
        loggers.CreateLogger<DemoSeeder>().LogInformation("Demo-Konto {Email} neu befüllt ({Days} Tage).", hosting.Demo.Email, Days);
    }

    private static async Task SessionAsync(LearningService learning, SeedClock seedClock, Random rng, double rightShare, CancellationToken ct)
    {
        var session = await learning.StartSessionAsync(10, ct: ct);
        var view = await learning.GetSessionAsync(session.Id, ct) ?? throw new InvalidOperationException("Demo-Session fehlt.");
        for (var i = 0; i < view.Exercises.Count; i++)
        {
            var e = view.Exercises[i];
            seedClock.Now = seedClock.Now.AddSeconds(e.EstimatedSeconds);
            var right = rng.NextDouble() < rightShare;
            string? answer = e.Type switch
            {
                ExerciseType.MultipleChoice or ExerciseType.SpotError when e.CorrectIndex is { } c => (right ? c : (c + 1) % Math.Max(2, e.Options.Count)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ExerciseType.Cloze or ExerciseType.Transform or ExerciseType.Translate or ExerciseType.Vocab or ExerciseType.Dictation or ExerciseType.WordOrder when e.Answers.Count > 0 => right ? e.Answers[0] : "",
                _ => null,
            };
            if (answer is null) await learning.SkipStepAsync(session.Id, i, ct);
            else await learning.SubmitAnswerAsync(session.Id, i, e.Id, answer, (int)(e.EstimatedSeconds * 900), hintUsed: !right && rng.NextDouble() < 0.3, ct);
        }
        await learning.FinishSessionAsync(session.Id, ct);
    }

    private async Task TelcAsync(TelcService service, SeedClock seedClock, DateTimeOffset now, Random rng, CancellationToken ct)
    {
        var bank = telc.Seed;
        (TelcPart Part, int DaysAgo, double Right)[] plan =
        [
            (TelcPart.Sprachbausteine1, 10, 0.6), (TelcPart.Lesen2, 9, 0.6), (TelcPart.Hoeren1, 7, 0.8),
            (TelcPart.Sprachbausteine2, 5, 0.7), (TelcPart.Lesen1, 3, 0.8), (TelcPart.Sprachbausteine1, 2, 0.8),
        ];
        foreach (var (part, daysAgo, right) in plan)
        {
            seedClock.Now = now.AddDays(-daysAgo).Date.AddHours(19);
            var id = bank.TaskIds(part)[daysAgo % bank.TaskIds(part).Count];
            var key = bank.Key(part, id)!;
            var answers = key.Select(k => (int?)(rng.NextDouble() < right ? k : k == 0 ? 1 : 0)).ToList();
            await service.SubmitObjectiveAsync(part, id, answers, ct: ct);
        }
        seedClock.Now = now.AddDays(-4).Date.AddHours(20);
        var writing = bank.Schreiben[0].A;
        if (writing.ModelAnswer is { } text)
            await service.SubmitProductionAsync(TelcPart.Schreiben, writing.Id, await service.PersonalizeAsync(text, ct), [TelcBand.B, TelcBand.B, TelcBand.C], ct: ct);
    }
}

/// <summary>Fills the demo account at startup and again every <see cref="DemoOptions.ResetHours"/> hours.</summary>
public sealed class DemoResetService(DemoSeeder seeder, HostingOptions hosting, ILogger<DemoResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, hosting.Demo.ResetHours)));
        do
        {
            try
            {
                await seeder.ResetAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Demo-Konto konnte nicht befüllt werden.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class DemoEndpoints
{
    /// <summary>True for the shared demo learner - pages use it to close what a shared account must not change.</summary>
    public static bool IsDemo(this System.Security.Claims.ClaimsPrincipal user, HostingOptions hosting)
        => hosting.Demo.Enabled && string.Equals(user.Identity?.Name, hosting.Demo.Email, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Keeps the shared demo account shared: no password, e-mail or passkey changes, no deleting it. Everything a
    /// learner does - sessions, telc, the tutor-free features - stays open; the next reset puts it back anyway.
    /// </summary>
    public static IApplicationBuilder UseDemoGuard(this IApplicationBuilder app, DemoOptions demo) => app.Use(async (context, next) =>
    {
        var hosting = context.RequestServices.GetRequiredService<HostingOptions>();
        var path = context.Request.Path;
        if (context.User.IsDemo(hosting)
            && (path.StartsWithSegments("/Account/Manage", StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments("/Account/PasskeyCreationOptions", StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect("/");
            return;
        }
        await next();
    });

    /// <summary>The login page's "Demo ansehen" button: signs in as the demo learner, no password involved.</summary>
    public static IEndpointRouteBuilder MapDemoSignIn(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/Account/Demo", async (SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, HostingOptions hosting, DemoSeeder seeder, [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
        {
            if (!hosting.Demo.Enabled) return Results.NotFound();
            var user = await users.FindByIdAsync(await seeder.EnsureUserAsync());
            if (user is null) return Results.NotFound();
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.LocalRedirect("~/" + (returnUrl ?? "").TrimStart('/'));
        }).AllowAnonymous();
        return endpoints;
    }
}
