namespace Lotse.Infrastructure.CurrentUser;

/// <summary>
/// A Scoped service that caches state for the signed-in learner (today: <see cref="Ai.TutorRegistry"/>, which keeps a
/// built tutor and its decrypted settings in memory). A Blazor Server circuit can in practice outlive a logout/login
/// round-trip, so "Scoped = one learner" is not a guarantee such a cache may rely on; implementations re-check the
/// learner behind <see cref="ICurrentUserAccessor"/> here and reload if it changed. Callers that read cached state
/// (e.g. <c>LearningService</c> before it looks at <c>ITutor.IsAvailable</c>) await this first.
/// </summary>
public interface ILearnerBound
{
    Task EnsureCurrentAsync(CancellationToken ct = default);
}
