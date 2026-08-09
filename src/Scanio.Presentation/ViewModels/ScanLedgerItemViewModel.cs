using Scanio.Application.Monitor;
using System.Diagnostics;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;

namespace Scanio.Presentation.ViewModels;

public sealed class ScanLedgerItemViewModel
{
    public ScanLedgerItemViewModel(LiveScanEvent scanEvent, IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(scanEvent);
        ArgumentNullException.ThrowIfNull(localizer);
        Source = scanEvent;
        Id = scanEvent.Id;
        Sequence = scanEvent.Scan.Sequence;
        Timestamp = scanEvent.Scan.EndedAt.ToLocalTime().ToString("HH:mm:ss.fff");
        Payload = scanEvent.Decoded.Text;
        Raw = ScanDiagnosticJsonSerializer.FormatRaw(scanEvent.Scan.RawBytes);
        Hex = ScanDiagnosticJsonSerializer.FormatHex(scanEvent.Scan.RawBytes);
        ByteCount = scanEvent.Scan.RawBytes.Length;
        Completion = localizer[$"Completion.{scanEvent.Scan.CompletionReason}"];
        StartedAt = scanEvent.Scan.StartedAt.ToLocalTime().ToString("HH:mm:ss.fff");
        EndedAt = scanEvent.Scan.EndedAt.ToLocalTime().ToString("HH:mm:ss.fff");
        Duration = FormatDuration(scanEvent.Scan.EndedAt - scanEvent.Scan.StartedAt);
        DuplicateCount = scanEvent.DuplicateCount;
        Analyses = scanEvent.Analyses.Select(result => new AnalysisItemViewModel(result, localizer)).ToArray();
        var primary = scanEvent.Analyses.FirstOrDefault(result => result.IsMatch);
        var primaryPresentation = primary is null ? null : new AnalysisItemViewModel(primary, localizer);
        Format = primaryPresentation?.Format ?? localizer["Analysis.UnknownFormat"];
        Evidence = primaryPresentation?.Evidence ?? localizer["Analysis.NoMatch"];
        Confidence = primaryPresentation is null
            ? localizer[UiTextKeys.ConfidenceUnknown]
            : primaryPresentation.Confidence;
        DecodingWarning = scanEvent.Decoded.DecodingWarning;
        HasWarning = scanEvent.Decoded.HasDecodingWarning || scanEvent.Analyses.Any(result => !result.ValidationErrors.IsEmpty || !result.ValidationWarnings.IsEmpty);
        var chunks = new List<ChunkItemViewModel>();
        long? previousTimestamp = null;
        foreach (var chunk in scanEvent.Scan.ContributingChunks)
        {
            var interval = previousTimestamp is null
                ? "—"
                : FormatDuration(Stopwatch.GetElapsedTime(previousTimestamp.Value, chunk.MonotonicTimestamp));
            chunks.Add(new ChunkItemViewModel(
                chunk.SequenceNumber,
                chunk.Bytes.Length,
                ScanDiagnosticJsonSerializer.FormatHex(chunk.Bytes.Take(16)),
                chunk.ReceivedAt.ToLocalTime().ToString("HH:mm:ss.fff"),
                interval));
            previousTimestamp = chunk.MonotonicTimestamp;
        }

        Chunks = chunks;
    }

    public long Id { get; }
    public LiveScanEvent Source { get; }
    public long Sequence { get; }
    public string Timestamp { get; }
    public string Payload { get; }
    public string Raw { get; }
    public string Hex { get; }
    public int ByteCount { get; }
    public string Completion { get; }
    public string StartedAt { get; }
    public string EndedAt { get; }
    public string Duration { get; }
    public int DuplicateCount { get; }
    public string Format { get; }
    public string Evidence { get; }
    public string Confidence { get; }
    public string? DecodingWarning { get; }
    public bool HasWarning { get; }
    public IReadOnlyList<AnalysisItemViewModel> Analyses { get; }
    public IReadOnlyList<ChunkItemViewModel> Chunks { get; }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMilliseconds < 1
            ? $"{duration.TotalMilliseconds:0.###} ms"
            : $"{duration.TotalMilliseconds:0.##} ms";
}

public sealed record ChunkItemViewModel(
    long Sequence,
    int ByteCount,
    string HexPreview,
    string ReceivedAt,
    string Interval);
