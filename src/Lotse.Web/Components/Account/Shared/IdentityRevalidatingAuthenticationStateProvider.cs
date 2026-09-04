using System.Security.Claims;
using Lotse.Infrastructure.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>
/// Periodically re-checks the signed-in learner's security stamp against the store, so a changed password or a
/// deleted account signs the circuit out live instead of waiting for the auth cookie to expire. Same class (and
/// name) as in the official Identity template; the name matters because the framework already ships a
/// <c>Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider</c> that this must not be confused with.
///
/// Lotse disables prerendering (see <c>App.razor</c>) — unlike the official template there is therefore no
/// prerender-to-interactive handoff whose auth state needs persisting via <c>PersistentComponentState</c>.
/// </summary>
internal sealed class IdentityRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory, IOptions<IdentityOptions> optionsAccessor)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    private readonly IdentityOptions _options = optionsAccessor.Value;

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(authenticationState.User);
        if (user is null) return false;
        if (!userManager.SupportsUserSecurityStamp) return true;

        var principalStamp = authenticationState.User.FindFirstValue(_options.ClaimsIdentity.SecurityStampClaimType);
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return principalStamp == userStamp;
    }
}
