using System.Text;
using System.Text.Json;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Content;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Ai;

/// <summary>
/// Provider-independent half of the AI tutor: prompts, JSON schemas, response mapping and validation.
/// Concrete providers only implement two calls: "give me JSON matching this schema" and "continue this chat".
/// </summary>
public abstract class TutorBase(ILogger logger) : ITutor
{
    protected readonly ILogger Logger = logger;

    public abstract bool IsAvailable { get; }
    public abstract string Description { get; }

    /// <summary>Returns raw JSON text conforming to <paramref name="schemaJson"/> (a JSON-schema object).</summary>
    protected abstract Task<string> CompleteJsonAsync(string system, string user, string schemaName, string schemaJson, CancellationToken ct);

    /// <summary>Plain text continuation of a conversation.</summary>
    protected abstract Task<string> CompleteChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct);

    // ------------------------------------------------------------------------------------------
    // Evaluation
    // ------------------------------------------------------------------------------------------

    private sealed record EvalDto(
        double OverallScore, string EstimatedLevel, List<RubricDto> Rubric, List<ErrorDto> Errors,
        string CorrectedText, string Feedback, List<string> SuggestedNodeIds, List<string> Upgrades);
    private sealed record RubricDto(string Criterion, int Score, string Comment);
    private sealed record ErrorDto(string Code, string Snippet, string Correction, string Explanation);

    protected const string EvalSchema = """
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

        var json = await CompleteJsonAsync(system, task.ToString(), "bewertung", EvalSchema, ct);
        var dto = JsonSerializer.Deserialize<EvalDto>(json, ContentLoader.Options)
                  ?? throw new InvalidOperationException("Leere Bewertung vom Tutor.");

        var knownCodes = context.ErrorCatalogue.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
        var errors = (dto.Errors ?? [])
            .Where(e => e.Code is not null && knownCodes.Contains(e.Code))
            .Select(e => new TutorError(e.Code, e.Snippet ?? "", e.Correction ?? "", e.Explanation ?? ""))
            .ToList();
        var dropped = (dto.Errors?.Count ?? 0) - errors.Count;
        if (dropped > 0) Logger.LogInformation("Tutor lieferte {Count} unbekannte Fehlercodes, verworfen.", dropped);

        return new ProductionEvaluation(
            Math.Clamp(dto.OverallScore, 0, 1),
            string.IsNullOrWhiteSpace(dto.EstimatedLevel) ? "?" : dto.EstimatedLevel,
            (dto.Rubric ?? []).Select(r => new RubricScore(r.Criterion ?? "", Math.Clamp(r.Score, 0, 5), r.Comment ?? "")).ToList(),
            errors,
            dto.CorrectedText ?? "",
            dto.Feedback ?? "",
            dto.SuggestedNodeIds ?? [],
            dto.Upgrades ?? []);
    }

    // ------------------------------------------------------------------------------------------
    // Drill generation
    // ------------------------------------------------------------------------------------------

    protected const string GenSchema = """
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
    private sealed record GenItem(string Type, string Band, string Prompt, string? Instruction, List<string>? Answers, List<string>? Options, int CorrectIndex, string? Explanation, string? SerbianNote);

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

        var json = await CompleteJsonAsync(system, sb.ToString(), "uebungen", GenSchema, ct);
        var parsed = JsonSerializer.Deserialize<GenFile>(json, ContentLoader.Options);
        if (parsed?.Exercises is null) return [];

        var result = new List<Exercise>();
        foreach (var g in parsed.Exercises)
        {
            if (g.Type is null || !Enum.TryParse<ExerciseType>(g.Type, ignoreCase: true, out var type)) continue;
            if (g.Band is null || !Enum.TryParse<CefrBand>(g.Band, ignoreCase: true, out var b)) b = band;
            var ex = new Exercise
            {
                Id = NewId(node.Id),
                Type = type,
                NodeId = node.Id,
                Band = b,
                Context = exerciseContext,
                Source = ExerciseSource.Generated,
                Prompt = g.Prompt ?? "",
                Instruction = string.IsNullOrWhiteSpace(g.Instruction) ? null : g.Instruction,
                Answers = g.Answers ?? [],
                Options = g.Options ?? [],
                CorrectIndex = type == ExerciseType.MultipleChoice ? g.CorrectIndex : null,
                Explanation = g.Explanation,
                SerbianNote = string.IsNullOrWhiteSpace(g.SerbianNote) ? null : g.SerbianNote,
            };
            if (ExerciseValidator.Validate(ex).Count == 0) result.Add(ex);
        }
        Logger.LogInformation("Tutor generierte {Ok}/{Total} gültige Übungen für {Node}.", result.Count, parsed.Exercises.Count, node.Id);
        return result;
    }

    // ------------------------------------------------------------------------------------------
    // Reading / listening generation
    // ------------------------------------------------------------------------------------------

    protected const string ReadingSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["part", "instruction", "text", "questions"],
          "properties": {
            "part": { "type": "string", "description": "z. B. Lesen Teil 3 oder Hören Teil 2" },
            "instruction": { "type": "string", "description": "Arbeitsanweisung wie in der Prüfung, ein Satz" },
            "text": { "type": "string", "description": "Der vollständige Text (250-400 Wörter); bei Hören ein natürlich gesprochener Monolog oder Dialog mit Sprechernamen" },
            "questions": { "type": "array", "minItems": 4, "maxItems": 7, "items": { "type": "object", "additionalProperties": false, "required": ["question", "options", "correctIndex"],
              "properties": { "question": { "type": "string" }, "options": { "type": "array", "minItems": 3, "maxItems": 4, "items": { "type": "string" } }, "correctIndex": { "type": "integer" } } } }
          }
        }
        """;

    private sealed record ReadingDto(string? Part, string? Instruction, string? Text, List<ReadingQuestionDto>? Questions);
    private sealed record ReadingQuestionDto(string Question, List<string> Options, int CorrectIndex);

    public async Task<Exercise?> GenerateReadingAsync(SkillNode node, CefrBand band, bool audioOnly, LearnerContext context, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(context) + "\n\nDu erstellst jetzt eine Prüfungsaufgabe im Format des Goethe-Zertifikats B2. Der Text muss authentisch klingen (Zeitungsartikel, Forumsbeiträge, Ansage, Interview), inhaltlich aktuell und für einen Berufstätigen in Deutschland relevant sein. Distraktoren müssen plausibel sein und dürfen nicht wörtlich im Text stehen.";
        var kind = audioOnly ? "HÖREN (wird per Sprachausgabe vorgelesen, nicht gezeigt)" : "LESEN";
        var user = $"Aufgabentyp: {kind}. Knoten: {node.Title} – {node.Description}. Niveau {band.Label()}. " +
                   (audioOnly ? "Wähle Teil 1 (Alltagsansage/Telefonat), Teil 2 (Interview), Teil 3 (Diskussion: Moderator und zwei Gäste mit gegensätzlichen Positionen – wer sagt was?) oder Teil 4 (Radiobeitrag/Vortrag). Bei Gesprächen beginnt jeder Redebeitrag auf einer neuen Zeile mit Sprechernamen und Doppelpunkt („Moderator: …“) – vorgelesen wird mit einer Stimme je Sprecher, der Name selbst nicht." : "Wähle Teil 1 (vier Meinungen, Zuordnung), Teil 3 (Artikel, Multiple Choice) oder Teil 5 (Regelwerk, Zuordnung).") +
                   " Liefere Text und 4–6 Fragen mit genau einer richtigen Antwort.";
        var json = await CompleteJsonAsync(system, user, "leseaufgabe", ReadingSchema, ct);
        var dto = JsonSerializer.Deserialize<ReadingDto>(json, ContentLoader.Options);
        if (dto?.Questions is null || dto.Questions.Count == 0 || string.IsNullOrWhiteSpace(dto.Text)) return null;

        var ex = new Exercise
        {
            Id = NewId(node.Id),
            Type = ExerciseType.Reading,
            NodeId = node.Id,
            Band = band,
            Context = ExerciseContext.Pruefung,
            Source = ExerciseSource.Generated,
            Prompt = $"{dto.Part}: {dto.Instruction}".Trim(':', ' '),
            Text = dto.Text,
            AudioOnly = audioOnly,
            Questions = dto.Questions.Where(q => q.Options is { Count: >= 2 }).Select(q => new ReadingQuestion(q.Question, q.Options, q.CorrectIndex)).ToList(),
            Tags = ["generiert"],
        };
        return ExerciseValidator.Validate(ex).Count == 0 ? ex : null;
    }

    // ------------------------------------------------------------------------------------------
    // Discussion partner
    // ------------------------------------------------------------------------------------------

    public Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(context) + $$"""


            Du bist jetzt Prüfungspartner in „Sprechen Teil 2“ (Diskussion) des Goethe-Zertifikats B2.
            These: „{{thesis}}“
            Verhalte dich wie ein echter Gesprächspartner auf B2-Niveau: vertritt eine klare Gegenposition zum Lerner, bring pro Redebeitrag EIN Argument oder EINE Nachfrage, maximal 3–4 Sätze, sprich den Lerner mit „Sie“ an.
            Am Ende jedes deiner Beiträge hänge – getrennt durch eine Leerzeile und beginnend mit „✎ “ – höchstens EINE knappe Korrektur des letzten Lernerbeitrags an (Fehler → richtig), oder „✎ Sprachlich gut.“ Keine weiteren Meta-Kommentare.
            Wenn der Lerner nach etwa fünf Wechseln keinen Kompromiss vorschlägt, steuere selbst auf einen Kompromiss zu.
            """;
        var turns = new List<ChatTurn>();
        if (history.Count == 0 || !history[0].FromLearner)
            turns.Add(new ChatTurn(true, "Bitte eröffnen Sie die Diskussion mit Ihrer Position zur These."));
        turns.AddRange(history);
        return CompleteChatAsync(system, turns, ct);
    }

    // ------------------------------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------------------------------

    protected static string BuildSystemPrompt(LearnerContext context)
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

    private static string NewId(string nodeId) => $"gen.{nodeId.ToLowerInvariant()}.{Guid.NewGuid():N}"[..40];

    /// <summary>Models without native JSON mode wrap the object in prose or code fences; take the outermost object.</summary>
    public static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Leere Antwort vom Modell.");
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("Antwort enthält kein JSON-Objekt: " + text[..Math.Min(text.Length, 200)]);
        return text[start..(end + 1)];
    }
}
