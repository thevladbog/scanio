using Scanio.Application.Notebook;
using Scanio.Presentation.Localization;

namespace Scanio.Presentation.ViewModels;

public static class NotebookRecordGrouping
{
    public static IReadOnlyList<NotebookRecordItemViewModel> Build(
        IEnumerable<NotebookRecord> records,
        IUiLocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var record in records)
        {
            var key = NotebookPayloadIdentity.Create(record.Scan.PayloadBytes.AsSpan());
            var count = groups.TryGetValue(key, out var previous) ? previous.Count + 1 : 1;
            groups[key] = new Group(record, count);
            order.Remove(key);
            order.Add(key);
        }

        return order
            .Select(key => new NotebookRecordItemViewModel(
                groups[key].Record,
                localizer,
                occurrenceCount: groups[key].Count))
            .ToArray();
    }

    private sealed record Group(NotebookRecord Record, int Count);
}
