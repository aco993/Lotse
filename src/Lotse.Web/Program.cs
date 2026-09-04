using Lotse.Core.Engine;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Ai;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Lotse.Web.Components;
using Lotse.Web.Components.Account.Shared;
using Lotse.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Persistence: one local SQLite file next to the app (or wherever Lotse:DataDirectory points) --------------------
var dataDir = builder.Configuration["Lotse:DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "lotse.db");
builder.Services.AddDbContextFactory<LotseDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

// ---- Content ---------------------------------------------------------------------------------------------------------
var contentDir = ContentLoader.ResolveContentDirectory(builder.Configuration["Lotse:ContentDirectory"]);
builder.Services.AddSingleton(sp => new ContentCatalogProvider(contentDir, sp.GetRequiredService<IDbContextFactory<LotseDbContext>>(), sp.GetRequiredService<ILogger<ContentCatalogProvider>>()));

// ---- Accounts: ASP.NET Core Identity, cookie auth -----------------------------------------------------------------
// Identity Core/Cookies/Authorization ship in the ASP.NET Core shared framework (Microsoft.NET.Sdk.Web already
// references it) - only the EF Core store is a separate package. Registration follows the shape of the official
// `dotnet new blazor -au Individual` template, trimmed to what Lotse actually uses (no external logins, no 2FA).
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<ICurrentUserAccessor, WebCurrentUserAccessor>();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
}).AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // No email sender is configured on this machine (see IdentityNoOpEmailSender) - requiring confirmation would
    // lock every new registration out immediately, so registration signs the learner in right away instead.
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = true;
    // Identity schema v3 = passkeys (WebAuthn) table. Declared on the DbContext so that tests and the EF tools,
    // which build the context without this service provider, still see the same schema.
    options.Stores.SchemaVersion = LotseDbContext.IdentitySchemaVersion;
    // Length over composition rules (NIST SP 800-63B): 8+ characters, no forced digit/symbol/case mix and no
    // unique-character minimum either. The registration form promises exactly "Mindestens 8 Zeichen", so the
    // two must stay in step - any rule added here needs a matching hint in Register/ResetPassword/ChangePassword.
    options.Password.RequiredLength = PasswordRules.MinLength;
    options.Password.RequiredUniqueChars = 1;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
})
    .AddEntityFrameworkStores<LotseDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<GermanIdentityErrorDescriber>();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Secure by default: every page requires a signed-in learner unless explicitly marked [AllowAnonymous]
// (the /Account/* pages, /Error, /not-found).
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---- Tutor: configurable at runtime from the settings page; API key encrypted at rest, one row per learner -----------
builder.Services.AddDataProtection()
    .SetApplicationName("Lotse")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
var tutorDefaults = builder.Configuration.GetSection("Lotse:Tutor").Get<TutorDefaultsConfig>() ?? new TutorDefaultsConfig();
// Scoped, not Singleton: one instance per circuit/learner, otherwise one account's saved settings (incl. API key)
// would overwrite the in-memory tutor for every other signed-in learner. See TutorRegistry's own doc comment.
builder.Services.AddScoped<TutorRegistry>(sp => new TutorRegistry(
    sp.GetRequiredService<IDbContextFactory<LotseDbContext>>(),
    sp.GetRequiredService<IDataProtectionProvider>(),
    sp.GetRequiredService<ILoggerFactory>(),
    sp.GetRequiredService<ICurrentUserAccessor>(),
    tutorDefaults.ToSettings()));
builder.Services.AddScoped<ITutor>(sp => sp.GetRequiredService<TutorRegistry>());

// ---- Engine + application service ------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionPlanner>();
builder.Services.AddScoped<ILearningService, LearningService>();
builder.Services.AddScoped<HealthService>();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<Lotse.Web.Components.Shared.SpeechService>();

// ---- UI --------------------------------------------------------------------------------------------------------------
builder.Services.AddMudServices(c =>
{
    c.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter;
    c.SnackbarConfiguration.VisibleStateDuration = 3500;
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// ---- Schema + generated content on startup -----------------------------------------------------------------------------
// No learner is signed in yet at boot, so nothing tutor-specific is initialised here anymore (that now happens
// per circuit, once per signed-in learner - see MainLayout.razor's OnAfterRenderAsync).
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LotseDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    foreach (var line in await DatabaseInitializer.InitializeAsync(db, app.Logger)) app.Logger.LogInformation("{Line}", line);
    await scope.ServiceProvider.GetRequiredService<ContentCatalogProvider>().RefreshAsync();
    app.Logger.LogInformation("Lotse bereit. Datenbank: {Db}. Konten: ASP.NET Core Identity (Registrierung offen).", dbPath);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
// Static assets (app.css, MudBlazor css/js, favicon, blazor.web.js, ...) are endpoints too, so the fallback
// "must be signed in" policy above would apply to them as well - a signed-out visitor would get every stylesheet
// and script answered with a redirect to the login page (HTML instead of CSS/JS), i.e. a completely unstyled login
// screen. Public by design; there is nothing account-specific in wwwroot.
app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous(); // infra probe, not a learner-facing page
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();

app.Run();

/// <summary>Makes the top-level-statements host visible to <c>WebApplicationFactory&lt;Program&gt;</c> in Lotse.Web.Tests.</summary>
public partial class Program { }

/// <summary>Defaults from appsettings / environment / command line; the settings page can override them at runtime.</summary>
internal sealed class TutorDefaultsConfig
{
    public string? Preset { get; set; }
    public string? Provider { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? Effort { get; set; }
    public int? TimeoutSeconds { get; set; }

    public TutorSettings ToSettings()
    {
        // Backwards compatible with the older Provider/BaseUrl form: pick the preset that matches, else "custom".
        var presetId = Preset ?? (Provider?.Equals("Anthropic", StringComparison.OrdinalIgnoreCase) == true ? "anthropic"
            : BaseUrl is { Length: > 0 } ? TutorPresets.All.FirstOrDefault(p => p.BaseUrl is not null && BaseUrl.StartsWith(p.BaseUrl, StringComparison.OrdinalIgnoreCase))?.Id ?? "custom"
            : "groq");
        var preset = TutorPresets.Get(presetId);
        return new TutorSettings(preset.Id, BaseUrl, string.IsNullOrWhiteSpace(Model) ? preset.DefaultModel : Model, ApiKey, Effort ?? "medium", TimeoutSeconds ?? 120);
    }
}
