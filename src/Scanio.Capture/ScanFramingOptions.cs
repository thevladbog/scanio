using System.Collections.Immutable;
using Scanio.Domain.Capture;

namespace Scanio.Capture;

public sealed class ScanFramingOptions
{
    public ScanFramingOptions()
        : this(new byte[] { 0x0D }, TimeSpan.FromMilliseconds(100), 65_536)
    {
    }

    public ScanFramingOptions(
        ReadOnlySpan<byte> terminator,
        TimeSpan silenceTimeout,
        int maximumUnfinishedBytes)
    {
        if (terminator.IsEmpty)
        {
            throw new ArgumentException("An explicit terminator cannot be empty.", nameof(terminator));
        }

        Validate(silenceTimeout, maximumUnfinishedBytes);

        Terminator = ImmutableArray.Create(terminator.ToArray());
        SilenceTimeout = silenceTimeout;
        MaximumUnfinishedBytes = maximumUnfinishedBytes;
    }

    private ScanFramingOptions(TimeSpan silenceTimeout, int maximumUnfinishedBytes)
    {
        Validate(silenceTimeout, maximumUnfinishedBytes);

        Terminator = ImmutableArray<byte>.Empty;
        SilenceTimeout = silenceTimeout;
        MaximumUnfinishedBytes = maximumUnfinishedBytes;
    }

    public ImmutableArray<byte> Terminator { get; }

    public TimeSpan SilenceTimeout { get; }

    public int MaximumUnfinishedBytes { get; }

    public static ScanFramingOptions WithoutTerminator(TimeSpan silenceTimeout, int maximumUnfinishedBytes) =>
        new(silenceTimeout, maximumUnfinishedBytes);

    internal ScanFramingSnapshot CreateSnapshot() =>
        ScanFramingSnapshot.Create(Terminator.AsSpan(), SilenceTimeout, MaximumUnfinishedBytes);

    private static void Validate(TimeSpan silenceTimeout, int maximumUnfinishedBytes)
    {
        if (silenceTimeout < TimeSpan.FromMilliseconds(10) || silenceTimeout > TimeSpan.FromMilliseconds(5000))
        {
            throw new ArgumentOutOfRangeException(nameof(silenceTimeout));
        }

        if (maximumUnfinishedBytes is < 1 or > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUnfinishedBytes));
        }
    }
}
