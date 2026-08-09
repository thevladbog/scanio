using Scanio.Application.Monitor;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Presentation.Services;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class MonitorViewModelTests
{
    [TestMethod]
    public async Task OlderSelectionRemainsFixedUntilReturnToLatest()
    {
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "first"), Decoded("first"), []);
        monitor.Append(Scan(2, "second"), Decoded("second"), []);
        var viewModel = new MonitorViewModel(monitor, new FakeConnectionService());

        viewModel.SelectedEvent = viewModel.Events[0];
        monitor.Append(Scan(3, "third"), Decoded("third"), []);

        Assert.AreEqual("first", viewModel.SelectedEvent?.Payload);
        Assert.IsTrue(viewModel.ShowReturnToLatest);

        await viewModel.ReturnToLatestCommand.ExecuteAsync();

        Assert.AreEqual("third", viewModel.SelectedEvent?.Payload);
        Assert.IsFalse(viewModel.ShowReturnToLatest);
    }

    [TestMethod]
    public void SelectedEventExposesPersistentRawAndHexEvidence()
    {
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "A\r"), Decoded("A"), []);
        var viewModel = new MonitorViewModel(monitor, new FakeConnectionService());

        Assert.AreEqual("A", viewModel.SelectedEvent?.Payload);
        Assert.AreEqual("41 0D", viewModel.SelectedEvent?.Hex);
        Assert.AreEqual("A<CR>", viewModel.SelectedEvent?.Raw);
    }

    [TestMethod]
    public void SelectedEventMapsStructuredFieldsAndValidationMessages()
    {
        var analysis = AnalysisResult.Match(
            "GS1",
            "GS1 element string",
            AnalysisConfidence.Inferred,
            "Application identifier structure.",
            "GS1 payload.",
            [new AnalysisField("01", "GTIN", "04601234567893")],
            ["Fixture error."],
            ["Fixture warning."]);
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "value"), Decoded("value"), [analysis]);

        var viewModel = new MonitorViewModel(monitor, new FakeConnectionService());

        Assert.HasCount(1, viewModel.SelectedEvent!.Analyses);
        var displayed = viewModel.SelectedEvent.Analyses.Single();
        Assert.AreEqual("Предположение по структуре", displayed.Confidence);
        Assert.AreEqual("04601234567893", displayed.Fields.Single().Value);
        CollectionAssert.Contains(displayed.Errors.ToArray(), "Fixture error.");
        CollectionAssert.Contains(displayed.Warnings.ToArray(), "Fixture warning.");
    }

    [TestMethod]
    public void SelectedEventRetainsAllOrderedAnalyzerInterpretations()
    {
        var first = AnalysisResult.Match(
            "HonestSign", "Честный знак", AnalysisConfidence.Inferred,
            "Serialized GS1 structure.", "Marking payload.");
        var second = AnalysisResult.Match(
            "GS1", "GS1 element string", AnalysisConfidence.Exact,
            "Explicit separators.", "GS1 payload.");
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "value"), Decoded("value"), [first, second]);

        var viewModel = new MonitorViewModel(monitor, new FakeConnectionService());

        var selected = viewModel.SelectedEvent!;
        CollectionAssert.AreEqual(
            new[] { "Честный знак", "GS1 element string" },
            selected.Analyses.Select(item => item.Format).ToArray());
        Assert.AreEqual("Честный знак", selected.Format);
    }

    private static readonly TransportIdentity Identity = new(TransportKind.Serial, "serial:test", "COM7");

    private static CompletedScan Scan(long sequence, string raw) =>
        CompletedScan.Create(
            sequence,
            System.Text.Encoding.ASCII.GetBytes(raw),
            System.Text.Encoding.ASCII.GetBytes(raw.TrimEnd('\r')),
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            sequence,
            sequence,
            ScanCompletionReason.Terminator,
            ScanFramingSnapshot.Create([0x0D], TimeSpan.FromMilliseconds(100), 65_536),
            Identity);

    private static DecodedPayload Decoded(string value) =>
        DecodedPayload.Create(System.Text.Encoding.ASCII.GetBytes(value), PayloadTextEncoding.Ascii, value, value);

    private sealed class FakeConnectionService : IConnectionService
    {
        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }
        public ConnectionState State => ConnectionState.Connected;
        public TransportIdentity? ActiveIdentity => Identity;
        public Task ConnectAsync(Scanio.Platform.Windows.Devices.SerialDeviceInfo device, Scanio.Transports.Serial.SerialConnectionOptions options, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
