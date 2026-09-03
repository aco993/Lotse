using Lotse.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace Lotse.Web.Components.Account.Shared;

/// <summary>
/// No SMTP is configured on this machine (see README — the project deliberately keeps no email/API keys in the
/// repo or on disk). Confirmation and password-reset links are logged instead of mailed — exactly what the
/// official ASP.NET Core Identity template does by default when no real <see cref="IEmailSender{TUser}"/> is
/// wired up. The registration/reset flows are fully implemented and tested end to end; swapping this class for a
/// real sender (SendGrid, SMTP, ...) is the only change needed to deliver actual email.
/// </summary>
internal sealed class IdentityNoOpEmailSender(ILogger<IdentityNoOpEmailSender> logger) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        logger.LogWarning("Kein E-Mail-Versand konfiguriert. Bestätigungslink für {Email}: {Link}", email, confirmationLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        logger.LogWarning("Kein E-Mail-Versand konfiguriert. Link zum Zurücksetzen des Passworts für {Email}: {Link}", email, resetLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        logger.LogWarning("Kein E-Mail-Versand konfiguriert. Code zum Zurücksetzen des Passworts für {Email}: {Code}", email, resetCode);
        return Task.CompletedTask;
    }
}
