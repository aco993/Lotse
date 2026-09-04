using Lotse.Infrastructure.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>
/// The few endpoints Identity's Razor components need that cannot be ordinary pages: signing out has to clear the
/// auth cookie from a real HTTP response, and the two WebAuthn option endpoints are called by
/// <c>PasskeySubmit.razor.js</c> via fetch (antiforgery token in a header, validated explicitly - minimal-API
/// endpoints are not covered by the automatic form validation the Razor pages get).
/// (Login/Register/ForgotPassword/ResetPassword themselves render as static SSR pages - see their <c>@page</c>
/// components - where <c>HttpContext.SignInAsync</c> works directly, so they need no separate endpoint here.)
/// </summary>
internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (SignInManager<ApplicationUser> signInManager, [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        // Registration ceremony options for the signed-in learner (Manage/Passkeys → "Passkey hinzufügen").
        accountGroup.MapPost("/PasskeyCreationOptions", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await userManager.GetUserAsync(context.User);
            if (user is null) return Results.NotFound();

            var userId = await userManager.GetUserIdAsync(user);
            var userName = await userManager.GetUserNameAsync(user) ?? "Lernende:r";
            var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(new() { Id = userId, Name = userName, DisplayName = userName });
            return TypedResults.Content(optionsJson, contentType: "application/json");
        });

        // Assertion ceremony options for the login page: anonymous by design (nobody is signed in yet); the
        // antiforgery header still ties the call to the page that rendered the token.
        accountGroup.MapPost("/PasskeyRequestOptions", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IAntiforgery antiforgery,
            [FromQuery] string? username) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = string.IsNullOrEmpty(username) ? null : await userManager.FindByNameAsync(username);
            var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(user);
            return TypedResults.Content(optionsJson, contentType: "application/json");
        }).AllowAnonymous();

        return accountGroup;
    }
}
