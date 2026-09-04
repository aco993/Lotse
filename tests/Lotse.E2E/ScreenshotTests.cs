using static Microsoft.Playwright.Assertions;

namespace Lotse.E2E;

/// <summary>
/// Renders the pages a change touched on both form factors and in both themes and writes PNGs next to the test
/// binaries (<c>screenshots/</c>). Web and mobile, light and dark are equal citizens here, so a layout that only
/// works at 1280 px or only in light mode fails visibly instead of quietly.
///
/// The assertions are deliberately thin - this is proof that the page renders and carries the expected text, not a
/// second copy of the behaviour tests.
/// </summary>
public class ScreenshotTests(LotseE2EFixture app) : IClassFixture<LotseE2EFixture>
{
    private const string Password = "lozinka-e2e-2026";

    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("web", 1280, 800),
        ("mobil", 375, 812),
    ];

    private static readonly (string Name, ColorScheme Scheme)[] Themes =
    [
        ("hell", ColorScheme.Light),
        ("dunkel", ColorScheme.Dark),
    ];

    [Fact]
    public async Task Target_level_pages_render_on_web_and_mobile_in_light_and_dark()
    {
        foreach (var (viewport, width, height) in Viewports)
        {
            foreach (var (theme, scheme) in Themes)
            {
                var (context, page) = await app.NewPageAsync(width, height, scheme);
                try
                {
                    await page.GotoAsync("/Account/Register");
                    await page.Locator("#email").FillAsync(LotseE2EFixture.UniqueEmail("shot"));
                    await page.Locator("#password").FillAsync(Password);
                    await page.Locator("#confirm").FillAsync(Password);
                    await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();
                    await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();

                    // Settings with the new target-level select
                    await page.GotoAsync("/einstellungen");
                    // The label itself is rendered twice by an outlined MudSelect (label + fieldset legend), so the
                    // consequence sentence is the unambiguous thing to wait for.
                    await Expect(page.GetByText("Schwerpunkte bleiben bei B2.2")).ToBeVisibleAsync();
                    await page.Locator("div.mud-input-control:has(input[aria-label='Zielniveau'])").ClickAsync();
                    await page.Locator(".mud-list-item", new() { HasTextString = "C1 – darüber hinaus" }).First.ClickAsync();
                    await page.Locator("div.mud-input-control:has(input[aria-label='Branche'])").ClickAsync();
                    await page.Locator(".mud-list-item", new() { HasTextString = "Pflege" }).First.ClickAsync();
                    await page.GetByLabel("Vorname").First.FillAsync("Marko");
                    await page.GetByLabel("Nachname").First.FillAsync("Petrović");
                    await page.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
                    await Expect(page.GetByText("Gespeichert.")).ToBeVisibleAsync();
                    await Shoot(page, $"einstellungen-zielniveau-{viewport}-{theme}");

                    // Heute with the C1 second line in the readiness tile
                    await page.GotoAsync("/");
                    await Expect(page.GetByText("C1-Nähe")).ToBeVisibleAsync();
                    await Shoot(page, $"heute-c1-{viewport}-{theme}");

                    // Fortschritt with the three retention cards
                    await page.GotoAsync("/fortschritt");
                    await Expect(page.GetByText("Wörter, die sitzen")).ToBeVisibleAsync();
                    await Shoot(page, $"fortschritt-karten-{viewport}-{theme}");

                    // A lesson addressed to the learner instead of to the author
                    await page.GotoAsync("/kurs/L02");
                    await Expect(page.GetByText("Sehr geehrter Herr Petrović", new() { Exact = false }).First).ToBeVisibleAsync();
                    await Shoot(page, $"lektion-name-{viewport}-{theme}");
                }
                finally
                {
                    await context.CloseAsync();
                }
            }
        }
    }

    private static Task Shoot(IPage page, string name) => page.ScreenshotAsync(new()
    {
        Path = Path.Combine(LotseE2EFixture.ScreenshotDirectory, name + ".png"),
        FullPage = true,
    });
}
