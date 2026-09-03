using Lotse.Infrastructure.CurrentUser;

namespace Lotse.Core.Tests;

/// <summary>Fixed test learner id, so LearningService/TutorRegistry tests don't need real ASP.NET Core Identity
/// wired up. Two instances with different ids are what <see cref="MultiUserIsolationTests"/> uses to prove one
/// account's data never leaks into another's.</summary>
public sealed class FakeCurrentUserAccessor(string userId = "test-user") : ICurrentUserAccessor
{
    public Task<string> GetUserIdAsync() => Task.FromResult(userId);
}
