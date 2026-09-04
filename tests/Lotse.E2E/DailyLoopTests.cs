using static Microsoft.Playwright.Assertions;

namespace Lotse.E2E;

/// <summary>The learner's actual day, in a real browser: register, run into the placement, answer, log out, log
/// back in and find the session waiting. Everything a bUnit or host test cannot see - the live circuit, the static
/// login handing over to the interactive app, the cookie surviving a full navigation.</summary>
public class DailyLoopTests(LotseE2EFixture app) : IClassFixture<LotseE2EFixture>
{
    private const string Password = "lozinka-e2e-2026";

    [Fact]
    public Task Register_start_placement_answer_logout_login_and_resume() => app.RunAsync(nameof(Register_start_placement_answer_logout_login_and_resume), async page =>
    {
        var email = LotseE2EFixture.UniqueEmail("loop");

        // Register (static page → cookie → interactive dashboard)
        await page.GotoAsync("/Account/Register");
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(Password);
        await page.Locator("#confirm").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();
        await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();

        // Placement: first item, "Weiß ich nicht" is a legitimate answer - feedback with the solution and the
        // Serbian contrast note, then "Weiter" moves the counter on
        await page.GetByRole(AriaRole.Button, new() { Name = "Einstufung starten" }).ClickAsync();
        await Expect(page.GetByText("1 / 28")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Weiß ich nicht" }).ClickAsync();
        await Expect(page.GetByText("Lösung:")).ToBeVisibleAsync();
        await Expect(page.GetByText("Serbisch ↔ Deutsch")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Weiter", Exact = true }).ClickAsync();
        await Expect(page.GetByText("2 / 28")).ToBeVisibleAsync();

        // Log out from the drawer (persistent at 1280px) - a plain form post, back on the static login page
        await page.GetByRole(AriaRole.Button, new() { Name = "Abmelden" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Willkommen zurück" })).ToBeVisibleAsync();

        // Log in again with the password; the open placement is waiting on the dashboard
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Session läuft noch")).ToBeVisibleAsync();
        await Expect(page.GetByText("1 / 28 erledigt")).ToBeVisibleAsync();
    });

    [Fact]
    public Task Wrong_password_is_refused_in_german() => app.RunAsync(nameof(Wrong_password_is_refused_in_german), async page =>
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("#email").FillAsync("niemand@lotse.test");
        await page.Locator("#password").FillAsync("falsch-falsch");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Anmeldung fehlgeschlagen")).ToBeVisibleAsync();
    });
}
