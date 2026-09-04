namespace Lotse.Web.Components.Account.Shared;

/// <summary>The one source for what a password has to look like. Identity's options (<c>Program.cs</c>), the
/// <c>[StringLength]</c> attributes on the Register/Reset/ChangePassword forms and the hints under those fields
/// all read from here, so the rule and its promise cannot drift apart again.</summary>
internal static class PasswordRules
{
    /// <summary>Length over composition rules (NIST SP 800-63B): this is the only requirement.</summary>
    public const int MinLength = 8;
}
