using Lotse.Core.Model;

namespace Lotse.Core.Tutor;

public enum ProductionMode
{
    Writing,
    Speaking,
}

public sealed record RubricScore(string Criterion, int Score, string Comment)
{
    /// <summary>Scores follow the exam scale 0–5; 3 is the pass threshold.</summary>
    public const int Max = 5;
}

public sealed record TutorError(string Code, string Snippet, string Correction, string Explanation);

/// <summary>Structured feedback on a piece of writing or a speaking transcript.</summary>
public sealed record ProductionEvaluation(
    /// <summary>0..1, derived from the rubric.</summary>
    double OverallScore,
    /// <summary>The tutor's estimate of the level this text demonstrates, e.g. "B1.2" or "B2.1".</summary>
    string EstimatedLevel,
    IReadOnlyList<RubricScore> Rubric,
    IReadOnlyList<TutorError> Errors,
    string CorrectedText,
    /// <summary>Coach message in German: what was good, the one or two things to fix next.</summary>
    string Feedback,
    /// <summary>Node ids the tutor recommends focusing on next.</summary>
    IReadOnlyList<string> SuggestedNodeIds,
    /// <summary>Two or three upgraded phrasings the learner can adopt ("So klingt es auf B2").</summary>
    IReadOnlyList<string> Upgrades);

/// <summary>What the tutor knows about the learner when evaluating or generating.</summary>
public sealed record LearnerContext(
    string NativeLanguage,
    IReadOnlyList<string> WeakNodeTitles,
    IReadOnlyList<string> RecentErrorCodes,
    IReadOnlyList<ErrorType> ErrorCatalogue,
    string? Occupation = null);

public sealed record ChatTurn(bool FromLearner, string Text);

/// <summary>
/// The AI tutor. The application works without it (deterministic checking, self-check rubrics, seed content);
/// with it, free production gets rubric-based feedback with tagged errors, the item bank grows on demand
/// and the learner gets a discussion partner for the speaking exam.
/// </summary>
public interface ITutor
{
    bool IsAvailable { get; }
    string Description { get; }

    Task<ProductionEvaluation> EvaluateAsync(Exercise exercise, string learnerText, ProductionMode mode, LearnerContext context, CancellationToken ct = default);

    Task<IReadOnlyList<Exercise>> GenerateExercisesAsync(SkillNode node, CefrBand band, int count, ExerciseContext exerciseContext, LearnerContext context, IReadOnlyList<Exercise> examples, CancellationToken ct = default);

    /// <summary>One turn of a B2 discussion. The tutor plays the exam partner and gently corrects at the end of its turn.</summary>
    Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default);

    /// <summary>A reading (or, when <paramref name="audioOnly"/>, listening) task in Goethe B2 part format for a Lesen/Hören node.</summary>
    Task<Exercise?> GenerateReadingAsync(SkillNode node, CefrBand band, bool audioOnly, LearnerContext context, CancellationToken ct = default);
}

/// <summary>Used when no API key is configured. Never throws; the UI falls back to self-checks.</summary>
public sealed class NullTutor : ITutor
{
    public bool IsAvailable => false;
    public string Description => "Kein KI-Tutor konfiguriert (ANTHROPIC_API_KEY fehlt). Schreiben/Sprechen werden per Selbstcheck bewertet.";

    public Task<ProductionEvaluation> EvaluateAsync(Exercise exercise, string learnerText, ProductionMode mode, LearnerContext context, CancellationToken ct = default)
        => throw new InvalidOperationException("Kein KI-Tutor konfiguriert.");

    public Task<IReadOnlyList<Exercise>> GenerateExercisesAsync(SkillNode node, CefrBand band, int count, ExerciseContext exerciseContext, LearnerContext context, IReadOnlyList<Exercise> examples, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Exercise>>([]);

    public Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
        => throw new InvalidOperationException("Kein KI-Tutor konfiguriert.");

    public Task<Exercise?> GenerateReadingAsync(SkillNode node, CefrBand band, bool audioOnly, LearnerContext context, CancellationToken ct = default)
        => Task.FromResult<Exercise?>(null);
}
