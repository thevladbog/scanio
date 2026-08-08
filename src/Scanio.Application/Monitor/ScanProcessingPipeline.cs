using System.Diagnostics;
using Scanio.Analysis;
using Scanio.Capture;
using Scanio.Domain.Analysis;
using Scanio.Transports;

namespace Scanio.Application.Monitor;

public interface IScanProcessingPipeline
{
    Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken);
}

internal interface IScanProcessingClock
{
    long GetTimestamp();

    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class ScanProcessingPipeline : IScanProcessingPipeline
{
    private readonly ScanAssembler _assembler;
    private readonly PayloadTextEncoding _encoding;
    private readonly ScanAnalyzerPipeline _analyzers;
    private readonly LiveMonitor _monitor;
    private readonly IScanProcessingClock _clock;

    public ScanProcessingPipeline(
        ScanAssembler assembler,
        PayloadTextEncoding encoding,
        ScanAnalyzerPipeline analyzers,
        LiveMonitor monitor)
        : this(assembler, encoding, analyzers, monitor, StopwatchProcessingClock.Instance)
    {
    }

    internal ScanProcessingPipeline(
        ScanAssembler assembler,
        PayloadTextEncoding encoding,
        ScanAnalyzerPipeline analyzers,
        LiveMonitor monitor,
        IScanProcessingClock clock)
    {
        ArgumentNullException.ThrowIfNull(assembler);
        ArgumentNullException.ThrowIfNull(analyzers);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(clock);

        _assembler = assembler;
        _encoding = encoding;
        _analyzers = analyzers;
        _monitor = monitor;
        _clock = clock;
    }

    public async Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);

        await using var reader = transport.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        CancellationTokenSource? silenceCancellation = null;
        Task? silenceDelay = null;
        long? lastChunkTimestamp = null;

        try
        {
            var moveNext = reader.MoveNextAsync().AsTask();
            while (true)
            {
                if (silenceDelay is null)
                {
                    await moveNext.ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAny(moveNext, silenceDelay).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // At the exact timeout boundary, a chunk that already arrived
                // takes precedence. This lets its terminator complete the scan
                // before the silence path can observe the same pending bytes.
                if (moveNext.IsCompleted)
                {
                    await CancelSilenceDelayAsync(silenceCancellation, silenceDelay).ConfigureAwait(false);
                    silenceCancellation = null;
                    silenceDelay = null;

                    if (!await moveNext.ConfigureAwait(false))
                    {
                        break;
                    }

                    var chunk = reader.Current;
                    lastChunkTimestamp = chunk.MonotonicTimestamp;
                    foreach (var scan in _assembler.Push(chunk))
                    {
                        Append(scan);
                    }

                    moveNext = reader.MoveNextAsync().AsTask();
                    if (_assembler.HasPending)
                    {
                        (silenceCancellation, silenceDelay) = StartSilenceDelay(
                            lastChunkTimestamp.Value,
                            cancellationToken);
                    }

                    continue;
                }

                await silenceDelay!.ConfigureAwait(false);
                silenceCancellation!.Dispose();
                silenceCancellation = null;
                silenceDelay = null;
                cancellationToken.ThrowIfCancellationRequested();

                var completed = _assembler.CompleteOnSilence(_clock.GetTimestamp());
                if (completed is not null)
                {
                    Append(completed);
                    lastChunkTimestamp = null;
                }
                else if (_assembler.HasPending && lastChunkTimestamp is not null)
                {
                    (silenceCancellation, silenceDelay) = StartSilenceDelay(
                        lastChunkTimestamp.Value,
                        cancellationToken);
                }
            }
        }
        finally
        {
            await CancelSilenceDelayAsync(silenceCancellation, silenceDelay).ConfigureAwait(false);
            // A partial scan belongs to this connection only. Disconnecting or
            // removing the device must never merge its bytes into a later session.
            _assembler.DiscardPending();
        }
    }

    private (CancellationTokenSource Cancellation, Task Delay) StartSilenceDelay(
        long lastChunkTimestamp,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetTimestamp();
        var elapsed = now < lastChunkTimestamp
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(lastChunkTimestamp, now);
        var remaining = _assembler.Framing.SilenceTimeout - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var silenceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = _clock.DelayAsync(remaining, silenceCancellation.Token).AsTask();
        return (silenceCancellation, delay);
    }

    private void Append(Scanio.Domain.Capture.CompletedScan scan)
    {
        var decoded = TextDecoder.Decode(scan.PayloadBytes.AsSpan(), _encoding);
        var analyses = _analyzers.Analyze(decoded);
        _monitor.Append(scan, decoded, analyses);
    }

    private static async Task CancelSilenceDelayAsync(
        CancellationTokenSource? cancellation,
        Task? delay)
    {
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (delay is not null)
            {
                await delay.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Resetting the deadline deliberately cancels the previous delay.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private sealed class StopwatchProcessingClock : IScanProcessingClock
    {
        public static StopwatchProcessingClock Instance { get; } = new();

        public long GetTimestamp() => Stopwatch.GetTimestamp();

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            new(Task.Delay(delay, cancellationToken));
    }
}
