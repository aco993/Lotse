namespace Lotse.Infrastructure.CurrentUser;

/// <summary>
/// One known account, outside any circuit: the demo seeder writing the demo learner's history at startup, before
/// anyone is signed in. Never registered in DI - a request must always resolve its learner from the sign-in.
/// </summary>
public sealed class FixedCurrentUserAccessor(string userId) : ICurrentUserAccessor
{
    public Task<string> GetUserIdAsync() => Task.FromResult(userId);
}
