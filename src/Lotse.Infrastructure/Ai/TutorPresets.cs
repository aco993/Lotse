namespace Lotse.Infrastructure.Ai;

/// <summary>A known provider with sensible defaults, so the settings page is a pick-list instead of a form to research.</summary>
public sealed record TutorPreset(
    string Id,
    string Name,
    TutorProvider Provider,
    string? BaseUrl,
    string DefaultModel,
    /// <summary>Environment variable the key falls back to when none is stored.</summary>
    string? KeyEnvVar,
    bool NeedsKey,
    string? SignupUrl,
    /// <summary>One line the settings page shows: cost, speed, quality.</summary>
    string Note,
    IReadOnlyList<string> SuggestedModels,
    /// <summary>
    /// Seconds to allow one completion, chosen per provider rather than globally. A hosted 70B answers a full
    /// evaluation in seconds; a 7B on this machine needs about two minutes for the same ~800 tokens (measured:
    /// 6.7 tok/s), which the old flat 120 s cut off just before the end. Local presets therefore start generous.
    /// </summary>
    int DefaultTimeoutSeconds = 120);

public static class TutorPresets
{
    public static readonly IReadOnlyList<TutorPreset> All =
    [
        new("groq", "Groq (kostenlos, sehr schnell)", TutorProvider.OpenAi, "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile",
            "GROQ_API_KEY", true, "https://console.groq.com/keys",
            "Kostenlose Kontingente, Antworten in Sekunden. 70B-Modelle bewerten Deutsch ordentlich – die Empfehlung ohne Budget.",
            ["llama-3.3-70b-versatile", "openai/gpt-oss-120b", "qwen/qwen3-32b", "llama-3.1-8b-instant"]),
        new("anthropic", "Claude (Anthropic)", TutorProvider.Anthropic, null, "claude-sonnet-5",
            "ANTHROPIC_API_KEY", true, "https://console.anthropic.com/",
            "Beste Qualität für Prüfungsbewertung. Pay-per-use, Sonnet ca. 1 Cent, Opus ca. 4 Cent pro bewertetem Text.",
            ["claude-sonnet-5", "claude-opus-5", "claude-haiku-4-5"]),
        new("openrouter", "OpenRouter (viele Modelle, teils kostenlos)", TutorProvider.OpenAi, "https://openrouter.ai/api/v1", "meta-llama/llama-3.3-70b-instruct:free",
            "OPENROUTER_API_KEY", true, "https://openrouter.ai/keys",
            "Ein Schlüssel, hunderte Modelle; Modelle mit „:free“ kosten nichts (Ratenlimit).",
            ["meta-llama/llama-3.3-70b-instruct:free", "qwen/qwen3-235b-a22b:free", "anthropic/claude-sonnet-4.5", "openai/gpt-5-mini"]),
        new("ollama", "Ollama (lokal, kostenlos)", TutorProvider.OpenAi, "http://localhost:11434/v1", "qwen2.5:7b",
            null, false, "https://ollama.com/download",
            "Läuft auf deinem Rechner, keine Daten verlassen ihn. Ohne GPU langsam (Minuten pro Bewertung); ab 7B brauchbar. Ollamas Standard-Kontext (4096) ist für eine ganze Bewertung zu klein – lege dir ein Modell mit „PARAMETER num_ctx 8192“ an.",
            ["qwen2.5:7b", "gemma3:12b", "mistral:7b", "llama3.1:8b"], DefaultTimeoutSeconds: 300),
        new("lmstudio", "LM Studio (lokal, kostenlos)", TutorProvider.OpenAi, "http://localhost:1234/v1", "local-model",
            null, false, "https://lmstudio.ai/",
            "Wie Ollama, mit grafischer Oberfläche. Modell in LM Studio laden und den Server starten.",
            ["local-model"], DefaultTimeoutSeconds: 300),
        new("mistral", "Mistral", TutorProvider.OpenAi, "https://api.mistral.ai/v1", "mistral-large-latest",
            "MISTRAL_API_KEY", true, "https://console.mistral.ai/",
            "Europäischer Anbieter, gutes Deutsch, günstig.",
            ["mistral-large-latest", "mistral-small-latest"]),
        new("deepseek", "DeepSeek", TutorProvider.OpenAi, "https://api.deepseek.com/v1", "deepseek-chat",
            "DEEPSEEK_API_KEY", true, "https://platform.deepseek.com/",
            "Sehr günstig, solide Qualität.",
            ["deepseek-chat", "deepseek-reasoner"]),
        new("openai", "OpenAI", TutorProvider.OpenAi, "https://api.openai.com/v1", "gpt-5-mini",
            "OPENAI_API_KEY", true, "https://platform.openai.com/api-keys",
            "Pay-per-use.",
            ["gpt-5-mini", "gpt-5"]),
        new("custom", "Eigener OpenAI-kompatibler Server", TutorProvider.OpenAi, null, "",
            "OPENAI_API_KEY", false, null,
            "Beliebiger Endpunkt, der /chat/completions spricht.",
            [], DefaultTimeoutSeconds: 300),
    ];

    public static TutorPreset Get(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}

/// <summary>What the learner configured in the app (persisted in the local database, key encrypted).</summary>
public sealed record TutorSettings(
    string PresetId,
    string? BaseUrl,
    string Model,
    /// <summary>Plain key in memory only; stored encrypted via Data Protection.</summary>
    string? ApiKey,
    string Effort = "medium",
    int TimeoutSeconds = 120)
{
    public TutorPreset Preset => TutorPresets.Get(PresetId);

    /// <summary>Resolves the key from the settings or, via <paramref name="env"/>, the preset's environment variable.</summary>
    public string? ResolveApiKey(Func<string, string?> env) => !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey
        : Preset.KeyEnvVar is { } name ? env(name) : null;

    public string? ResolvedBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl) ? BaseUrl : Preset.BaseUrl;

    public TutorOptions ToOptions(Func<string, string?> env) => new()
    {
        Provider = Preset.Provider,
        ApiKey = ResolveApiKey(env),
        BaseUrl = ResolvedBaseUrl,
        Model = string.IsNullOrWhiteSpace(Model) ? Preset.DefaultModel : Model,
        Effort = Effort,
        TimeoutSeconds = TimeoutSeconds,
    };

    public static TutorSettings Default => ForPreset("groq");

    /// <summary>A fresh setting for one provider: its model and its timeout, so switching provider never leaves the
    /// previous provider's budget behind (120 s from a hosted model would cut a local one off mid-answer).</summary>
    public static TutorSettings ForPreset(string presetId)
    {
        var p = TutorPresets.Get(presetId);
        return new(p.Id, null, p.DefaultModel, null, TimeoutSeconds: p.DefaultTimeoutSeconds);
    }
}
