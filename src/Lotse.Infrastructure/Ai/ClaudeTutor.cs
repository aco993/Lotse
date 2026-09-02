using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Ai;

/// <summary>Configuration for the Claude-based tutor. Bound from the "Lotse:Tutor" section / user secrets / environment.</summary>
public sealed class TutorOptions
{
    public const string Section = "Lotse:Tutor";

    /// <summary>Falls back to the ANTHROPIC_API_KEY environment variable when empty.</summary>
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5";
    /// <summary>low | medium | high – evaluation quality vs. cost. Medium is plenty for B2 feedback.</summary>
    public string Effort { get; set; } = "medium";

    public string? ResolvedApiKey => string.IsNullOrWhiteSpace(ApiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : ApiKey;
}

/// <summary>
/// Claude as language tutor. Three jobs: rubric-based evaluation of free production with tagged errors,
/// on-demand generation of exercises for a weak node, and a discussion partner for the speaking exam.
/// All structured answers go through JSON schema output so the deterministic engine can ingest them.
/// </summary>
public sealed class ClaudeTutor : ITutor
{
    private readonly TutorOptions _options;
    private readonly ILogger<ClaudeTutor> _logger;
    private readonly Lazy<AnthropicClient> _client;

    public ClaudeTutor(TutorOptions options, ILogger<ClaudeTutor> logger)
    {
        _options = options;
        _logger = logger;
        _client = new Lazy<AnthropicClient>(() => new AnthropicClient { ApiKey = _options.ResolvedApiKey });
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ResolvedApiKey);
    public string Description => IsAvailable ? $"Claude ({_options.Model}, Aufwand {_options.Effort})" : "Kein KI-Tutor konfiguriert (ANTHROPIC_API_KEY fehlt).";

    // ------------------------------------------------------------------------------------------
    // Evaluation
    // ------------------------------------------------------------------------------------------

    private sealed record EvalDto(
        double OverallScore, string EstimatedLevel, List<RubricDto> Rubric, List<ErrorDto> Errors,
        string CorrectedText, string Feedback, List<string> SuggestedNodeIds, List<string> Upgrades);
    private sealed record RubricDto(string Criterion, int Score, string Comment);
    private sealed record ErrorDto(string Code, string Snippet, string Correction, string Explanation);

    private const string EvalSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["overallScore", "estimatedLevel", "rubric", "errors", "correctedText", "feedback", "suggestedNodeIds", "upgrades"],
          "properties": {
            "overallScore": { "type": "number", "description": "0 bis 1, aus der Rubrik abgeleitet (3 von 5 Punkten je Kriterium = 0.6 = bestanden)" },
            "estimatedLevel": { "type": "string", "description": "Eine von: B1.1, B1.2, B2.1, B2.2, C1" },
            "rubric": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["criterion", "score", "comment"],
              "properties": { "criterion": { "type": "string" }, "score": { "type": "integer", "minimum": 0, "maximum": 5 }, "comment": { "type": "string" } } } },
            "errors": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["code", "snippet", "correction", "explanation"],
              "properties": { "code": { "type": "string", "description": "Fehlercode aus dem Katalog" }, "snippet": { "type": "string" }, "correction": { "type": "string" }, "explanation": { "type": "string", "description": "Kurz, auf Deutsch, mit serbischem Kontrast wenn hilfreich" } } } },
            "correctedText": { "type": "string" },
            "feedback": { "type": "string", "description": "Coach-Nachricht auf Deutsch: 2 Stärken, die 1-2 wichtigsten Baustellen, ein konkreter nächster Schritt" },
            "suggestedNodeIds": { "type": "array", "items": { "type": "string" } },
            "upgrades": { "type": "array", "items": { "type": "string" }, "description": "2-3 Sätze des Lerners in B2-Qualität umformuliert" }
          }
        }
        """;

    public async Task<ProductionEvaluation> EvaluateAsync(Exercise exercise, string learnerText, ProductionMode mode, LearnerContext context, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(context);
        var task = new StringBuilder()
            .AppendLine(mode == ProductionMode.Writing ? "AUFGABE (Schreiben):" : "AUFGABE (Sprechen – du bewertest ein automatisches Transkript, ignoriere fehlende Satzzeichen und Groß-/Kleinschreibung, bewerte Aussprache nicht):")
            .AppendLine(exercise.Prompt)
            .AppendLine()
            .AppendLine("BEWERTUNGSKRITERIEN (je 0–5 Punkte, 3 = B2 bestanden):")
            .AppendJoin("\n", exercise.Rubric.Select(r => "- " + r)).AppendLine()
            .AppendLine(mode == ProductionMode.Writing
                ? "Bewerte zusätzlich immer die vier Prüfungskriterien: Erfüllung der Aufgabe, Kohärenz und Aufbau, Wortschatz, Strukturen."
                : "Bewerte zusätzlich immer: Erfüllung der Aufgabe, Kohärenz, Wortschatz, Strukturen, Flüssigkeit.")
            .AppendLine();
        if (exercise.MinWords is > 0) task.AppendLine($"Erwartete Länge: ca. {exercise.MinWords} Wörter.");
        task.AppendLine().AppendLine("TEXT DES LERNERS:").AppendLine(learnerText.Trim());

        var json = await CompleteJsonAsync(system, task.ToString(), EvalSchema, ct);
        var dto = JsonSerializer.Deserialize<EvalDto>(json, ContentLoader.Options)
                  ?? throw new InvalidOperationException("Leere Bewertung vom Tutor.");

        var knownCodes = context.ErrorCatalogue.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
        var errors = dto.Errors
            .Where(e => knownCodes.Contains(e.Code))
            .Select(e => new TutorError(e.Code, e.Snippet, e.Correction, e.Explanation))
            .ToList();
        var dropped = dto.Errors.Count - errors.Count;
        if (dropped > 0) _logger.LogInformation("Tutor lieferte {Count} unbekannte Fehlercodes, verworfen.", dropped);

        return new ProductionEvaluation(
            Math.Clamp(dto.OverallScore, 0, 1),
            dto.EstimatedLevel,
            dto.Rubric.Select(r => new RubricScore(r.Criterion, Math.Clamp(r.Score, 0, 5), r.Comment)).ToList(),
            errors,
            dto.CorrectedText,
            dto.Feedback,
            dto.SuggestedNodeIds,
            dto.Upgrades);
    }

    // ------------------------------------------------------------------------------------------
    // Generation
    // ------------------------------------------------------------------------------------------

    private const string GenSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["exercises"],
          "properties": {
            "exercises": { "type": "array", "items": { "type": "object", "additionalProperties": false,
              "required": ["type", "band", "prompt", "instruction", "answers", "options", "correctIndex", "explanation", "serbianNote"],
              "properties": {
                "type": { "type": "string", "enum": ["cloze", "transform", "translate", "multipleChoice"] },
                "band": { "type": "string", "enum": ["B1_2", "B2_1", "B2_2"] },
                "prompt": { "type": "string" },
                "instruction": { "type": "string" },
                "answers": { "type": "array", "items": { "type": "string" }, "description": "Alle akzeptablen Antworten; leer bei multipleChoice" },
                "options": { "type": "array", "items": { "type": "string" }, "description": "Nur bei multipleChoice, sonst leer" },
                "correctIndex": { "type": "integer", "description": "Nur bei multipleChoice, sonst -1" },
                "explanation": { "type": "string" },
                "serbianNote": { "type": "string", "description": "Kontrastiver Hinweis für serbische Muttersprachler oder leer" }
              } } }
          }
        }
        """;

    private sealed record GenFile(List<GenItem> Exercises);
    private sealed record GenItem(string Type, string Band, string Prompt, string Instruction, List<string> Answers, List<string> Options, int CorrectIndex, string Explanation, string SerbianNote);

    public async Task<IReadOnlyList<Exercise>> GenerateExercisesAsync(SkillNode node, CefrBand band, int count, ExerciseContext exerciseContext, LearnerContext context, IReadOnlyList<Exercise> examples, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(context) + "\n\nDu erstellst jetzt neue Übungen für die Übungsbank. Qualität vor Menge: natürliche, idiomatische Sätze aus Beruf und Alltag in Deutschland, keine Lehrbuchkünstlichkeit.";
        var sb = new StringBuilder()
            .AppendLine($"THEMA (Knoten {node.Id}): {node.Title} – {node.Description}")
            .AppendLine($"Niveau: {band.Label()}. Kontext: {exerciseContext}. Anzahl: {count}.")
            .AppendLine(node.InterferenceNote is null ? "" : $"Typische Interferenz: {node.InterferenceNote}");
        if (context.RecentErrorCodes.Count > 0)
            sb.AppendLine($"Der Lerner machte zuletzt diese Fehler: {string.Join(", ", context.RecentErrorCodes)}. Ziele darauf ab.");
        sb.AppendLine()
          .AppendLine("Regeln: Bei cloze steht die Lücke als ___ und die Basisform in Klammern, z. B. „mit ___ (der Bus)“. Bei translate ist prompt ein serbischer Satz (lateinische Schrift) und answers enthält 2–4 deutsche Varianten. Bei transform beschreibt instruction die Umformung. Antworten müssen eindeutig prüfbar sein – keine offenen Aufgaben.")
          .AppendLine()
          .AppendLine("Beispiele im gewünschten Stil:");
        foreach (var ex in examples.Take(3))
            sb.AppendLine($"- [{ex.Type}] {ex.Prompt} → {string.Join(" | ", ex.Answers.Take(2))}");

        var json = await CompleteJsonAsync(system, sb.ToString(), GenSchema, ct);
        var parsed = JsonSerializer.Deserialize<GenFile>(json, ContentLoader.Options);
        if (parsed is null) return [];

        var result = new List<Exercise>();
        foreach (var g in parsed.Exercises)
        {
            if (!Enum.TryParse<ExerciseType>(g.Type, ignoreCase: true, out var type)) continue;
            if (!Enum.TryParse<CefrBand>(g.Band, ignoreCase: true, out var b)) b = band;
            var ex = new Exercise
            {
                Id = $"gen.{node.Id.ToLowerInvariant()}.{Guid.NewGuid():N}"[..40],
                Type = type,
                NodeId = node.Id,
                Band = b,
                Context = exerciseContext,
                Source = ExerciseSource.Generated,
                Prompt = g.Prompt,
                Instruction = string.IsNullOrWhiteSpace(g.Instruction) ? null : g.Instruction,
                Answers = g.Answers,
                Options = g.Options,
                CorrectIndex = type == ExerciseType.MultipleChoice ? g.CorrectIndex : null,
                Explanation = g.Explanation,
                SerbianNote = string.IsNullOrWhiteSpace(g.SerbianNote) ? null : g.SerbianNote,
            };
            if (ExerciseValidator.Validate(ex).Count == 0) result.Add(ex);
        }
        _logger.LogInformation("Tutor generierte {Ok}/{Total} gültige Übungen für {Node}.", result.Count, parsed.Exercises.Count, node.Id);
        return result;
    }

    // ------------------------------------------------------------------------------------------
    // Discussion partner
    // ------------------------------------------------------------------------------------------

    public async Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(context) + $$"""


            Du bist jetzt Prüfungspartner in „Sprechen Teil 2“ (Diskussion) des Goethe-Zertifikats B2.
            These: „{{thesis}}“
            Verhalte dich wie ein echter Gesprächspartner auf B2-Niveau: vertritt eine klare Gegenposition zum Lerner, bring pro Redebeitrag EIN Argument oder EINE Nachfrage, maximal 3–4 Sätze, sprich den Lerner mit „Sie“ an.
            Am Ende jedes deiner Beiträge hänge – getrennt durch eine Leerzeile und beginnend mit „✎ “ – höchstens EINE knappe Korrektur des letzten Lernerbeitrags an (Fehler → richtig), oder „✎ Sprachlich gut.“ Keine weiteren Meta-Kommentare.
            Wenn der Lerner nach etwa fünf Wechseln keinen Kompromiss vorschlägt, steuere selbst auf einen Kompromiss zu.
            """;

        var messages = new List<MessageParam>();
        if (history.Count == 0 || !history[0].FromLearner)
            messages.Add(new MessageParam { Role = Role.User, Content = "Bitte eröffnen Sie die Diskussion mit Ihrer Position zur These." });
        foreach (var turn in history)
            messages.Add(new MessageParam { Role = turn.FromLearner ? Role.User : Role.Assistant, Content = turn.Text });

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

    // ------------------------------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------------------------------

    private static string BuildSystemPrompt(LearnerContext context)
    {
        var sb = new StringBuilder()
            .AppendLine("Du bist eine erfahrene DaF-Lehrkraft (Deutsch als Fremdsprache) und Prüferin für das Goethe-Zertifikat B2. Du betreust einen einzelnen Lerner individuell.")
            .AppendLine($"Lerner: Muttersprache {context.NativeLanguage}, lebt und arbeitet in Deutschland{(context.Occupation is null ? "" : $" als {context.Occupation}")}, Niveau B1–B2, versteht deutlich mehr, als er aktiv produziert. Ziel: B2-Prüfung bestehen und im Beruf sicher Deutsch sprechen und schreiben.")
            .AppendLine("Bewerte streng nach B2-Maßstab, aber ermutigend. Erkläre auf Deutsch (B2-verständlich). Wenn ein Fehler typisch für serbische Muttersprachler ist (Artikel, Wortstellung, Kasus nach Präposition, haben/sein, Verben mit Präposition, doppelte Verneinung, falsche Freunde), nenne den Kontrast kurz mit serbischem Beispiel.")
            .AppendLine();
        if (context.WeakNodeTitles.Count > 0)
            sb.AppendLine($"Bekannte Schwachstellen des Lerners: {string.Join(", ", context.WeakNodeTitles)}. Achte besonders darauf.");
        sb.AppendLine()
          .AppendLine("FEHLERKATALOG – verwende ausschließlich diese Codes, wähle den spezifischsten:");
        foreach (var e in context.ErrorCatalogue)
            sb.AppendLine($"- {e.Code}: {e.Title} ({e.Description})");
        return sb.ToString();
    }

    private async Task<string> CompleteJsonAsync(string system, string user, string schemaJson, CancellationToken ct)
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

        var text = ExtractText(response);
        _logger.LogDebug("Tutor-Antwort: {Tokens} Ausgabetoken", response.Usage.OutputTokens);
        return text;
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
