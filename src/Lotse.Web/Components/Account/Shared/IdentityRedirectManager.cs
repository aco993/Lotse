using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>Redirects from Identity's static-SSR account pages, with an optional one-shot status message carried
/// in a short-lived cookie (the target page reads and clears it). Matches the current (.NET 10) ASP.NET Core
/// Identity template shape exactly: plain <c>NavigateTo(uri)</c>, no <c>forceLoad</c> and no defensive trailing
/// throw. An earlier .NET 8-era version of this template relied on <c>NavigateTo</c> throwing a
/// <c>NavigationException</c> synchronously to short-circuit rendering, with a "this should be unreachable"
/// <c>[DoesNotReturn]</c> throw as a safety net if it didn't - that mechanism no longer applies in .NET 10, and the
/// safety-net throw fired for real on every submit here (registration/login otherwise worked - user created,
/// signed in - only the redirect step failed).</summary>
internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Lotse.Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    public void RedirectTo(string? uri)
    {
        // Open-redirect guard. The template's check (Uri.IsWellFormedUriString(uri, UriKind.Relative)) is not
        // enough: a protocol-relative "//evil.example" IS a well-formed relative URI, resolves to
        // https://evil.example/ against the base and would send a freshly signed-in learner off-site via a crafted
        // ?returnUrl=. So resolve first and only accept what stays under our own base; anything else goes home.
        var target = navigationManager.ToAbsoluteUri(uri ?? "");
        if (!target.AbsoluteUri.StartsWith(navigationManager.BaseUri, StringComparison.Ordinal))
            target = new Uri(navigationManager.BaseUri);
        navigationManager.NavigateTo(navigationManager.ToBaseRelativePath(target.AbsoluteUri));
    }

    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);
}
