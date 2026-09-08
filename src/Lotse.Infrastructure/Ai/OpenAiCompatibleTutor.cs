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
    // Both flags are learned from the provider's answers and read from async continuations of several requests
    // at once (FillWeakestAsync fans out), hence volatile.
    private volatile bool _schemaFormatUnsupported;
    private volatile bool _temperatureUnsupported;
    /// <summary>The provider's last rejection text, so the final error names the real cause instead of "keine gültige Antwort".</summary>
    private string? _lastRejection;

    /// <summary>Longest pause a Retry-After header may impose before the attempt is given up instead.</summary>
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(20);

    public OpenAiCompatibleTutor(TutorOptions options, ILogger<OpenAiCompatibleTutor> logger, HttpMessageHandler? handler = null) : base(logger)
    {
        _options = options;
        var baseUrl = (options.BaseUrl ?? "").TrimEnd('/') + "/";
        // OpenAI's gpt-5 line rejects any temperature but the default with a 400; sending none is the only value
        // every current model accepts. Other providers learn it the same way at runtime (see TryCompleteAsync).
        _temperatureUnsupported = baseUrl.Contains("openai.com", StringComparison.OrdinalIgnoreCase);
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
            if (strict.Text is not null) return ExtractJsonObject(strict.Text);
            // Only a rejected request shape says anything about the schema. An empty answer (DeepSeek documents them
            // as occasional) used to latch this flag for the rest of the circuit and silently downgrade every later
            // evaluation to json_object.
            if (strict.Rejected)
            {
                _schemaFormatUnsupported = true;
                Logger.LogInformation("{Provider} akzeptiert kein json_schema-Format, nutze json_object.", ProviderName);
            }
        }

        var loose = await TryCompleteAsync(system, userWithSchema, new JsonObject { ["type"] = "json_object" }, ct);
        var text = loose.Text ?? (await TryCompleteAsync(system, userWithSchema, null, ct)).Text
                   ?? throw new InvalidOperationException($"{ProviderName} hat keine gültige Antwort geliefert{(_lastRejection is null ? "." : $": {_lastRejection}")}");
        return ExtractJsonObject(text);
    }

    protected override async Task<string> CompleteChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var t in turns) messages.Add(new JsonObject { ["role"] = t.FromLearner ? "user" : "assistant", ["content"] = t.Text });
        var body = new JsonObject { ["model"] = _options.Model, ["messages"] = messages };
        if (!_temperatureUnsupported) body["temperature"] = 0.7;
        try
        {
            return await SendAsync(body, ct) ?? throw new InvalidOperationException($"{ProviderName} hat keine Antwort geliefert.");
        }
        catch (HttpRequestException e) when (IsTemperatureRejection(e) && !_temperatureUnsupported)
        {
            _temperatureUnsupported = true;
            body.Remove("temperature");
            return await SendAsync(body, ct) ?? throw new InvalidOperationException($"{ProviderName} hat keine Antwort geliefert.");
        }
    }

    /// <summary>One completion attempt: the text, or why there is none - the caller must tell a rejected request
    /// shape (try the next stage, remember it) from an empty answer (try the next stage, remember nothing).</summary>
    private readonly record struct Completion(string? Text, bool Rejected);

    private async Task<Completion> TryCompleteAsync(string system, string user, JsonObject? responseFormat, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
        };
        if (!_temperatureUnsupported) body["temperature"] = 0.2;
        // A clone: on the temperature retry this method builds a second body, and a JsonNode has exactly one parent.
        if (responseFormat is not null) body["response_format"] = responseFormat.DeepClone();
        try
        {
            return new Completion(await SendAsync(body, ct), false);
        }
        catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            if (IsTemperatureRejection(e) && !_temperatureUnsupported)
            {
                // The model minds the temperature, not the response format: drop it and try this very stage again.
                _temperatureUnsupported = true;
                Logger.LogInformation("{Provider} akzeptiert keine Temperatur, sende keine mehr.", ProviderName);
                return await TryCompleteAsync(system, user, responseFormat, ct);
            }
            _lastRejection = e.Message;
            Logger.LogDebug(e, "Anfrageform abgelehnt, nächste Stufe.");
            return new Completion(null, true);
        }
    }

    private static bool IsTemperatureRejection(HttpRequestException e)
        => e.Message.Contains("temperature", StringComparison.OrdinalIgnoreCase);

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
                        var wait = RetryAfter(response) ?? TimeSpan.FromSeconds(attempt * 3);
                        // Free tiers answer a daily-quota hit with a Retry-After of hours. Honouring that literally
                        // kept the "Bewertung läuft …" spinner up with nothing the learner could do about it.
                        if (wait > MaxRetryWait)
                            throw new InvalidOperationException($"{ProviderName}: Ratenlimit erreicht – der Anbieter bittet um {Math.Ceiling(wait.TotalMinutes)} Minuten Pause. Später noch einmal, oder ein anderes Modell wählen.");
                        Logger.LogWarning("{Provider} antwortete {Status}, neuer Versuch in {Wait}s", ProviderName, (int)response.StatusCode, wait.TotalSeconds);
                        await Task.Delay(wait, ct);
                        continue;
                    }
                    Logger.LogWarning("{Provider} antwortete {Status}: {Body}", ProviderName, (int)response.StatusCode, text[..Math.Min(text.Length, 400)]);
                    throw new HttpRequestException($"{ProviderName}: HTTP {(int)response.StatusCode} – {Trim(text)}", null, response.StatusCode);
                }
                var parsed = JsonSerializer.Deserialize<ChatResponse>(text, Wire);
                if (parsed?.Error?.Message is { } err) throw new InvalidOperationException($"{ProviderName}: {err}");
                var choice = parsed?.Choices?.FirstOrDefault();
                // A cut-off object used to reach ExtractJsonObject, which then failed on an inner brace with a raw
                // System.Text.Json message in the learner's alert.
                if (string.Equals(choice?.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{ProviderName}: Die Antwort wurde am Token-Limit abgeschnitten. Ein kürzerer Text hilft, oder ein Modell mit größerem Kontext.");
                var content = choice?.Message?.Content;
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
        }
    }

    /// <summary>Retry-After as a duration, whether the header carried seconds or an HTTP date.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var h = response.Headers.RetryAfter;
        if (h is null) return null;
        if (h.Delta is { } delta) return delta;
        if (h.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
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
