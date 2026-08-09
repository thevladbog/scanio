using System.Text;
using System.Text.Json;
using Scanio.Application.Notebook;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Application.Tests;

[TestClass]
public sealed class NotebookExportServiceTests
{
    private const char GroupSeparator = '\u001D';

    [TestMethod]
    public void BuildExactClipboardText_PreservesControlsAndByteExactFirstOccurrenceOrder()
    {
        var records = new[]
        {
            CreateRecord(1, $"01{GroupSeparator}21", "01<GS>21", [0x30, 0x31, 0x1D, 0x32, 0x31]),
            CreateRecord(2, $"01{GroupSeparator}21", "01<GS>21", [0x30, 0x31, 0x1D, 0x32, 0x31]),
            CreateRecord(3, "other", "other", [0x6F])
        };

        Assert.AreEqual(
            $"01{GroupSeparator}21{Environment.NewLine}other",
            NotebookExportService.BuildExactClipboardText(records, unique: true));
    }

    [TestMethod]
    public void BuildReadableClipboardText_UsesVisibleControlLabels()
    {
        var record = CreateRecord(
            1, $"01{GroupSeparator}21", "01<GS>21", [0x30, 0x31, 0x1D, 0x32, 0x31]);

        Assert.AreEqual("01<GS>21", NotebookExportService.BuildReadableClipboardText([record]));
    }

    [TestMethod]
    public void Export_CsvUsesUtf8AndRfc4180Quoting()
    {
        var path = CreateTemporaryPath();
        try
        {
            NotebookExportService.Export(
                path,
                NotebookExportFormat.Csv,
                [
                    CreateRecord(1, $"01{GroupSeparator}21", "01<GS>21", [0x30, 0x31, 0x1D, 0x32, 0x31]),
                    CreateRecord(2, "значение, \"A\"", "значение, \"A\"", [0xFF])
                ]);

            var csv = File.ReadAllText(path, Encoding.UTF8);
            var firstRecordFields = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1].Split(',');
            StringAssert.StartsWith(csv, "Sequence,RecordedAt,Transport,Format,Value,RawBase64");
            Assert.AreEqual($"01{GroupSeparator}21", firstRecordFields[4]);
            StringAssert.Contains(csv, "\"значение, \"\"A\"\"\"");
            StringAssert.Contains(csv, Convert.ToBase64String([0xFF]));
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [TestMethod]
    public void Export_TextWritesExactControlCharacters()
    {
        var path = CreateTemporaryPath();
        try
        {
            NotebookExportService.Export(
                path,
                NotebookExportFormat.Text,
                [CreateRecord(1, $"one{GroupSeparator}", "one<GS>", [0x31, 0x1D]), CreateRecord(2, "two", "two", [0x32])]);

            Assert.AreEqual($"one{GroupSeparator}{Environment.NewLine}two", File.ReadAllText(path));
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [TestMethod]
    public void Export_ReadableTextWritesVisibleControlLabels()
    {
        var path = CreateTemporaryPath();
        try
        {
            NotebookExportService.Export(
                path,
                NotebookExportFormat.ReadableText,
                [CreateRecord(1, $"01{GroupSeparator}21", "01<GS>21", [0x30, 0x31, 0x1D, 0x32, 0x31])]);

            Assert.AreEqual("01<GS>21", File.ReadAllText(path));
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [TestMethod]
    public void Export_JsonContainsExactBytesAndStructuredAnalysis()
    {
        var path = CreateTemporaryPath();
        try
        {
            NotebookExportService.Export(
                path,
                NotebookExportFormat.Json,
                [CreateRecord(1, $"01{GroupSeparator}21", "01<GS>21", [0x00, 0x1D, 0xFF])]);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var record = document.RootElement.GetProperty("records")[0];
            Assert.AreEqual($"01{GroupSeparator}21", record.GetProperty("text").GetString());
            Assert.AreEqual("01<GS>21", record.GetProperty("escapedDisplay").GetString());
            Assert.AreEqual(Convert.ToBase64String([0x00, 0x1D, 0xFF]), record.GetProperty("rawBase64").GetString());
            Assert.AreEqual("GS1", record.GetProperty("analyses")[0].GetProperty("format").GetString());
            Assert.AreEqual("GTIN", record.GetProperty("analyses")[0].GetProperty("fields")[0].GetProperty("name").GetString());
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [TestMethod]
    public void AtomicTextFileWriter_WhenWritingFails_PreservesExistingTargetAndRemovesTemporaryFile()
    {
        var path = CreateTemporaryPath();
        File.WriteAllText(path, "original");
        try
        {
            Assert.ThrowsExactly<IOException>(() =>
                AtomicTextFileWriter.Write(path, writer =>
                {
                    writer.Write("partial");
                    throw new IOException("write failed");
                }));

            Assert.AreEqual("original", File.ReadAllText(path));
            Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    private static NotebookRecord CreateRecord(long sequence, string text, string escapedDisplay, byte[] payload)
    {
        var identity = new TransportIdentity(TransportKind.Serial, "COM7", "COM7");
        var scan = CompletedScan.Create(
            sequence,
            payload,
            payload,
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            sequence,
            sequence,
            ScanCompletionReason.SilenceTimeout,
            ScanFramingSnapshot.Create([0x0D], TimeSpan.FromMilliseconds(100), 65_536),
            identity);
        var decoded = DecodedPayload.Create(payload, PayloadTextEncoding.Utf8, text, escapedDisplay);
        var analysis = AnalysisResult.Match(
            "Fixture",
            "GS1",
            AnalysisConfidence.Exact,
            "fixture",
            "fixture",
            [new AnalysisField("01", "GTIN", "04601234567890")]);
        return NotebookRecord.Create(
            sequence,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            scan,
            decoded,
            [analysis],
            1,
            DateTimeOffset.UnixEpoch);
    }

    private static string CreateTemporaryPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scanio-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "export.data");
    }

    private static void DeleteTemporaryPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
