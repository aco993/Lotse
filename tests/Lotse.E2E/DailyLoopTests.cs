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

        // "Beenden" never ends a placement on one tap: it asks, and "Pausieren" keeps the answers for later
        await page.GetByRole(AriaRole.Button, new() { Name = "Beenden" }).ClickAsync();
        await Expect(page.GetByText("Einstufung unterbrechen?")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Pausieren" }).ClickAsync();
        // Two of 28, not one: "Weiß ich nicht" on the easy item of a topic also settles its harder sibling,
        // which the placement now marks as skipped instead of asking anyway (0.9.0).
        await Expect(page.GetByText("2 von 28 Aufgaben")).ToBeVisibleAsync();

        // The account link in the nav leads to the static Manage page - a full navigation, not a circuit render
        await page.GetByRole(AriaRole.Link, new() { Name = email }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dein Konto" })).ToBeVisibleAsync();
        await page.GotoAsync("/");

        // Log out from the drawer (persistent at 1280px) - a plain form post, back on the static login page
        await page.GetByRole(AriaRole.Button, new() { Name = "Abmelden" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Willkommen zurück" })).ToBeVisibleAsync();

        // Log in again with the password; the paused placement is waiting, and resuming continues the SAME session
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await Expect(page.GetByText("2 von 28 Aufgaben")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Einstufung fortsetzen" }).ClickAsync();
        await Expect(page.GetByText("2 / 28")).ToBeVisibleAsync();
    });

    [Fact]
    public Task Writing_without_a_tutor_gets_rule_based_findings_that_survive_a_reload() => app.RunAsync(nameof(Writing_without_a_tutor_gets_rule_based_findings_that_survive_a_reload), async page =>
    {
        var email = LotseE2EFixture.UniqueEmail("write");
        await page.GotoAsync("/Account/Register");
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(Password);
        await page.Locator("#confirm").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();
        await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();

        await page.GotoAsync("/schreiben");
        await page.Locator(".task-card").First.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Textbox)).ToBeVisibleAsync();
        const string text = "Sehr geehrte Damen und Herren, ich schreibe Ihnen weil ich habe ein Problem. Gestern ich habe gegangen zu die Firma und niemand war da. Ich warte für Ihre Antwort. Mit freundliche Grüße Marko";
        await page.GetByRole(AriaRole.Textbox).FillAsync(text);

        // The draft is saved in the browser: a reload must not cost the learner the text
        await page.WaitForTimeoutAsync(1800);
        await page.ReloadAsync();
        await Expect(page.GetByRole(AriaRole.Textbox)).ToHaveValueAsync(text);

        await page.GetByRole(AriaRole.Button, new() { Name = "Abgeben und selbst prüfen" }).ClickAsync();
        await Expect(page.GetByText("Automatisch gefunden")).ToBeVisibleAsync();
        foreach (var code in new[] { "TEMPUS_HILFSVERB", "WORTST_NEBENSATZ", "VERB_PRAEP", "REG_ANREDE_GRUSS", "KOMMA" })
            await Expect(page.GetByText(code, new() { Exact = true })).ToBeVisibleAsync();

        // Submitted but not yet self-checked: a reload brings the same state back from the database
        await page.ReloadAsync();
        await Expect(page.GetByText("Selbstcheck – ehrlich abhaken")).ToBeVisibleAsync();
        await Expect(page.GetByText("Automatisch gefunden")).ToBeVisibleAsync();

        // ... and the findings are already in the journal
        await page.GotoAsync("/fehler");
        await Expect(page.GetByText("haben/sein im Perfekt").First).ToBeVisibleAsync();
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
