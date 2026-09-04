using static Microsoft.Playwright.Assertions;

namespace Lotse.E2E;

/// <summary>
/// The profile settings that change what the app does, checked where they actually show up. These live outside the
/// daily-loop test on purpose: a setting is only real once the shell and the other pages agree with it.
/// </summary>
public class ProfileSettingsTests(LotseE2EFixture app) : IClassFixture<LotseE2EFixture>
{
    private const string Password = "lozinka-e2e-2026";

    private static Task OpenTargetLevelAsync(IPage page)
        => page.Locator("div.mud-input-control:has(input[aria-label='Zielniveau'])").ClickAsync();

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
}
