using Resend;
using System.Web;

namespace JeemzuApi.Services;

public class EmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly string _to = "jamesfriedenberg@gmail.com";
    private readonly string _from;
    private readonly string _accountFrom;
    private readonly bool _configured;

    public EmailService(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _from = config["Resend:From"] ?? "Contact Form <onboarding@resend.dev>";
        _accountFrom = config["Resend:AccountFrom"] ?? _from;
        _configured = !string.IsNullOrEmpty(config["Resend:ApiKey"]);
    }

    public async Task<bool> SendContactEmailAsync(string subject, string content, CancellationToken ct = default)
    {
        if (!_configured)
            return false;

        var safeContent = HttpUtility.HtmlEncode(content).Replace("\n", "<br>");

        var message = new EmailMessage
        {
            From = _from,
            Subject = $"[jeemzu.me] {subject}",
            HtmlBody = $"<p>{safeContent}</p>",
        };
        message.To.Add(_to);

        var response = await _resend.EmailSendAsync(message, ct);
        return response.Success;
    }

    public Task<bool> SendEmailVerificationAsync(string to, string username, string link, CancellationToken ct = default) =>
        SendAccountEmailAsync(
            to,
            "Verify your email address",
            username,
            "Confirm this address so you can reset your password if you ever forget it.",
            "Verify email",
            link,
            "This link expires in 24 hours.",
            ct);

    public Task<bool> SendPasswordResetAsync(string to, string username, string link, CancellationToken ct = default) =>
        SendAccountEmailAsync(
            to,
            "Reset your password",
            username,
            "Use the link below to choose a new password.",
            "Reset password",
            link,
            "This link expires in 1 hour and can only be used once. If you didn't request it, you can ignore this email.",
            ct);

    private async Task<bool> SendAccountEmailAsync(
        string to, string subject, string username, string intro,
        string action, string link, string footer, CancellationToken ct)
    {
        if (!_configured)
            return false;

        var safeUsername = HttpUtility.HtmlEncode(username);
        var safeLink = HttpUtility.HtmlAttributeEncode(link);

        var message = new EmailMessage
        {
            From = _accountFrom,
            Subject = $"[jeemzu.me] {subject}",
            HtmlBody =
                $"<p>Hi {safeUsername},</p>" +
                $"<p>{HttpUtility.HtmlEncode(intro)}</p>" +
                $"<p><a href=\"{safeLink}\">{HttpUtility.HtmlEncode(action)}</a></p>" +
                $"<p>{HttpUtility.HtmlEncode(footer)}</p>",
        };
        message.To.Add(to);

        var response = await _resend.EmailSendAsync(message, ct);
        return response.Success;
    }
}
