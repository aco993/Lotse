using System.Security.Claims;
using Lotse.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>
/// The one endpoint Identity's Razor components need that cannot run as an ordinary interactive Blazor page:
/// signing out has to clear the auth cookie from a real HTTP response, which a live SignalR circuit cannot do.
/// (Login/Register/ForgotPassword/ResetPassword instead render as static SSR pages — see their <c>@page</c>
/// components — where <c>HttpContext.SignInAsync</c> works directly, so they need no separate endpoint here.)
/// </summary>
internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (ClaimsPrincipal _, SignInManager<ApplicationUser> signInManager, [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        return accountGroup;
    }
}
