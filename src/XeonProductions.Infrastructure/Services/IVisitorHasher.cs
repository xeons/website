using System.Net;

namespace XeonProductions.Infrastructure.Services;

public interface IVisitorHasher
{
    /// <summary>
    /// An opaque identifier for one visitor on one day. The address is never stored, and the
    /// value cannot be reversed or matched against tomorrow's.
    /// </summary>
    Task<string> VisitorAsync(IPAddress? address, string? userAgent, DateTimeOffset when,
        CancellationToken ct = default);

    /// <summary>
    /// Groups a visitor's views into one visit. Changes when the session window elapses, so
    /// a return later in the day counts as a new session.
    /// </summary>
    string Session(string visitorHash, DateTimeOffset when, TimeSpan window);
}
