using Scanio.Application.Monitor;
using Scanio.Application.Notebook;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Application.Tests;

[TestClass]
public sealed class NotebookRecorderTests
{
    [TestMethod]
    public async Task Recording_PauseResumeAndStop_PersistsOnlyActiveOccurrencesInOrder()
    {
        var monitor = new LiveMonitor();
        var repository = new RecordingRepository();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);

        var session = recorder.Start("Session");
        monitor.Append(CreateScan(1, [0x31]), CreateDecoded("one"), []);
        recorder.Pause();
        monitor.Append(CreateScan(2, [0x32]), CreateDecoded("paused"), []);
        recorder.Resume();
        monitor.Append(CreateScan(3, [0x31]), CreateDecoded("three"), []);
        await recorder.StopAsync();

        Assert.AreEqual(NotebookRecordingState.Off, recorder.State);
        Assert.AreEqual(session.Id, repository.CompletedSessionId);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, repository.Records.Select(item => item.Sequence).ToArray());
        CollectionAssert.AreEqual(new long[] { 1, 3 }, repository.Records.Select(item => item.Scan.Sequence).ToArray());
    }

    [TestMethod]
    public async Task StopAsync_WaitsUntilAllEarlierScansArePersistedBeforeCompletingSession()
    {
        var monitor = new LiveMonitor();
        var repository = new RecordingRepository { BlockAppend = true };
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        recorder.Start("Session");
        monitor.Append(CreateScan(1, [0x31]), CreateDecoded("one"), []);

        var stopping = recorder.StopAsync();

        Assert.IsFalse(stopping.IsCompleted);
        Assert.IsNull(repository.CompletedSessionId);
        Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Start("Too early"));
        repository.ReleaseAppend.TrySetResult();
        await stopping;
        Assert.HasCount(1, repository.Records);
        Assert.IsNotNull(repository.CompletedSessionId);
    }

    [TestMethod]
    public async Task PersistenceFailure_IsReportedAndNeverEscapesMonitorAppend()
    {
        var monitor = new LiveMonitor();
        var repository = new RecordingRepository { AppendException = new IOException("disk full") };
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var failureObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.Changed += (_, _) =>
        {
            if (recorder.LastError is not null)
            {
                failureObserved.TrySetResult();
            }
        };
        recorder.Start("Session");

        monitor.Append(CreateScan(1, [0x31]), CreateDecoded("one"), []);
        await failureObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        StringAssert.Contains(recorder.LastError, "disk full");
        await recorder.StopAsync();
    }

    [TestMethod]
    public async Task StateTransitions_RejectInvalidOperations()
    {
        var monitor = new LiveMonitor();
        await using var recorder = new NotebookRecorder(
            new RecordingRepository(),
            monitor,
            () => DateTimeOffset.UnixEpoch);

        Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Pause());
        recorder.Start("Session");
        Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Start("Other"));
        recorder.Pause();
        Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Pause());
        recorder.Resume();
        Assert.ThrowsExactly<InvalidOperationException>(() => recorder.Resume());
        await recorder.StopAsync();
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
            new TransportIdentity(TransportKind.Serial, "COM7", "COM7"));

    private static DecodedPayload CreateDecoded(string value) =>
        DecodedPayload.Create(System.Text.Encoding.UTF8.GetBytes(value), PayloadTextEncoding.Utf8, value, value);

    private sealed class RecordingRepository : INotebookRepository
    {
        public List<NotebookRecord> Records { get; } = [];
        public Guid? CompletedSessionId { get; private set; }
        public bool BlockAppend { get; init; }
        public Exception? AppendException { get; init; }
        public TaskCompletionSource ReleaseAppend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Initialize()
        {
        }

        public NotebookSession CreateSession(string name, DateTimeOffset startedAt) =>
            NotebookSession.Create(Guid.NewGuid(), name, startedAt);

        public void Append(NotebookRecord record)
        {
            if (BlockAppend)
            {
                ReleaseAppend.Task.GetAwaiter().GetResult();
            }

            if (AppendException is not null)
            {
                throw AppendException;
            }

            Records.Add(record);
        }

        public void CompleteSession(Guid sessionId, DateTimeOffset endedAt) => CompletedSessionId = sessionId;
        public IReadOnlyList<NotebookSession> GetSessions() => [];
        public IReadOnlyList<NotebookRecord> GetRecords(Guid sessionId) => [];
        public void RenameSession(Guid sessionId, string name)
        {
        }

        public void DeleteSession(Guid sessionId)
        {
        }
    }
}
