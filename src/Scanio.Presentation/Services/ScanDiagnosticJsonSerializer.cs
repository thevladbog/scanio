using System.Text.Json;
using System.Text.Json.Serialization;
using Scanio.Application.Monitor;

namespace Scanio.Presentation.Services;

public static class ScanDiagnosticJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(LiveScanEvent scanEvent)
    {
        ArgumentNullException.ThrowIfNull(scanEvent);
        var scan = scanEvent.Scan;
        return JsonSerializer.Serialize(new
        {
            scan.Sequence,
            scan.StartedAt,
            scan.EndedAt,
            durationMilliseconds = (scan.EndedAt - scan.StartedAt).TotalMilliseconds,
            scan.CompletionReason,
            transport = new
            {
                kind = scan.Transport.Kind.ToString(),
                scan.Transport.Endpoint,
                scan.Transport.DisplayName,
                scan.Transport.StableId,
                scan.Transport.HardwareId
            },
            payload = scanEvent.Decoded.Text,
            raw = FormatRaw(scan.RawBytes),
            hex = FormatHex(scan.RawBytes),
            byteCount = scan.RawBytes.Length,
            scanEvent.DuplicateCount,
            chunks = scan.ContributingChunks.Select(chunk => new
            {
                chunk.SequenceNumber,
                chunk.ReceivedAt,
                chunk.MonotonicTimestamp,
                byteCount = chunk.Bytes.Length,
                hex = FormatHex(chunk.Bytes)
            }),
            analyses = scanEvent.Analyses.Select(result => new
            {
                result.AnalyzerName,
                result.Format,
                result.IsMatch,
                result.Confidence,
                result.Evidence,
                result.Summary,
                result.Fields,
                result.ValidationErrors,
                result.ValidationWarnings
            })
        }, Options);
    }

    public static string FormatHex(IEnumerable<byte> bytes) =>
        string.Join(' ', bytes.Select(value => value.ToString("X2")));

    public static string FormatRaw(IEnumerable<byte> bytes) => string.Concat(bytes.Select(value => value switch
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
