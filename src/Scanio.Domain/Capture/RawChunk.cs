using System.Collections.Immutable;
using Scanio.Domain.Transport;

namespace Scanio.Domain.Capture;

public sealed record RawChunk(
    long SequenceNumber,
    ImmutableArray<byte> Bytes,
    DateTimeOffset ReceivedAt,
    long MonotonicTimestamp,
    TransportIdentity TransportIdentity)
{
    public static RawChunk Create(
        long sequenceNumber,
        ReadOnlySpan<byte> bytes,
        DateTimeOffset receivedAt,
        long monotonicTimestamp,
        TransportIdentity transportIdentity) =>
        new(sequenceNumber, ImmutableArray.Create(bytes.ToArray()), receivedAt, monotonicTimestamp, transportIdentity);
}
