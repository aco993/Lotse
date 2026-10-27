using Lotse.Core.Model;

namespace Lotse.Core.Training;

/// <summary>
/// One line of a training session as the learner sees it. Closed items carry <see cref="Answers"/>; free items
/// (own sentence, short text, speaking) carry <see cref="Checks"/> and a <see cref="ModelAnswer"/> instead.
/// Stored as JSON with the session, so a session reads the same after a content edit.
/// </summary>
public sealed record TrainingItem
{
    public required string Key { get; init; }
    public required TrainingBlock Block { get; init; }
    public TrainingStage? Stage { get; init; }
    public required string NodeId { get; init; }
    public string? ErrorCode { get; init; }
    public required string Prompt { get; init; }
    public string? Instruction { get; init; }
    public IReadOnlyList<string> Answers { get; init; } = [];
    public string? Explanation { get; init; }
    public string? Trap { get; init; }
    public string? PointId { get; init; }
    public string? VariantId { get; init; }
    /// <summary>Set when the item is a catalogue exercise (the Schwerpunkt's gap and transformation).</summary>
    public string? ExerciseId { get; init; }
    public string? TaskId { get; init; }
    public IReadOnlyList<string> Checks { get; init; } = [];
    public string? ModelAnswer { get; init; }
    public bool Speaking { get; init; }
    public ErrorKind? SuggestedKind { get; init; }
    /// <summary>Why this item is here, e.g. "Fehler aus Sitzung 1 – neuer Kontext".</summary>
    public string? Reason { get; init; }

    public bool IsFree => Answers.Count == 0;

    /// <summary>The shape <see cref="Engine.AnswerChecker"/> understands, so training answers get the same tolerance as everywhere else.</summary>
    public Exercise ToExercise() => new()
    {
        Id = ExerciseId ?? $"tr.{PointId}.{VariantId}",
        Type = ExerciseType.Transform,
        NodeId = NodeId,
        Band = CefrBand.B2_1,
        Prompt = Prompt,
        Answers = Answers,
    };
}

public sealed record TrainingPlan(IReadOnlyList<TrainingItem> Items, string? TopicId);

/// <summary>
/// Composes a training session. Session 1 is the diagnostic. Every later session has three blocks in a fixed order:
/// the open points in a new context, then one weak topic from controlled to free (gap → transformation → own
/// sentence → short text), then a speaking simulation on the same topic.
/// </summary>
public static class TrainingPlanner
{
    /// <summary>More than this many old mistakes in one go turns the review into a second test.</summary>
    public const int MaxReviewItems = 8;

    public static TrainingPlan Plan(
        int sessionNumber,
        TrainingContent content,
        ContentCatalog catalog,
        IReadOnlyList<PointAnswer> history,
        ISet<string>? recentExerciseIds = null,
        int seed = 0,
        Func<Exercise, Exercise>? render = null)
    {
        if (sessionNumber <= 1) return new TrainingPlan(Diagnostic(content), null);

        render ??= e => e;
        var rng = new Random(seed);
        var progress = LearnedRule.EvaluateAll(content.Points.Select(p => p.Id), history);
        var items = new List<TrainingItem>();

        // 1. Wiederholung: sure-and-wrong first, then the oldest mistakes.
        var due = progress.Where(p => p.NeedsReview)
            .OrderByDescending(p => p.Urgent)
            .ThenBy(p => p.LastMistakeSession)
            .ThenBy(p => p.PointId, StringComparer.Ordinal)
            .Take(MaxReviewItems)
            .ToList();
        foreach (var p in due)
        {
            var point = content.Point(p.PointId)!;
            var variant = PickVariant(point, p, history);
            var reason = p.Urgent
                ? $"Dringend: in Sitzung {p.LastMistakeSession} sicher und falsch – neuer Kontext"
                : $"Fehler aus Sitzung {p.LastMistakeSession} – neuer Kontext";
            items.Add(FromPoint(point, variant, TrainingBlock.Wiederholung, $"w{items.Count + 1}", reason));
        }

        // 2. Schwerpunkt: the topic with the most open points (sure-and-wrong count double); ties go to the earlier,
        //    higher-priority topic. Nothing open: rotate, so a clean slate still practises everything in turn.
        var weight = content.Topics.ToDictionary(t => t.Id, _ => 0);
        foreach (var p in progress.Where(p => p.NeedsReview))
            weight[content.Point(p.PointId)!.TopicId] += p.Urgent ? 2 : 1;
        var topic = weight.Values.Any(v => v > 0)
            ? content.Topics.OrderByDescending(t => weight[t.Id]).First()
            : content.Topics[(sessionNumber - 2) % content.Topics.Count];
        var openInTopic = weight[topic.Id];
        var topicReason = openInTopic > 0 ? $"Schwerpunkt {topic.Title}: {openInTopic} offene Punkte" : $"Schwerpunkt {topic.Title}: im Wechsel";

        var recent = recentExerciseIds ?? new HashSet<string>();
        var pool = catalog.Exercises
            .Where(e => topic.NodeIds.Contains(e.NodeId) && e.Band >= CefrBand.B1_2 && !recent.Contains(e.Id))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        Exercise? Pick(ExerciseType type, Exercise? not = null)
        {
            var candidates = pool.Where(e => e.Type == type && e != not).ToList();
            return candidates.Count == 0 ? null : candidates[rng.Next(candidates.Count)];
        }
        var gap = Pick(ExerciseType.Cloze);
        var transform = Pick(ExerciseType.Transform) ?? Pick(ExerciseType.Cloze, gap) ?? Pick(ExerciseType.Translate);
        if (gap is not null) items.Add(FromExercise(render(gap), TrainingStage.Lueckentext, "s1", topicReason));
        if (transform is not null) items.Add(FromExercise(render(transform), TrainingStage.Umformung, "s2", topicReason));
        items.Add(FromTask(topic.OwnSentence, TrainingBlock.Schwerpunkt, TrainingStage.EigenerSatz, "s3", topicReason));
        items.Add(FromTask(topic.ShortText, TrainingBlock.Schwerpunkt, TrainingStage.KurzerText, "s4", topicReason));

        // 3. Sprechsimulation on the same topic: the structure just drilled, now without time to look it up.
        items.Add(FromTask(topic.Speaking, TrainingBlock.Sprechen, null, "p1", $"Sprechen mit {topic.Title}"));

        return new TrainingPlan(items, topic.Id);
    }

    private static IReadOnlyList<TrainingItem> Diagnostic(TrainingContent content)
    {
        var items = new List<TrainingItem>();
        foreach (var id in content.Diagnostic.Items)
        {
            var key = $"d{items.Count + 1}";
            if (content.Point(id) is { } point) items.Add(FromPoint(point, point.Variants[0], TrainingBlock.Diagnose, key, null));
            else items.Add(FromTask(content.Task(id)!, TrainingBlock.Diagnose, null, key, null));
        }
        return items;
    }

    /// <summary>
    /// A variant the learner never got wrong - otherwise a right answer could be memory of the correction rather
    /// than the rule. Among those, one never asked before, then the one asked longest ago.
    /// </summary>
    private static TrainingVariant PickVariant(TrainingPoint point, PointProgress progress, IReadOnlyList<PointAnswer> history)
    {
        var lastAsked = history.Where(a => a.PointId == point.Id)
            .GroupBy(a => a.VariantId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.Session));
        var fresh = point.Variants.Where(v => !progress.FailedVariants.Contains(v.Id)).ToList();
        var candidates = fresh.Count > 0 ? fresh : point.Variants.ToList();
        return candidates.OrderBy(v => lastAsked.TryGetValue(v.Id, out var s) ? s : 0).ThenBy(v => point.Variants.ToList().IndexOf(v)).First();
    }

    private static TrainingItem FromPoint(TrainingPoint p, TrainingVariant v, TrainingBlock block, string key, string? reason) => new()
    {
        Key = key,
        Block = block,
        NodeId = p.NodeId,
        ErrorCode = p.ErrorCode,
        Prompt = v.Prompt,
        Instruction = v.Instruction,
        Answers = v.Answers,
        Explanation = v.Explanation,
        Trap = p.Trap,
        PointId = p.Id,
        VariantId = v.Id,
        SuggestedKind = p.SuggestedKind,
        Reason = reason,
    };

    private static TrainingItem FromExercise(Exercise e, TrainingStage stage, string key, string reason) => new()
    {
        Key = key,
        Block = TrainingBlock.Schwerpunkt,
        Stage = stage,
        NodeId = e.NodeId,
        Prompt = e.Prompt,
        Instruction = e.Instruction ?? (e.Type == ExerciseType.Cloze ? "Ergänzen Sie die Lücke." : null),
        Answers = e.Answers,
        Explanation = e.Explanation,
        ExerciseId = e.Id,
        Reason = reason,
    };

    private static TrainingItem FromTask(TrainingTask t, TrainingBlock block, TrainingStage? stage, string key, string? reason) => new()
    {
        Key = key,
        Block = block,
        Stage = stage,
        NodeId = t.NodeId,
        Prompt = t.Prompt,
        Instruction = t.Instruction,
        Checks = t.Checks,
        ModelAnswer = t.ModelAnswer,
        Speaking = t.Speaking,
        TaskId = t.Id,
        Reason = reason,
    };
}
