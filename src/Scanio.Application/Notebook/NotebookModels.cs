using System.Collections.Immutable;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;

namespace Scanio.Application.Notebook;

public enum NotebookRecordingState
{
    Off,
    Recording,
    Paused
}

public sealed record NotebookSession
{
    private NotebookSession(
        Guid id,
        string name,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        int recordCount)
    {
        Id = id;
        Name = name;
        StartedAt = startedAt;
        EndedAt = endedAt;
        RecordCount = recordCount;
    }

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; }

    public int RecordCount { get; }

    public static NotebookSession Create(Guid id, string name, DateTimeOffset startedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A notebook session must have a non-empty identifier.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new NotebookSession(id, name.Trim(), startedAt, null, 0);
    }

    public NotebookSession WithSummary(string name, DateTimeOffset? endedAt, int recordCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        return new NotebookSession(Id, name.Trim(), StartedAt, endedAt, recordCount);
    }
}

public sealed record NotebookRecord
{
    private NotebookRecord(
        long sequence,
        Guid sessionId,
        CompletedScan scan,
        DecodedPayload decoded,
        ImmutableArray<AnalysisResult> analyses,
        int duplicateCount,
        DateTimeOffset recordedAt)
    {
        Sequence = sequence;
        SessionId = sessionId;
        Scan = scan;
        Decoded = decoded;
        Analyses = ImmutableArray.CreateRange(analyses);
        DuplicateCount = duplicateCount;
        RecordedAt = recordedAt;
    }

    public long Sequence { get; }

    public Guid SessionId { get; }

    public CompletedScan Scan { get; }

    public DecodedPayload Decoded { get; }

    public ImmutableArray<AnalysisResult> Analyses { get; }

    public int DuplicateCount { get; }

    public DateTimeOffset RecordedAt { get; }

    public static NotebookRecord Create(
        long sequence,
        Guid sessionId,
        CompletedScan scan,
        DecodedPayload decoded,
        IEnumerable<AnalysisResult> analyses,
        int duplicateCount,
        DateTimeOffset recordedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A notebook record must belong to a session.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duplicateCount);

        return new NotebookRecord(
            sequence,
            sessionId,
            scan,
            decoded,
            ImmutableArray.CreateRange(
                analyses.Select(result => result ?? throw new ArgumentException(
                    "Notebook analyses cannot contain null.", nameof(analyses)))),
            duplicateCount,
            recordedAt);
    }
}
