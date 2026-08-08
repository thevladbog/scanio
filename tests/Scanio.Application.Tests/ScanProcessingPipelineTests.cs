using System.Runtime.CompilerServices;
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
            RawChunk.Create(1, "partial"u8, DateTimeOffset.UnixEpoch, 1, Identity));
        using var cancellation = new CancellationTokenSource();

        var processing = pipeline.ProcessAsync(transport, cancellation.Token);
        await transport.ChunkDelivered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
        Assert.IsEmpty(monitor.Events);

        await pipeline.ProcessAsync(
            new ChunkTransport(RawChunk.Create(2, "fresh\r"u8, DateTimeOffset.UnixEpoch, 2, Identity)),
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

    private static ScanProcessingPipeline CreatePipeline(LiveMonitor monitor, params IScanAnalyzer[] analyzers) =>
        new(
            new ScanAssembler(new ScanFramingOptions([0x0D], TimeSpan.FromMilliseconds(100), 65_536)),
            PayloadTextEncoding.Utf8,
            new ScanAnalyzerPipeline(analyzers),
            monitor);

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
}
