namespace XeonProductions.Domain.Entities;

public class ContactMessage
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRead { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>Set when the honeypot or rate limiter flagged the submission.</summary>
    public bool IsSpam { get; set; }

    /// <summary>False when the SMTP relay failed; the row is still kept so nothing is lost.</summary>
    public bool WasEmailed { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
