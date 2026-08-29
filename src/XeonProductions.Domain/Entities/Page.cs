using XeonProductions.Domain.Enums;

namespace XeonProductions.Domain.Entities;

public class Page
{
    public int Id { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;

    public ContentStatus Status { get; set; } = ContentStatus.Draft;
    public PageTemplate Template { get; set; } = PageTemplate.Default;

    /// <summary>Pages nest, so /snippets/c-simple-list resolves through the parent chain.</summary>
    public int? ParentId { get; set; }
    public Page? Parent { get; set; }
    public List<Page> Children { get; set; } = [];

    public int MenuOrder { get; set; }

    /// <summary>Hide the H1 when the content supplies its own hero heading.</summary>
    public bool ShowTitle { get; set; } = true;

    /// <summary>
    /// Append a list of the child pages to the content. Off by default: a page that has
    /// children usually introduces them in its own words, and the automatic list is then
    /// the same links a second time.
    /// </summary>
    public bool ShowChildLinks { get; set; }

    public int? FeaturedImageId { get; set; }
    public MediaItem? FeaturedImage { get; set; }

    public string? AuthorId { get; set; }
    public ApplicationUser? Author { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? CanonicalUrl { get; set; }
    public bool NoIndex { get; set; }
    public string? SocialImageUrl { get; set; }

    public bool IsVisible =>
        Status == ContentStatus.Published && (PublishedAt is null || PublishedAt <= DateTimeOffset.UtcNow);
}
