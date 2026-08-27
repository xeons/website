using Microsoft.AspNetCore.Identity;

namespace XeonProductions.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? WebsiteUrl { get; set; }

    /// <summary>Used for the author archive at /author/{slug}.</summary>
    public string? Slug { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public List<Post> Posts { get; set; } = [];
}
