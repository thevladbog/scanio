using System.Collections.Immutable;

namespace Scanio.Domain.Capture;

public sealed record ScanFramingSnapshot
{
    private ScanFramingSnapshot(ImmutableArray<byte> terminator, TimeSpan silenceTimeout, int maximumUnfinishedBytes)
    {
        Terminator = terminator;
        SilenceTimeout = silenceTimeout;
        MaximumUnfinishedBytes = maximumUnfinishedBytes;
    }

    public ImmutableArray<byte> Terminator { get; }

    public TimeSpan SilenceTimeout { get; }

    public int MaximumUnfinishedBytes { get; }

    public static ScanFramingSnapshot Create(
        ReadOnlySpan<byte> terminator,
        TimeSpan silenceTimeout,
        int maximumUnfinishedBytes)
    {
        if (silenceTimeout < TimeSpan.FromMilliseconds(10) || silenceTimeout > TimeSpan.FromMilliseconds(5000))
        {
            throw new ArgumentOutOfRangeException(nameof(silenceTimeout));
        }

        if (maximumUnfinishedBytes is < 1 or > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUnfinishedBytes));
        }

        return new ScanFramingSnapshot(ImmutableArray.Create(terminator.ToArray()), silenceTimeout, maximumUnfinishedBytes);
    }
}
