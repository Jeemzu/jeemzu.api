namespace JeemzuApi.Services;

public interface IEmailService
{
    Task<bool> SendContactEmailAsync(string subject, string content, CancellationToken ct = default);

    /// <summary>Sends the email-verification link to a newly registered (or re-requesting) user.</summary>
    Task<bool> SendEmailVerificationAsync(string to, string username, string link, CancellationToken ct = default);

    /// <summary>Sends the password-reset link. Only ever called for a verified address.</summary>
    Task<bool> SendPasswordResetAsync(string to, string username, string link, CancellationToken ct = default);
}
