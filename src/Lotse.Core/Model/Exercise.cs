namespace Lotse.Core.Model;

public enum ExerciseType
{
    /// <summary>Gap fill with a typed answer. "Ich warte ___ den Bus."</summary>
    Cloze,
    /// <summary>One correct option out of several.</summary>
    MultipleChoice,
    /// <summary>Rewrite a sentence following an instruction (Aktiv → Passiv, Nebensatz, Konjunktiv II, ...).</summary>
    Transform,
    /// <summary>Translate a Serbian sentence into German. Production-heavy by design.</summary>
    Translate,
    /// <summary>Put shuffled chunks into the right order (word order / Satzklammer).</summary>
    WordOrder,
    /// <summary>Vocabulary card, always asked in the productive direction (Serbian → German).</summary>
    Vocab,
    /// <summary>Listen (TTS) and type what you heard.</summary>
    Dictation,
    /// <summary>Free writing task with a rubric; evaluated by the AI tutor or by a self-check.</summary>
    FreeWrite,
    /// <summary>Speaking task; the browser transcribes, the AI tutor (or a self-check) evaluates.</summary>
    Speak,
    /// <summary>Reading text with comprehension questions.</summary>
    Reading,
}

/// <summary>Context the exercise is embedded in. Used to balance everyday, professional and exam-specific language.</summary>
public enum ExerciseContext
{
    Alltag,
    Beruf,
    Pruefung,
}

public enum ExerciseSource
{
    Seed,
    Generated,
}

/// <summary>A comprehension question attached to a <see cref="ExerciseType.Reading"/> exercise.</summary>
public sealed record ReadingQuestion(string Question, IReadOnlyList<string> Options, int CorrectIndex);

/// <summary>
/// One exercise ("Aufgabe"). A single flat shape for all types keeps the JSON content files and the database simple;
/// <see cref="ExerciseValidator"/> enforces which fields each type needs.
/// </summary>
public sealed record Exercise
{
    public required string Id { get; init; }
    public required ExerciseType Type { get; init; }
    public required string NodeId { get; init; }
    public required CefrBand Band { get; init; }
    public ExerciseContext Context { get; init; } = ExerciseContext.Alltag;
    public ExerciseSource Source { get; init; } = ExerciseSource.Seed;

    /// <summary>What the learner sees: the sentence with a gap, the Serbian sentence, the writing prompt, ...</summary>
    public required string Prompt { get; init; }
    /// <summary>Task instruction, e.g. "Setzen Sie den Satz ins Passiv."</summary>
    public string? Instruction { get; init; }
    /// <summary>Optional hint shown on request (costs a little score).</summary>
    public string? Hint { get; init; }

    /// <summary>Accepted answers for typed exercises. The first one is the model answer shown as feedback.</summary>
    public IReadOnlyList<string> Answers { get; init; } = [];
    /// <summary>Options for MultipleChoice (with <see cref="CorrectIndex"/>) or the chunks for WordOrder (answer is the ordered list).</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
    public int? CorrectIndex { get; init; }

    /// <summary>Explanation shown after answering, in German.</summary>
    public string? Explanation { get; init; }
    /// <summary>Contrastive note for Serbian speakers, shown when helpful.</summary>
    public string? SerbianNote { get; init; }

    // Vocab-specific
    public string? Lemma { get; init; }
    public string? Article { get; init; }
    public string? Plural { get; init; }
    public string? ExampleDe { get; init; }

    // Production-specific (FreeWrite / Speak)
    public int? MinWords { get; init; }
    public int? TargetSeconds { get; init; }
    public IReadOnlyList<string> Rubric { get; init; } = [];
    public string? ModelAnswer { get; init; }

    // Reading / listening
    /// <summary>The passage (Reading) or the sentence/monologue to be spoken by TTS (Dictation, audio-only Reading).</summary>
    public string? Text { get; init; }
    /// <summary>When true the text is only played (Hören), never shown.</summary>
    public bool AudioOnly { get; init; }
    public IReadOnlyList<ReadingQuestion> Questions { get; init; } = [];

    /// <summary>Free-form tags (e.g. "email", "beschwerde", "smalltalk") used for variety and later filtering.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool IsProduction => Type is ExerciseType.FreeWrite or ExerciseType.Speak;
    public bool IsReceptive => Type is ExerciseType.Reading or ExerciseType.Dictation;

    /// <summary>Rough time the exercise takes, used by the session planner to fill a time budget.</summary>
    public int EstimatedSeconds => Type switch
    {
        ExerciseType.Vocab => 15,
        ExerciseType.MultipleChoice => 20,
        ExerciseType.Cloze => 25,
        ExerciseType.WordOrder => 35,
        ExerciseType.Transform => 40,
        ExerciseType.Translate => 45,
        ExerciseType.Dictation => 40,
        ExerciseType.Reading => 300,
        ExerciseType.Speak => TargetSeconds is > 0 ? TargetSeconds.Value + 60 : 180,
        ExerciseType.FreeWrite => MinWords is > 0 ? Math.Clamp(MinWords.Value * 4, 120, 900) : 300,
        _ => 30,
    };
}

/// <summary>Validates exercises from content files and from the AI generator before they enter the item bank.</summary>
public static class ExerciseValidator
{
    public static IReadOnlyList<string> Validate(Exercise e, ISet<string>? knownNodeIds = null)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Id)) problems.Add("Id fehlt");
        if (string.IsNullOrWhiteSpace(e.Prompt)) problems.Add($"{e.Id}: Prompt fehlt");
        if (string.IsNullOrWhiteSpace(e.NodeId)) problems.Add($"{e.Id}: NodeId fehlt");
        else if (knownNodeIds is not null && !knownNodeIds.Contains(e.NodeId)) problems.Add($"{e.Id}: unbekannter Knoten '{e.NodeId}'");

        switch (e.Type)
        {
            case ExerciseType.Cloze:
            case ExerciseType.Transform:
            case ExerciseType.Translate:
            case ExerciseType.Dictation:
                if (e.Answers.Count == 0 || e.Answers.Any(string.IsNullOrWhiteSpace)) problems.Add($"{e.Id}: Answers fehlen");
                break;
            case ExerciseType.Vocab:
                if (e.Answers.Count == 0) problems.Add($"{e.Id}: Answers (deutsche Form) fehlen");
                if (string.IsNullOrWhiteSpace(e.Lemma)) problems.Add($"{e.Id}: Lemma fehlt");
                break;
            case ExerciseType.MultipleChoice:
                if (e.Options.Count < 2) problems.Add($"{e.Id}: mindestens 2 Optionen nötig");
                if (e.CorrectIndex is null || e.CorrectIndex < 0 || e.CorrectIndex >= e.Options.Count) problems.Add($"{e.Id}: CorrectIndex ungültig");
                break;
            case ExerciseType.WordOrder:
                if (e.Options.Count < 3) problems.Add($"{e.Id}: mindestens 3 Bausteine nötig");
                if (e.Answers.Count == 0) problems.Add($"{e.Id}: Answers (korrekte Reihenfolge als Satz) fehlen");
                break;
            case ExerciseType.FreeWrite:
                if (e.MinWords is null or <= 0) problems.Add($"{e.Id}: MinWords fehlt");
                if (e.Rubric.Count == 0) problems.Add($"{e.Id}: Rubric fehlt");
                break;
            case ExerciseType.Speak:
                if (e.Rubric.Count == 0) problems.Add($"{e.Id}: Rubric fehlt");
                break;
            case ExerciseType.Reading:
                if (string.IsNullOrWhiteSpace(e.Text)) problems.Add($"{e.Id}: Text fehlt");
                if (e.Questions.Count == 0) problems.Add($"{e.Id}: Questions fehlen");
                foreach (var q in e.Questions)
                {
                    if (q.Options.Count < 2 || q.CorrectIndex < 0 || q.CorrectIndex >= q.Options.Count)
                        problems.Add($"{e.Id}: Frage '{q.Question}' ungültig");
                }
                break;
        }
        return problems;
    }
}
