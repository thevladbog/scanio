using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Capture.Tests;

[TestClass]
public sealed class ScanAssemblerPartitionTests
{
    private static readonly TransportIdentity Identity =
        new(TransportKind.Serial, "scanner-1", "Test scanner");

    private static readonly byte[] Terminator = { 0xDE, 0xAD, 0xBE, 0xEF };

    private static readonly byte[] Stream =
    {
        0x41, 0x42, 0xDE, 0xAD, 0xBE, 0xEF,
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
        0x58, 0xDE, 0xAD, 0xBE, 0xEF,
        0xDE, 0xAD, 0xBE, 0xEF
    };

    [TestMethod]
    public void RandomPartitions_ProduceIdenticalScansForOneHundredFixedSeeds()
    {
        var expected = Assemble(new[] { Stream.Length });
        AssertExpectedBaseline(expected);

        for (var seed = 0; seed < 100; seed++)
        {
            var random = new Random(seed);
            var sizes = new List<int>();
            var remaining = Stream.Length;
            while (remaining > 0)
            {
                var size = random.Next(1, Math.Min(4, remaining) + 1);
                sizes.Add(size);
                remaining -= size;
            }

            var actual = Assemble(sizes);

            Assert.HasCount(expected.Count, actual, $"Seed {seed} produced a different scan count.");
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.AreEqual(expected[index].Sequence, actual[index].Sequence, $"Seed {seed}, scan {index}: order");
                CollectionAssert.AreEqual(
                    expected[index].RawBytes.ToArray(),
                    actual[index].RawBytes.ToArray(),
                    $"Seed {seed}, scan {index}: raw bytes");
                CollectionAssert.AreEqual(
                    expected[index].PayloadBytes.ToArray(),
                    actual[index].PayloadBytes.ToArray(),
                    $"Seed {seed}, scan {index}: payload bytes");
                Assert.AreEqual(
                    expected[index].CompletionReason,
                    actual[index].CompletionReason,
                    $"Seed {seed}, scan {index}: completion reason");
            }
        }
    }

    private static IReadOnlyList<CompletedScan> Assemble(IEnumerable<int> sizes)
    {
        var options = new ScanFramingOptions(Terminator, TimeSpan.FromMilliseconds(100), 8);
        var assembler = new ScanAssembler(options);
        var scans = new List<CompletedScan>();
        var offset = 0;
        long chunkSequence = 1;

        foreach (var size in sizes)
        {
            var bytes = Stream.AsSpan(offset, size).ToArray();
            var chunk = RawChunk.Create(
                chunkSequence,
                bytes,
                DateTimeOffset.UnixEpoch.AddTicks(chunkSequence),
                chunkSequence,
                Identity);
            scans.AddRange(assembler.Push(chunk));
            offset += size;
            chunkSequence++;
        }

        Assert.AreEqual(Stream.Length, offset);
        return scans;
    }

    private static void AssertExpectedBaseline(IReadOnlyList<CompletedScan> scans)
    {
        Assert.HasCount(4, scans);
        AssertResult(
            scans[0],
            1,
            new byte[] { 0x41, 0x42, 0xDE, 0xAD, 0xBE, 0xEF },
            new byte[] { 0x41, 0x42 },
            ScanCompletionReason.Terminator);
        AssertResult(
            scans[1],
            2,
            new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 },
            new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 },
            ScanCompletionReason.BufferOverflow);
        AssertResult(
            scans[2],
            3,
            new byte[] { 0x58, 0xDE, 0xAD, 0xBE, 0xEF },
            new byte[] { 0x58 },
            ScanCompletionReason.Terminator);
        AssertResult(
            scans[3],
            4,
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            Array.Empty<byte>(),
            ScanCompletionReason.Terminator);
    }

    private static void AssertResult(
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
