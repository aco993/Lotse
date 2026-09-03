namespace Lotse.Core.Model;

/// <summary>One row of a grammar explanation: German example, Serbian equivalent, optional note.</summary>
public sealed record ExampleRow(string German, string Serbian, string? Note = null);

/// <summary>The "Erklärung" block of a lesson – short, example-driven, contrastive.</summary>
public sealed record LessonExplanation(string Title, string NodeId, string Text, IReadOnlyList<ExampleRow> Examples);

/// <summary>
/// A lesson ("Lektion") of the course: a situation from work or everyday life told as a dialogue, one grammar focus
/// explained with examples, a sequence of interactive steps, and a production task. Lessons reference exercises by id;
/// the exercises live in the normal bank so the adaptive planner can reuse them.
/// </summary>
public sealed record Lesson(
    string Id,
    int Order,
    string Title,
    string Subtitle,
    /// <summary>Beruf | Alltag | Pruefung – drives colour and icon.</summary>
    ExerciseContext Theme,
    CefrBand Band,
    /// <summary>Key of an illustration scene (see LessonArt): team, email, meeting, customer, office, home, doctor, server, debate, weekend, salary, exam.</summary>
    string Scene,
    /// <summary>Two sentences that set the situation before the dialogue.</summary>
    string Intro,
    IReadOnlyList<DialogueLine> Story,
    LessonExplanation Grammar,
    /// <summary>Exercise ids in order; the runner mixes them with a short recap of the story.</summary>
    IReadOnlyList<string> Steps,
    string? ProductionExerciseId,
    /// <summary>One sentence to remember – shown at the end and on the lesson card.</summary>
    string Merksatz,
    int Minutes,
    /// <summary>Node ids the lesson trains; used for the recommendation "which lesson next".</summary>
    IReadOnlyList<string> NodeIds)
{
    public IEnumerable<string> AllExerciseIds => ProductionExerciseId is null ? Steps : Steps.Append(ProductionExerciseId);
}
