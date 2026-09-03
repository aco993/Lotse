using System.Diagnostics;
using System.Text.Json;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.CurrentUser;
using Lotse.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Ai;

public sealed record TutorProbe(bool Ok, string Message, int LatencyMs, string? Sample = null);

/// <summary>
/// The one <see cref="ITutor"/> the app talks to. It delegates to whichever provider is currently configured and can be
/// reconfigured at runtime from the settings page (no restart). Settings live in the local database, one row per
/// learner (the API key is encrypted with ASP.NET Data Protection before it is stored) — one account's key must
/// never be readable, let alone usable, from another.
///
/// <b>Registered Scoped, not Singleton</b>: one instance per Blazor Server circuit, i.e. per signed-in learner.
/// A shared Singleton would mean whichever user last called <see cref="SaveAsync"/> or <see cref="InitializeAsync"/>
/// overwrites the one in-memory <see cref="Current"/> tutor for literally every other learner's circuit — the
/// in-memory cache would leak across accounts even with the DB row correctly scoped.
/// </summary>
public sealed class TutorRegistry : ITutor
{
    private const string SettingsKey = "tutor.settings";
    private const string ProtectorPurpose = "Lotse.Tutor.ApiKey.v1";

    private readonly IDbContextFactory<LotseDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TutorRegistry> _logger;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TutorSettings _defaults;
    private readonly Func<string, string?> _env;
    private volatile ITutor _current = new NullTutor();
    private string? _initializedForUserId;

    /// <param name="env">Environment lookup for key fallbacks; injectable so tests are independent of the machine.</param>
    public TutorRegistry(IDbContextFactory<LotseDbContext> dbFactory, IDataProtectionProvider protection, ILoggerFactory loggerFactory, ICurrentUserAccessor currentUser, TutorSettings? defaults = null, Func<string, string?>? env = null)
    {
        _dbFactory = dbFactory;
        _protector = protection.CreateProtector(ProtectorPurpose);
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TutorRegistry>();
        _currentUser = currentUser;
        _env = env ?? Environment.GetEnvironmentVariable;
        _defaults = defaults ?? TutorSettings.Default;
        Settings = _defaults;
        _current = Build(Settings);
    }

    public TutorSettings Settings { get; private set; }
    public ITutor Current => _current;

    public bool IsAvailable => _current.IsAvailable;
    public string Description => _current.Description;

    /// <summary>Loads this learner's stored settings (if any) and activates them. Called once per circuit
    /// (<c>MainLayout</c>, on first render); safe to call again. Also called automatically (see
    /// <see cref="EnsureCurrentAsync"/>) before every provider call, in case the signed-in learner changed on a
    /// circuit that <c>MainLayout</c> did not re-initialize (observed with Blazor's enhanced navigation reusing a
    /// circuit across a logout/login round-trip - the previous learner's cached <see cref="Settings"/>, API key
    /// included, must never be used for the next one).</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        string userId;
        try
        {
            userId = await _currentUser.GetUserIdAsync();
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId && s.Key == SettingsKey, ct);
            if (row is null)
            {
                Settings = _defaults;
                _current = Build(Settings);
                _initializedForUserId = userId;
                return;
            }
            var stored = JsonSerializer.Deserialize<StoredSettings>(row.Value);
            if (stored is null)
            {
                Settings = _defaults;
                _current = Build(Settings);
                _initializedForUserId = userId;
                return;
            }
            string? key = null;
            if (!string.IsNullOrEmpty(stored.ProtectedApiKey))
            {
                try { key = _protector.Unprotect(stored.ProtectedApiKey); }
                catch (Exception e) { _logger.LogWarning(e, "Gespeicherter API-Schlüssel konnte nicht entschlüsselt werden (Schlüsselring gewechselt?). Bitte neu eingeben."); }
            }
            Settings = new TutorSettings(stored.PresetId, stored.BaseUrl, stored.Model, key, stored.Effort ?? "medium", stored.TimeoutSeconds > 0 ? stored.TimeoutSeconds : 120);
            _current = Build(Settings);
            _initializedForUserId = userId;
            _logger.LogInformation("Tutor aktiv: {Description}", Description);
        }
        catch (Exception e)
        {
            // Never let a broken settings row take the whole app down – fall back to defaults.
            _logger.LogWarning(e, "Tutor-Einstellungen unlesbar, Standard wird verwendet.");
            Settings = _defaults;
            _current = Build(Settings);
            _initializedForUserId = null; // unknown - force a retry on the next call rather than pinning this failure.
        }
    }

    /// <summary>Re-runs <see cref="InitializeAsync"/> if the signed-in learner for this circuit is not the one
    /// <see cref="Settings"/> was last loaded for. Cheap when nothing changed (one claims lookup, no DB round trip).
    /// Public so a page that displays <see cref="IsAvailable"/>/<see cref="Description"/> (Einstellungen, Themen,
    /// Sprechen, ProductionExercise) can call this in its own <c>OnInitializedAsync</c> and be sure its first render
    /// already reflects the signed-in learner's settings - MainLayout also calls it, but that runs in
    /// <c>OnAfterRenderAsync</c> (after the page's own first render) and updating this Scoped instance's state there
    /// does not, on its own, make an already-rendered sibling page's markup re-evaluate.</summary>
    public async Task EnsureCurrentAsync(CancellationToken ct = default)
    {
        var userId = await _currentUser.GetUserIdAsync();
        if (userId != _initializedForUserId) await InitializeAsync(ct);
    }

    public async Task SaveAsync(TutorSettings settings, CancellationToken ct = default)
    {
        var userId = await _currentUser.GetUserIdAsync();
        var stored = new StoredSettings(settings.PresetId, settings.BaseUrl, settings.Model,
            string.IsNullOrWhiteSpace(settings.ApiKey) ? null : _protector.Protect(settings.ApiKey), settings.Effort, settings.TimeoutSeconds);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.UserId == userId && s.Key == SettingsKey, ct);
        if (row is null) db.Settings.Add(new SettingEntity { UserId = userId, Key = SettingsKey, Value = JsonSerializer.Serialize(stored) });
        else row.Value = JsonSerializer.Serialize(stored);
        await db.SaveChangesAsync(ct);
        Settings = settings;
        _current = Build(settings);
        _initializedForUserId = userId;
        _logger.LogInformation("Tutor umkonfiguriert: {Description}", Description);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var userId = await _currentUser.GetUserIdAsync();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.UserId == userId && s.Key == SettingsKey, ct);
        if (row is not null) { db.Settings.Remove(row); await db.SaveChangesAsync(ct); }
        Settings = _defaults;
        _current = Build(_defaults);
        _initializedForUserId = userId;
    }

    /// <summary>Sends one tiny request through a throw-away tutor built from <paramref name="settings"/> and reports what happened.</summary>
    public async Task<TutorProbe> ProbeAsync(TutorSettings settings, CancellationToken ct = default)
    {
        var tutor = Build(settings);
        if (!tutor.IsAvailable) return new TutorProbe(false, tutor.Description, 0);
        var sw = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(20, settings.TimeoutSeconds)));
            var reply = await tutor.DiscussAsync("Probeverbindung", [new ChatTurn(true, "Antworte nur mit dem Wort: bereit")], ProbeContext, timeout.Token);
            return new TutorProbe(true, $"Verbunden ({tutor.Description})", (int)sw.ElapsedMilliseconds, reply.Length > 120 ? reply[..120] + "…" : reply);
        }
        catch (OperationCanceledException)
        {
            return new TutorProbe(false, "Zeitüberschreitung – Server erreichbar? Modell geladen?", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            return new TutorProbe(false, Friendly(e), (int)sw.ElapsedMilliseconds);
        }
    }

    private static readonly LearnerContext ProbeContext = new("Serbisch", [], [], []);

    private ITutor Build(TutorSettings settings)
    {
        var options = settings.ToOptions(_env);
        var keyMissing = settings.Preset.NeedsKey && string.IsNullOrWhiteSpace(options.ApiKey);
        if (!options.IsConfigured || keyMissing) return new NullTutor(NotConfiguredMessage(settings, _env));
        return options.Provider switch
        {
            TutorProvider.Anthropic => new ClaudeTutor(options, _loggerFactory.CreateLogger<ClaudeTutor>()),
            _ => new OpenAiCompatibleTutor(options, _loggerFactory.CreateLogger<OpenAiCompatibleTutor>()),
        };
    }

    private static string NotConfiguredMessage(TutorSettings s, Func<string, string?> env)
    {
        var p = s.Preset;
        if (p.NeedsKey && string.IsNullOrWhiteSpace(s.ResolveApiKey(env)))
            return $"{p.Name}: API-Schlüssel fehlt{(p.SignupUrl is null ? "" : $" – kostenlos unter {p.SignupUrl}")}. Schreiben/Sprechen werden per Selbstcheck bewertet.";
        if (string.IsNullOrWhiteSpace(s.ResolvedBaseUrl)) return $"{p.Name}: Server-URL fehlt.";
        if (string.IsNullOrWhiteSpace(s.Model)) return $"{p.Name}: Modell fehlt.";
        return "Kein KI-Tutor konfiguriert.";
    }

    /// <summary>Turns provider exceptions into a sentence a learner can act on.</summary>
    public static string Friendly(Exception e)
    {
        var m = e.Message;
        if (e is HttpRequestException http)
        {
            return http.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "Schlüssel abgelehnt (401/403). Bitte prüfen, ob er vollständig kopiert wurde und zum gewählten Anbieter gehört.",
                System.Net.HttpStatusCode.NotFound => "Endpunkt oder Modell nicht gefunden (404). Modellname prüfen – bei Ollama zuerst „ollama pull <modell>“.",
                System.Net.HttpStatusCode.TooManyRequests => "Ratenlimit erreicht (429). Kurz warten oder ein anderes Modell wählen.",
                System.Net.HttpStatusCode.PaymentRequired => "Guthaben aufgebraucht (402).",
                null => "Server nicht erreichbar. Läuft er? Stimmt die URL (inkl. /v1)?",
                _ => m,
            };
        }
        return m;
    }

    // ---- ITutor delegation ------------------------------------------------------------------------
    // Each entry point re-checks the signed-in learner first (see EnsureCurrentAsync): the one place an actual
    // provider call - and the API key that pays for it - could otherwise run under a stale, previously-loaded
    // account's settings on a reused circuit.
    public async Task<ProductionEvaluation> EvaluateAsync(Exercise exercise, string learnerText, ProductionMode mode, LearnerContext context, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _current.EvaluateAsync(exercise, learnerText, mode, context, ct);
    }

    public async Task<IReadOnlyList<Exercise>> GenerateExercisesAsync(SkillNode node, CefrBand band, int count, ExerciseContext exerciseContext, LearnerContext context, IReadOnlyList<Exercise> examples, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _current.GenerateExercisesAsync(node, band, count, exerciseContext, context, examples, ct);
    }

    public async Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _current.DiscussAsync(thesis, history, context, ct);
    }

    public async Task<Exercise?> GenerateReadingAsync(SkillNode node, CefrBand band, bool audioOnly, LearnerContext context, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _current.GenerateReadingAsync(node, band, audioOnly, context, ct);
    }

    private sealed record StoredSettings(string PresetId, string? BaseUrl, string Model, string? ProtectedApiKey, string? Effort, int TimeoutSeconds);
}
