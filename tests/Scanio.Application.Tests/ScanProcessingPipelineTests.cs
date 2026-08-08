using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Scanio.Analysis;
using Scanio.Application.Monitor;
using Scanio.Capture;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Transports;

namespace Scanio.Application.Tests;

[TestClass]
public sealed class ScanProcessingPipelineTests
{
    private static readonly TransportIdentity Identity =
        new(TransportKind.Serial, "COM7", "COM7");

    [TestMethod]
    public async Task ProcessAsync_AssemblesDecodesAnalyzesAndContinuesAfterAnalyzerFailure()
    {
        var monitor = new LiveMonitor();
        var pipeline = CreatePipeline(monitor, new ThrowingAnalyzer(), new PlainTextAnalyzer());
        var transport = new ChunkTransport(
            RawChunk.Create(1, "first\rsecond\r"u8, DateTimeOffset.UnixEpoch, 1, Identity));

        await pipeline.ProcessAsync(transport, CancellationToken.None);

        Assert.HasCount(2, monitor.Events);
        Assert.AreEqual("first", monitor.Events[0].Decoded.Text);
        Assert.AreEqual("second", monitor.Events[1].Decoded.Text);
        Assert.AreEqual("Throwing", monitor.Events[0].Analyses[0].AnalyzerName);
        Assert.AreEqual("Plain text", monitor.Events[0].Analyses[1].Format);
    }

    [TestMethod]
    public async Task ProcessAsync_CancellationDoesNotCompleteAPartialScan()
    {
        var monitor = new LiveMonitor();
        var pipeline = CreatePipeline(monitor, new PlainTextAnalyzer());
        var transport = new ChunkThenBlockTransport(
            RawChunk.Create(
                1,
                "partial"u8,
                DateTimeOffset.UnixEpoch,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                Identity));
        using var cancellation = new CancellationTokenSource();

        var processing = pipeline.ProcessAsync(transport, cancellation.Token);
        await transport.ChunkDelivered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
        Assert.IsEmpty(monitor.Events);

        await pipeline.ProcessAsync(
            new ChunkTransport(RawChunk.Create(
                2,
                "fresh\r"u8,
                DateTimeOffset.UnixEpoch,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                Identity)),
            CancellationToken.None);

        Assert.HasCount(1, monitor.Events);
        Assert.AreEqual("fresh", monitor.Events[0].Decoded.Text);
    }

    [TestMethod]
    public async Task ProcessAsync_PreservesRawBytesAndCountsByteExactDuplicates()
    {
        var monitor = new LiveMonitor();
        var pipeline = CreatePipeline(monitor, new PlainTextAnalyzer());
        var transport = new ChunkTransport(
            RawChunk.Create(1, [0x31, 0x0D, 0x31, 0x0D], DateTimeOffset.UnixEpoch, 1, Identity));

        await pipeline.ProcessAsync(transport, CancellationToken.None);

        Assert.HasCount(2, monitor.Events);
        CollectionAssert.AreEqual(new byte[] { 0x31, 0x0D }, monitor.Events[0].Scan.RawBytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x31 }, monitor.Events[0].Decoded.Bytes.ToArray());
        Assert.AreEqual(2, monitor.Events[0].DuplicateCount);
        Assert.AreEqual(2, monitor.Events[1].DuplicateCount);
    }

    [TestMethod]
    public async Task ProcessAsync_CompletesANonTerminatedPayloadAfterSilence()
    {
        var monitor = new LiveMonitor();
        var clock = new ManualProcessingClock();
        var pipeline = CreatePipeline(monitor, clock, new PlainTextAnalyzer());
        var transport = new ControlledChunkTransport();
        using var cancellation = new CancellationTokenSource();
        var appended = NextAppendAsync(monitor);
        var processing = pipeline.ProcessAsync(transport, cancellation.Token);

        transport.Add(RawChunk.Create(1, "silent"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await clock.WaitForNextTimerAsync();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await appended.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.HasCount(1, monitor.Events);
        Assert.AreEqual("silent", monitor.Events[0].Decoded.Text);
        Assert.AreEqual(ScanCompletionReason.SilenceTimeout, monitor.Events[0].Scan.CompletionReason);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    [TestMethod]
    public async Task ProcessAsync_ResetsSilenceDeadlineWhenAnotherChunkArrives()
    {
        var monitor = new LiveMonitor();
        var clock = new ManualProcessingClock();
        var pipeline = CreatePipeline(monitor, clock, new PlainTextAnalyzer());
        var transport = new ControlledChunkTransport();
        using var cancellation = new CancellationTokenSource();
        var processing = pipeline.ProcessAsync(transport, cancellation.Token);

        transport.Add(RawChunk.Create(1, "a"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await clock.WaitForNextTimerAsync();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        transport.Add(RawChunk.Create(2, "b"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await clock.WaitForNextTimerAsync();

        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.IsEmpty(monitor.Events);
        var appended = NextAppendAsync(monitor);
        clock.Advance(TimeSpan.FromMilliseconds(51));
        await appended.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.HasCount(1, monitor.Events);
        Assert.AreEqual("ab", monitor.Events[0].Decoded.Text);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    [TestMethod]
    public async Task ProcessAsync_TerminatorCompletionPreventsATimerDuplicate()
    {
        var monitor = new LiveMonitor();
        var clock = new ManualProcessingClock();
        var pipeline = CreatePipeline(monitor, clock, new PlainTextAnalyzer());
        var transport = new ControlledChunkTransport();
        using var cancellation = new CancellationTokenSource();
        var appended = NextAppendAsync(monitor);
        var processing = pipeline.ProcessAsync(transport, cancellation.Token);

        transport.Add(RawChunk.Create(1, "done\r"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await appended.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.HasCount(1, monitor.Events);
        Assert.AreEqual(0, clock.ScheduledCount);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        await Task.Yield();

        Assert.HasCount(1, monitor.Events);
        Assert.AreEqual(ScanCompletionReason.Terminator, monitor.Events[0].Scan.CompletionReason);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    [TestMethod]
    public async Task ProcessAsync_CancellationWinsOverPendingSilenceCompletion()
    {
        var monitor = new LiveMonitor();
        var clock = new ManualProcessingClock();
        var pipeline = CreatePipeline(monitor, clock, new PlainTextAnalyzer());
        var transport = new ControlledChunkTransport();
        using var cancellation = new CancellationTokenSource();
        var processing = pipeline.ProcessAsync(transport, cancellation.Token);

        transport.Add(RawChunk.Create(1, "partial"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await clock.WaitForNextTimerAsync();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        await Task.Yield();
        Assert.IsEmpty(monitor.Events);
    }

    [TestMethod]
    public async Task ProcessAsync_CancellationWaitsForPendingReadBeforeDisposalAndAlwaysThrowsCancellation()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var monitor = new LiveMonitor();
            var pipeline = CreatePipeline(monitor, new PlainTextAnalyzer());
            var transport = new CancellationSensitiveTransport();
            using var cancellation = new CancellationTokenSource();
            var processing = pipeline.ProcessAsync(transport, cancellation.Token);

            await transport.SecondMoveStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            cancellation.Cancel();
            await transport.ReadCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var earlyDisposal = await Task.WhenAny(
                transport.EnumeratorDisposed.Task,
                Task.Delay(TimeSpan.FromMilliseconds(5)));
            transport.AllowReadCancellationToFinish.TrySetResult();

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
            Assert.AreNotSame(transport.EnumeratorDisposed.Task, earlyDisposal, $"Iteration {iteration}");
            Assert.IsFalse(transport.DisposedWhileMoveNextInFlight, $"Iteration {iteration}");
            Assert.AreEqual(1, transport.EnumeratorDisposeCount, $"Iteration {iteration}");
            Assert.IsEmpty(monitor.Events);
        }
    }

    [TestMethod]
    public async Task ProcessAsync_LateChunkAfterCompletedDeadlineStartsANewScan()
    {
        var monitor = new LiveMonitor();
        var clock = new ManualProcessingClock();
        var pipeline = CreatePipeline(monitor, clock, new PlainTextAnalyzer());
        var transport = new ManualChunkTransport();
        using var cancellation = new CancellationTokenSource();
        var twoEvents = WaitForEventCountAsync(monitor, 2);
        var processing = pipeline.ProcessAsync(transport, cancellation.Token);

        transport.Add(RawChunk.Create(1, "old"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await clock.WaitForNextTimerAsync();
        var deadline = clock.Timestamp + ManualProcessingClock.ToStopwatchTicks(TimeSpan.FromMilliseconds(100));
        clock.BeforeCompletingNextDelay = () => transport.Add(
            RawChunk.Create(2, "new\r"u8, DateTimeOffset.UnixEpoch, deadline + 1, Identity));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await twoEvents.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.HasCount(2, monitor.Events);
        Assert.AreEqual("old", monitor.Events[0].Decoded.Text);
        Assert.AreEqual(ScanCompletionReason.SilenceTimeout, monitor.Events[0].Scan.CompletionReason);
        Assert.AreEqual("new", monitor.Events[1].Decoded.Text);
        Assert.AreEqual(ScanCompletionReason.Terminator, monitor.Events[1].Scan.CompletionReason);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    [TestMethod]
    public async Task ProcessAsync_TerminatorAtDeadlineKeepsTerminatorPrecedence()
    {
        var monitor = new LiveMonitor();
        var clock = new ManualProcessingClock();
        var pipeline = CreatePipeline(monitor, clock, new PlainTextAnalyzer());
        var transport = new ManualChunkTransport();
        using var cancellation = new CancellationTokenSource();
        var appended = NextAppendAsync(monitor);
        var processing = pipeline.ProcessAsync(transport, cancellation.Token);

        transport.Add(RawChunk.Create(1, "old"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await clock.WaitForNextTimerAsync();
        var deadline = clock.Timestamp + ManualProcessingClock.ToStopwatchTicks(TimeSpan.FromMilliseconds(100));
        clock.BeforeCompletingNextDelay = () => transport.Add(
            RawChunk.Create(2, [0x0D], DateTimeOffset.UnixEpoch, deadline, Identity));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await appended.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.HasCount(1, monitor.Events);
        Assert.AreEqual("old", monitor.Events[0].Decoded.Text);
        Assert.AreEqual(ScanCompletionReason.Terminator, monitor.Events[0].Scan.CompletionReason);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    [TestMethod]
    public async Task ProcessAsync_EmptyChunkDoesNotResetSilenceDeadline()
    {
        var monitor = new LiveMonitor();
        var clock = new ManualProcessingClock();
        var pipeline = CreatePipeline(monitor, clock, new PlainTextAnalyzer());
        var transport = new ControlledChunkTransport();
        using var cancellation = new CancellationTokenSource();
        var processing = pipeline.ProcessAsync(transport, cancellation.Token);

        transport.Add(RawChunk.Create(1, "data"u8, DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await clock.WaitForNextTimerAsync();
        await transport.WaitForConsumedChunkAsync();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        transport.Add(RawChunk.Create(2, [], DateTimeOffset.UnixEpoch, clock.Timestamp, Identity));
        await transport.WaitForConsumedChunkAsync();

        Assert.AreEqual(1, clock.ScheduledCount);
        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.IsEmpty(monitor.Events);
        var appended = NextAppendAsync(monitor);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await appended.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.HasCount(1, monitor.Events);
        Assert.AreEqual("data", monitor.Events[0].Decoded.Text);
        Assert.AreEqual(ScanCompletionReason.SilenceTimeout, monitor.Events[0].Scan.CompletionReason);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    private static ScanProcessingPipeline CreatePipeline(LiveMonitor monitor, params IScanAnalyzer[] analyzers) =>
        new(
            new ScanAssembler(new ScanFramingOptions([0x0D], TimeSpan.FromMilliseconds(100), 65_536)),
            PayloadTextEncoding.Utf8,
            new ScanAnalyzerPipeline(analyzers),
            monitor);

    private static ScanProcessingPipeline CreatePipeline(
        LiveMonitor monitor,
        IScanProcessingClock clock,
        params IScanAnalyzer[] analyzers) =>
        new(
            new ScanAssembler(new ScanFramingOptions([0x0D], TimeSpan.FromMilliseconds(100), 65_536)),
            PayloadTextEncoding.Utf8,
            new ScanAnalyzerPipeline(analyzers),
            monitor,
            clock);

    private static Task NextAppendAsync(LiveMonitor monitor)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            monitor.Changed -= handler;
            completion.TrySetResult();
        };
        monitor.Changed += handler;
        return completion.Task;
    }

    private static Task WaitForEventCountAsync(LiveMonitor monitor, int expectedCount)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (monitor.Events.Length < expectedCount)
            {
                return;
            }

            monitor.Changed -= handler;
            completion.TrySetResult();
        };
        monitor.Changed += handler;
        return completion.Task;
    }

    private sealed class ThrowingAnalyzer : IScanAnalyzer
    {
        public string Name => "Throwing";

        public int Order => 1;

        public bool IsFallback => false;

        public AnalysisResult? Analyze(DecodedPayload payload) =>
            throw new InvalidOperationException("Expected analyzer failure.");
    }

    private sealed class ChunkTransport(params RawChunk[] chunks) : IScannerTransport
    {
        public TransportIdentity Identity => ScanProcessingPipelineTests.Identity;

        public ConnectionState State => ConnectionState.Connected;

        public ValueTask OpenAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<RawChunk> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }

            await Task.CompletedTask;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ChunkThenBlockTransport(RawChunk chunk) : IScannerTransport
    {
        public TaskCompletionSource ChunkDelivered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TransportIdentity Identity => ScanProcessingPipelineTests.Identity;

        public ConnectionState State => ConnectionState.Connected;

        public ValueTask OpenAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<RawChunk> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return chunk;
            ChunkDelivered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ControlledChunkTransport : IScannerTransport
    {
        private readonly Channel<RawChunk> _chunks = Channel.CreateUnbounded<RawChunk>();
        private readonly SemaphoreSlim _consumed = new(0);

        public TransportIdentity Identity => ScanProcessingPipelineTests.Identity;

        public ConnectionState State => ConnectionState.Connected;

        public void Add(RawChunk chunk) => Assert.IsTrue(_chunks.Writer.TryWrite(chunk));

        public ValueTask OpenAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<RawChunk> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var chunk in _chunks.Reader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
                _consumed.Release();
            }
        }

        public async Task WaitForConsumedChunkAsync() =>
            Assert.IsTrue(await _consumed.WaitAsync(TimeSpan.FromSeconds(1)));

        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellationSensitiveTransport : IScannerTransport
    {
        public TaskCompletionSource SecondMoveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowReadCancellationToFinish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource EnumeratorDisposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool DisposedWhileMoveNextInFlight { get; private set; }

        public int EnumeratorDisposeCount { get; private set; }

        public TransportIdentity Identity => ScanProcessingPipelineTests.Identity;

        public ConnectionState State => ConnectionState.Connected;

        public ValueTask OpenAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public IAsyncEnumerable<RawChunk> ReadAllAsync(CancellationToken cancellationToken) =>
            new CancellationSensitiveEnumerable(this);

        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class CancellationSensitiveEnumerable(CancellationSensitiveTransport owner)
            : IAsyncEnumerable<RawChunk>, IAsyncEnumerator<RawChunk>
        {
            private CancellationToken _cancellationToken;
            private bool _returnedFirst;
            private bool _moveNextInFlight;

            public RawChunk Current { get; private set; } = null!;

            public IAsyncEnumerator<RawChunk> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                _cancellationToken = cancellationToken;
                return this;
            }

            public ValueTask<bool> MoveNextAsync()
            {
                if (!_returnedFirst)
                {
                    _returnedFirst = true;
                    Current = RawChunk.Create(
                        1,
                        "partial"u8,
                        DateTimeOffset.UnixEpoch,
                        System.Diagnostics.Stopwatch.GetTimestamp(),
                        owner.Identity);
                    return ValueTask.FromResult(true);
                }

                return new ValueTask<bool>(WaitForCancellationAsync());
            }

            public ValueTask DisposeAsync()
            {
                owner.EnumeratorDisposeCount++;
                owner.DisposedWhileMoveNextInFlight |= _moveNextInFlight;
                owner.EnumeratorDisposed.TrySetResult();
                return new ValueTask(Task.FromException(
                    new IOException("Expected disposal failure after cancellation.")));
            }

            private async Task<bool> WaitForCancellationAsync()
            {
                _moveNextInFlight = true;
                owner.SecondMoveStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, _cancellationToken);
                    return false;
                }
                catch (OperationCanceledException)
                {
                    owner.ReadCancellationObserved.TrySetResult();
                    await owner.AllowReadCancellationToFinish.Task;
                    throw;
                }
                finally
                {
                    _moveNextInFlight = false;
                }
            }
        }
    }

    private sealed class ManualChunkTransport : IScannerTransport, IAsyncEnumerable<RawChunk>, IAsyncEnumerator<RawChunk>
    {
        private readonly object _gate = new();
        private readonly Queue<RawChunk> _queued = new();
        private TaskCompletionSource<bool>? _pendingMove;
        private CancellationTokenRegistration _pendingCancellation;

        public TransportIdentity Identity => ScanProcessingPipelineTests.Identity;

        public ConnectionState State => ConnectionState.Connected;

        public RawChunk Current { get; private set; } = null!;

        public void Add(RawChunk chunk)
        {
            TaskCompletionSource<bool>? pending;
            lock (_gate)
            {
                pending = _pendingMove;
                if (pending is null)
                {
                    _queued.Enqueue(chunk);
                    return;
                }

                _pendingMove = null;
                _pendingCancellation.Dispose();
                Current = chunk;
            }

            pending.TrySetResult(true);
        }

        public ValueTask OpenAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public IAsyncEnumerable<RawChunk> ReadAllAsync(CancellationToken cancellationToken) => this;

        public IAsyncEnumerator<RawChunk> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            PendingCancellationToken = cancellationToken;
            return this;
        }

        private CancellationToken PendingCancellationToken { get; set; }

        public ValueTask<bool> MoveNextAsync()
        {
            lock (_gate)
            {
                if (_queued.Count > 0)
                {
                    Current = _queued.Dequeue();
                    return ValueTask.FromResult(true);
                }

                _pendingMove = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingCancellation = PendingCancellationToken.Register(
                    () => _pendingMove?.TrySetCanceled(PendingCancellationToken));
                return new ValueTask<bool>(_pendingMove.Task);
            }
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            _pendingCancellation.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _pendingCancellation.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualProcessingClock : IScanProcessingClock
    {
        private readonly object _gate = new();
        private readonly List<DelayRequest> _requests = [];
        private readonly SemaphoreSlim _scheduled = new(0);

        public int ScheduledCount { get; private set; }

        public Action? BeforeCompletingNextDelay { get; set; }

        public long Timestamp { get; private set; }

        public long GetTimestamp() => Timestamp;

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new DelayRequest(
                Timestamp + ToStopwatchTicks(delay),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            lock (_gate)
            {
                _requests.Add(request);
            }

            request.CancellationRegistration = cancellationToken.Register(
                () => request.Completion.TrySetCanceled(cancellationToken));
            ScheduledCount++;
            _scheduled.Release();
            return new ValueTask(request.Completion.Task);
        }

        public async Task WaitForNextTimerAsync() =>
            Assert.IsTrue(await _scheduled.WaitAsync(TimeSpan.FromSeconds(1)));

        public void Advance(TimeSpan elapsed)
        {
            List<DelayRequest> due;
            lock (_gate)
            {
                Timestamp += ToStopwatchTicks(elapsed);
                due = _requests.Where(request => request.DueTimestamp <= Timestamp).ToList();
                _requests.RemoveAll(request => due.Contains(request));
            }

            foreach (var request in due)
            {
                request.CancellationRegistration.Dispose();
                if (!request.Completion.Task.IsCompleted && BeforeCompletingNextDelay is { } beforeCompletion)
                {
                    BeforeCompletingNextDelay = null;
                    beforeCompletion();
                }

                request.Completion.TrySetResult();
            }
        }

        public static long ToStopwatchTicks(TimeSpan duration) =>
            (long)Math.Ceiling(duration.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);

        private sealed class DelayRequest(long dueTimestamp, TaskCompletionSource completion)
        {
            public long DueTimestamp { get; } = dueTimestamp;

            public TaskCompletionSource Completion { get; } = completion;

            public CancellationTokenRegistration CancellationRegistration { get; set; }
        }
    }
}
