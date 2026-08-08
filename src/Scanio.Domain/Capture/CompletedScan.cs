using System.Collections.Immutable;
using Scanio.Domain.Transport;

namespace Scanio.Domain.Capture;

public sealed record CompletedScan
{
    public CompletedScan(
        long sequence,
        ImmutableArray<byte> rawBytes,
        ImmutableArray<byte> payloadBytes,
        ImmutableArray<RawChunk> contributingChunks,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        long startMonotonicTimestamp,
        long endMonotonicTimestamp,
        ScanCompletionReason completionReason,
        ScanFramingSnapshot framing,
        TransportIdentity transport)
    {
        ArgumentNullException.ThrowIfNull(framing);
        ArgumentNullException.ThrowIfNull(transport);

        Sequence = sequence;
        RawBytes = ImmutableArray.Create(rawBytes.ToArray());
        PayloadBytes = ImmutableArray.Create(payloadBytes.ToArray());
        ContributingChunks = ImmutableArray.CreateRange(contributingChunks);
        StartedAt = startedAt;
        EndedAt = endedAt;
        StartMonotonicTimestamp = startMonotonicTimestamp;
        EndMonotonicTimestamp = endMonotonicTimestamp;
        CompletionReason = completionReason;
        Framing = framing;
        Transport = transport;
    }

    public long Sequence { get; }

    public ImmutableArray<byte> RawBytes { get; }

    public ImmutableArray<byte> PayloadBytes { get; }

    public ImmutableArray<RawChunk> ContributingChunks { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset EndedAt { get; }

    public long StartMonotonicTimestamp { get; }

    public long EndMonotonicTimestamp { get; }

    public ScanCompletionReason CompletionReason { get; }

    public ScanFramingSnapshot Framing { get; }

    public TransportIdentity Transport { get; }

    public static CompletedScan Create(
        long sequence,
        ReadOnlySpan<byte> rawBytes,
        ReadOnlySpan<byte> payloadBytes,
        IEnumerable<RawChunk> contributingChunks,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        long startMonotonicTimestamp,
        long endMonotonicTimestamp,
        ScanCompletionReason completionReason,
        ScanFramingSnapshot framing,
        TransportIdentity transport)
    {
        ArgumentNullException.ThrowIfNull(contributingChunks);
        ArgumentNullException.ThrowIfNull(framing);
        ArgumentNullException.ThrowIfNull(transport);

        return new CompletedScan(
            sequence,
            ImmutableArray.Create(rawBytes.ToArray()),
            ImmutableArray.Create(payloadBytes.ToArray()),
            ImmutableArray.CreateRange(contributingChunks),
            startedAt,
            endedAt,
            startMonotonicTimestamp,
            endMonotonicTimestamp,
            completionReason,
            framing,
            transport);
    }
}
