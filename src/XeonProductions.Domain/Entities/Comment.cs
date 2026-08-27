using XeonProductions.Domain.Enums;

namespace XeonProductions.Domain.Entities;

public class Comment
{
    public int Id { get; set; }

    public int PostId { get; set; }
    public Post? Post { get; set; }

    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public string? AuthorUrl { get; set; }

    /// <summary>Stored as plain text and escaped on render; comments never carry HTML.</summary>
    public string Body { get; set; } = string.Empty;

    public CommentStatus Status { get; set; } = CommentStatus.Pending;

    public int? ParentId { get; set; }
    public Comment? Parent { get; set; }
    public List<Comment> Replies { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
