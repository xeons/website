using XeonProductions.Domain.Entities;

namespace XeonProductions.Web.Services;

public interface IStatsRecorder
{
    /// <summary>
    /// Queues a view to be written. Returns false when the queue is full, in which case the
    /// view is dropped rather than made to wait.
    /// </summary>
    bool Record(PageView view);

    /// <summary>Queues a dwell time reported by the beacon.</summary>
    bool RecordDuration(Guid viewId, int seconds);

    /// <summary>Views dropped because the queue was full, for the admin to see.</summary>
    long Dropped { get; }
}
