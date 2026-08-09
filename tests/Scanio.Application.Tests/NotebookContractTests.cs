using Scanio.Application.Notebook;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Application.Tests;

[TestClass]
public sealed class NotebookContractTests
{
    [TestMethod]
    public void NotebookRecord_CreateOwnsImmutableAnalysisSnapshotAndExactBytes()
    {
        var source = new List<AnalysisResult>
        {
            AnalysisResult.Match(
                "PlainText",
                "Text",
                AnalysisConfidence.Inferred,
                "The payload can be decoded as text.",
                "value")
        };
        var record = NotebookRecord.Create(
            7,
            Guid.NewGuid(),
            CreateScan([0x00, 0x1D, 0xFF]),
            DecodedPayload.Create([0x00, 0x1D, 0xFF], PayloadTextEncoding.Latin1, "value", "value"),
            source,
            2,
            DateTimeOffset.UnixEpoch);

        source.Clear();

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x1D, 0xFF }, record.Scan.PayloadBytes.ToArray());
        Assert.HasCount(1, record.Analyses);
        Assert.AreEqual(2, record.DuplicateCount);
    }

    [TestMethod]
    public void NotebookSession_NormalizesNameAndRejectsBlankName()
    {
        var session = NotebookSession.Create(Guid.NewGuid(), "  Shift A  ", DateTimeOffset.UnixEpoch);

        Assert.AreEqual("Shift A", session.Name);
        Assert.ThrowsExactly<ArgumentException>(() =>
            NotebookSession.Create(Guid.NewGuid(), "  ", DateTimeOffset.UnixEpoch));
    }

    private static CompletedScan CreateScan(byte[] payload) =>
        CompletedScan.Create(
            1,
            payload,
            payload,
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            1,
            1,
            ScanCompletionReason.SilenceTimeout,
            ScanFramingSnapshot.Create([0x0D], TimeSpan.FromMilliseconds(100), 65_536),
            new TransportIdentity(TransportKind.Serial, "COM7", "COM7"));
}
