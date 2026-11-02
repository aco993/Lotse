using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Core.Tutor;

namespace Lotse.Core.Tests;

/// <summary>A tutor whose answers a test sets in advance - for the pipeline, the hint guard and the eval harness.</summary>
public sealed class ScriptedTutor : ITutor
{
    public bool IsAvailable => true;
    public string Description => "Skript";

    public Func<TelcPart, string> Telc { get; set; } = _ => throw new NotSupportedException();
    public Func<string, TaskCritique> Critique { get; set; } = _ => new TaskCritique(true, []);
    public Func<Exercise, string> Hint { get; set; } = _ => "Welche Regel gilt hier?";
    public Func<TelcRatingRequest, TelcAiRating> Rate { get; set; } = _ => throw new NotSupportedException();

    public Task<string> GenerateTelcAsync(TelcPart part, string brief, string exampleJson, LearnerContext context, CancellationToken ct = default)
        => Task.FromResult(Telc(part));

    public Task<TaskCritique> CritiqueTelcAsync(TelcPart part, string taskWithKey, LearnerContext context, CancellationToken ct = default)
        => Task.FromResult(Critique(taskWithKey));

    public Task<string> HintAsync(Exercise exercise, string? attempt, LearnerContext context, CancellationToken ct = default)
        => Task.FromResult(Hint(exercise));

    public Task<TelcAiRating> RateTelcAsync(TelcRatingRequest request, LearnerContext context, CancellationToken ct = default)
        => Task.FromResult(Rate(request));

    public Task<ProductionEvaluation> EvaluateAsync(Exercise exercise, string learnerText, ProductionMode mode, LearnerContext context, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<Exercise>> GenerateExercisesAsync(SkillNode node, CefrBand band, int count, ExerciseContext exerciseContext, LearnerContext context, IReadOnlyList<Exercise> examples, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Exercise>>([]);

    public Task<string> DiscussAsync(string thesis, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<Exercise?> GenerateReadingAsync(SkillNode node, CefrBand band, bool audioOnly, LearnerContext context, CancellationToken ct = default)
        => Task.FromResult<Exercise?>(null);

    public Task<string> PartnerAsync(TelcPart part, string scenario, IReadOnlyList<ChatTurn> history, LearnerContext context, CancellationToken ct = default)
        => throw new NotSupportedException();
}
