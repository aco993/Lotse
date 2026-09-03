namespace Lotse.Infrastructure.CurrentUser;

/// <summary>
/// Resolves the signed-in learner's id for the current call. <see cref="LearningService"/>,
/// <see cref="Ai.TutorRegistry"/> and <see cref="Services.HealthService"/> scope every query and every insert by
/// this id — it is the one seam through which "which account is this?" enters the persistence layer, kept as a
/// plain interface here so Infrastructure stays free of any Blazor/ASP.NET Core Identity dependency. The concrete
/// implementation (<c>WebCurrentUserAccessor</c>, wrapping <c>AuthenticationStateProvider</c>) lives in
/// <c>Lotse.Web</c> and is registered <b>Scoped</b> — one instance per Blazor Server circuit, i.e. per signed-in
/// learner, which is what makes this safe without threading a UserId through every method signature.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>The signed-in learner's id (ASP.NET Core Identity's <c>ApplicationUser.Id</c>).</summary>
    /// <exception cref="InvalidOperationException">No authenticated user in the current circuit. Every page that
    /// calls into the learning service sits behind the app's fallback "must be signed in" authorization policy, so
    /// this should never happen in practice; it is a defect (a page slipped past the auth gate), not a normal
    /// "not logged in" case to handle gracefully.</exception>
    Task<string> GetUserIdAsync();
}
