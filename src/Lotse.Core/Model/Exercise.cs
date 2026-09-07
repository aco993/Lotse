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
    /// <summary>Interactive dialogue: the learner plays one role and picks the best line at each of their turns.</summary>
    Dialogue,
    /// <summary>A sentence with exactly one wrong word; the learner taps it and sees the correction.</summary>
    SpotError,
    /// <summary>Pairs to match (word ↔ meaning, Serbian ↔ German, phrase ↔ situation).</summary>
    Match,
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
/// One line of a dialogue. Lines without <see cref="Options"/> are spoken by the story; lines with options are the
/// learner's turn: exactly one option is the best reply, <see cref="Feedback"/> explains each option.
/// </summary>
public sealed record DialogueLine(
    string Speaker,
    string Text,
    IReadOnlyList<string>? Options = null,
    int? CorrectIndex = null,
    IReadOnlyList<string>? Feedback = null)
{
    public bool IsLearnerTurn => Options is { Count: > 0 };

    /// <summary>
    /// What a voice reads for this line: the sentence itself, never the speaker label. Who is talking is already
    /// carried twice - the name stands beside the line on screen, and each character has their own voice - so
    /// announcing "Sabine:" before every turn only interrupts the German the learner is here to hear.
    /// </summary>
    public string SpokenText => IsLearnerTurn && CorrectIndex is { } i && i >= 0 && i < Options!.Count
        ? Options[i]
        : Text;
}

public sealed record MatchPair(string Left, string Right);

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
    /// <summary>
    /// The same note written for English speakers. Deliberately not a translation of <see cref="SerbianNote"/>:
    /// "sa + instrumental → mit + Dativ" says nothing to someone whose first language has no cases, so the contrast
    /// has to be made against English instead.
    /// </summary>
    public string? EnglishNote { get; init; }
    /// <summary>
    /// The prompt in English, for the two types whose prompt IS the helper language (Vocab, Translate). Everything
    /// else has a German prompt and leaves this null. A content test asserts no Vocab/Translate item lacks it.
    /// </summary>
    public string? PromptEn { get; init; }

    /// <summary>
    /// The contrastive note in the learner's helper language, or null. No fallback on purpose: showing a Serbian
    /// note to someone who chose English is worse than showing none, and the note is an extra, never the task.
    /// </summary>
    public string? NoteFor(HelperLanguage lang) => lang == HelperLanguage.English ? EnglishNote : SerbianNote;

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

    // Dialogue / Match
    public IReadOnlyList<DialogueLine> Lines { get; init; } = [];
    public IReadOnlyList<MatchPair> Pairs { get; init; } = [];

    /// <summary>Free-form tags (e.g. "email", "beschwerde", "smalltalk") used for variety and later filtering.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool IsProduction => Type is ExerciseType.FreeWrite or ExerciseType.Speak;
    public bool IsReceptive => Type is ExerciseType.Reading or ExerciseType.Dictation;
    /// <summary>Multi-part tasks scored as a fraction rather than right/wrong.</summary>
    public bool IsComposite => Type is ExerciseType.Reading or ExerciseType.Dialogue or ExerciseType.Match;

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
        ExerciseType.Dialogue => 30 + 25 * Lines.Count(l => l.IsLearnerTurn),
        ExerciseType.SpotError => 30,
        ExerciseType.Match => 15 + 8 * Pairs.Count,
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
            case ExerciseType.Dialogue:
                if (e.Lines.Count < 2) problems.Add($"{e.Id}: Dialog braucht mindestens 2 Zeilen");
                if (!e.Lines.Any(l => l.IsLearnerTurn)) problems.Add($"{e.Id}: Dialog ohne Lerner-Zug");
                foreach (var l in e.Lines.Where(l => l.IsLearnerTurn))
                {
                    if (l.CorrectIndex is null || l.CorrectIndex < 0 || l.CorrectIndex >= l.Options!.Count) problems.Add($"{e.Id}: Dialogzug „{l.Text}“ hat keinen gültigen CorrectIndex");
                    if (l.Feedback is not null && l.Feedback.Count != l.Options!.Count) problems.Add($"{e.Id}: Feedback-Anzahl passt nicht zu den Optionen");
                }
                break;
            case ExerciseType.SpotError:
                if (e.Options.Count < 3) problems.Add($"{e.Id}: SpotError braucht die Satzwörter als Options");
                if (e.CorrectIndex is null || e.CorrectIndex < 0 || e.CorrectIndex >= e.Options.Count) problems.Add($"{e.Id}: CorrectIndex (falsches Wort) ungültig");
                if (e.Answers.Count == 0) problems.Add($"{e.Id}: Answers (korrigiertes Wort) fehlen");
                break;
            case ExerciseType.Match:
                if (e.Pairs.Count < 3) problems.Add($"{e.Id}: Match braucht mindestens 3 Paare");
                if (e.Pairs.Select(p => p.Right).Distinct().Count() != e.Pairs.Count) problems.Add($"{e.Id}: rechte Seiten müssen eindeutig sein");
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
