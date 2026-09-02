using Lotse.Core.Engine;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Ai;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Lotse.Web.Components;
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

// ---- Tutor (optional) ------------------------------------------------------------------------------------------------
var tutorOptions = builder.Configuration.GetSection(TutorOptions.Section).Get<TutorOptions>() ?? new TutorOptions();
builder.Services.AddSingleton(tutorOptions);
builder.Services.AddSingleton<ITutor>(sp => tutorOptions.ResolvedApiKey is { Length: > 0 }
    ? new ClaudeTutor(tutorOptions, sp.GetRequiredService<ILogger<ClaudeTutor>>())
    : new NullTutor());

// ---- Engine + application service ------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionPlanner>();
builder.Services.AddScoped<LearningService>();
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
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
