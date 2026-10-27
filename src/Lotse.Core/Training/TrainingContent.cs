using Lotse.Core.Model;

namespace Lotse.Core.Training;

/// <summary>
/// Why an answer was wrong - the learner's own diagnosis, not the checker's. The four kinds call for four different
/// remedies: a rule (<see cref="A"/>) has to be explained, a known rule (<see cref="B"/>) has to be drilled until it
/// is automatic, a collocation (<see cref="K"/>) has to be learned as a chunk, and a slip (<see cref="S"/>) needs
/// nothing but attention.
/// </summary>
public enum ErrorKind
{
    /// <summary>Regel unbekannt.</summary>
    A,
    /// <summary>Regel bekannt, aber nicht automatisiert.</summary>
    B,
    /// <summary>Kollokation / Wortwahl.</summary>
    K,
    /// <summary>Flüchtigkeitsfehler.</summary>
    S,
}

public static class ErrorKindExtensions
{
    public static string Label(this ErrorKind kind) => kind switch
    {
        ErrorKind.A => "Regel unbekannt",
        ErrorKind.B => "Regel bekannt, nicht automatisiert",
        ErrorKind.K => "Kollokation / Wort",
        _ => "Flüchtigkeitsfehler",
    };
}

/// <summary>The three parts of a training session after the diagnostic, in this fixed order.</summary>
public enum TrainingBlock
{
    /// <summary>Session 1: the diagnostic test.</summary>
    Diagnose,
    /// <summary>Old mistakes, asked again in a sentence the learner has not got wrong before.</summary>
    Wiederholung,
    /// <summary>One weak topic, from controlled to free: gap, transformation, own sentence, short text.</summary>
    Schwerpunkt,
    /// <summary>A spoken (or dictated) answer to a work situation.</summary>
    Sprechen,
}

public enum TrainingStage
{
    Lueckentext,
    Umformung,
    EigenerSatz,
    KurzerText,
}

public static class TrainingStageExtensions
{
    public static string Label(this TrainingStage stage) => stage switch
    {
        TrainingStage.Lueckentext => "Lückentext",
        TrainingStage.Umformung => "Umformung",
        TrainingStage.EigenerSatz => "Eigener Satz",
        _ => "Kurzer Text",
    };
}

/// <summary>One sentence that tests a <see cref="TrainingPoint"/>. A point has several, so it can come back in a new context.</summary>
public sealed record TrainingVariant(
    string Id,
    string Prompt,
    string? Instruction,
    IReadOnlyList<string> Answers,
    string? Explanation = null);

/// <summary>
/// One thing to learn ("Lernpunkt"): a single rule or collocation, e.g. "abhängen von → davon ab". The first variant
/// is the one the diagnostic asks; the others are the new contexts a review uses.
/// </summary>
public sealed record TrainingPoint(
    string Id,
    string TopicId,
    string NodeId,
    string ErrorCode,
    string Title,
    /// <summary>What usually goes wrong, shown with the solution.</summary>
    string Trap,
    IReadOnlyList<TrainingVariant> Variants,
    /// <summary>Default diagnosis offered when the answer is wrong, e.g. K for collocations. Null = no default.</summary>
    ErrorKind? SuggestedKind = null);

/// <summary>A free task: an own sentence, a short text or a spoken answer. Checked against a list, not a key.</summary>
public sealed record TrainingTask(
    string Id,
    string NodeId,
    string Prompt,
    string? Instruction,
    IReadOnlyList<string> Checks,
    string? ModelAnswer,
    bool Speaking = false);

/// <summary>A focus topic of the Schwerpunkt block: its nodes and its three free tasks.</summary>
public sealed record TrainingTopic(
    string Id,
    string Title,
    IReadOnlyList<string> NodeIds,
    TrainingTask OwnSentence,
    TrainingTask ShortText,
    TrainingTask Speaking);

/// <summary>Session 1: the diagnostic, as point ids (asked with their first variant) and free task ids, in order.</summary>
public sealed record TrainingDiagnostic(string Title, IReadOnlyList<string> Items);

/// <summary>Everything in <c>content/training/</c>. Topics are listed in priority order: on a tie, the earlier one wins.</summary>
public sealed record TrainingContent(
    IReadOnlyList<TrainingTopic> Topics,
    IReadOnlyList<TrainingPoint> Points,
    IReadOnlyList<TrainingTask> Tasks,
    TrainingDiagnostic Diagnostic)
{
    public TrainingPoint? Point(string id) => Points.FirstOrDefault(p => p.Id == id);
    public TrainingTopic? Topic(string id) => Topics.FirstOrDefault(t => t.Id == id);

    public TrainingTask? Task(string id) => Tasks.FirstOrDefault(t => t.Id == id)
        ?? Topics.SelectMany(t => new[] { t.OwnSentence, t.ShortText, t.Speaking }).FirstOrDefault(t => t.Id == id);

    /// <summary>Problems that would make a session unusable: unknown nodes or codes, points without a second context, dangling ids.</summary>
    public IReadOnlyList<string> Validate(ContentCatalog catalog)
    {
        var problems = new List<string>();
        var nodeIds = catalog.Nodes.Select(n => n.Id).ToHashSet();
        var codes = catalog.ErrorTypes.Select(e => e.Code).ToHashSet();
        var topicIds = Topics.Select(t => t.Id).ToHashSet();

        foreach (var dup in Points.Select(p => p.Id).Concat(Tasks.Select(t => t.Id)).GroupBy(x => x).Where(g => g.Count() > 1))
            problems.Add($"Training: doppelte ID {dup.Key}");
        foreach (var t in Topics)
        {
            if (t.NodeIds.Count == 0) problems.Add($"Training-Thema {t.Id}: keine Knoten");
            problems.AddRange(t.NodeIds.Where(n => !nodeIds.Contains(n)).Select(n => $"Training-Thema {t.Id}: unbekannter Knoten {n}"));
            if (!t.Speaking.Speaking) problems.Add($"Training-Thema {t.Id}: Sprechaufgabe ist nicht als speaking markiert");
        }
        foreach (var p in Points)
        {
            if (!topicIds.Contains(p.TopicId)) problems.Add($"Lernpunkt {p.Id}: unbekanntes Thema {p.TopicId}");
            if (!nodeIds.Contains(p.NodeId)) problems.Add($"Lernpunkt {p.Id}: unbekannter Knoten {p.NodeId}");
            if (!codes.Contains(p.ErrorCode)) problems.Add($"Lernpunkt {p.Id}: unbekannter Fehlercode {p.ErrorCode}");
            // "Learned" needs a new context, so a point with one sentence could never be learned.
            if (p.Variants.Count < 2) problems.Add($"Lernpunkt {p.Id}: braucht mindestens zwei Varianten");
            if (p.Variants.Select(v => v.Id).Distinct().Count() != p.Variants.Count) problems.Add($"Lernpunkt {p.Id}: doppelte Varianten-ID");
            foreach (var v in p.Variants.Where(v => v.Answers.Count == 0 || v.Answers.Any(string.IsNullOrWhiteSpace)))
                problems.Add($"Lernpunkt {p.Id}/{v.Id}: Antworten fehlen");
        }
        foreach (var t in Tasks.Concat(Topics.SelectMany(t => new[] { t.OwnSentence, t.ShortText, t.Speaking })))
        {
            if (!nodeIds.Contains(t.NodeId)) problems.Add($"Aufgabe {t.Id}: unbekannter Knoten {t.NodeId}");
            if (t.Checks.Count == 0) problems.Add($"Aufgabe {t.Id}: Prüfpunkte fehlen");
        }
        foreach (var id in Diagnostic.Items.Where(id => Point(id) is null && Task(id) is null))
            problems.Add($"Diagnose: unbekannte ID {id}");
        return problems;
    }
}
