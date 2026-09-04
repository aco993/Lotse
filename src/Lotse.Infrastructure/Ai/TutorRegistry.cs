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
/// overwrites the one in-memory tutor for literally every other learner's circuit — the in-memory cache would
/// leak across accounts even with the DB row correctly scoped. And because a circuit can outlive a logout/login
/// round-trip, every provider call and every cached read goes through <see cref="EnsureCurrentAsync"/> first
/// (see <see cref="ILearnerBound"/>) rather than trusting a single load at circuit start.
/// </summary>
public sealed class TutorRegistry : ITutor, ILearnerBound
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

    /// <summary>Everything that must change together when the learner (or their settings) changes, swapped as ONE
    /// reference — a reader can never see the previous learner's tutor next to the next one's settings.
    /// <c>UserId</c> null = not loaded for anyone yet (or the last load failed), so the next check reloads.</summary>
    private sealed record Active(string? UserId, TutorSettings Settings, ITutor Tutor);
    private volatile Active _active;

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
        _active = new Active(null, _defaults, Build(_defaults));
    }

    public TutorSettings Settings => _active.Settings;
    public ITutor Current => _active.Tutor;

    public bool IsAvailable => _active.Tutor.IsAvailable;
    public string Description => _active.Tutor.Description;

    /// <summary>Loads this learner's stored settings (if any) and activates them. Safe to call any time; normally
    /// reached through <see cref="EnsureCurrentAsync"/>.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Outside the try on purpose: no signed-in learner here is a defect (a page slipped past the login gate,
        // see ICurrentUserAccessor) and must surface as such, not be logged away as "Einstellungen unlesbar".
        var userId = await _currentUser.GetUserIdAsync();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId && s.Key == SettingsKey, ct);
            var stored = row is null ? null : JsonSerializer.Deserialize<StoredSettings>(row.Value);
            if (stored is null)
            {
                _active = new Active(userId, _defaults, Build(_defaults));
                return;
            }
            string? key = null;
            if (!string.IsNullOrEmpty(stored.ProtectedApiKey))
            {
                try { key = _protector.Unprotect(stored.ProtectedApiKey); }
                catch (Exception e) { _logger.LogWarning(e, "Gespeicherter API-Schlüssel konnte nicht entschlüsselt werden (Schlüsselring gewechselt?). Bitte neu eingeben."); }
            }
            var settings = new TutorSettings(stored.PresetId, stored.BaseUrl, stored.Model, key, stored.Effort ?? "medium", stored.TimeoutSeconds > 0 ? stored.TimeoutSeconds : 120);
            _active = new Active(userId, settings, Build(settings));
            _logger.LogInformation("Tutor aktiv: {Description}", Description);
        }
        catch (Exception e)
        {
            // Never let a broken settings row take the whole app down – fall back to defaults, and leave UserId
            // null so the next call retries rather than pinning this failure to the circuit.
            _logger.LogWarning(e, "Tutor-Einstellungen unlesbar, Standard wird verwendet.");
            _active = new Active(null, _defaults, Build(_defaults));
        }
    }

    /// <inheritdoc cref="ILearnerBound.EnsureCurrentAsync" />
    /// <remarks>Cheap when nothing changed (one claims lookup, no DB round trip).</remarks>
    public async Task EnsureCurrentAsync(CancellationToken ct = default)
    {
        var userId = await _currentUser.GetUserIdAsync();
        if (userId != _active.UserId) await InitializeAsync(ct);
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
        _active = new Active(userId, settings, Build(settings));
        _logger.LogInformation("Tutor umkonfiguriert: {Description}", Description);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var userId = await _currentUser.GetUserIdAsync();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.UserId == userId && s.Key == SettingsKey, ct);
        if (row is not null) { db.Settings.Remove(row); await db.SaveChangesAsync(ct); }
        _active = new Active(userId, _defaults, Build(_defaults));
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
    // Each entry point re-checks the signed-in learner first: the one place an actual provider call - and the API
    // key that pays for it - could otherwise run under a stale, previously-loaded account's settings.
    public async Task<ProductionEvaluation> EvaluateAsync(Exercise exercise, string learnerText, ProductionMode mode, LearnerContext context, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _active.Tutor.EvaluateAsync(exercise, learnerText, mode, context, ct);
    }

    public async Task<IReadOnlyList<Exercise>> GenerateExercisesAsync(SkillNode node, CefrBand band, int count, ExerciseContext exerciseContext, LearnerContext context, IReadOnlyList<Exercise> examples, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _active.Tutor.GenerateExercisesAsync(node, band, count, exerciseContext, context, examples, ct);
    }

    public async Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _active.Tutor.DiscussAsync(thesis, history, context, ct);
    }

    public async Task<Exercise?> GenerateReadingAsync(SkillNode node, CefrBand band, bool audioOnly, LearnerContext context, CancellationToken ct = default)
    {
        await EnsureCurrentAsync(ct);
        return await _active.Tutor.GenerateReadingAsync(node, band, audioOnly, context, ct);
    }

    private sealed record StoredSettings(string PresetId, string? BaseUrl, string Model, string? ProtectedApiKey, string? Effort, int TimeoutSeconds);
}
