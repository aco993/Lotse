using System.Reflection;
using Lotse.Web.Components.Account.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Lotse.Web.Tests;

/// <summary>The redirect after login/registration takes a caller-supplied <c>returnUrl</c>; anything that would leave
/// this host must fall back to the start page. Uses bUnit's fake <see cref="NavigationManager"/> (base
/// <c>http://localhost/</c>), which records where it was sent.</summary>
public class IdentityRedirectManagerTests : BunitContext
{
    [Theory]
    [InlineData("//evil.example/x")]               // protocol-relative: well-formed *relative* URI, resolves off-site
    [InlineData("https://evil.example/x")]         // absolute foreign host
    [InlineData("http://localhost.evil.example/")] // same-host prefix trick
    [InlineData("\\\\evil.example")]               // backslash variant browsers normalise to //
    public void Foreign_targets_fall_back_to_home(string target)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        new IdentityRedirectManager(nav).RedirectTo(target);
        Assert.Equal(nav.BaseUri, nav.Uri);
    }

    [Theory]
    [InlineData("themen", "http://localhost/themen")]
    [InlineData("kurs/L01?tab=2", "http://localhost/kurs/L01?tab=2")]
    [InlineData("http://localhost/themen", "http://localhost/themen")]
    [InlineData("", "http://localhost/")]
    [InlineData(null, "http://localhost/")]
    public void Own_targets_pass_through(string? target, string expected)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        new IdentityRedirectManager(nav).RedirectTo(target);
        Assert.Equal(expected, nav.Uri);
    }
}

/// <summary>Every message Identity can produce under Lotse's configuration must come back in German, under the
/// same error code Identity itself uses - the register form shows these verbatim.</summary>
public class GermanIdentityErrorDescriberTests
{
    [Fact]
    public void Every_override_is_german_and_keeps_identitys_error_code()
    {
        var ours = new GermanIdentityErrorDescriber();
        var theirs = new IdentityErrorDescriber();
        var overrides = typeof(GermanIdentityErrorDescriber)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(IdentityError))
            .ToList();
        Assert.NotEmpty(overrides);

        foreach (var method in overrides)
        {
            var args = method.GetParameters().Select(p => p.ParameterType == typeof(int) ? (object)8 : "x@y.de").ToArray();
            var mine = (IdentityError)method.Invoke(ours, args)!;
            var original = (IdentityError)typeof(IdentityErrorDescriber)
                .GetMethod(method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray())!
                .Invoke(theirs, args)!;

            Assert.Equal(original.Code, mine.Code);
            Assert.NotEqual(original.Description, mine.Description);
            Assert.False(string.IsNullOrWhiteSpace(mine.Description), method.Name);
        }
    }

    [Fact]
    public void Password_too_short_names_the_configured_minimum()
    {
        var error = new GermanIdentityErrorDescriber().PasswordTooShort(PasswordRules.MinLength);
        Assert.Contains(PasswordRules.MinLength.ToString(), error.Description);
    }
}
