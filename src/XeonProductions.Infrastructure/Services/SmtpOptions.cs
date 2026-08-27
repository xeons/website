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
