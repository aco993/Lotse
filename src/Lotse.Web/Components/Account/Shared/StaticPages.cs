using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>
/// The one definition of "this page renders statically": every page on <see cref="AccountLayout"/> (Account/*,
/// /Error, /not-found) needs a genuine HTTP response - to set or clear the auth cookie, to return a status code -
/// and therefore must never be rendered inside an interactive circuit. <c>App.razor</c> uses this to pick the
/// render mode per request; <c>Routes.razor</c> uses it to hand such a navigation back to the browser when it
/// originates from an interactive page (a link click in the nav), instead of letting the circuit's router render
/// a page whose <c>HttpContext</c> would be null.
/// </summary>
public static class StaticPages
{
    public static bool IsStatic(Type? pageType)
        => pageType?.GetCustomAttribute<LayoutAttribute>()?.LayoutType == typeof(AccountLayout);
}
