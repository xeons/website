using System.Threading.Channels;
using Microsoft.Extensions.Options;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Services;

/// <summary>
/// The queue between the request and the database.
///
/// Writing a row inside the request would put a database round trip on every page the site
/// serves, so views are handed to a bounded channel and written in batches by
/// <see cref="StatsWriter"/>. The channel drops rather than waits when full: statistics are
/// not worth making a visitor wait for, and a burst that outruns the writer should cost
/// accuracy rather than latency.
/// </summary>
public class StatsRecorder : IStatsRecorder
{
    private readonly Channel<PageView> _views;
    private readonly Channel<(Guid ViewId, int Seconds)> _durations;
    private long _dropped;

    public StatsRecorder(IOptions<StatsOptions> options)
    {
        var capacity = Math.Max(100, options.Value.QueueCapacity);

        var channelOptions = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        };

        _views = Channel.CreateBounded<PageView>(channelOptions);
        _durations = Channel.CreateBounded<(Guid, int)>(channelOptions);
    }

    public ChannelReader<PageView> Views => _views.Reader;
    public ChannelReader<(Guid ViewId, int Seconds)> Durations => _durations.Reader;

    public long Dropped => Interlocked.Read(ref _dropped);

    public bool Record(PageView view)
    {
        if (_views.Writer.TryWrite(view)) return true;

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public bool RecordDuration(Guid viewId, int seconds) =>
        _durations.Writer.TryWrite((viewId, seconds));
}
