using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;

namespace Lotse.Web.Services;

/// <summary>
/// Per-circuit cache of the signed-in learner's profile, plus a notification when it is saved.
///
/// The shell outlives every page: <c>MainLayout</c> is initialised once per circuit, so anything it shows from the
/// profile (the target level in the app bar, later the learner's name) would keep showing the old value after a save
/// on Einstellungen until a full reload. Pages do not need this - they re-initialise on navigation - but the layout
/// does, and a scoped state object with one event is a great deal less than reloading the whole app for a setting.
/// </summary>
public sealed class LearnerProfileState(ILearningService learning)
{
    private LearnerProfile? _profile;

    /// <summary>Raised after <see cref="Set"/>; subscribers must re-render themselves.</summary>
    public event Action? Changed;

    public async ValueTask<LearnerProfile> GetAsync(CancellationToken ct = default)
        => _profile ??= await learning.GetProfileAsync(ct);

    /// <summary>Called by whoever just saved the profile, with the object that was written.</summary>
    public void Set(LearnerProfile profile)
    {
        _profile = profile;
        Changed?.Invoke();
    }
}
