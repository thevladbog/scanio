using System.Text;
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

namespace Scanio.Presentation.Windows.Tests.Fixtures;

internal static class CPlusFixtureFactory
{
    public static RenderedFixture Create(UiLanguage language, ShellDestination destination)
    {
        var settings = new MemorySettingsService(new AppSettings(Language: language));
        var localizer = new UiLocalizer(settings);
        LocalizationSource.Initialize(localizer);

        var identity = new TransportIdentity(
            TransportKind.Serial,
            "usb\\vid_05f9&pid_2216\\datalogic-powerscan-9600",
            "Datalogic PowerScan 9600 Industrial Barcode Scanner (COM18)",
            endpoint: "COM18");
        var connection = new FixtureConnectionService(identity);
        var connectionViewModel = new ConnectionViewModel(
            new FixtureDeviceEnumerator(),
            connection,
            localizer);
        foreach (var device in FixtureDeviceEnumerator.Devices)
        {
            connectionViewModel.Devices.Add(device);
        }
        connectionViewModel.SelectedDevice = connectionViewModel.Devices[0];

        var monitor = new LiveMonitor();
        var scan = CreateScan(identity);
        var decoded = DecodedPayload.Create(
            scan.PayloadBytes.ToArray(),
            PayloadTextEncoding.Utf8,
            "010460123456789321SERIAL-000042\u001D91ABCD92LONG-CRYPTOGRAPHIC-TAIL",
            "010460123456789321SERIAL-000042<GS>91ABCD92LONG-CRYPTOGRAPHIC-TAIL");
        var analysis = AnalysisResult.Match(
            "HonestSign",
            "Честный знак",
            AnalysisConfidence.Exact,
            "GTIN (01) and serial (21) are present; no online check was performed.",
            "Honest Sign marking code.",
            [
                new AnalysisField("01", "GTIN", "04601234567893"),
                new AnalysisField("21", "Serial number", "SERIAL-000042"),
                new AnalysisField("91", "Verification key", "ABCD"),
                new AnalysisField("92", "Cryptographic tail", "LONG-CRYPTOGRAPHIC-TAIL")
            ]);
        monitor.Append(scan, decoded, [analysis]);
        var monitorViewModel = new MonitorViewModel(
            monitor,
            connection,
            new FixtureClipboardService(),
            localizer);

        var repository = new FixtureNotebookRepository(scan, decoded, analysis);
        var recorder = new NotebookRecorder(repository, monitor, () => new DateTimeOffset(2026, 8, 9, 12, 30, 0, TimeSpan.Zero));
        var interaction = new FixtureNotebookInteractionService();
        var notebookViewModel = new NotebookViewModel(recorder, interaction, localizer)
        {
            SessionName = "Datalogic and Zebra acceptance — long production shift"
        };
        recorder.Start(notebookViewModel.SessionName);
        for (var index = 1; index <= 8; index++)
        {
            notebookViewModel.Records.Add(new NotebookRecordItemViewModel(
                repository.CreateRecord(recorder.CurrentSession!.Id, index),
                localizer));
        }

        var historyViewModel = new HistoryViewModel(repository, interaction, recorder, localizer);
        foreach (var session in repository.GetSessions())
        {
            historyViewModel.Sessions.Add(NotebookSessionItemViewModel.From(session, localizer));
        }

        historyViewModel.SelectedSession = historyViewModel.Sessions.FirstOrDefault(session => session.RecordCount > 0);
        if (historyViewModel.SelectedSession is not null)
        {
            foreach (var record in repository.GetRecords(historyViewModel.SelectedSession.Session.Id))
            {
                historyViewModel.Records.Add(new NotebookRecordItemViewModel(record, localizer));
            }
        }

        var settingsViewModel = new SettingsViewModel(
            settings,
            localizer,
            new FixturePlatformInteractionService(),
            true,
            @"C:\Users\Operator\AppData\Local\Scanio\Data\scanio-notebook.sqlite3",
            "0.4.0-alpha.1",
            new Uri("https://github.com/thevladbog/scanio/releases"));
        var shell = new ShellViewModel(
            connectionViewModel,
            monitorViewModel,
            notebookViewModel,
            historyViewModel,
            settingsViewModel,
            recorder,
            connection);
        typeof(ShellViewModel)
            .GetProperty(nameof(ShellViewModel.SelectedDestination))!
            .SetValue(shell, destination);

        return new RenderedFixture(new Scanio.Presentation.MainWindow(shell), recorder);
    }

    private static CompletedScan CreateScan(TransportIdentity identity)
    {
        const string payload = "010460123456789321SERIAL-000042\u001D91ABCD92LONG-CRYPTOGRAPHIC-TAIL";
        var raw = Encoding.UTF8.GetBytes(payload + "\r");
        return CompletedScan.Create(
            42,
            raw,
            Encoding.UTF8.GetBytes(payload),
            [RawChunk.Create(1, raw[..22], new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero), 10_000, identity),
             RawChunk.Create(2, raw[22..], new DateTimeOffset(2026, 8, 9, 12, 34, 56, 18, TimeSpan.Zero), 10_018, identity)],
            new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 9, 12, 34, 56, 18, TimeSpan.Zero),
            10_000,
            10_018,
            ScanCompletionReason.Terminator,
            ScanFramingSnapshot.Create([0x0D], TimeSpan.FromMilliseconds(100), 65_536),
            identity);
    }
}

internal sealed class RenderedFixture(Scanio.Presentation.MainWindow window, NotebookRecorder recorder) : IDisposable
{
    public Scanio.Presentation.MainWindow Window { get; } = window;

    public void Dispose()
    {
        Window.Hide();
        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

internal sealed class MemorySettingsService(AppSettings initial) : IAppSettingsService
{
    public AppSettings Current { get; private set; } = initial;
    public event EventHandler? Changed;

    public void Update(Func<AppSettings, AppSettings> update)
    {
        Current = update(Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FixtureDeviceEnumerator : ISerialDeviceEnumerator
{
    public static IReadOnlyList<SerialDeviceInfo> Devices { get; } =
    [
        Device("COM18", "Datalogic PowerScan 9600 Industrial Barcode Scanner (COM18)", "Datalogic S.p.A."),
        Device("COM24", "Zebra DS3608 Rugged Barcode Scanner with USB CDC Host (COM24)", "Zebra Technologies"),
        Device("COM7", "USB-SERIAL CH340 compatible adapter (COM7)", "wch.cn"),
        Device("COM31", "Datalogic Gryphon GD4590 USB COM Port with a deliberately long device label (COM31)", "Datalogic")
    ];

    public Task<IReadOnlyList<SerialDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Devices);

    private static SerialDeviceInfo Device(string port, string name, string maker) =>
        new(port, name, maker, 0x05F9, 0x2216, "SERIAL-0123456789", $"USB\\VID_05F9&PID_2216\\{port}", $"fixture:{port}");
}

internal sealed class FixtureConnectionService(TransportIdentity identity) : IConnectionService
{
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public ConnectionState State { get; private set; } = ConnectionState.Connected;
    public TransportIdentity? ActiveIdentity { get; private set; } = identity;
    public ConnectionPresentationSnapshot? CurrentSnapshot { get; private set; } =
        new(identity, ConnectionState.Connected, SerialConnectionOptions.Default(identity.Endpoint!));
    public Scanio.Transports.Keyboard.IKeyboardCaptureInput? KeyboardInput => null;

    public Task ConnectAsync(SerialDeviceInfo device, SerialConnectionOptions options, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ConnectKeyboardAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        State = ConnectionState.Disconnected;
        ActiveIdentity = null;
        CurrentSnapshot = null;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(State, null));
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FixtureNotebookInteractionService : INotebookInteractionService
{
    public void SetClipboardText(string text) { }
    public string? ChooseExportPath(NotebookExportFormat format, string suggestedName) => null;
    public bool ConfirmDelete(string sessionName) => false;
    public void ShowError(string message) => throw new AssertFailedException(message);
}

internal sealed class FixtureClipboardService : IClipboardService
{
    public void SetText(string text) { }
}

internal sealed class FixturePlatformInteractionService : IPlatformInteractionService
{
    public void OpenFolder(string path) { }
    public void OpenUri(Uri uri) { }
}

internal sealed class FixtureNotebookRepository : INotebookRepository
{
    private readonly CompletedScan _scan;
    private readonly DecodedPayload _decoded;
    private readonly AnalysisResult _analysis;
    private readonly List<NotebookSession> _sessions = [];
    private readonly List<NotebookRecord> _records = [];

    public FixtureNotebookRepository(CompletedScan scan, DecodedPayload decoded, AnalysisResult analysis)
    {
        _scan = scan;
        _decoded = decoded;
        _analysis = analysis;
        var first = NotebookSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Warehouse acceptance — Zebra DS3608", new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero))
            .WithSummary("Warehouse acceptance — Zebra DS3608", new DateTimeOffset(2026, 8, 8, 10, 42, 0, TimeSpan.Zero), 128);
        var second = NotebookSession.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Datalogic PowerScan long-run COM test", new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero))
            .WithSummary("Datalogic PowerScan long-run COM test", new DateTimeOffset(2026, 8, 7, 11, 37, 0, TimeSpan.Zero), 42);
        _sessions.AddRange([first, second]);
        for (var index = 1; index <= 12; index++)
        {
            _records.Add(CreateRecord(first.Id, index));
        }
    }

    public void Initialize() { }

    public NotebookSession CreateSession(string name, DateTimeOffset startedAt)
    {
        var session = NotebookSession.Create(Guid.NewGuid(), name, startedAt);
        _sessions.Insert(0, session);
        return session;
    }

    public NotebookRecord CreateRecord(Guid sessionId, int index) => NotebookRecord.Create(
        index,
        sessionId,
        _scan,
        _decoded,
        [_analysis],
        index % 3 == 0 ? 2 : 1,
        new DateTimeOffset(2026, 8, 9, 12, 30, index, TimeSpan.Zero));

    public void Append(NotebookRecord record) => _records.Add(record);

    public void CompleteSession(Guid sessionId, DateTimeOffset endedAt) { }

    public IReadOnlyList<NotebookSession> GetSessions() => _sessions.ToArray();

    public IReadOnlyList<NotebookRecord> GetRecords(Guid sessionId) =>
        _records.Where(record => record.SessionId == sessionId).ToArray();

    public void RenameSession(Guid sessionId, string name) { }

    public void DeleteSession(Guid sessionId) => _sessions.RemoveAll(session => session.Id == sessionId);
}
