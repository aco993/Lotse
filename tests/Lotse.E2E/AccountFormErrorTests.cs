using static Microsoft.Playwright.Assertions;

namespace Lotse.E2E;

/// <summary>
/// What a rejected Account form does for someone who cannot see it. These pages are static SSR, so a failed
/// submit is a full page load: before 15.09.2026 the field messages appeared somewhere down the page, focus
/// stayed on &lt;body&gt; and nothing was announced - aria-invalid said "wrong" without saying why. Only a real
/// browser can prove the fix, because autofocus and document.activeElement need a page that actually has focus.
/// </summary>
public class AccountFormErrorTests(LotseE2EFixture app) : IClassFixture<LotseE2EFixture>
{
    private const string Password = "lozinka-e2e-2026";

    [Fact]
    public Task A_rejected_registration_lists_every_problem_and_takes_the_focus() => app.RunAsync(nameof(A_rejected_registration_lists_every_problem_and_takes_the_focus), async page =>
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("#email").FillAsync(LotseE2EFixture.UniqueEmail("summary"));
        await page.Locator("#password").FillAsync("kurz");   // under the 8-character minimum
        await page.Locator("#confirm").FillAsync("anders");  // and not the same
        await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();

        // One summary, announced (role=alert), naming both problems and how many there are
        var summary = page.Locator(".acct-summary");
        await Expect(summary).ToBeVisibleAsync();
        await Expect(summary).ToHaveAttributeAsync("role", "alert");
        await Expect(summary).ToContainTextAsync("2 Angaben stimmen noch nicht:");
        await Expect(summary).ToContainTextAsync("Mindestens 8 Zeichen.");
        await Expect(summary).ToContainTextAsync("Die beiden Passwörter stimmen nicht überein.");

        // The caret is in it, so a screen reader starts reading there instead of at the top of the document
        Assert.Equal("acct-alert error acct-summary", await page.EvaluateAsync<string>("document.activeElement.className"));

        // Each message leads to its field, and the field says which message describes it
        // The href carries the current path, not a bare "#password": <base href="/"> would otherwise send the
        // link to "/#password" - away from the form and, signed out, on to the login page.
        await Expect(summary.GetByRole(AriaRole.Link, new() { Name = "Mindestens 8 Zeichen." })).ToHaveAttributeAsync("href", "Account/Register#password");
        Assert.Equal("password-error password-hint", await page.Locator("#password").GetAttributeAsync("aria-describedby"));
        Assert.Equal("Mindestens 8 Zeichen.", await page.Locator("#password-error").InnerTextAsync());
        Assert.Equal("true", await page.Locator("#password").GetAttributeAsync("aria-invalid"));

        // Following the link puts the caret in the field it names
        await summary.GetByRole(AriaRole.Link, new() { Name = "Die beiden Passwörter stimmen nicht überein." }).ClickAsync();
        Assert.Equal("confirm", await page.EvaluateAsync<string>("document.activeElement.id"));
    });

    [Fact]
    public Task A_valid_form_focuses_the_first_field_instead() => app.RunAsync(nameof(A_valid_form_focuses_the_first_field_instead), async page =>
    {
        await page.GotoAsync("/Account/Register");
        await Expect(page.Locator(".acct-summary")).ToHaveCountAsync(0);
        Assert.Equal("email", await page.EvaluateAsync<string>("document.activeElement.id"));
    });

    [Fact]
    public Task A_failed_login_announces_the_reason_and_takes_the_focus() => app.RunAsync(nameof(A_failed_login_announces_the_reason_and_takes_the_focus), async page =>
    {
        var email = LotseE2EFixture.UniqueEmail("wrongpw");
        await page.GotoAsync("/Account/Register");
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(Password);
        await page.Locator("#confirm").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();
        await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Abmelden" }).ClickAsync();

        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync("das-ist-es-nicht");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();

        var alert = page.Locator(".acct-alert.error");
        await Expect(alert).ToContainTextAsync("E-Mail oder Passwort stimmt nicht");
        await Expect(alert).ToHaveAttributeAsync("role", "alert");
        Assert.Equal("acct-alert error", await page.EvaluateAsync<string>("document.activeElement.className"));
    });
}
