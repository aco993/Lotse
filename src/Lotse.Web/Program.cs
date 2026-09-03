using Lotse.Core.Engine;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Ai;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Lotse.Web.Components;
using Microsoft.AspNetCore.DataProtection;
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

// ---- Tutor: configurable at runtime from the settings page; API key encrypted at rest --------------------------------
builder.Services.AddDataProtection()
    .SetApplicationName("Lotse")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
var tutorDefaults = builder.Configuration.GetSection("Lotse:Tutor").Get<TutorDefaultsConfig>() ?? new TutorDefaultsConfig();
builder.Services.AddSingleton(sp => new TutorRegistry(
    sp.GetRequiredService<IDbContextFactory<LotseDbContext>>(),
    sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
    sp.GetRequiredService<ILoggerFactory>(),
    tutorDefaults.ToSettings()));
builder.Services.AddSingleton<ITutor>(sp => sp.GetRequiredService<TutorRegistry>());

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
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LotseDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<ContentCatalogProvider>().RefreshAsync();
    await scope.ServiceProvider.GetRequiredService<TutorRegistry>().InitializeAsync();
    app.Logger.LogInformation("Lotse bereit. Datenbank: {Db}. Tutor: {Tutor}", dbPath, scope.ServiceProvider.GetRequiredService<ITutor>().Description);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

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
