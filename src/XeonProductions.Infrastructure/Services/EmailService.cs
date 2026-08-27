using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace XeonProductions.Infrastructure.Services;

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseStartTls { get; set; } = true;

    public string FromAddress { get; set; } = "noreply@xeonproductions.com";
    public string FromName { get; set; } = "Xeon Productions";

    /// <summary>Where contact form submissions are delivered.</summary>
    public string? NotificationAddress { get; set; }

    /// <summary>When false, mail is logged instead of sent. Keeps development quiet.</summary>
    public bool Enabled { get; set; }
}

public interface IEmailService
{
    Task<bool> SendAsync(string to, string subject, string body, bool isHtml = false,
        string? replyTo = null, CancellationToken ct = default);

    Task<bool> SendNotificationAsync(string subject, string body, string? replyTo = null,
        CancellationToken ct = default);
}

public class EmailService(IOptions<SmtpOptions> options, ILogger<EmailService> logger) : IEmailService
{
    private readonly SmtpOptions _opts = options.Value;

    public async Task<bool> SendAsync(string to, string subject, string body, bool isHtml = false,
        string? replyTo = null, CancellationToken ct = default)
    {
        if (!_opts.Enabled || string.IsNullOrWhiteSpace(_opts.Host))
        {
            logger.LogInformation("SMTP disabled. Would have sent to {To}: {Subject}", to, subject);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            if (!string.IsNullOrWhiteSpace(replyTo) &&
                MailboxAddress.TryParse(replyTo, out var reply))
            {
                message.ReplyTo.Add(reply);
            }

            message.Body = new TextPart(isHtml ? TextFormat.Html : TextFormat.Plain) { Text = body };

            using var client = new SmtpClient();
            var security = _opts.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.SslOnConnect;

            await client.ConnectAsync(_opts.Host, _opts.Port, security, ct);

            if (!string.IsNullOrEmpty(_opts.Username))
                await client.AuthenticateAsync(_opts.Username, _opts.Password ?? string.Empty, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            return true;
        }
        catch (Exception ex)
        {
            // Callers persist the message regardless, so a relay outage never loses data.
            logger.LogError(ex, "Failed to send mail to {To}", to);
            return false;
        }
    }

    public Task<bool> SendNotificationAsync(string subject, string body, string? replyTo = null,
        CancellationToken ct = default)
    {
        var to = _opts.NotificationAddress ?? _opts.FromAddress;
        return SendAsync(to, subject, body, isHtml: false, replyTo, ct);
    }
}
