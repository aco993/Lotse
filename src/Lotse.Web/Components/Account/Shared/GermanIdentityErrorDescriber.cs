using Microsoft.AspNetCore.Identity;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>ASP.NET Core Identity's validation messages are English by default ("Username 'x' is already taken.")
/// and would be the only English sentences in an otherwise German UI. Only the errors Lotse's configuration can
/// actually produce are translated; everything else falls through to the English default.</summary>
internal sealed class GermanIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Make(nameof(DefaultError), "Das hat leider nicht geklappt. Bitte noch einmal versuchen.");
    public override IdentityError DuplicateEmail(string email) => Make(nameof(DuplicateEmail), $"Für {email} gibt es schon ein Konto – bitte anmelden oder das Passwort zurücksetzen.");
    public override IdentityError DuplicateUserName(string userName) => Make(nameof(DuplicateUserName), $"Für {userName} gibt es schon ein Konto – bitte anmelden oder das Passwort zurücksetzen.");
    public override IdentityError InvalidEmail(string? email) => Make(nameof(InvalidEmail), "Das sieht nicht nach einer E-Mail-Adresse aus.");
    public override IdentityError InvalidUserName(string? userName) => Make(nameof(InvalidUserName), "Das sieht nicht nach einer E-Mail-Adresse aus.");
    public override IdentityError InvalidToken() => Make(nameof(InvalidToken), "Der Link ist abgelaufen oder wurde schon benutzt – bitte einen neuen anfordern.");
    public override IdentityError PasswordMismatch() => Make(nameof(PasswordMismatch), "Das aktuelle Passwort stimmt nicht.");
    public override IdentityError PasswordTooShort(int length) => Make(nameof(PasswordTooShort), $"Das Passwort braucht mindestens {length} Zeichen.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Make(nameof(PasswordRequiresUniqueChars), $"Das Passwort braucht mindestens {uniqueChars} verschiedene Zeichen.");
    public override IdentityError PasswordRequiresDigit() => Make(nameof(PasswordRequiresDigit), "Das Passwort braucht mindestens eine Ziffer.");
    public override IdentityError PasswordRequiresLower() => Make(nameof(PasswordRequiresLower), "Das Passwort braucht mindestens einen Kleinbuchstaben.");
    public override IdentityError PasswordRequiresUpper() => Make(nameof(PasswordRequiresUpper), "Das Passwort braucht mindestens einen Großbuchstaben.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Make(nameof(PasswordRequiresNonAlphanumeric), "Das Passwort braucht mindestens ein Sonderzeichen.");
    public override IdentityError UserAlreadyHasPassword() => Make(nameof(UserAlreadyHasPassword), "Dieses Konto hat schon ein Passwort.");
    public override IdentityError ConcurrencyFailure() => Make(nameof(ConcurrencyFailure), "Das Konto wurde gerade an anderer Stelle geändert – bitte neu laden und noch einmal versuchen.");

    private static IdentityError Make(string code, string description) => new() { Code = code, Description = description };
}
