using System.Text;
using System.Text.Json;
using Lotse.Core.Model;
using Lotse.Core.Telc;
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
            .AppendLine("Jeder gemeldete Fehler muss im Feld snippet WÖRTLICH aus dem Text des Lerners zitiert sein. Melde nichts, was du nicht zitieren kannst.")
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
        errors = QuotedErrors.Filter(errors, learnerText, out var unquoted).ToList();
        if (unquoted > 0) Logger.LogInformation("Tutor meldete {Count} Fehler, die nicht wörtlich im Text stehen, verworfen.", unquoted);

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
    // telc Deutsch B2: rating on telc's own criteria, and the exam partner
    // ------------------------------------------------------------------------------------------

    protected const string TelcRatingSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["themaVerfehlt", "criteria", "errors", "feedback", "upgrades"],
          "properties": {
            "themaVerfehlt": { "type": "boolean", "description": "Nur true, wenn der Text mit der Aufgabe keine oder kaum eine Verbindung hat" },
            "criteria": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["criterion", "band", "comment"],
              "properties": { "criterion": { "type": "string" }, "band": { "type": "string", "enum": ["A", "B", "C", "D"] }, "comment": { "type": "string", "description": "1-2 Sätze Begründung, mit einem Zitat aus dem Text" } } } },
            "errors": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["code", "snippet", "correction", "explanation"],
              "properties": { "code": { "type": "string", "description": "Fehlercode aus dem Katalog" }, "snippet": { "type": "string", "description": "WÖRTLICHES Zitat aus dem Text des Lerners" }, "correction": { "type": "string" }, "explanation": { "type": "string" } } } },
            "feedback": { "type": "string", "description": "Auf Deutsch: was die nächsthöhere Stufe bringen würde, konkret und kurz" },
            "upgrades": { "type": "array", "items": { "type": "string" }, "description": "2-3 Sätze des Lerners in besserer B2-Qualität" }
          }
        }
        """;

    private sealed record TelcRatingDto(bool ThemaVerfehlt, List<TelcCriterionDto>? Criteria, List<ErrorDto>? Errors, string? Feedback, List<string>? Upgrades);
    private sealed record TelcCriterionDto(string? Criterion, string? Band, string? Comment);

    public async Task<TelcAiRating> RateTelcAsync(TelcRatingRequest request, LearnerContext context, CancellationToken ct = default)
    {
        var criteria = request.Speaking ? TelcSpeakingRating.Criteria : TelcWritingRating.Criteria;
        var system = BuildSystemPrompt(context) + "\n\n" + TelcRaterBrief(request.Part);
        var user = new StringBuilder()
            .AppendLine($"PRÜFUNGSTEIL: {TelcBlueprint.Info(request.Part).Title}")
            .AppendLine("AUFGABE:").AppendLine(request.Task.Trim()).AppendLine()
            .AppendLine($"KRITERIEN - gib GENAU diese {criteria.Count} in dieser Reihenfolge, jeweils Band A, B, C oder D:")
            .AppendJoin("\n", criteria.Select(c => $"- {c.Title}: {c.Question}")).AppendLine().AppendLine();
        if (request.Conversation is { Length: > 0 } conversation)
            user.AppendLine("GESPRÄCHSVERLAUF (nur zum Verständnis; bewertet wird nur der Lerner):").AppendLine(conversation.Trim()).AppendLine();
        user.AppendLine(request.Speaking
                ? "BEITRÄGE DES LERNERS (automatisches Transkript - Satzzeichen und Groß-/Kleinschreibung nicht bewerten; Aussprache nur bewerten, wenn das Transkript Hinweise gibt, sonst B):"
                : "TEXT DES LERNERS:")
            .AppendLine(request.LearnerText.Trim());

        var json = await CompleteJsonAsync(system, user.ToString(), "telc_bewertung", TelcRatingSchema, ct);
        var dto = JsonSerializer.Deserialize<TelcRatingDto>(json, ContentLoader.Options) ?? throw new InvalidOperationException("Leere Bewertung vom Tutor.");

        var rated = dto.Criteria ?? [];
        var bands = criteria.Select((c, i) =>
        {
            var r = i < rated.Count ? rated[i] : null;
            var band = r?.Band is { } b && Enum.TryParse<TelcBand>(b, ignoreCase: true, out var parsed) ? parsed : TelcBand.C;
            return new TelcCriterionRating(c.Title, band, r?.Comment ?? "");
        }).ToList();

        var knownCodes = context.ErrorCatalogue.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
        var errors = (dto.Errors ?? [])
            .Where(e => e.Code is not null && knownCodes.Contains(e.Code))
            .Select(e => new TutorError(e.Code, e.Snippet ?? "", e.Correction ?? "", e.Explanation ?? ""));
        var quoted = QuotedErrors.Filter(errors, request.LearnerText, out var unquoted);
        if (unquoted > 0) Logger.LogInformation("telc-Bewertung: {Count} nicht zitierbare Fehler verworfen.", unquoted);

        return new TelcAiRating(bands, quoted, dto.Feedback ?? "", dto.Upgrades ?? [], dto.ThemaVerfehlt && !request.Speaking, unquoted);
    }

    private static string TelcRaterBrief(TelcPart part) => TelcBlueprint.IsWritten(part)
        ? """
          Du bist jetzt lizenzierte Bewerterin für telc Deutsch B2, Schriftlicher Ausdruck. Bewerte streng nach den telc-Kriterien:
          I Aufgabenbewältigung: A = drei Leitpunkte (oder zwei und ein eigener Aspekt) angemessen bearbeitet, B = zwei, C = einer, D = keiner. Ein Leitpunkt braucht mehr als ein einziges Satzgefüge.
          II Kommunikative Gestaltung: Register, Verknüpfung, Wortschatzspektrum. Kein A ohne Betreff, Anrede und Schlussformel. Kein B, wenn das Register schwankt oder die Leitpunkte linear ohne Verknüpfung aufgelistet sind.
          III Formale Richtigkeit: A kaum Fehler, B wenige systematische Fehler, C mehrere systematische Fehler, aber verständlich, D viele elementare Fehler.
          „Thema verfehlt“ nur, wenn der Text mit dem Schreibanlass keine oder kaum eine Verbindung hat. B bedeutet „B2 erfüllt“, C bedeutet B1.
          Jeder gemeldete Fehler muss WÖRTLICH aus dem Text zitiert sein - melde nichts, was du nicht zitieren kannst.
          """
        : """
          Du bist jetzt lizenzierte Prüferin für telc Deutsch B2, Mündlicher Ausdruck. Bewerte nach den vier telc-Kriterien:
          Ausdrucksfähigkeit (inhalts- und rollenbezogen, Vielfalt, Absicht kommt an), Aufgabenbewältigung (Gesprächsbeteiligung, Strategien, Flüssigkeit),
          Formale Richtigkeit (Satzbau, Formen), Aussprache und Intonation. A = voll angemessen, B = im Großen und Ganzen, C = kaum noch akzeptabel, D = nicht ausreichend.
          Ein Beitrag aus nur wenigen Sätzen, oder einer, der nicht auf den Partner eingeht, kommt in der Aufgabenbewältigung nicht über C.
          Jeder gemeldete Fehler muss WÖRTLICH aus den Beiträgen des Lerners zitiert sein.
          """;

    public Task<string> PartnerAsync(TelcPart part, string scenario, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
    {
        var role = part switch
        {
            TelcPart.Sprechen1 => """
                Du bist Prüfungspartner in telc Deutsch B2, Mündlicher Ausdruck Teil 1 „Über Erfahrungen sprechen“. Der Lerner hat gerade über sein Thema erzählt.
                Stelle wie ein echter Partner EINE interessierte, konkrete Nachfrage pro Beitrag (höchstens zwei Sätze), die an etwas anknüpft, das er gesagt hat. Sprich ihn mit „Sie“ an.
                Nach drei Nachfragen erzählst du selbst kurz (vier bis fünf Sätze) von einer eigenen Erfahrung zu einem anderen Thema, damit der Lerner dir Fragen stellen kann.
                """,
            TelcPart.Sprechen2 => """
                Du bist Prüfungspartner in telc Deutsch B2, Mündlicher Ausdruck Teil 2 „Diskussion“ über den Zeitungstext unten.
                Vertritt eine erkennbar andere Position als der Lerner, bring pro Beitrag EIN Argument, eine Erfahrung oder eine Nachfrage, höchstens drei bis vier Sätze, sprich ihn mit „Sie“ an.
                Geh auf sein letztes Argument ein, bevor du deins bringst. Nach etwa fünf Wechseln steuere auf eine mögliche Lösung oder einen Kompromiss zu.
                """,
            _ => """
                Du bist Prüfungspartner in telc Deutsch B2, Mündlicher Ausdruck Teil 3 „Gemeinsam etwas planen“. Ihr duzt euch.
                Mach pro Beitrag höchstens EINEN Vorschlag oder reagiere auf den Vorschlag des Lerners (zustimmen, begründet ablehnen, ergänzen), höchstens drei Sätze.
                Lehne ab und zu einen Vorschlag mit Begründung ab, damit der Lerner verhandeln muss. Achte darauf, dass alle Planungspunkte drankommen, und fasse das Ergebnis zusammen, wenn alles geklärt ist.
                """,
        };
        var system = BuildSystemPrompt(context) + "\n\n" + role + $"\n\nAUFGABE:\n{scenario}\n\n"
            + "Am Ende jedes deiner Beiträge hänge - getrennt durch eine Leerzeile und beginnend mit „✎ “ - höchstens EINE knappe Korrektur des letzten Lernerbeitrags an (Fehler → richtig), oder „✎ Sprachlich gut.“ Keine weiteren Meta-Kommentare.";
        var turns = new List<ChatTurn>();
        if (history.Count == 0 || !history[0].FromLearner)
            turns.Add(new ChatTurn(true, part == TelcPart.Sprechen3 ? "Fang du bitte an." : "Bitte beginnen Sie."));
        turns.AddRange(history);
        return CompleteChatAsync(system, turns, ct);
    }

    // ------------------------------------------------------------------------------------------
    // Socratic hint: one question, never the answer
    // ------------------------------------------------------------------------------------------

    public Task<string> HintAsync(Exercise exercise, string? attempt, LearnerContext context, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(context) + """


            Du gibst jetzt einen sokratischen Hinweis zu einer Übung. Der Lerner soll die Lösung SELBST finden.
            Regeln, ohne Ausnahme:
            - Nenne NIEMALS die Lösung, kein Wort der Lösung, keine Endung, die sie verrät.
            - Stelle genau EINE Frage (höchstens zwei Sätze), die auf den nächsten Denkschritt führt: welche Regel, welcher Fall, welches Signalwort, wo im Satz.
            - Wenn ein Versuch des Lerners vorliegt, knüpfe an seinen Denkfehler an, ohne ihn zu korrigieren.
            - Nur die Frage, keine Einleitung, kein Lob.
            """;
        var user = new StringBuilder()
            .AppendLine($"ÜBUNG ({exercise.Type}): {exercise.Prompt}");
        if (exercise.Instruction is { } instruction) user.AppendLine($"Anweisung: {instruction}");
        if (exercise.Options.Count > 0 && exercise.Type == ExerciseType.MultipleChoice) user.AppendLine("Optionen: " + string.Join(" | ", exercise.Options));
        // The model needs the solution to aim at it; the caller's leak filter makes sure it never comes back.
        user.AppendLine("LÖSUNG (nur für dich, niemals nennen): " + (exercise.Type == ExerciseType.MultipleChoice && exercise.CorrectIndex is { } c ? exercise.Options[c] : string.Join(" | ", exercise.Answers)));
        if (!string.IsNullOrWhiteSpace(attempt)) user.AppendLine($"VERSUCH DES LERNERS: {attempt}");
        return CompleteChatAsync(system, [new ChatTurn(true, user.ToString())], ct);
    }

    // ------------------------------------------------------------------------------------------
    // telc task generation and critique
    // ------------------------------------------------------------------------------------------

    private const string NullableString = """{ "type": ["string", "null"] }""";

    /// <summary>Strict JSON schemas for the objective telc parts, mirroring the content records (ids are assigned by the caller).</summary>
    public static string TelcSchema(TelcPart part) => part switch
    {
        TelcPart.Lesen1 => """
            { "type": "object", "additionalProperties": false, "required": ["title", "headings", "texts", "solution"],
              "properties": {
                "title": { "type": "string" },
                "headings": { "type": "array", "items": { "type": "string" }, "description": "Genau 10 Überschriften" },
                "texts": { "type": "array", "items": { "type": "string" }, "description": "Genau 5 Texte, je 60-110 Wörter" },
                "solution": { "type": "array", "items": { "type": "integer" }, "description": "Für jeden Text der Index (0-9) seiner Überschrift, jede höchstens einmal" } } }
            """,
        TelcPart.Lesen2 => $$"""
            { "type": "object", "additionalProperties": false, "required": ["title", "text", "questions"],
              "properties": {
                "title": { "type": "string" },
                "text": { "type": "string", "description": "Ein Zeitungs- oder Magazintext, 450-600 Wörter" },
                "questions": { "type": "array", "description": "Genau 5 Aufgaben in Textreihenfolge", "items": { "type": "object", "additionalProperties": false, "required": ["question", "options", "correct", "explanation"],
                  "properties": { "question": { "type": "string" }, "options": { "type": "array", "items": { "type": "string" }, "description": "Genau 3 Optionen" }, "correct": { "type": "integer" }, "explanation": {{NullableString}} } } } } }
            """,
        TelcPart.Lesen3 => """
            { "type": "object", "additionalProperties": false, "required": ["title", "situations", "ads", "solution"],
              "properties": {
                "title": { "type": "string" },
                "situations": { "type": "array", "items": { "type": "string" }, "description": "Genau 10 Situationen: wer sucht was" },
                "ads": { "type": "array", "description": "Genau 12 Anzeigen", "items": { "type": "object", "additionalProperties": false, "required": ["title", "text"],
                  "properties": { "title": { "type": "string" }, "text": { "type": "string" } } } },
                "solution": { "type": "array", "items": { "type": "integer" }, "description": "Für jede Situation der Anzeigen-Index (0-11) oder -1, wenn keine passt; jede Anzeige höchstens einmal, genau eine bis zwei Situationen mit -1" } } }
            """,
        TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2 => $$"""
            { "type": "object", "additionalProperties": false, "required": ["title", "text", "gaps", "box"],
              "properties": {
                "title": { "type": "string" },
                "text": { "type": "string", "description": "Ein Brief oder eine E-Mail mit den Lücken [1] bis [10] im Text" },
                "gaps": { "type": "array", "description": "Genau 10 Lücken in Textreihenfolge", "items": { "type": "object", "additionalProperties": false, "required": ["correct", "nodeId", "options", "errorCode", "explanation"],
                  "properties": {
                    "correct": { "type": "integer", "description": "Teil 1: Index der richtigen Option (0-2). Teil 2: Index des richtigen Wortes im Kasten (0-14)" },
                    "nodeId": { "type": "string", "description": "Knoten-Id aus der Liste" },
                    "options": { "type": ["array", "null"], "items": { "type": "string" }, "description": "Teil 1: genau 3 Optionen. Teil 2: null" },
                    "errorCode": {{NullableString}},
                    "explanation": {{NullableString}} } } },
                "box": { "type": ["array", "null"], "items": { "type": "string" }, "description": "Teil 2: genau 15 verschiedene Wörter, davon 10 Lösungen. Teil 1: null" } } }
            """,
        TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3 => $$"""
            { "type": "object", "additionalProperties": false, "required": ["title", "recordings"],
              "properties": {
                "title": { "type": "string" },
                "recordings": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["audio", "label", "statements"],
                  "properties": {
                    "audio": { "type": "string", "description": "Der gesprochene Text; bei Gesprächen jede Zeile „Name: …“" },
                    "label": {{NullableString}},
                    "statements": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["text", "true", "explanation"],
                      "properties": { "text": { "type": "string" }, "true": { "type": "boolean" }, "explanation": {{NullableString}} } } } } } } } }
            """,
        _ => throw new ArgumentException($"{part} wird nicht generiert.", nameof(part)),
    };

    public Task<string> GenerateTelcAsync(TelcPart part, string brief, string exampleJson, LearnerContext context, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(context) + """


            Du schreibst jetzt eine NEUE Aufgabe im Format von telc Deutsch B2 - einen eigenen Text, keine Kopie veröffentlichter Prüfungsmaterialien.
            Der Text klingt authentisch (Zeitung, Forum, Anzeige, Radio, Durchsage, Brief) und ist für Erwachsene relevant, die in Deutschland leben und arbeiten.
            Jede Aufgabe hat GENAU EINE eindeutig richtige Lösung; Distraktoren sind plausibel, aber am Text klar widerlegbar.
            Halte das Format des Beispiels exakt ein: dieselbe Zahl an Elementen, dieselben Felder.
            """;
        var user = $"PRÜFUNGSTEIL: {TelcBlueprint.Info(part).Title} - {TelcBlueprint.Info(part).Format}\n\n{brief}\n\nBEISPIEL IM GEWÜNSCHTEN FORMAT (nicht kopieren, nur das Format übernehmen):\n{exampleJson}";
        return CompleteJsonAsync(system, user, "telc_aufgabe", TelcSchema(part), ct);
    }

    protected const string CritiqueSchema = """
        { "type": "object", "additionalProperties": false, "required": ["ok", "problems"],
          "properties": {
            "ok": { "type": "boolean", "description": "true nur, wenn jede Aufgabe genau eine richtige Lösung hat und das Deutsch fehlerfrei ist" },
            "problems": { "type": "array", "items": { "type": "string" }, "description": "Je Problem ein Satz mit Nummer der Aufgabe; leer, wenn ok" } } }
        """;

    private sealed record CritiqueDto(bool Ok, List<string>? Problems);

    public async Task<TaskCritique> CritiqueTelcAsync(TelcPart part, string taskWithKey, LearnerContext context, CancellationToken ct = default)
    {
        const string system = """
            Du bist Gutachterin für Prüfungsaufgaben (telc Deutsch B2). Prüfe die Aufgabe unten samt Lösungsschlüssel streng:
            1. Ist für jede Aufgabe die angegebene Lösung richtig - und ist sie die EINZIGE richtige?
            2. Lässt sich jede Lösung am Text belegen, ohne Weltwissen?
            3. Ist das Deutsch fehlerfrei und auf B2-Niveau?
            4. Passt das Format zum Prüfungsteil?
            Melde nur echte Probleme, jedes in einem Satz mit der Nummer der Aufgabe.
            """;
        var json = await CompleteJsonAsync(system, $"PRÜFUNGSTEIL: {TelcBlueprint.Info(part).Title}\n\n{taskWithKey}", "gutachten", CritiqueSchema, ct);
        var dto = JsonSerializer.Deserialize<CritiqueDto>(json, ContentLoader.Options) ?? throw new InvalidOperationException("Leeres Gutachten.");
        var problems = dto.Problems ?? [];
        return new TaskCritique(dto.Ok && problems.Count == 0, problems);
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
