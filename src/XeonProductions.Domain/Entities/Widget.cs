using XeonProductions.Domain.Enums;

namespace XeonProductions.Domain.Entities;

public class Widget
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public WidgetType Type { get; set; }
    public WidgetArea Area { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Body for Html and About widgets.</summary>
    public string? HtmlContent { get; set; }

    /// <summary>Cap for the dynamic widget types: recent posts, categories, tags, feeds.</summary>
    public int MaxItems { get; set; } = 5;

    /// <summary>Source for an <see cref="WidgetType.RssFeed"/> widget.</summary>
    public string? FeedUrl { get; set; }

    /// <summary>Show the publication date beside each feed item.</summary>
    public bool ShowFeedDates { get; set; } = true;

    public List<WidgetLink> Links { get; set; } = [];
}

public class WidgetLink
{
    public int Id { get; set; }

    public int WidgetId { get; set; }
    public Widget? Widget { get; set; }

    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int SortOrder { get; set; }
    public bool OpenInNewTab { get; set; } = true;

    /// <summary>Marks affiliate links so the renderer adds rel=sponsored.</summary>
    public bool IsSponsored { get; set; }
}
