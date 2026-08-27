namespace XeonProductions.Web.Services;

/// <summary>
/// The result of claiming a transfer slot. <paramref name="Slot"/> is set only when allowed,
/// and must be disposed when the response finishes.
/// </summary>
public record TrafficDecision(TrafficVerdict Verdict, IDisposable? Slot, int RetryAfterSeconds)
{
    public bool Allowed => Verdict == TrafficVerdict.Allowed;
}
