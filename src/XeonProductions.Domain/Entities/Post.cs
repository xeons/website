using XeonProductions.Domain.Enums;

namespace XeonProductions.Domain.Entities;

public class Post
{
    public int Id { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>Short summary shown in archive listings. Auto-derived from content when blank.</summary>
    public string? Excerpt { get; set; }

    /// <summary>Sanitised HTML body.</summary>
    public string ContentHtml { get; set; } = string.Empty;

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    /// <summary>The moment the post becomes publicly visible. Null while it is a draft.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? FeaturedImageId { get; set; }
    public MediaItem? FeaturedImage { get; set; }

    public string? AuthorId { get; set; }
    public ApplicationUser? Author { get; set; }

    /// <summary>Pinned to the top of the blog index.</summary>
    public bool IsSticky { get; set; }

    public bool AllowComments { get; set; } = true;

    public int ViewCount { get; set; }

    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? CanonicalUrl { get; set; }
    public bool NoIndex { get; set; }
    public string? SocialImageUrl { get; set; }

    public List<Category> Categories { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
    public List<Comment> Comments { get; set; } = [];

    public bool IsVisible =>
        Status == ContentStatus.Published && PublishedAt <= DateTimeOffset.UtcNow;
}
