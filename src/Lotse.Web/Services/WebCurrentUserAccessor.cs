using System.Security.Claims;
using Lotse.Infrastructure.CurrentUser;
using Microsoft.AspNetCore.Components.Authorization;

namespace Lotse.Web.Services;

/// <summary>
/// Resolves the signed-in learner's id from the circuit's <see cref="AuthenticationStateProvider"/>. Registered
/// Scoped in <c>Program.cs</c>: one instance per Blazor Server circuit, i.e. per signed-in learner — the whole
/// reason <see cref="LearningService"/>/<see cref="TutorRegistry"/> can stay unaware of accounts in their public
/// method signatures.
/// </summary>
public sealed class WebCurrentUserAccessor(AuthenticationStateProvider authenticationStateProvider) : ICurrentUserAccessor
{
    public async Task<string> GetUserIdAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            throw new InvalidOperationException(
                "Kein angemeldeter Benutzer im aktuellen Kreis. Jede Seite, die Lerndaten liest oder schreibt, " +
                "sitzt hinter der globalen Login-Pflicht (siehe Program.cs, AddAuthorization/FallbackPolicy) – " +
                "wenn das hier auftritt, hat eine Seite diese Pflicht umgangen.");
        return userId;
    }
}
