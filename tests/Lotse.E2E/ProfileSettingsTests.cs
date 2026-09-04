using static Microsoft.Playwright.Assertions;

namespace Lotse.E2E;

/// <summary>
/// The profile settings that change what the app does, checked where they actually show up. These live outside the
/// daily-loop test on purpose: a setting is only real once the shell and the other pages agree with it.
/// </summary>
public class ProfileSettingsTests(LotseE2EFixture app) : IClassFixture<LotseE2EFixture>
{
    private const string Password = "lozinka-e2e-2026";

    /// <summary>MudSelect's own input is type=hidden, so the click has to land on the control wrapper around it.</summary>
    private static Task OpenSelectAsync(IPage page, string label)
        => page.Locator($"div.mud-input-control:has(input[aria-label='{label}'])").ClickAsync();

    private static Task OpenTargetLevelAsync(IPage page) => OpenSelectAsync(page, "Zielniveau");

    private static async Task RegisterAsync(IPage page, string email)
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(Password);
        await page.Locator("#confirm").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();
        await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();
    }

    [Fact]
    public Task Switching_to_C1_changes_the_shell_and_says_the_exam_page_stays_B2() => app.RunAsync(nameof(Switching_to_C1_changes_the_shell_and_says_the_exam_page_stays_B2), async page =>
    {
        await RegisterAsync(page, LotseE2EFixture.UniqueEmail("ziel"));

        // Default is B2 and the app bar says so.
        await Expect(page.GetByText("Dein Kurs auf B2")).ToBeVisibleAsync();

        await page.GotoAsync("/einstellungen");
        // MudSelect's own input is type=hidden (it only carries the value and the a11y role), so the click has to
        // go to the control wrapper around it - clicking the input itself waits forever for something visible.
        await OpenTargetLevelAsync(page);
        await page.Locator(".mud-list-item", new() { HasTextString = "C1 – darüber hinaus" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Gespeichert.")).ToBeVisibleAsync();

        // The shell is built once per circuit, so this only passes because saving notifies it - no reload here.
        await Expect(page.GetByText("Dein Kurs auf C1")).ToBeVisibleAsync();

        // And the exam page is honest about what the C1 setting does not change.
        await page.GotoAsync("/pruefung");
        await Expect(page.GetByText("Diese Seite bleibt trotzdem die")).ToBeVisibleAsync();

        // Back to B2: the label follows again, and the exam page drops the note.
        await page.GotoAsync("/einstellungen");
        await OpenTargetLevelAsync(page);
        await page.Locator(".mud-list-item", new() { HasTextString = "B2 – Prüfungsvorbereitung" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Dein Kurs auf B2")).ToBeVisibleAsync();
    });

    [Fact]
    public Task The_course_greets_the_learner_by_their_own_name() => app.RunAsync(nameof(The_course_greets_the_learner_by_their_own_name), async page =>
    {
        await RegisterAsync(page, LotseE2EFixture.UniqueEmail("name"));

        // Without a name of their own, the learner gets the author's - the course reads exactly as it always did.
        await page.GotoAsync("/kurs/L02");
        await Expect(page.GetByText("Sehr geehrter Herr Micić", new() { Exact = false }).First).ToBeVisibleAsync();

        await page.GotoAsync("/einstellungen");
        await page.GetByLabel("Vorname").First.FillAsync("Marko");
        await page.GetByLabel("Nachname").First.FillAsync("Petrović");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Gespeichert.")).ToBeVisibleAsync();

        // Same lesson, now addressed to them - the tokens live in the content, the name never does.
        await page.GotoAsync("/kurs/L02");
        await Expect(page.GetByText("Sehr geehrter Herr Petrović", new() { Exact = false }).First).ToBeVisibleAsync();
        await Expect(page.GetByText("Micić", new() { Exact = false })).ToHaveCountAsync(0);
    });

    [Fact]
    public Task Weekly_goal_and_the_retention_cards_show_up_without_any_practice() => app.RunAsync(nameof(Weekly_goal_and_the_retention_cards_show_up_without_any_practice), async page =>
    {
        await RegisterAsync(page, LotseE2EFixture.UniqueEmail("ziel2"));

        // Default goal, nothing done yet - the bar has to render at zero rather than break.
        await Expect(page.GetByText("0 von 60 Minuten diese Woche")).ToBeVisibleAsync();

        await page.GotoAsync("/einstellungen");
        await OpenSelectAsync(page, "Wochenziel");
        await page.Locator(".mud-list-item", new() { HasTextString = "120 Minuten pro Woche" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Gespeichert.")).ToBeVisibleAsync();

        await page.GotoAsync("/");
        await Expect(page.GetByText("0 von 120 Minuten diese Woche")).ToBeVisibleAsync();

        // The three FSRS cards must be honest, not empty, for a learner with no schedule yet.
        await page.GotoAsync("/fortschritt");
        await Expect(page.GetByText("Wörter, die sitzen")).ToBeVisibleAsync();
        await Expect(page.GetByText("Fällig in den nächsten 7 Tagen")).ToBeVisibleAsync();
        await Expect(page.GetByText("Noch keine geplanten Wiederholungen.")).ToBeVisibleAsync();
    });

    [Fact]
    public Task Heute_speaks_plainly_while_Themen_keeps_the_terms() => app.RunAsync(nameof(Heute_speaks_plainly_while_Themen_keeps_the_terms), async page =>
    {
        await RegisterAsync(page, LotseE2EFixture.UniqueEmail("klartext"));

        // "TeKaMoLo" is the giveaway: a term that means nothing to an A2/B1 learner being told what to do next.
        await Expect(page.GetByText("Der Lotse schlägt vor")).ToBeVisibleAsync();
        await Expect(page.GetByText("TeKaMoLo")).ToHaveCountAsync(0);

        // Themen is where you look a topic up, so there the precise title stays.
        await page.GotoAsync("/themen");
        await Expect(page.GetByText("TeKaMoLo").First).ToBeVisibleAsync();
    });

    [Fact]
    public Task Occupation_is_saved_explained_and_survives_a_reload() => app.RunAsync(nameof(Occupation_is_saved_explained_and_survives_a_reload), async page =>
    {
        await RegisterAsync(page, LotseE2EFixture.UniqueEmail("beruf"));

        await page.GotoAsync("/einstellungen");
        // Unset by default, and the hint says the planner behaves as it always did.
        await Expect(page.GetByText("rein nach deinen Schwächen")).ToBeVisibleAsync();

        await OpenSelectAsync(page, "Branche");
        await page.Locator(".mud-list-item", new() { HasTextString = "Pflege" }).First.ClickAsync();
        await Expect(page.GetByText("bevorzugt der Lotse Aufgaben aus deinem Feld")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Gespeichert.")).ToBeVisibleAsync();

        // A setting that does not survive a reload was never saved.
        await page.ReloadAsync();
        await Expect(page.GetByText("bevorzugt der Lotse Aufgaben aus deinem Feld")).ToBeVisibleAsync();
    });
}
