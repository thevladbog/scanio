using System.Collections.Immutable;
using Scanio.Domain.Transport;

namespace Scanio.Domain.Capture;

public sealed record RawChunk
{
    public RawChunk(
        long sequenceNumber,
        ImmutableArray<byte> bytes,
        DateTimeOffset receivedAt,
        long monotonicTimestamp,
        TransportIdentity transportIdentity)
    {
        ArgumentNullException.ThrowIfNull(transportIdentity);

        SequenceNumber = sequenceNumber;
        Bytes = ImmutableArray.Create(bytes.ToArray());
        ReceivedAt = receivedAt;
        MonotonicTimestamp = monotonicTimestamp;
        TransportIdentity = transportIdentity;
    }

    public long SequenceNumber { get; }

    public ImmutableArray<byte> Bytes { get; }

    public DateTimeOffset ReceivedAt { get; }

    public long MonotonicTimestamp { get; }

    public TransportIdentity TransportIdentity { get; }

    public static RawChunk Create(
        long sequenceNumber,
        ReadOnlySpan<byte> bytes,
        DateTimeOffset receivedAt,
        long monotonicTimestamp,
        TransportIdentity transportIdentity) =>
        new(sequenceNumber, ImmutableArray.Create(bytes.ToArray()), receivedAt, monotonicTimestamp, transportIdentity);
}
