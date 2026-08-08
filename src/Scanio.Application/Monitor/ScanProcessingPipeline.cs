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

        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reader = transport.ReadAllAsync(readCancellation.Token).GetAsyncEnumerator(readCancellation.Token);
        CancellationTokenSource? silenceCancellation = null;
        Task? silenceDelay = null;
        long? lastChunkTimestamp = null;
        long? silenceDeadlineTimestamp = null;
        Task<bool>? moveNext = null;
        var externalCancellationObserved = false;

        try
        {
            moveNext = reader.MoveNextAsync().AsTask();
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

                if (moveNext.IsCompleted)
                {
                    if (!await moveNext.ConfigureAwait(false))
                    {
                        break;
                    }

                    var chunk = reader.Current;

                    if (chunk.Bytes.IsEmpty)
                    {
                        if (silenceDelay?.IsCompleted == true)
                        {
                            await silenceDelay.ConfigureAwait(false);
                            silenceCancellation!.Dispose();
                            silenceCancellation = null;
                            silenceDelay = null;
                            silenceDeadlineTimestamp = null;
                            cancellationToken.ThrowIfCancellationRequested();

                            if (CompleteOnSilence(_clock.GetTimestamp()))
                            {
                                lastChunkTimestamp = null;
                            }
                            else if (_assembler.HasPending && lastChunkTimestamp is not null)
                            {
                                (silenceCancellation, silenceDelay, silenceDeadlineTimestamp) =
                                    StartSilenceDelay(lastChunkTimestamp.Value, cancellationToken);
                            }
                        }

                        moveNext = reader.MoveNextAsync().AsTask();
                        continue;
                    }

                    var deadlineReached = silenceDeadlineTimestamp is not null &&
                        _clock.GetTimestamp() >= silenceDeadlineTimestamp.Value;
                    var startsNewScan = deadlineReached &&
                        chunk.MonotonicTimestamp > silenceDeadlineTimestamp!.Value;

                    if (silenceDelay is not null)
                    {
                        await CancelSilenceDelayAsync(silenceCancellation, silenceDelay).ConfigureAwait(false);
                        silenceCancellation = null;
                        silenceDelay = null;
                        cancellationToken.ThrowIfCancellationRequested();

                        if (startsNewScan && CompleteOnSilence(silenceDeadlineTimestamp!.Value))
                        {
                            lastChunkTimestamp = null;
                        }

                        silenceDeadlineTimestamp = null;
                    }

                    // A chunk timestamp at or before the deadline belongs to the
                    // pending scan, so its terminator takes precedence. A strictly
                    // later chunk is processed only after the old scan completes.
                    lastChunkTimestamp = chunk.MonotonicTimestamp;
                    foreach (var scan in _assembler.Push(chunk))
                    {
                        Append(scan);
                    }

                    if (_assembler.HasPending)
                    {
                        (silenceCancellation, silenceDelay, silenceDeadlineTimestamp) =
                            StartSilenceDelay(lastChunkTimestamp.Value, cancellationToken);
                    }
                    else
                    {
                        lastChunkTimestamp = null;
                    }

                    moveNext = reader.MoveNextAsync().AsTask();
                    continue;
                }

                await silenceDelay!.ConfigureAwait(false);
                silenceCancellation!.Dispose();
                silenceCancellation = null;
                silenceDelay = null;
                silenceDeadlineTimestamp = null;
                cancellationToken.ThrowIfCancellationRequested();

                if (CompleteOnSilence(_clock.GetTimestamp()))
                {
                    lastChunkTimestamp = null;
                }
                else if (_assembler.HasPending && lastChunkTimestamp is not null)
                {
                    (silenceCancellation, silenceDelay, silenceDeadlineTimestamp) =
                        StartSilenceDelay(lastChunkTimestamp.Value, cancellationToken);
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            externalCancellationObserved = true;
        }
        finally
        {
            readCancellation.Cancel();
            await CancelSilenceDelayAsync(silenceCancellation, silenceDelay).ConfigureAwait(false);
            await ObservePendingReadAsync(moveNext).ConfigureAwait(false);
            // A partial scan belongs to this connection only. Disconnecting or
            // removing the device must never merge its bytes into a later session.
            _assembler.DiscardPending();

            try
            {
                await reader.DisposeAsync().ConfigureAwait(false);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                externalCancellationObserved = true;
            }
        }

        if (externalCancellationObserved || cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private (CancellationTokenSource Cancellation, Task Delay, long DeadlineTimestamp) StartSilenceDelay(
        long lastChunkTimestamp,
        CancellationToken cancellationToken)
    {
        var deadlineTimestamp = AddStopwatchDuration(
            lastChunkTimestamp,
            _assembler.Framing.SilenceTimeout);
        var now = _clock.GetTimestamp();
        var remaining = now >= deadlineTimestamp
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(now, deadlineTimestamp);

        var silenceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = _clock.DelayAsync(remaining, silenceCancellation.Token).AsTask();
        return (silenceCancellation, delay, deadlineTimestamp);
    }

    private bool CompleteOnSilence(long timestamp)
    {
        var completed = _assembler.CompleteOnSilence(timestamp);
        if (completed is null)
        {
            return false;
        }

        Append(completed);
        return true;
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

    private static async Task ObservePendingReadAsync(Task<bool>? moveNext)
    {
        if (moveNext is null)
        {
            return;
        }

        try
        {
            await moveNext.ConfigureAwait(false);
        }
        catch
        {
            // Cancellation or a transport failure is already represented by the
            // pipeline outcome. The read must still be observed before disposal.
        }
    }

    private static long AddStopwatchDuration(long timestamp, TimeSpan duration)
    {
        var ticks = (long)Math.Ceiling(duration.TotalSeconds * Stopwatch.Frequency);
        return timestamp > long.MaxValue - ticks
            ? long.MaxValue
            : timestamp + ticks;
    }

    private sealed class StopwatchProcessingClock : IScanProcessingClock
    {
        public static StopwatchProcessingClock Instance { get; } = new();

        public long GetTimestamp() => Stopwatch.GetTimestamp();

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            new(Task.Delay(delay, cancellationToken));
    }
}
