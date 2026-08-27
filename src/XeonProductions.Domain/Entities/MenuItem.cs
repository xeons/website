namespace XeonProductions.Domain.Entities;

public class MenuItem
{
    public int Id { get; set; }

    public int MenuId { get; set; }
    public Menu? Menu { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>Absolute URL, or a site-relative path such as /about</summary>
    public string Url { get; set; } = "/";

    public int? ParentId { get; set; }
    public MenuItem? Parent { get; set; }
    public List<MenuItem> Children { get; set; } = [];

    public int SortOrder { get; set; }
    public bool OpenInNewTab { get; set; }

    /// <summary>Inline SVG markup, used by social menus.</summary>
    public string? IconSvg { get; set; }

    public string? CssClass { get; set; }
}
