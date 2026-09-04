using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;

namespace Lotse.Web.Tests;

/// <summary>Records what the UI asked for and answers with canned results; anything a test does not expect throws.</summary>
public sealed class FakeLearningService : ILearningService
{
    public List<(string ExerciseId, string? Answer, bool Hint)> Answers { get; } = [];
    public Func<Exercise, string?, AnswerResult>? OnAnswer { get; set; }
    public Dictionary<string, Exercise> Exercises { get; } = new();

    public ITutor Tutor { get; set; } = new NullTutor();

    public Task<AnswerResult> SubmitAnswerAsync(Guid? sessionId, int stepIndex, string exerciseId, string? answer, int durationMs, bool hintUsed, CancellationToken ct = default)
    {
        Answers.Add((exerciseId, answer, hintUsed));
        var ex = Exercises[exerciseId];
        if (OnAnswer is not null) return Task.FromResult(OnAnswer(ex, answer));
        var check = AnswerChecker.Check(ex, answer);
        return Task.FromResult(new AnswerResult(check, ex, null, 0.5, null, false));
    }

    private static Exception Unexpected() => new NotSupportedException("Not expected in this test.");
    public Task<LearnerProfile> GetProfileAsync(CancellationToken ct = default) => throw Unexpected();
    public Task SaveProfileAsync(LearnerProfile profile, CancellationToken ct = default) => throw Unexpected();
    public Task<DashboardModel> GetDashboardAsync(CancellationToken ct = default) => throw Unexpected();
    public Task<SessionEntity> StartSessionAsync(int minutes, string? requestedNode = null, CancellationToken ct = default) => throw Unexpected();
    public Task<SessionEntity> StartPlacementAsync(bool quick = false, CancellationToken ct = default) => throw Unexpected();
    public Task<SessionEntity> StartSingleAsync(string exerciseId, SessionKind kind, CancellationToken ct = default) => throw Unexpected();
    public Task<SessionView?> GetSessionAsync(Guid id, CancellationToken ct = default) => throw Unexpected();
    public Task<SessionEntity?> GetOpenSessionAsync(CancellationToken ct = default) => throw Unexpected();
    public Task<IReadOnlyList<SessionEntity>> RecentSessionsAsync(int take = 20, CancellationToken ct = default) => throw Unexpected();
    public Task<SessionEntity> FinishSessionAsync(Guid id, CancellationToken ct = default) => throw Unexpected();
    public Task AbandonSessionAsync(Guid id, CancellationToken ct = default) => throw Unexpected();
    public Task SkipStepAsync(Guid sessionId, int stepIndex, CancellationToken ct = default) => throw Unexpected();
    public Task<AnswerResult> SubmitReadingAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosen, int durationMs, CancellationToken ct = default) => throw Unexpected();
    public List<IReadOnlyList<int>> DialogueChoices { get; } = [];
    public Task<AnswerResult> SubmitDialogueAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosen, int durationMs, CancellationToken ct = default)
    {
        DialogueChoices.Add(chosen);
        var ex = Exercises[exerciseId];
        var turns = ex.Lines.Where(l => l.IsLearnerTurn).ToList();
        var correct = turns.Select((t, i) => i < chosen.Count && chosen[i] == t.CorrectIndex ? 1 : 0).Sum();
        var check = new CheckResult(correct == turns.Count ? Outcome.Correct : Outcome.AlmostCorrect, $"{correct} von {turns.Count} richtig", [], "ok");
        return Task.FromResult(new AnswerResult(check, ex, null, 0.5, null, false));
    }
    public Task<AnswerResult> SubmitMatchAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosenRight, int durationMs, CancellationToken ct = default) => throw Unexpected();
    public Task<CourseOverview> GetCourseAsync(CancellationToken ct = default) => throw Unexpected();
    public Task<SessionEntity> StartLessonAsync(string lessonId, CancellationToken ct = default) => throw Unexpected();
    public Task<LearnerContext> BuildLearnerContextAsync(CancellationToken ct = default) => throw Unexpected();
    public Task<ProductionResult> SubmitProductionAsync(Guid? sessionId, int stepIndex, string exerciseId, string text, bool speaking, CancellationToken ct = default) => throw Unexpected();
    public Task<double> SubmitSelfCheckAsync(long productionId, Guid? sessionId, int stepIndex, IReadOnlyList<bool> checks, CancellationToken ct = default) => throw Unexpected();
    /// <summary>Nothing pending by default: the production component asks on every first render.</summary>
    public Task<ProductionResult?> GetPendingProductionAsync(Guid sessionId, string exerciseId, CancellationToken ct = default) => Task.FromResult<ProductionResult?>(null);
    public Task<IReadOnlyList<ProductionEntity>> RecentProductionsAsync(int take = 20, CancellationToken ct = default) => throw Unexpected();
    public Task<IReadOnlyDictionary<string, SkillState>> GetSkillStatesAsync(CancellationToken ct = default) => throw Unexpected();
    public Task<IReadOnlyList<(string Code, string Title, string NodeTitle, int Count)>> GetSessionErrorsAsync(Guid sessionId, CancellationToken ct = default) => throw Unexpected();
    public Task<IReadOnlyList<ErrorJournalEntry>> GetErrorJournalAsync(int days = 30, int take = 100, CancellationToken ct = default) => throw Unexpected();
    public Task<IReadOnlyList<(DateTime Day, int Attempts, double Accuracy)>> GetDailyHistoryAsync(int days = 30, CancellationToken ct = default) => throw Unexpected();
    public Task<ProgressStats> GetProgressStatsAsync(CancellationToken ct = default) => throw Unexpected();
    public Task<IReadOnlyList<Exercise>> GenerateForNodeAsync(string nodeId, int count = 6, CancellationToken ct = default) => throw Unexpected();
    public Task<Exercise?> GenerateReadingForNodeAsync(string nodeId, CancellationToken ct = default) => throw Unexpected();
    public Task<IReadOnlyList<(string NodeTitle, int Count)>> FillWeakestAsync(int nodes = 3, int perNode = 6, CancellationToken ct = default) => throw Unexpected();
    public Task ResetAllDataAsync(CancellationToken ct = default) => throw Unexpected();
}
