using Scanio.Application.Monitor;

namespace Scanio.Presentation.ViewModels;

public sealed class ScanLedgerItemViewModel
{
    public ScanLedgerItemViewModel(LiveScanEvent scanEvent)
    {
        ArgumentNullException.ThrowIfNull(scanEvent);
        Id = scanEvent.Id;
        Sequence = scanEvent.Scan.Sequence;
        Timestamp = scanEvent.Scan.EndedAt.ToLocalTime().ToString("HH:mm:ss.fff");
        Payload = scanEvent.Decoded.Text;
        Raw = FormatRaw(scanEvent.Scan.RawBytes);
        Hex = string.Join(' ', scanEvent.Scan.RawBytes.Select(value => value.ToString("X2")));
        ByteCount = scanEvent.Scan.RawBytes.Length;
        Completion = scanEvent.Scan.CompletionReason.ToString();
        DuplicateCount = scanEvent.DuplicateCount;
        Format = scanEvent.Analyses.FirstOrDefault(result => result.IsMatch)?.Format ?? "Unknown";
        Evidence = scanEvent.Analyses.FirstOrDefault(result => result.IsMatch)?.Evidence ?? "No analyzer matched.";
        HasWarning = scanEvent.Decoded.HasDecodingWarning || scanEvent.Analyses.Any(result => !result.ValidationErrors.IsEmpty || !result.ValidationWarnings.IsEmpty);
        Chunks = scanEvent.Scan.ContributingChunks
            .Select(chunk => new ChunkItemViewModel(
                chunk.SequenceNumber,
                chunk.Bytes.Length,
                string.Join(' ', chunk.Bytes.Take(16).Select(value => value.ToString("X2"))),
                chunk.ReceivedAt.ToLocalTime().ToString("HH:mm:ss.fff")))
            .ToArray();
    }

    public long Id { get; }
    public long Sequence { get; }
    public string Timestamp { get; }
    public string Payload { get; }
    public string Raw { get; }
    public string Hex { get; }
    public int ByteCount { get; }
    public string Completion { get; }
    public int DuplicateCount { get; }
    public string Format { get; }
    public string Evidence { get; }
    public bool HasWarning { get; }
    public IReadOnlyList<ChunkItemViewModel> Chunks { get; }

    private static string FormatRaw(IEnumerable<byte> bytes) => string.Concat(bytes.Select(value => value switch
    {
        0x0D => "<CR>",
        0x0A => "<LF>",
        0x1D => "<GS>",
        0x1E => "<RS>",
        0x04 => "<EOT>",
        0x1B => "<ESC>",
        >= 0x20 and <= 0x7E => ((char)value).ToString(),
        _ => $"\\x{value:X2}"
    }));
}

public sealed record ChunkItemViewModel(long Sequence, int ByteCount, string HexPreview, string ReceivedAt);
