namespace XeonProductions.Infrastructure.Services;

public record FeedItem(string Title, string Link, DateTimeOffset? Published, string? Summary);
