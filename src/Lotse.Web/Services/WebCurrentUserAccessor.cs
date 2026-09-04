using System.Security.Claims;
using Lotse.Infrastructure.CurrentUser;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Lotse.Web.Services;

/// <summary>
/// Resolves the signed-in learner's id from the circuit's <see cref="AuthenticationStateProvider"/>. Registered
/// Scoped in <c>Program.cs</c>: one instance per Blazor Server circuit, i.e. per signed-in learner — the whole
/// reason <see cref="LearningService"/>/<see cref="TutorRegistry"/> can stay unaware of accounts in their public
/// method signatures. The claim type comes from <see cref="IdentityOptions"/>, the same place Identity itself and the
/// revalidating auth state provider read it from, rather than a hard-coded <see cref="ClaimTypes.NameIdentifier"/>.
/// </summary>
public sealed class WebCurrentUserAccessor(AuthenticationStateProvider authenticationStateProvider, IOptions<IdentityOptions> identityOptions) : ICurrentUserAccessor
{
    public async Task<string> GetUserIdAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
        if (string.IsNullOrEmpty(userId))
            throw new InvalidOperationException(
                "Kein angemeldeter Benutzer im aktuellen Kreis. Jede Seite, die Lerndaten liest oder schreibt, " +
                "sitzt hinter der globalen Login-Pflicht (siehe Program.cs, AddAuthorization/FallbackPolicy) – " +
                "wenn das hier auftritt, hat eine Seite diese Pflicht umgangen.");
        return userId;
    }
}
