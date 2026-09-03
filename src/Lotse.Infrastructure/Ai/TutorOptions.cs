namespace Lotse.Infrastructure.Ai;

public enum TutorProvider
{
    /// <summary>Official Anthropic SDK (Claude). Key from <see cref="TutorOptions.ApiKey"/> or ANTHROPIC_API_KEY.</summary>
    Anthropic,
    /// <summary>Any OpenAI-compatible chat/completions endpoint: Ollama, LM Studio, OpenRouter, Groq, Mistral, DeepSeek, OpenAI.</summary>
    OpenAi,
}

/// <summary>Configuration for the AI tutor. Bound from the "Lotse:Tutor" section / user secrets / environment / command line.</summary>
public sealed class TutorOptions
{
    public const string Section = "Lotse:Tutor";

    public TutorProvider Provider { get; set; } = TutorProvider.Anthropic;

    /// <summary>Anthropic: falls back to ANTHROPIC_API_KEY. OpenAI-compatible: falls back to OPENAI_API_KEY; local servers (Ollama) need none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>OpenAI-compatible base URL including the version segment, e.g. http://localhost:11434/v1 (Ollama) or https://openrouter.ai/api/v1.</summary>
    public string? BaseUrl { get; set; }

    public string Model { get; set; } = "claude-opus-5";

    /// <summary>low | medium | high – Anthropic effort. Ignored by other providers.</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Seconds to wait for one completion. Local models on CPU can be slow.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    public string? ResolvedApiKey => !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey
        : Provider == TutorProvider.Anthropic ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        : Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public bool IsConfigured => Provider switch
    {
        TutorProvider.Anthropic => !string.IsNullOrWhiteSpace(ResolvedApiKey),
        TutorProvider.OpenAi => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model),
        _ => false,
    };
}
