using XeonProductions.Domain.Enums;

namespace XeonProductions.Domain.Entities;

public class Menu
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MenuLocation Location { get; set; }
    public List<MenuItem> Items { get; set; } = [];
}
