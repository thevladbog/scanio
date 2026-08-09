using System.Text;
using System.Text.Json;

namespace Scanio.Application.Notebook;

public enum NotebookExportFormat
{
    Text,
    Csv,
    Json
}

public static class NotebookExportService
{
    public static string BuildClipboardText(IEnumerable<NotebookRecord> records)
    {
        var owned = Own(records);
        return string.Join(Environment.NewLine, owned.Select(record => record.Decoded.EscapedDisplay));
    }

    public static void Export(
        string path,
        NotebookExportFormat format,
        IEnumerable<NotebookRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var owned = Own(records);
        AtomicTextFileWriter.Write(path, writer =>
        {
            switch (format)
            {
                case NotebookExportFormat.Text:
                    writer.Write(BuildClipboardText(owned));
                    break;
                case NotebookExportFormat.Csv:
                    WriteCsv(writer, owned);
                    break;
                case NotebookExportFormat.Json:
                    WriteJson(writer, owned);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported notebook export format.");
            }
        });
    }

    private static NotebookRecord[] Own(IEnumerable<NotebookRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records.Select(record => record ?? throw new ArgumentException(
            "Notebook export cannot contain null records.", nameof(records))).ToArray();
    }

    private static void WriteCsv(TextWriter writer, IReadOnlyList<NotebookRecord> records)
    {
        writer.Write("Sequence,RecordedAt,Transport,Format,Value,RawBase64\r\n");
        foreach (var record in records)
        {
            var values = new[]
            {
                record.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                record.RecordedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                record.Scan.Transport.DisplayName,
                record.Analyses.FirstOrDefault()?.Format ?? "Unknown",
                record.Decoded.EscapedDisplay,
                Convert.ToBase64String(record.Scan.RawBytes.AsSpan())
            };
            writer.Write(string.Join(',', values.Select(EscapeCsv)));
            writer.Write("\r\n");
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static void WriteJson(TextWriter writer, IReadOnlyList<NotebookRecord> records)
    {
        var document = new
        {
            exportedAt = DateTimeOffset.UtcNow,
            records = records.Select(record => new
            {
                sequence = record.Sequence,
                sessionId = record.SessionId,
                recordedAt = record.RecordedAt,
                duplicateCount = record.DuplicateCount,
                text = record.Decoded.Text,
                escapedDisplay = record.Decoded.EscapedDisplay,
                encoding = record.Decoded.Encoding.ToString(),
                decodingWarning = record.Decoded.DecodingWarning,
                rawBase64 = Convert.ToBase64String(record.Scan.RawBytes.AsSpan()),
                payloadBase64 = Convert.ToBase64String(record.Scan.PayloadBytes.AsSpan()),
                transport = new
                {
                    kind = record.Scan.Transport.Kind.ToString(),
                    stableId = record.Scan.Transport.StableId,
                    displayName = record.Scan.Transport.DisplayName,
                    hardwareId = record.Scan.Transport.HardwareId
                },
                framing = new
                {
                    terminatorBase64 = Convert.ToBase64String(record.Scan.Framing.Terminator.AsSpan()),
                    silenceTimeoutMilliseconds = record.Scan.Framing.SilenceTimeout.TotalMilliseconds,
                    maximumUnfinishedBytes = record.Scan.Framing.MaximumUnfinishedBytes,
                    completionReason = record.Scan.CompletionReason.ToString()
                },
                analyses = record.Analyses.Select(analysis => new
                {
                    analyzerName = analysis.AnalyzerName,
                    format = analysis.Format,
                    isMatch = analysis.IsMatch,
                    confidence = analysis.Confidence.ToString(),
                    evidence = analysis.Evidence,
                    summary = analysis.Summary,
                    fields = analysis.Fields.Select(field => new
                    {
                        code = field.Code,
                        name = field.Name,
                        value = field.Value
                    }),
                    validationErrors = analysis.ValidationErrors,
                    validationWarnings = analysis.ValidationWarnings
                })
            })
        };

        writer.Write(JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }
}
