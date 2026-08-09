using Scanio.Application.Notebook;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.ViewModels;

public sealed class NotebookRecordItemViewModel : ObservableObject
{
    private bool _isArrivalPulseActive;

    public NotebookRecordItemViewModel(
        NotebookRecord record,
        IUiLocalizer? localizer = null,
        bool pulseArrival = false,
        int occurrenceCount = 1)
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
        OccurrenceCount = occurrenceCount;
        OccurrenceLabel = FormatOccurrenceCount(
            occurrenceCount,
            localizer?.Language ?? UiLanguage.Russian);
        _isArrivalPulseActive = pulseArrival;
    }

    public NotebookRecord Record { get; }
    public long Sequence { get; }
    public string Timestamp { get; }
    public string Payload { get; }
    public string Format { get; }
    public string Transport { get; }
    public int ByteCount { get; }
    public int DuplicateCount { get; }
    public int OccurrenceCount { get; }
    public string OccurrenceLabel { get; }
    public bool IsDuplicate => OccurrenceCount > 1;

    public bool IsArrivalPulseActive
    {
        get => _isArrivalPulseActive;
        private set => SetProperty(ref _isArrivalPulseActive, value);
    }

    public void ClearArrivalPulse() => IsArrivalPulseActive = false;

    public static string FormatOccurrenceCount(int count, UiLanguage language) =>
        count <= 1 ? string.Empty :
        language == UiLanguage.English ? $"{count} scans" :
        count % 10 == 1 && count % 100 != 11 ? $"{count} раз" :
        count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14)
            ? $"{count} раза"
            : $"{count} раз";
}
