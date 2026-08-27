namespace XeonProductions.Domain.Entities;

public class Tag
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public List<Post> Posts { get; set; } = [];
}
