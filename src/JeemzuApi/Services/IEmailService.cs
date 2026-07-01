namespace JeemzuApi.Services;

public interface IEmailService
{
    Task<bool> SendContactEmailAsync(string subject, string content, CancellationToken ct = default);
}
