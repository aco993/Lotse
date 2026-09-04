using Lotse.Infrastructure.Content;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lotse.E2E;

/// <summary>The real app on a real Kestrel port (WebApplicationFactory.UseKestrel, .NET 10) with its own temp SQLite
/// and Data Protection keys, plus one headless Chromium for the whole class. Pages are created per test with a
/// fresh browser context (own cookies), 1280×800, and a Playwright trace that is saved only if the test fails.</summary>
public sealed class LotseE2EFixture : IAsyncLifetime
{
    private sealed class KestrelHost : WebApplicationFactory<Program>
    {
        private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"lotse-e2e-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Lotse:DataDirectory", _dataDir);
            builder.UseSetting("Lotse:ContentDirectory", ContentLoader.ResolveContentDirectory());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_dataDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private readonly KestrelHost _host = new();
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>http://localhost:{port} - "localhost", not 127.0.0.1, because WebAuthn refuses an IP address as RP id.</summary>
    public string BaseUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        _host.UseKestrel(0);
        _host.StartServer();
        // (WebApplicationFactory.Server is TestServer-only; with Kestrel the address comes from the real IServer.)
        var address = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? _host.CreateClient().BaseAddress?.ToString()
            ?? throw new InvalidOperationException("Kestrel started without an address.");
        BaseUrl = $"http://localhost:{new Uri(address).Port}";
        _playwright = await Playwright.CreateAsync();
        // Headless is not silent: a dictation auto-plays on render and "Gespräch anhören" streams a WAV rendered
        // by Piper, so without --mute-audio a test run talks German through the machine's speakers.
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--mute-audio"] });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _host.Dispose();
    }

    /// <summary>Runs <paramref name="test"/> on a fresh page; on failure the Playwright trace (screenshots, DOM,
    /// network) lands in <c>playwright-traces/{name}.zip</c> next to the test binaries - open with `playwright show-trace`.</summary>
    public async Task RunAsync(string name, Func<IPage, Task> test)
    {
        var context = await _browser!.NewContextAsync(new() { BaseURL = BaseUrl, ViewportSize = new() { Width = 1280, Height = 800 }, Locale = "de-DE" });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = false });
        var page = await context.NewPageAsync();
        try
        {
            await test(page);
            await context.Tracing.StopAsync();
        }
        catch
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "playwright-traces");
            Directory.CreateDirectory(dir);
            await context.Tracing.StopAsync(new() { Path = Path.Combine(dir, $"{name}.zip") });
            throw;
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    /// A page in a context of the given size and colour scheme, for the screen shots that prove a UI change on both
    /// form factors and in both themes. The caller closes the context. No tracing here - these runs are expected to
    /// pass and their artefact is the PNG, not a trace.
    /// </summary>
    public async Task<(IBrowserContext Context, IPage Page)> NewPageAsync(int width, int height, ColorScheme scheme)
    {
        var context = await _browser!.NewContextAsync(new()
        {
            BaseURL = BaseUrl,
            ViewportSize = new() { Width = width, Height = height },
            Locale = "de-DE",
            ColorScheme = scheme,
        });
        return (context, await context.NewPageAsync());
    }

    /// <summary>Where the screen shots land: next to the test binaries, so a run never writes into the repo.</summary>
    public static string ScreenshotDirectory
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string UniqueEmail(string tag) => $"{tag}-{Guid.NewGuid():N}@lotse.test";
}
