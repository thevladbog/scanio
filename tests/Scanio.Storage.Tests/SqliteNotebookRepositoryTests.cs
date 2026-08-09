using Scanio.Application.Notebook;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Storage.Tests;

[TestClass]
public sealed class SqliteNotebookRepositoryTests
{
    private string _directory = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "scanio-storage-tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_directory, "notebook.db");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void Initialize_IsIdempotentAndCreatesTheDatabaseDirectory()
    {
        var repository = new SqliteNotebookRepository(_databasePath);

        repository.Initialize();
        repository.Initialize();

        Assert.IsTrue(File.Exists(_databasePath));
        Assert.IsEmpty(repository.GetSessions());
    }

    [TestMethod]
    public void Repository_RoundTripsExactScanChunksAnalysisAndSessionLifecycle()
    {
        var repository = new SqliteNotebookRepository(_databasePath);
        repository.Initialize();
        var startedAt = DateTimeOffset.Parse("2026-08-09T08:00:00+03:00");
        var session = repository.CreateSession("  Shift A  ", startedAt);
        var record = CreateRecord(session.Id, 2, [0x5D, 0x64, 0x32, 0x1D, 0xFF]);

        repository.Append(record);
        repository.RenameSession(session.Id, "Shift B");
        repository.CompleteSession(session.Id, startedAt.AddMinutes(1));

        var loadedSession = repository.GetSessions().Single();
        var loaded = repository.GetRecords(session.Id).Single();
        Assert.AreEqual("Shift B", loadedSession.Name);
        Assert.AreEqual(1, loadedSession.RecordCount);
        Assert.AreEqual(startedAt.AddMinutes(1), loadedSession.EndedAt);
        CollectionAssert.AreEqual(record.Scan.RawBytes.ToArray(), loaded.Scan.RawBytes.ToArray());
        CollectionAssert.AreEqual(record.Scan.PayloadBytes.ToArray(), loaded.Scan.PayloadBytes.ToArray());
        CollectionAssert.AreEqual(record.Scan.Framing.Terminator.ToArray(), loaded.Scan.Framing.Terminator.ToArray());
        Assert.AreEqual(record.Scan.Transport, loaded.Scan.Transport);
        AssertChunk(record.Scan.ContributingChunks.Single(), loaded.Scan.ContributingChunks.Single());
        Assert.AreEqual(record.Decoded.Encoding, loaded.Decoded.Encoding);
        Assert.AreEqual(record.Decoded.Text, loaded.Decoded.Text);
        Assert.AreEqual(record.Decoded.EscapedDisplay, loaded.Decoded.EscapedDisplay);
        Assert.AreEqual(record.Decoded.DecodingWarning, loaded.Decoded.DecodingWarning);
        Assert.HasCount(2, loaded.Analyses);
        AssertAnalysis(record.Analyses[0], loaded.Analyses[0]);
        AssertAnalysis(record.Analyses[1], loaded.Analyses[1]);
        Assert.AreEqual(2, loaded.DuplicateCount);
    }

    [TestMethod]
    public void GetRecords_ReturnsEveryOccurrenceInRecordingOrder()
    {
        var repository = new SqliteNotebookRepository(_databasePath);
        repository.Initialize();
        var session = repository.CreateSession("Session", DateTimeOffset.UnixEpoch);
        repository.Append(CreateRecord(session.Id, 2, [0x31]));
        repository.Append(CreateRecord(session.Id, 1, [0x31]));

        var records = repository.GetRecords(session.Id);

        CollectionAssert.AreEqual(new long[] { 2, 1 }, records.Select(item => item.Sequence).ToArray());
    }

    [TestMethod]
    public void DeleteSession_CascadesToPersistedRecords()
    {
        var repository = new SqliteNotebookRepository(_databasePath);
        repository.Initialize();
        var session = repository.CreateSession("Session", DateTimeOffset.UnixEpoch);
        repository.Append(CreateRecord(session.Id, 1, [0x31]));

        repository.DeleteSession(session.Id);

        Assert.IsEmpty(repository.GetSessions());
        Assert.IsEmpty(repository.GetRecords(session.Id));
    }

    [TestMethod]
    public void Repository_ReleasesTheDatabaseFileAfterOperations()
    {
        var repository = new SqliteNotebookRepository(_databasePath);
        repository.Initialize();
        repository.GetSessions();
        var movedPath = Path.Combine(_directory, "moved.db");

        File.Move(_databasePath, movedPath);

        Assert.IsTrue(File.Exists(movedPath));
        Assert.IsFalse(File.Exists(_databasePath));
    }

    private static NotebookRecord CreateRecord(Guid sessionId, long sequence, byte[] payload)
    {
        var transport = new TransportIdentity(TransportKind.Serial, "COM7", "Zebra COM7", "USB\\VID_05E0");
        var scan = CompletedScan.Create(
            sequence,
            [.. payload, 0x0D],
            payload,
            [RawChunk.Create(sequence, payload, DateTimeOffset.UnixEpoch, 10, transport)],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(5),
            10,
            15,
            ScanCompletionReason.Terminator,
            ScanFramingSnapshot.Create([0x0D], TimeSpan.FromMilliseconds(100), 65_536),
            transport);
        var decoded = DecodedPayload.Create(payload, PayloadTextEncoding.Latin1, "value", "value", "warning");
        var analysis = AnalysisResult.Match(
            "Fixture",
            "GS1",
            AnalysisConfidence.Exact,
            "fixture evidence",
            "fixture summary",
            [new AnalysisField("01", "GTIN", "04601234567890")],
            ["error"],
            ["warning"]);

        return NotebookRecord.Create(
            sequence,
            sessionId,
            scan,
            decoded,
            [analysis, AnalysisResult.Failure("BrokenFixture")],
            2,
            DateTimeOffset.UnixEpoch);
    }

    private static void AssertChunk(RawChunk expected, RawChunk actual)
    {
        Assert.AreEqual(expected.SequenceNumber, actual.SequenceNumber);
        CollectionAssert.AreEqual(expected.Bytes.ToArray(), actual.Bytes.ToArray());
        Assert.AreEqual(expected.ReceivedAt, actual.ReceivedAt);
        Assert.AreEqual(expected.MonotonicTimestamp, actual.MonotonicTimestamp);
        Assert.AreEqual(expected.TransportIdentity, actual.TransportIdentity);
    }

    private static void AssertAnalysis(AnalysisResult expected, AnalysisResult actual)
    {
        Assert.AreEqual(expected.AnalyzerName, actual.AnalyzerName);
        Assert.AreEqual(expected.Format, actual.Format);
        Assert.AreEqual(expected.IsMatch, actual.IsMatch);
        Assert.AreEqual(expected.Confidence, actual.Confidence);
        Assert.AreEqual(expected.Evidence, actual.Evidence);
        Assert.AreEqual(expected.Summary, actual.Summary);
        CollectionAssert.AreEqual(expected.Fields.ToArray(), actual.Fields.ToArray());
        CollectionAssert.AreEqual(expected.ValidationErrors.ToArray(), actual.ValidationErrors.ToArray());
        CollectionAssert.AreEqual(expected.ValidationWarnings.ToArray(), actual.ValidationWarnings.ToArray());
    }
}
