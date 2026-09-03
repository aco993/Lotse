using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lotse.Core.Tutor;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Ai;

/// <summary>
/// Any server that speaks the OpenAI chat/completions protocol: Ollama and LM Studio locally (free), OpenRouter,
/// Groq, Mistral, DeepSeek, OpenAI. No SDK – one HTTP call. JSON answers are requested with a json_schema response
/// format when the server accepts it, and fall back to json_object plus the schema in the prompt otherwise.
/// </summary>
public sealed class OpenAiCompatibleTutor : TutorBase
{
    private readonly TutorOptions _options;
    private readonly HttpClient _http;
    private bool _schemaFormatUnsupported;

    public OpenAiCompatibleTutor(TutorOptions options, ILogger<OpenAiCompatibleTutor> logger, HttpMessageHandler? handler = null) : base(logger)
    {
        _options = options;
        var baseUrl = (options.BaseUrl ?? "").TrimEnd('/') + "/";
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(30, options.TimeoutSeconds));
        if (!string.IsNullOrWhiteSpace(options.ResolvedApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ResolvedApiKey);
        // OpenRouter likes to know who calls; harmless elsewhere.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", "Lotse");
    }

    public override bool IsAvailable => _options.IsConfigured;
    public override string Description => IsAvailable
        ? $"{ProviderName} ({_options.Model} @ {_options.BaseUrl})"
        : "Kein KI-Tutor konfiguriert (Lotse:Tutor:BaseUrl und Model fehlen).";

    private string ProviderName
    {
        get
        {
            var url = _options.BaseUrl ?? "";
            if (url.Contains("11434")) return "Ollama";
            if (url.Contains("1234")) return "LM Studio";
            if (url.Contains("openrouter")) return "OpenRouter";
            if (url.Contains("groq")) return "Groq";
            if (url.Contains("mistral")) return "Mistral";
            if (url.Contains("deepseek")) return "DeepSeek";
            if (url.Contains("openai.com")) return "OpenAI";
            return "OpenAI-kompatibel";
        }
    }

    protected override async Task<string> CompleteJsonAsync(string system, string user, string schemaName, string schemaJson, CancellationToken ct)
    {
        var schema = JsonNode.Parse(schemaJson)!;
        var userWithSchema = user + "\n\nAntworte ausschließlich mit einem JSON-Objekt, das exakt diesem Schema entspricht (keine Erklärungen, keine Markdown-Zäune):\n" + schemaJson;

        if (!_schemaFormatUnsupported)
        {
            var strict = await TryCompleteAsync(system, userWithSchema, new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = schemaName, ["strict"] = true, ["schema"] = schema.DeepClone() },
            }, ct);
            if (strict is not null) return ExtractJsonObject(strict);
            _schemaFormatUnsupported = true;
            Logger.LogInformation("{Provider} akzeptiert kein json_schema-Format, nutze json_object.", ProviderName);
        }

        var loose = await TryCompleteAsync(system, userWithSchema, new JsonObject { ["type"] = "json_object" }, ct)
                    ?? await TryCompleteAsync(system, userWithSchema, null, ct)
                    ?? throw new InvalidOperationException($"{ProviderName} hat keine gültige Antwort geliefert.");
        return ExtractJsonObject(loose);
    }

    protected override async Task<string> CompleteChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var t in turns) messages.Add(new JsonObject { ["role"] = t.FromLearner ? "user" : "assistant", ["content"] = t.Text });
        var body = new JsonObject { ["model"] = _options.Model, ["messages"] = messages, ["temperature"] = 0.7 };
        return await SendAsync(body, ct) ?? throw new InvalidOperationException($"{ProviderName} hat keine Antwort geliefert.");
    }

    /// <summary>Returns the assistant text, or null when the server rejected the request shape (so the caller can degrade).</summary>
    private async Task<string?> TryCompleteAsync(string system, string user, JsonObject? responseFormat, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
            ["temperature"] = 0.2,
        };
        if (responseFormat is not null) body["response_format"] = responseFormat;
        try
        {
            return await SendAsync(body, ct);
        }
        catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            Logger.LogDebug(e, "Anfrageform abgelehnt, nächste Stufe.");
            return null;
        }
    }

    private sealed record ChatResponse(List<Choice>? Choices, ErrorInfo? Error);
    private sealed record Choice(ChoiceMessage? Message, string? FinishReason);
    private sealed record ChoiceMessage(string? Content);
    private sealed record ErrorInfo(string? Message);

    private static readonly JsonSerializerOptions Wire = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>One call with a small retry budget for the transient cases (429, 5xx, connection blips) – free tiers hit these often.</summary>
    private async Task<string?> SendAsync(JsonObject body, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync("chat/completions", body, ct);
            }
            catch (HttpRequestException e) when (attempt < maxAttempts && e.StatusCode is null)
            {
                Logger.LogWarning("{Provider} nicht erreichbar (Versuch {Attempt}): {Message}", ProviderName, attempt, e.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                continue;
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    var transient = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                    if (transient && attempt < maxAttempts)
                    {
                        var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 3);
                        Logger.LogWarning("{Provider} antwortete {Status}, neuer Versuch in {Wait}s", ProviderName, (int)response.StatusCode, wait.TotalSeconds);
                        await Task.Delay(wait, ct);
                        continue;
                    }
                    Logger.LogWarning("{Provider} antwortete {Status}: {Body}", ProviderName, (int)response.StatusCode, text[..Math.Min(text.Length, 400)]);
                    throw new HttpRequestException($"{ProviderName}: HTTP {(int)response.StatusCode} – {Trim(text)}", null, response.StatusCode);
                }
                var parsed = JsonSerializer.Deserialize<ChatResponse>(text, Wire);
                if (parsed?.Error?.Message is { } err) throw new InvalidOperationException($"{ProviderName}: {err}");
                var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
        }
    }

    private static string Trim(string s)
    {
        try
        {
            var node = JsonNode.Parse(s);
            var msg = node?["error"]?["message"]?.GetValue<string>();
            if (msg is not null) return msg;
        }
        catch (JsonException) { }
        return s.Length > 200 ? s[..200] : s;
    }
}
