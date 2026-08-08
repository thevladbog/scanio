using Scanio.Application.Monitor;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Application.Tests;

[TestClass]
public sealed class LiveMonitorTests
{
    private static readonly TransportIdentity Identity =
        new(TransportKind.Serial, "COM7", "COM7");

    [TestMethod]
    public void Append_RetainsOneThousandEventsAndEvictsTheOldestInStableOrder()
    {
        var monitor = new LiveMonitor();

        for (var sequence = 1; sequence <= 1_001; sequence++)
        {
            monitor.Append(CreateScan(sequence, BitConverter.GetBytes(sequence)), CreateDecoded(sequence), []);
        }

        Assert.HasCount(1_000, monitor.Events);
        Assert.AreEqual(2L, monitor.Events[0].Scan.Sequence);
        Assert.AreEqual(1_001L, monitor.Events[^1].Scan.Sequence);
        CollectionAssert.AreEqual(
            Enumerable.Range(2, 1_000).Select(value => (long)value).ToArray(),
            monitor.Events.Select(value => value.Scan.Sequence).ToArray());
    }

    [TestMethod]
    public void Append_CountsDuplicatesByExactPayloadBytes()
    {
        var monitor = new LiveMonitor();

        monitor.Append(CreateScan(1, [0xC3, 0xA9]), CreateDecoded(1), []);
        monitor.Append(CreateScan(2, [0xC3, 0xA9]), CreateDecoded(2), []);
        monitor.Append(CreateScan(3, [0xE9]), CreateDecoded(3), []);

        Assert.AreEqual(2, monitor.Events[0].DuplicateCount);
        Assert.AreEqual(2, monitor.Events[1].DuplicateCount);
        Assert.AreEqual(1, monitor.Events[2].DuplicateCount);
    }

    [TestMethod]
    public void Clear_OnlyClearsTheLiveBufferAndCannotMutateAnEarlierSnapshot()
    {
        var monitor = new LiveMonitor();
        monitor.Append(CreateScan(1, [0x31]), CreateDecoded(1), []);
        var snapshot = monitor.Events;

        monitor.Clear();

        Assert.IsEmpty(monitor.Events);
        Assert.HasCount(1, snapshot);
        CollectionAssert.AreEqual(new byte[] { 0x31 }, snapshot[0].Scan.PayloadBytes.ToArray());
    }

    [TestMethod]
    public void Selection_RemainsOnAnOlderEventUntilReturnToLatest()
    {
        var monitor = new LiveMonitor();
        var first = monitor.Append(CreateScan(1, [0x31]), CreateDecoded(1), []);
        monitor.Append(CreateScan(2, [0x32]), CreateDecoded(2), []);

        monitor.Select(first.Id);
        var third = monitor.Append(CreateScan(3, [0x33]), CreateDecoded(3), []);

        Assert.IsFalse(monitor.IsFollowingLatest);
        Assert.AreEqual(first.Id, monitor.SelectedEvent?.Id);

        monitor.ReturnToLatest();

        Assert.IsTrue(monitor.IsFollowingLatest);
        Assert.AreEqual(third.Id, monitor.SelectedEvent?.Id);
    }

    [TestMethod]
    public void Changed_ObservesTheUpdatedImmutableSnapshot()
    {
        var monitor = new LiveMonitor();
        var observed = new List<System.Collections.Immutable.ImmutableArray<LiveScanEvent>>();
        monitor.Changed += (_, _) => observed.Add(monitor.Events);

        monitor.Append(CreateScan(1, [0x31]), CreateDecoded(1), []);
        monitor.Clear();

        Assert.HasCount(2, observed);
        Assert.HasCount(1, observed[0]);
        Assert.IsEmpty(observed[1]);
        Assert.HasCount(1, observed[0]);
    }

    private static CompletedScan CreateScan(long sequence, byte[] payload) =>
        CompletedScan.Create(
            sequence,
            payload,
            payload,
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            sequence,
            sequence,
            ScanCompletionReason.SilenceTimeout,
            ScanFramingSnapshot.Create([0x0D], TimeSpan.FromMilliseconds(100), 65_536),
            Identity);

    private static DecodedPayload CreateDecoded(long sequence) =>
        DecodedPayload.Create(BitConverter.GetBytes(sequence), PayloadTextEncoding.Utf8, sequence.ToString(), sequence.ToString());
}
