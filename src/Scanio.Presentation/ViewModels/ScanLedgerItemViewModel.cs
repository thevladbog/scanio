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
        Analyses = scanEvent.Analyses.Select(result => new AnalysisItemViewModel(result)).ToArray();
        var primary = scanEvent.Analyses.FirstOrDefault(result => result.IsMatch);
        Format = primary?.Format ?? "Unknown";
        Evidence = primary?.Evidence ?? "No analyzer matched.";
        Confidence = primary is null
            ? "Не определено"
            : new AnalysisItemViewModel(primary).Confidence;
        DecodingWarning = scanEvent.Decoded.DecodingWarning;
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
    public string Confidence { get; }
    public string? DecodingWarning { get; }
    public bool HasWarning { get; }
    public IReadOnlyList<AnalysisItemViewModel> Analyses { get; }
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
