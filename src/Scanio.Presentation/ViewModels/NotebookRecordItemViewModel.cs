using Scanio.Application.Notebook;

namespace Scanio.Presentation.ViewModels;

public sealed class NotebookRecordItemViewModel
{
    public NotebookRecordItemViewModel(NotebookRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Record = record;
        Sequence = record.Sequence;
        Timestamp = record.RecordedAt.ToLocalTime().ToString("HH:mm:ss.fff");
        Payload = record.Decoded.EscapedDisplay;
        Format = record.Analyses.FirstOrDefault(result => result.IsMatch)?.Format ?? "Unknown";
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
