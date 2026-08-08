using System.Collections.Immutable;
using Scanio.Domain.Transport;

namespace Scanio.Domain.Capture;

public sealed record CompletedScan(
    long Sequence,
    ImmutableArray<byte> RawBytes,
    ImmutableArray<byte> PayloadBytes,
    ImmutableArray<RawChunk> ContributingChunks,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long StartMonotonicTimestamp,
    long EndMonotonicTimestamp,
    ScanCompletionReason CompletionReason,
    ScanFramingSnapshot Framing,
    TransportIdentity Transport)
{
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
