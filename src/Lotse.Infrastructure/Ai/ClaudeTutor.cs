using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Lotse.Core.Tutor;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Ai;

/// <summary>Claude via the official Anthropic SDK. Structured answers use JSON-schema output; effort is configurable.</summary>
public sealed class ClaudeTutor : TutorBase
{
    private readonly TutorOptions _options;
    private readonly Lazy<AnthropicClient> _client;

    public ClaudeTutor(TutorOptions options, ILogger<ClaudeTutor> logger) : base(logger)
    {
        _options = options;
        _client = new Lazy<AnthropicClient>(() => new AnthropicClient { ApiKey = _options.ResolvedApiKey });
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
            MaxTokens = 8000,
            System = system,
            Messages = [new MessageParam { Role = Role.User, Content = user }],
            OutputConfig = new OutputConfig
            {
                Effort = ParseEffort(_options.Effort),
                Format = new JsonOutputFormat { Schema = schema },
            },
        }, cancellationToken: ct);

        if (response.StopReason?.ToString() == "refusal")
            throw new InvalidOperationException("Der Tutor hat die Anfrage abgelehnt.");

        Logger.LogDebug("Claude-Antwort: {Tokens} Ausgabetoken", response.Usage.OutputTokens);
        return ExtractText(response);
    }

    protected override async Task<string> CompleteChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        var messages = turns.Select(t => new MessageParam { Role = t.FromLearner ? Role.User : Role.Assistant, Content = t.Text }).ToList();
        var response = await _client.Value.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = 2000,
            System = system,
            Messages = messages,
            OutputConfig = new OutputConfig { Effort = ParseEffort(_options.Effort) },
        }, cancellationToken: ct);
        return ExtractText(response);
    }

    private static string ExtractText(Message response)
        => string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));

    private static Effort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };
}
