using Scanio.Application.Notebook;
using Scanio.Presentation.Localization;

namespace Scanio.Presentation.ViewModels;

public sealed class NotebookRecordItemViewModel
{
    public NotebookRecordItemViewModel(NotebookRecord record, IUiLocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        Record = record;
        Sequence = record.Sequence;
        Timestamp = record.RecordedAt.ToLocalTime().ToString("HH:mm:ss.fff");
        Payload = record.Decoded.EscapedDisplay;
        var primary = record.Analyses.FirstOrDefault(result => result.IsMatch);
        Format = primary is null
            ? localizer?["Analysis.UnknownFormat"] ?? "Unknown"
            : localizer is null
                ? primary.Format
                : new AnalysisItemViewModel(primary, localizer).Format;
        Transport = record.Scan.Transport.DisplayName;
        ByteCount = record.Scan.RawBytes.Length;
        DuplicateCount = record.DuplicateCount;
    }

    public NotebookRecord Record { get; }
    public long Sequence { get; }
    public string Timestamp { get; }
    public string Payload { get; }
    public string Format { get; }
    public string Transport { get; }
    public int ByteCount { get; }
    public int DuplicateCount { get; }
}
