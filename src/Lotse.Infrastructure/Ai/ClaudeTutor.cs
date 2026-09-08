using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Lotse.Core.Tutor;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Ai;

/// <summary>Claude via the official Anthropic SDK. Structured answers use JSON-schema output; effort is configurable.</summary>
public sealed class ClaudeTutor : TutorBase, IDisposable
{
    private readonly TutorOptions _options;
    private readonly Lazy<AnthropicClient> _client;

    /// <summary>Releases the SDK client (and the HttpClient it owns) if one was ever created; TutorRegistry calls
    /// this when the tutor is replaced or the circuit ends.</summary>
    public void Dispose()
    {
        if (_client.IsValueCreated) _client.Value.Dispose();
    }

    public ClaudeTutor(TutorOptions options, ILogger<ClaudeTutor> logger) : base(logger)
    {
        _options = options;
        // The per-answer budget from the settings page applies here too. Without it the SDK's own defaults hold
        // (ten minutes, two retries): a stalled request kept the "Bewertung läuft …" spinner for half an hour while
        // the learner had set 120 s. One retry is the SDK's business; the retry budget in our own loop is for the
        // OpenAI-compatible path.
        _client = new Lazy<AnthropicClient>(() => new AnthropicClient
        {
            ApiKey = _options.ResolvedApiKey,
            Timeout = TimeSpan.FromSeconds(Math.Max(20, _options.TimeoutSeconds)),
            MaxRetries = 1,
        });
    }

    public override bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ResolvedApiKey);
    public override string Description => IsAvailable ? $"Claude ({_options.Model}, Aufwand {_options.Effort})" : "Kein KI-Tutor konfiguriert (ANTHROPIC_API_KEY fehlt).";

    protected override async Task<string> CompleteJsonAsync(string system, string user, string schemaName, string schemaJson, CancellationToken ct)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schemaJson)
                     ?? throw new InvalidOperationException("Schema ungültig.");

        var response = await _client.Value.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            // Adaptive thinking is on by default for these models and its tokens count against max_tokens; 8 000
            // was enough for the answer alone and cut the JSON off mid-object at higher effort.
            MaxTokens = 16000,
            System = system,
            Messages = [new MessageParam { Role = Role.User, Content = user }],
            OutputConfig = new OutputConfig
            {
                Effort = ParseEffort(_options.Effort),
                Format = new JsonOutputFormat { Schema = schema },
            },
        }, cancellationToken: ct);

        ThrowIfNotComplete(response);
        Logger.LogDebug("Claude-Antwort: {Tokens} Ausgabetoken", response.Usage.OutputTokens);
        // Same salvage as the OpenAI path: a model that wraps the object in a sentence must not become a JsonException.
        return ExtractJsonObject(ExtractText(response));
    }

    /// <summary>
    /// <see cref="StopReason"/> is a C# enum. The first version compared <c>StopReason?.ToString() == "refusal"</c>,
    /// which can never be true (an enum prints "Refusal"), so a refusal fell through as empty text and surfaced to
    /// the learner as "The input does not contain any JSON tokens".
    /// </summary>
    private static void ThrowIfNotComplete(Message response)
    {
        if (response.StopReason == StopReason.Refusal)
            throw new InvalidOperationException("Der Tutor hat diese Anfrage abgelehnt – formuliere den Text bitte anders oder lass eine Passage weg.");
        if (response.StopReason == StopReason.MaxTokens)
            throw new InvalidOperationException("Die Antwort wurde am Token-Limit abgeschnitten. Ein kürzerer Text oder ein geringerer Aufwand (Einstellungen) hilft.");
        if (response.StopReason == StopReason.ModelContextWindowExceeded)
            throw new InvalidOperationException("Der Text sprengt das Kontextfenster des Modells – bitte kürzen.");
    }

    protected override async Task<string> CompleteChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        var messages = turns.Select(t => new MessageParam { Role = t.FromLearner ? Role.User : Role.Assistant, Content = t.Text }).ToList();
        var response = await _client.Value.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = 4000,
            System = system,
            Messages = messages,
            OutputConfig = new OutputConfig { Effort = ParseEffort(_options.Effort) },
        }, cancellationToken: ct);
        ThrowIfNotComplete(response);
        return ExtractText(response);
    }

    private static string ExtractText(Message response)
        => string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));

    private static Effort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };
}
