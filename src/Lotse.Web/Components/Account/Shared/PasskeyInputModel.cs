namespace Lotse.Web.Components.Account.Shared;

/// <summary>What the <c>&lt;passkey-submit&gt;</c> element (PasskeySubmit.razor.js) posts back: either the WebAuthn
/// credential as JSON, or the reason the browser could not produce one. Bound via <c>[SupplyParameterFromForm]</c>.</summary>
public sealed class PasskeyInputModel
{
    public string? CredentialJson { get; set; }
    public string? Error { get; set; }
}
