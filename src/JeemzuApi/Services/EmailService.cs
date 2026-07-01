using Resend;
using System.Web;

namespace JeemzuApi.Services;

public class EmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly string _to = "jamesfriedenberg@gmail.com";
    private readonly string _from;

    public EmailService(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _from = config["Resend:From"] ?? "Contact Form <onboarding@resend.dev>";
    }

    public async Task<bool> SendContactEmailAsync(string subject, string content, CancellationToken ct = default)
    {
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
}
