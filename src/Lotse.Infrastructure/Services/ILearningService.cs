using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Data;

namespace Lotse.Infrastructure.Services;

/// <summary>Application service contract used by the UI. Extracted so components can be tested with a fake.</summary>
public interface ILearningService
{
    ITutor Tutor { get; }

    Task<LearnerProfile> GetProfileAsync(CancellationToken ct = default);
    Task SaveProfileAsync(LearnerProfile profile, CancellationToken ct = default);
    Task<DashboardModel> GetDashboardAsync(CancellationToken ct = default);

    Task<SessionEntity> StartSessionAsync(int minutes, string? requestedNode = null, CancellationToken ct = default);
    /// <param name="quick">One item per core node (~8 minutes) instead of two (~20). An open placement is resumed either way.</param>
    Task<SessionEntity> StartPlacementAsync(bool quick = false, CancellationToken ct = default);
    Task<SessionEntity> StartSingleAsync(string exerciseId, SessionKind kind, CancellationToken ct = default);
    Task<SessionView?> GetSessionAsync(Guid id, CancellationToken ct = default);
    Task<SessionEntity?> GetOpenSessionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SessionEntity>> RecentSessionsAsync(int take = 20, CancellationToken ct = default);
    Task<SessionEntity> FinishSessionAsync(Guid id, CancellationToken ct = default);
    Task AbandonSessionAsync(Guid id, CancellationToken ct = default);
    Task SkipStepAsync(Guid sessionId, int stepIndex, CancellationToken ct = default);

    Task<AnswerResult> SubmitAnswerAsync(Guid? sessionId, int stepIndex, string exerciseId, string? answer, int durationMs, bool hintUsed, CancellationToken ct = default);
    Task<AnswerResult> SubmitReadingAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosen, int durationMs, CancellationToken ct = default);
    Task<AnswerResult> SubmitDialogueAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosen, int durationMs, CancellationToken ct = default);
    Task<AnswerResult> SubmitMatchAsync(Guid? sessionId, int stepIndex, string exerciseId, IReadOnlyList<int> chosenRight, int durationMs, CancellationToken ct = default);

    Task<CourseOverview> GetCourseAsync(CancellationToken ct = default);
    Task<SessionEntity> StartLessonAsync(string lessonId, CancellationToken ct = default);

    Task<LearnerContext> BuildLearnerContextAsync(CancellationToken ct = default);
    Task<ProductionResult> SubmitProductionAsync(Guid? sessionId, int stepIndex, string exerciseId, string text, bool speaking, CancellationToken ct = default);
    Task<double> SubmitSelfCheckAsync(long productionId, Guid? sessionId, int stepIndex, IReadOnlyList<bool> checks, CancellationToken ct = default);
    /// <summary>A submitted text whose self-check was never saved (page reload) - so the step can resume with it.</summary>
    Task<ProductionResult?> GetPendingProductionAsync(Guid sessionId, string exerciseId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductionEntity>> RecentProductionsAsync(int take = 20, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, SkillState>> GetSkillStatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<(string Code, string Title, string NodeTitle, int Count)>> GetSessionErrorsAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<ErrorJournalEntry>> GetErrorJournalAsync(int days = 30, int take = 100, CancellationToken ct = default);
    Task<IReadOnlyList<(DateTime Day, int Attempts, double Accuracy)>> GetDailyHistoryAsync(int days = 30, CancellationToken ct = default);

    /// <summary>Retention figures for Fortschritt, straight from the FSRS state.</summary>
    Task<ProgressStats> GetProgressStatsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Exercise>> GenerateForNodeAsync(string nodeId, int count = 6, CancellationToken ct = default);
    Task<Exercise?> GenerateReadingForNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<(string NodeTitle, int Count)>> FillWeakestAsync(int nodes = 3, int perNode = 6, CancellationToken ct = default);
    Task ResetAllDataAsync(CancellationToken ct = default);
}
