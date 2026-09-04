using Lotse.Infrastructure.CurrentUser;

namespace Lotse.Core.Tests;

/// <summary>Test learner id, so LearningService/TutorRegistry tests don't need real ASP.NET Core Identity wired up.
/// Two instances with different ids are what <see cref="MultiUserIsolationTests"/> uses to prove one account's data
/// never leaks into another's; <see cref="UserId"/> is settable so a test can also simulate the learner behind one
/// instance changing underneath a cached service.</summary>
public sealed class FakeCurrentUserAccessor(string userId = "test-user") : ICurrentUserAccessor
{
    public string UserId { get; set; } = userId;

    public Task<string> GetUserIdAsync() => Task.FromResult(UserId);
}
