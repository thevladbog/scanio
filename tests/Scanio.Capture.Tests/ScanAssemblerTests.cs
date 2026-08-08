using System.Diagnostics;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Capture.Tests;

[TestClass]
public sealed class ScanAssemblerTests
{
    private static readonly TransportIdentity Identity =
        new(TransportKind.Serial, "scanner-1", "Test scanner");

    [TestMethod]
    public void Options_DefaultToCrOneHundredMillisecondsAndSixtyFourKibibytes()
    {
        var options = new ScanFramingOptions();

        CollectionAssert.AreEqual(new byte[] { 0x0D }, options.Terminator.ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), options.SilenceTimeout);
        Assert.AreEqual(65_536, options.MaximumUnfinishedBytes);
    }

    [TestMethod]
    public void Options_RejectTimeoutsOutsideInclusiveLimits()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(9), 65_536));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(5001), 65_536));

        _ = new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(10), 65_536);
        _ = new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(5000), 65_536);
    }

    [TestMethod]
    public void Options_RejectAnEmptyExplicitTerminator()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ScanFramingOptions(Array.Empty<byte>(), TimeSpan.FromMilliseconds(100), 65_536));
    }

    [TestMethod]
    public void WithoutTerminator_PreservesAllBytesUntilSilenceCompletion()
    {
        var start = Stopwatch.GetTimestamp();
        var options = ScanFramingOptions.WithoutTerminator(TimeSpan.FromMilliseconds(100), 65_536);
        var assembler = new ScanAssembler(options);

        CollectionAssert.AreEqual(Array.Empty<byte>(), options.Terminator.ToArray());
        Assert.IsEmpty(assembler.Push(Chunk(1, new byte[] { 0x41, 0x0D }, start)));
        var scan = assembler.CompleteOnSilence(start + (Stopwatch.Frequency / 10));

        Assert.IsNotNull(scan);
        AssertScan(
            scan,
            1,
            new byte[] { 0x41, 0x0D },
            new byte[] { 0x41, 0x0D },
            ScanCompletionReason.SilenceTimeout);
        CollectionAssert.AreEqual(Array.Empty<byte>(), scan.Framing.Terminator.ToArray());
    }

    [TestMethod]
    public void Options_RejectMaximumSizesOutsideInclusiveLimits()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(100), 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(100), 65_537));

        _ = new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(100), 1);
        _ = new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(100), 65_536);
    }

    [TestMethod]
    public void Push_CompletesCrTerminatedScanAndPreservesDiagnosticData()
    {
        var assembler = new ScanAssembler();
        var chunk = Chunk(7, new byte[] { 0x41, 0x0D }, 123, DateTimeOffset.UnixEpoch.AddSeconds(2));

        var scans = assembler.Push(chunk);

        Assert.HasCount(1, scans);
        AssertScan(scans[0], 1, new byte[] { 0x41, 0x0D }, new byte[] { 0x41 }, ScanCompletionReason.Terminator);
        CollectionAssert.AreEqual(new[] { chunk }, scans[0].ContributingChunks.ToArray());
        Assert.AreEqual(chunk.ReceivedAt, scans[0].StartedAt);
        Assert.AreEqual(chunk.ReceivedAt, scans[0].EndedAt);
        Assert.AreEqual(123, scans[0].StartMonotonicTimestamp);
        Assert.AreEqual(123, scans[0].EndMonotonicTimestamp);
        Assert.AreSame(Identity, scans[0].Transport);
        CollectionAssert.AreEqual(new byte[] { 0x0D }, scans[0].Framing.Terminator.ToArray());
    }

    [TestMethod]
    public void Push_CompletesLfTerminatedScan()
    {
        var assembler = new ScanAssembler(Options(new byte[] { 0x0A }));

        var scans = assembler.Push(Chunk(1, new byte[] { 0x42, 0x0A }, 10));

        Assert.HasCount(1, scans);
        AssertScan(scans[0], 1, new byte[] { 0x42, 0x0A }, new byte[] { 0x42 }, ScanCompletionReason.Terminator);
    }

    [TestMethod]
    public void Push_CompletesCrLfSplitAcrossChunks()
    {
        var assembler = new ScanAssembler(Options(new byte[] { 0x0D, 0x0A }));
        var first = Chunk(1, new byte[] { 0x43, 0x0D }, 10);
        var second = Chunk(2, new byte[] { 0x0A }, 20);

        Assert.IsEmpty(assembler.Push(first));
        var scans = assembler.Push(second);

        Assert.HasCount(1, scans);
        AssertScan(scans[0], 1, new byte[] { 0x43, 0x0D, 0x0A }, new byte[] { 0x43 }, ScanCompletionReason.Terminator);
        CollectionAssert.AreEqual(new[] { first, second }, scans[0].ContributingChunks.ToArray());
        Assert.AreEqual(first.ReceivedAt, scans[0].StartedAt);
        Assert.AreEqual(second.ReceivedAt, scans[0].EndedAt);
        Assert.AreEqual(10, scans[0].StartMonotonicTimestamp);
        Assert.AreEqual(20, scans[0].EndMonotonicTimestamp);
    }

    [TestMethod]
    public void Push_CompletesArbitraryTerminatorSplitAcrossChunks()
    {
        var terminator = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var assembler = new ScanAssembler(Options(terminator));

        Assert.IsEmpty(assembler.Push(Chunk(1, new byte[] { 0x50, 0xDE, 0xAD }, 10)));
        var scans = assembler.Push(Chunk(2, new byte[] { 0xBE, 0xEF }, 20));

        Assert.HasCount(1, scans);
        AssertScan(
            scans[0],
            1,
            new byte[] { 0x50, 0xDE, 0xAD, 0xBE, 0xEF },
            new byte[] { 0x50 },
            ScanCompletionReason.Terminator);
    }

    [TestMethod]
    public void Push_EmitsMultipleScansInOneReadInOrder()
    {
        var assembler = new ScanAssembler();

        var scans = assembler.Push(Chunk(1, new byte[] { 0x41, 0x0D, 0x42, 0x0D }, 10));

        Assert.HasCount(2, scans);
        AssertScan(scans[0], 1, new byte[] { 0x41, 0x0D }, new byte[] { 0x41 }, ScanCompletionReason.Terminator);
        AssertScan(scans[1], 2, new byte[] { 0x42, 0x0D }, new byte[] { 0x42 }, ScanCompletionReason.Terminator);
    }

    [TestMethod]
    public void Push_CompletesWhenPayloadAndTerminatorArriveSeparately()
    {
        var assembler = new ScanAssembler();
        var payload = Chunk(1, new byte[] { 0x41, 0x42, 0x43 }, 10);
        var terminator = Chunk(2, new byte[] { 0x0D }, 20);

        Assert.IsEmpty(assembler.Push(payload));
        var scans = assembler.Push(terminator);

        Assert.HasCount(1, scans);
        AssertScan(
            scans[0],
            1,
            new byte[] { 0x41, 0x42, 0x43, 0x0D },
            new byte[] { 0x41, 0x42, 0x43 },
            ScanCompletionReason.Terminator);
        CollectionAssert.AreEqual(new[] { payload, terminator }, scans[0].ContributingChunks.ToArray());
    }

    [TestMethod]
    public void Push_RepeatedTerminatorsEmitEmptyScans()
    {
        var assembler = new ScanAssembler();

        var scans = assembler.Push(Chunk(1, new byte[] { 0x0D, 0x0D }, 10));

        Assert.HasCount(2, scans);
        AssertScan(scans[0], 1, new byte[] { 0x0D }, Array.Empty<byte>(), ScanCompletionReason.Terminator);
        AssertScan(scans[1], 2, new byte[] { 0x0D }, Array.Empty<byte>(), ScanCompletionReason.Terminator);
    }

    [TestMethod]
    public void Push_GivesATerminatorPrecedenceAtTheMaximumPayloadBoundary()
    {
        var assembler = new ScanAssembler(
            new ScanFramingOptions(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(100), 3));

        var scans = assembler.Push(Chunk(1, new byte[] { 0x41, 0x42, 0x43, 0x0D }, 10));

        Assert.HasCount(1, scans);
        AssertScan(
            scans[0],
            1,
            new byte[] { 0x41, 0x42, 0x43, 0x0D },
            new byte[] { 0x41, 0x42, 0x43 },
            ScanCompletionReason.Terminator);
    }

    [TestMethod]
    public void CompleteOnSilence_CompletesAtTheInclusiveTimeoutAndResets()
    {
        var assembler = new ScanAssembler();
        var receivedAt = DateTimeOffset.UnixEpoch.AddSeconds(3);
        var start = Stopwatch.GetTimestamp();
        assembler.Push(Chunk(1, new byte[] { 0x41, 0x42, 0x43 }, start, receivedAt));
        var beforeTimeout = start + (long)(Stopwatch.Frequency * 0.099);
        var atTimeout = start + (Stopwatch.Frequency / 10);

        Assert.IsNull(assembler.CompleteOnSilence(beforeTimeout));
        var scan = assembler.CompleteOnSilence(atTimeout);

        Assert.IsNotNull(scan);
        AssertScan(
            scan,
            1,
            new byte[] { 0x41, 0x42, 0x43 },
            new byte[] { 0x41, 0x42, 0x43 },
            ScanCompletionReason.SilenceTimeout);
        Assert.AreEqual(receivedAt, scan.StartedAt);
        Assert.AreEqual(receivedAt, scan.EndedAt);
        Assert.AreEqual(start, scan.StartMonotonicTimestamp);
        Assert.AreEqual(start, scan.EndMonotonicTimestamp);
        Assert.IsNull(assembler.CompleteOnSilence(atTimeout + Stopwatch.Frequency));
    }

    [TestMethod]
    public void Push_ExceedingSixtyFourKibibytesEmitsOverflowAndResets()
    {
        var assembler = new ScanAssembler();
        var maximum = Enumerable.Repeat((byte)0x41, 65_536).ToArray();

        Assert.IsEmpty(assembler.Push(Chunk(1, maximum, 10)));
        var overflow = assembler.Push(Chunk(2, new byte[] { 0x42 }, 20));
        var afterReset = assembler.Push(Chunk(3, new byte[] { 0x43, 0x0D }, 30));

        Assert.HasCount(1, overflow);
        Assert.HasCount(65_537, overflow[0].RawBytes);
        Assert.HasCount(65_537, overflow[0].PayloadBytes);
        Assert.AreEqual(ScanCompletionReason.BufferOverflow, overflow[0].CompletionReason);
        Assert.AreEqual(1, overflow[0].Sequence);
        CollectionAssert.AreEqual(new long[] { 1, 2 },
            overflow[0].ContributingChunks.Select(chunk => chunk.SequenceNumber).ToArray());

        Assert.HasCount(1, afterReset);
        AssertScan(afterReset[0], 2, new byte[] { 0x43, 0x0D }, new byte[] { 0x43 }, ScanCompletionReason.Terminator);
    }

    private static ScanFramingOptions Options(byte[] terminator) =>
        new(terminator, TimeSpan.FromMilliseconds(100), 65_536);

    private static RawChunk Chunk(
        long sequence,
        byte[] bytes,
        long monotonicTimestamp,
        DateTimeOffset? receivedAt = null) =>
        RawChunk.Create(sequence, bytes, receivedAt ?? DateTimeOffset.UnixEpoch.AddTicks(sequence), monotonicTimestamp, Identity);

    private static void AssertScan(
        CompletedScan scan,
        long sequence,
        byte[] raw,
        byte[] payload,
        ScanCompletionReason reason)
    {
        Assert.AreEqual(sequence, scan.Sequence);
        CollectionAssert.AreEqual(raw, scan.RawBytes.ToArray());
        CollectionAssert.AreEqual(payload, scan.PayloadBytes.ToArray());
        Assert.AreEqual(reason, scan.CompletionReason);
    }
}
