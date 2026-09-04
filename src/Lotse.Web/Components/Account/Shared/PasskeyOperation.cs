namespace Lotse.Web.Components.Account.Shared;

/// <summary>Which WebAuthn ceremony a <see cref="PasskeySubmit"/> button starts.</summary>
public enum PasskeyOperation
{
    /// <summary>Register a new passkey for the signed-in learner (navigator.credentials.create).</summary>
    Create = 0,
    /// <summary>Sign in with an existing passkey (navigator.credentials.get).</summary>
    Request = 1,
}
