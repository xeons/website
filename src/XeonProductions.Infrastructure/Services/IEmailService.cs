namespace XeonProductions.Infrastructure.Services;

public interface IEmailService
{
    Task<bool> SendAsync(string to, string subject, string body, bool isHtml = false,
        string? replyTo = null, CancellationToken ct = default);

    Task<bool> SendNotificationAsync(string subject, string body, string? replyTo = null,
        CancellationToken ct = default);
}
