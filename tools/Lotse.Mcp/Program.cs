using Lotse.Core.Engine;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Lotse.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Lotse as an MCP server over stdio, for one learner of a local Lotse database. The client (Claude Desktop, Claude
// Code, any MCP host) starts this process; stdout carries the protocol, so every log line goes to stderr.
//
//   dotnet run --project tools/Lotse.Mcp -- --user you@example.com [--data path/to/data]
//
// See docs/MCP.md for the client configuration.

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddCommandLine(args, new Dictionary<string, string>
{
    ["--user"] = "Lotse:Mcp:User",
    ["--data"] = "Lotse:DataDirectory",
    ["--content"] = "Lotse:ContentDirectory",
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

var dataDir = builder.Configuration["Lotse:DataDirectory"] ?? McpStartup.DefaultDataDirectory();
var dbPath = Path.Combine(dataDir, "lotse.db");
if (!File.Exists(dbPath))
    return McpStartup.Fail($"Keine Lotse-Datenbank unter {dbPath}. Lotse einmal starten oder --data angeben.");

builder.Services.AddDbContextFactory<LotseDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
var contentDir = ContentLoader.ResolveContentDirectory(builder.Configuration["Lotse:ContentDirectory"] ?? McpStartup.DefaultContentDirectory());
builder.Services.AddSingleton(sp => new ContentCatalogProvider(contentDir, sp.GetRequiredService<IDbContextFactory<LotseDbContext>>(), sp.GetRequiredService<ILogger<ContentCatalogProvider>>()));
builder.Services.AddSingleton(sp => new TelcContentProvider(
    TelcContentLoader.Load(contentDir, sp.GetRequiredService<ContentCatalogProvider>().Catalog),
    sp.GetRequiredService<ContentCatalogProvider>(),
    sp.GetRequiredService<IDbContextFactory<LotseDbContext>>(),
    sp.GetRequiredService<ILogger<TelcContentProvider>>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionPlanner>();
// The chat client is the language model here. The app's own tutor stays out of it: no API key is read, and nothing
// this process does costs a token.
builder.Services.AddSingleton<ITutor>(new NullTutor());
builder.Services.AddSingleton<McpLearner>();
builder.Services.AddSingleton<ICurrentUserAccessor>(sp => sp.GetRequiredService<McpLearner>());
builder.Services.AddSingleton<ILearningService, LearningService>();
builder.Services.AddSingleton<ITelcService, TelcService>();
builder.Services.AddSingleton<ChatBridge>();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "lotse", Title = "Lotse – Deutsch B2", Version = typeof(McpStartup).Assembly.GetName().Version?.ToString() ?? "1.0" };
        o.ServerInstructions = LotsePrompts.Instructions;
    })
    .WithStdioServerTransport()
    .WithTools<LotseTools>()
    .WithPrompts<LotsePrompts>();

var host = builder.Build();
try
{
    await McpStartup.CheckDatabaseAsync(host.Services);
    await host.Services.GetRequiredService<McpLearner>().ResolveAsync(builder.Configuration["Lotse:Mcp:User"]);
    await host.Services.GetRequiredService<ContentCatalogProvider>().RefreshAsync();
    await host.Services.GetRequiredService<TelcContentProvider>().RefreshAsync();
}
catch (McpStartupException e)
{
    return McpStartup.Fail(e.Message);
}
await host.RunAsync();
return 0;
