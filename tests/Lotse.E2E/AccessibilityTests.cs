using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lotse.E2E;

/// <summary>
/// axe-core against the pages a learner sees every day, for WCAG 2.2 A and AA, on a phone and a desktop, in light and
/// dark. A serious or critical violation fails the build; minor and moderate ones do not. It checks what the palette
/// alone cannot: names, roles, labels, landmarks, and contrast where components layer colours on top of each other.
/// </summary>
[Collection(nameof(AccessibilityTests))]
public class AccessibilityTests(LotseE2EFixture app) : IClassFixture<LotseE2EFixture>
{
    private const string Password = "lozinka-e2e-2026";

    private static readonly AxeRunOptions Wcag22AA = new()
    {
        RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
    };

    private static readonly string[] Blocking = ["serious", "critical"];

    /// <summary>Pages that need no account: the first thing a visitor of the demo sees.</summary>
    private static readonly string[] Anonymous = ["/Account/Login", "/Account/Register"];

    /// <summary>The daily loop and the exam pages. Each page has one visible h1; Einstellungen also loads the profile.</summary>
    private static readonly (string Path, string? Ready)[] SignedIn =
    [
        ("/", null),
        ("/telc", null),
        ("/fortschritt", null),
        ("/themen", null),
        ("/sprechen", null),
        ("/einstellungen", "Schwerpunkte bleiben bei B2.2"),
        // The exam itself: twelve ads in a grid, and the email with its live checks.
        ("/telc/ueben/Lesen3", null),
        ("/telc/ueben/Schreiben", null),
    ];

    public static TheoryData<int, int, ColorScheme> Layouts => new()
    {
        { 375, 812, ColorScheme.Light },
        { 375, 812, ColorScheme.Dark },
        { 1280, 800, ColorScheme.Light },
        { 1280, 800, ColorScheme.Dark },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task Daily_pages_have_no_serious_wcag_violations(int width, int height, ColorScheme scheme)
    {
        var (context, page) = await app.NewPageAsync(width, height, scheme);
        var found = new List<string>();
        try
        {
            foreach (var path in Anonymous)
                found.AddRange(await ScanAsync(page, path));

            await page.GotoAsync("/Account/Register");
            await page.Locator("#email").FillAsync(LotseE2EFixture.UniqueEmail("a11y"));
            await page.Locator("#password").FillAsync(Password);
            await page.Locator("#confirm").FillAsync(Password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();
            await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();

            foreach (var (path, ready) in SignedIn)
                found.AddRange(await ScanAsync(page, path, ready));

            // A running session is where the learner spends the time: the exercise card, its input and the hint ladder.
            await page.GotoAsync("/kurz");
            await Expect(page.Locator(".ex-prompt").First).ToBeVisibleAsync();
            found.AddRange(await ScanAsync(page, null));
        }
        finally
        {
            await context.CloseAsync();
        }

        Assert.True(found.Count == 0, $"{width}px, {scheme}:\n" + string.Join("\n", found));
    }

    private static async Task<IEnumerable<string>> ScanAsync(IPage page, string? path, string? ready = null)
    {
        if (path is not null)
        {
            await page.GotoAsync(path);
            await Expect(page.Locator("h1:visible").First).ToBeVisibleAsync();
            if (ready is not null) await Expect(page.GetByText(ready).First).ToBeVisibleAsync();
        }
        // Blazor Server finishes its first interactive render a moment after the prerendered HTML is visible.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var result = await page.RunAxe(Wcag22AA);
        var where = path ?? new Uri(page.Url).AbsolutePath;
        return result.Violations
            .Where(v => Blocking.Contains(v.Impact))
            .Select(v => $"{where}: {v.Impact} {v.Id} – {v.Help} ({v.Nodes.Length}×) e.g. {Shorten(v.Nodes[0].Html)}");
    }

    private static string Shorten(string html) => html.Length <= 400 ? html : html[..400] + "…";
}

/// <summary>
/// axe-core keeps a Chromium busy for seconds per page, four layouts in a row. Next to another test class that was
/// enough to delay a Blazor render in the other class past its timeout now and then, so this class runs on its own.
/// </summary>
[CollectionDefinition(nameof(AccessibilityTests), DisableParallelization = true)]
public sealed class AccessibilityCollection;
