using System.Diagnostics;

namespace XeonProductions.Web.Services;

/// <summary>
/// Reads a stored file for the response, optionally rate limited, holding the client's
/// concurrency slot until disposed. Seeking is forwarded so Range requests work.
/// </summary>
public sealed class DownloadTransferStream : Stream
{
    private readonly Stream _inner;
    private readonly IDisposable? _slot;
    private readonly long _bytesPerSecond;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _delivered;
    private bool _disposed;

    /// <param name="bytesPerSecond">Zero or less leaves the transfer unthrottled.</param>
    public DownloadTransferStream(Stream inner, IDisposable? slot, long bytesPerSecond)
    {
        _inner = inner;
        _slot = slot;
        _bytesPerSecond = bytesPerSecond;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        if (read > 0) await ThrottleAsync(read, cancellationToken);

        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);

        if (read > 0 && _bytesPerSecond > 0)
        {
            _delivered += read;

            var behind = TargetElapsed() - _clock.Elapsed;
            if (behind > TimeSpan.Zero) Thread.Sleep(behind);
        }

        return read;
    }

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <summary>
    /// Delays until the elapsed time matches the bytes delivered, holding an average rate
    /// over the whole transfer rather than capping each read.
    /// </summary>
    private async ValueTask ThrottleAsync(int read, CancellationToken ct)
    {
        if (_bytesPerSecond <= 0) return;

        _delivered += read;

        var behind = TargetElapsed() - _clock.Elapsed;
        if (behind > TimeSpan.Zero) await Task.Delay(behind, ct);
    }

    private TimeSpan TargetElapsed() =>
        TimeSpan.FromSeconds(_delivered / (double)_bytesPerSecond);

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _inner.Dispose();
            _slot?.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _inner.DisposeAsync();
        _slot?.Dispose();

        await base.DisposeAsync();
    }
}
