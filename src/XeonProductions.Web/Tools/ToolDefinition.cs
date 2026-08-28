namespace XeonProductions.Web.Tools;

/// <summary>
/// One tool on the index.
/// </summary>
/// <param name="Slug">URL segment under /tools. Lower case, hyphenated.</param>
/// <param name="Name">Heading and card title.</param>
/// <param name="Summary">One line shown on the index card.</param>
/// <param name="Description">Meta description for the tool's own page.</param>
/// <param name="Category">Section the card appears under.</param>
/// <param name="ComponentType">Component rendered into the tool page body.</param>
public sealed record ToolDefinition(
    string Slug,
    string Name,
    string Summary,
    string Description,
    ToolCategory Category,
    Type ComponentType);
