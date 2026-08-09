using Scanio.Application.Monitor;
using Scanio.Application.Notebook;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class ShellViewModelTests
{
    [TestMethod]
    public async Task ShowMonitorCommand_ActivatesMonitorBeforeItBecomesCurrent()
    {
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "first"), Decoded("first"), []);
        monitor.Append(Scan(2, "second"), Decoded("second"), []);
        var settings = new TestSettingsService();
        var localizer = new UiLocalizer(settings);
        var connection = new FakeConnectionService();
        var monitorViewModel = new MonitorViewModel(monitor, connection, new FakeClipboardService(), localizer);
        monitorViewModel.SelectedEvent = monitorViewModel.Events[0];
        monitor.Append(Scan(3, "third"), Decoded("third"), []);
        var repository = new FakeRepository();
        await using var recorder = new NotebookRecorder(repository, monitor);
        var shell = new ShellViewModel(
            new ConnectionViewModel(new FakeDeviceEnumerator(), connection, localizer),
            monitorViewModel,
            new NotebookViewModel(recorder, new FakeNotebookInteractionService(), localizer),
            new HistoryViewModel(repository, new FakeNotebookInteractionService(), recorder, localizer),
            new SettingsViewModel(settings, localizer, new FakePlatformInteractionService(), true, "/Scanio/Data/scanio.db", "test", new Uri("https://example.test/releases")),
            recorder,
            connection);
        string? selectedPayloadWhenMonitorBecameCurrent = null;
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.SelectedDestination) && shell.SelectedDestination == ShellDestination.Monitor)
            {
                selectedPayloadWhenMonitorBecameCurrent = monitorViewModel.SelectedEvent?.Payload;
            }
        };

        await shell.ShowMonitorCommand.ExecuteAsync();

        Assert.AreEqual(ShellDestination.Monitor, shell.SelectedDestination);
        Assert.AreEqual("third", selectedPayloadWhenMonitorBecameCurrent);
    }

    private static readonly TransportIdentity Identity = new(TransportKind.Serial, "serial:test", "COM7");

    private static CompletedScan Scan(long sequence, string raw) =>
        CompletedScan.Create(
            sequence,
            System.Text.Encoding.ASCII.GetBytes(raw),
            System.Text.Encoding.ASCII.GetBytes(raw),
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

        public ConnectionState State => ConnectionState.Detected;
        public TransportIdentity? ActiveIdentity => null;
        public ConnectionPresentationSnapshot? CurrentSnapshot => null;
        public Scanio.Transports.Keyboard.IKeyboardCaptureInput? KeyboardInput => null;
        public Task ConnectAsync(SerialDeviceInfo device, SerialConnectionOptions options, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ConnectKeyboardAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public void SetText(string text)
        {
        }
    }

    private sealed class FakeDeviceEnumerator : ISerialDeviceEnumerator
    {
        public Task<IReadOnlyList<SerialDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SerialDeviceInfo>>([]);
    }

    private sealed class FakeNotebookInteractionService : INotebookInteractionService
    {
        public void SetClipboardText(string text)
        {
        }

        public string? ChooseExportPath(NotebookExportFormat format, string suggestedName) => null;
        public bool ConfirmDelete(string sessionName) => false;
        public void ShowError(string message)
        {
        }
    }

    private sealed class FakePlatformInteractionService : IPlatformInteractionService
    {
        public void OpenFolder(string path)
        {
        }

        public void OpenUri(Uri uri)
        {
        }
    }

    private sealed class FakeRepository : INotebookRepository
    {
        public void Initialize()
        {
        }

        public NotebookSession CreateSession(string name, DateTimeOffset startedAt) => throw new NotSupportedException();
        public void Append(NotebookRecord record) => throw new NotSupportedException();
        public void CompleteSession(Guid sessionId, DateTimeOffset endedAt) => throw new NotSupportedException();
        public IReadOnlyList<NotebookSession> GetSessions() => [];
        public IReadOnlyList<NotebookRecord> GetRecords(Guid sessionId) => [];
        public void RenameSession(Guid sessionId, string name) => throw new NotSupportedException();
        public void DeleteSession(Guid sessionId) => throw new NotSupportedException();
    }

    private sealed class TestSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler? Changed;

        public void Update(Func<AppSettings, AppSettings> update)
        {
            Current = update(Current);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
