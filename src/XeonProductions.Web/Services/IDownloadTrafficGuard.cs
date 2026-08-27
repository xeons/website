namespace XeonProductions.Web.Services;

public interface IDownloadTrafficGuard
{
    /// <summary>
    /// Claims a transfer slot for one client. Dispose the slot on the returned decision when
    /// the response finishes; the concurrency count does not fall until then. A limit of zero
    /// or less disables that limit.
    /// </summary>
    TrafficDecision TryStart(string clientKey, int maxPerHour, int maxConcurrent);
}
