namespace XeonProductions.Domain.Entities;

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
