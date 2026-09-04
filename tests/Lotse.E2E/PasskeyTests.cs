using static Microsoft.Playwright.Assertions;

namespace Lotse.E2E;

/// <summary>Passkeys (WebAuthn) end to end with Chromium's virtual authenticator (CDP <c>WebAuthn.*</c>): no real
/// fingerprint reader, but the full ceremony - creation options from the server, credential created and stored by
/// the authenticator, attestation verified, then an assertion-based sign-in without any password.</summary>
public class PasskeyTests(LotseE2EFixture app) : IClassFixture<LotseE2EFixture>
{
    private const string Password = "lozinka-passkey-2026";

    [Fact]
    public Task Add_rename_and_sign_in_with_a_passkey() => app.RunAsync(nameof(Add_rename_and_sign_in_with_a_passkey), async page =>
    {
        // The login page also tries a conditional-mediation ("autofill") sign-in on load; with the virtual
        // authenticator that resolves instantly and would sign in before the test gets to click anything. Turn it
        // off here so the explicit "Mit Passkey anmelden" path is what gets exercised.
        await page.AddInitScriptAsync("PublicKeyCredential.isConditionalMediationAvailable = async () => false;");
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("WebAuthn.enable");
        await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new()
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2",
                ["transport"] = "internal",
                ["hasResidentKey"] = true,
                ["hasUserVerification"] = true,
                ["isUserVerified"] = true,
                ["automaticPresenceSimulation"] = true,
            },
        });

        var email = LotseE2EFixture.UniqueEmail("passkey");
        await page.GotoAsync("/Account/Register");
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(Password);
        await page.Locator("#confirm").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Konto erstellen" }).ClickAsync();
        await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();

        // Add: the button starts the ceremony, the virtual authenticator answers, the page posts the credential
        // and hands straight over to naming it.
        await page.GotoAsync("/Account/Manage/Passkeys");
        await Expect(page.GetByText("Noch kein Passkey hinterlegt.")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Passkey hinzufügen" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Passkey benennen" })).ToBeVisibleAsync();
        await page.Locator("#name").FillAsync("Test-Laptop");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
        await Expect(page.GetByText("Passkey gespeichert.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Test-Laptop")).ToBeVisibleAsync();

        // The Konto page counts it
        await page.GotoAsync("/Account/Manage");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "1 verwalten" })).ToBeVisibleAsync();

        // Sign out, then back in with the passkey only - no password typed
        await page.GotoAsync("/");
        await page.GetByRole(AriaRole.Button, new() { Name = "Abmelden" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Willkommen zurück" })).ToBeVisibleAsync();
        await page.Locator("#email").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Mit Passkey anmelden" }).ClickAsync();
        await Expect(page.GetByText("Willkommen an Bord.")).ToBeVisibleAsync();
    });
}
