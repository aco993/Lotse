using System.Net;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Web.Components.Account.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lotse.Web.Tests;

/// <summary>The real app in-process (own temp SQLite + Data Protection keys), for the things bUnit cannot see: the
/// middleware pipeline, the static Identity pages and <see cref="UserManager{TUser}"/> with the configured policy.</summary>
public sealed class LotseHost : WebApplicationFactory<Program>
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"lotse-host-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Lotse:DataDirectory", _dataDir);
        builder.UseSetting("Lotse:ContentDirectory", ContentLoader.ResolveContentDirectory());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dataDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public class HostTests : IDisposable
{
    private readonly LotseHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>Regression for 0.7.1: the fallback authorization policy used to cover the static-asset endpoints
    /// too, so a signed-out visitor got the login page's HTML for every stylesheet and script.</summary>
    [Theory]
    [InlineData("/app.css", "text/css")]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/_framework/blazor.web.js", "text/javascript")]
    public async Task Static_assets_are_served_without_a_cookie(string path, string mediaType)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_page_is_plain_static_html_without_a_circuit()
    {
        var client = _host.CreateClient();
        var html = await client.GetStringAsync("/Account/Login");
        Assert.Contains("<form", html);
        Assert.Contains("Willkommen zurück", html);
        // No circuit bootstrap of any kind: no script tag, no preload, no import map entry for it.
        Assert.DoesNotMatch("<script[^>]*blazor\\.web", html);
        Assert.DoesNotMatch("<link[^>]*blazor\\.web", html);
        Assert.DoesNotContain("importmap", html);
        Assert.DoesNotContain("MudBlazor.min.js", html);
    }

    /// <summary>Every page, and every unknown path (the router's catch-all is an endpoint like any other), asks a
    /// signed-out visitor to log in first - the app does not reveal which routes exist.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/themen")]
    [InlineData("/einstellungen")]
    [InlineData("/diese-seite-gibt-es-nicht")]
    public async Task Protected_pages_send_a_signed_out_visitor_to_login(string path)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Not_found_page_is_static_and_answers_404()
    {
        var client = _host.CreateClient();
        var response = await client.GetAsync("/not-found");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Diese Seite gibt es nicht", html);
        Assert.DoesNotMatch("<script[^>]*blazor\\.web", html);
    }

    /// <summary>The policy is exactly what the register form promises - length only - and every message is German.</summary>
    [Fact]
    public async Task Password_policy_is_length_only_and_speaks_german()
    {
        using var scope = _host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var tooShort = await users.CreateAsync(NewUser("policy@lotse.test"), "kurz");
        Assert.False(tooShort.Succeeded);
        var error = Assert.Single(tooShort.Errors);
        Assert.Equal("PasswordTooShort", error.Code);
        Assert.Contains(PasswordRules.MinLength.ToString(), error.Description);

        var lettersOnly = await users.CreateAsync(NewUser("policy@lotse.test"), "abcdefgh"); // no digit, no symbol, no upper case
        Assert.True(lettersOnly.Succeeded, string.Join(" | ", lettersOnly.Errors.Select(e => e.Description)));

        var duplicate = await users.CreateAsync(NewUser("policy@lotse.test"), "abcdefgh");
        Assert.False(duplicate.Succeeded);
        var message = Assert.Single(duplicate.Errors.Select(e => e.Description).Distinct()); // shown once, not twice
        Assert.Contains("gibt es schon ein Konto", message);
    }

    private static ApplicationUser NewUser(string email) => new() { UserName = email, Email = email };
}
