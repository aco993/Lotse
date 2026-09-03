using Lotse.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>Loads the current <see cref="ApplicationUser"/> for the Manage/* pages, or redirects to login if the
/// account has gone missing mid-session (e.g. deleted from another device) — an edge case, not a normal path.
/// Returns null in that redirect case: <see cref="IdentityRedirectManager"/> issues the redirect but (unlike an
/// older template generation) does not stop the caller's own method from continuing, so every call site must
/// check for null and <c>return;</c> right away, same as the current official template does.</summary>
internal sealed class IdentityUserAccessor(UserManager<ApplicationUser> userManager, IdentityRedirectManager redirectManager)
{
    public async Task<ApplicationUser?> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            redirectManager.RedirectToWithStatus("Account/Login", "Fehler: Dein Konto wurde nicht gefunden.", context);
        }
        return user;
    }
}
