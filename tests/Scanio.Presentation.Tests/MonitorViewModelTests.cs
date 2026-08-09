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
