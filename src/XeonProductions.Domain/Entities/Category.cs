namespace XeonProductions.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = [];

    public int SortOrder { get; set; }

    public List<Post> Posts { get; set; } = [];
}
