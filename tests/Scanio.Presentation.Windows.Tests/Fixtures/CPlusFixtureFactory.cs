using System.Text;
using System.Windows;
using Scanio.Analysis;
using Scanio.Application.Connection;
using Scanio.Application.Monitor;
using Scanio.Application.Notebook;
using Scanio.Capture;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using Scanio.Presentation.Tests.Infrastructure;
using Scanio.Presentation.ViewModels;
using Scanio.Presentation.Views;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.Windows.Tests.Fixtures;

internal static class CPlusFixtureFactory
{
    public static KeyboardCaptureFixture CreateKeyboardCapture(UiLanguage language)
    {
        var settings = new MemorySettingsService(new AppSettings(Language: language));
        var localizer = new UiLocalizer(settings);
        InitializeGlobalSources(settings, localizer);
        var monitor = new LiveMonitor();
        var pipeline = new ScanProcessingPipeline(
            new ScanAssembler(),
            PayloadTextEncoding.Utf8,
            BuiltInAnalyzers.CreatePipeline(),
            monitor);
        var connection = new ConnectionService(new ConnectionCoordinator(pipeline));
        var connectionViewModel = new ConnectionViewModel(
            new FixtureDeviceEnumerator(),
            connection,
            localizer)
        {
            SelectedMode = ConnectionMode.Keyboard
        };
        var monitorViewModel = new MonitorViewModel(
            monitor,
            connection,
            new FixtureClipboardService(),
            localizer);
        var view = new ConnectionView { DataContext = connectionViewModel };
        var window = new Window
        {
            Content = view,
            Title = "Keyboard capture integration"
        };

        return new KeyboardCaptureFixture(
            window,
            connectionViewModel,
            monitorViewModel,
            monitor,
            connection);
    }

    public static RenderedFixture Create(
        UiLanguage language,
        ShellDestination destination,
        ConnectionMode connectionMode = ConnectionMode.Serial) =>
        CreateCore(
            language,
            destination,
            connectionMode,
            ListDensity.Comfortable,
            showHexPreview: true,
            showChunkBoundaries: true);

    public static RenderedFixture Create(RenderedEvidenceVariant variant) =>
        CreateCore(
            variant.Language,
            variant.Destination,
            variant.ConnectionMode,
            variant.ListDensity,
            variant.ShowHexPreview,
            variant.ShowChunkBoundaries);

    private static RenderedFixture CreateCore(
        UiLanguage language,
        ShellDestination destination,
        ConnectionMode connectionMode,
        ListDensity listDensity,
        bool showHexPreview,
        bool showChunkBoundaries)
    {
        var settings = new MemorySettingsService(new AppSettings(
            Language: language,
            ShowHexPreview: showHexPreview,
            ShowChunkBoundaries: showChunkBoundaries,
            ListDensity: listDensity));
        var localizer = new UiLocalizer(settings);
        InitializeGlobalSources(settings, localizer);

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var contextRestored = false;
        try
        {
            var identity = new TransportIdentity(
                TransportKind.Serial,
                "usb\\vid_05f9&pid_2216\\datalogic-powerscan-9600",
                "Datalogic PowerScan 9600 Industrial Barcode Scanner (COM18)",
                endpoint: "COM18");
            var connection = connectionMode == ConnectionMode.Keyboard
                ? new FixtureConnectionService()
                : new FixtureConnectionService(identity);
            var connectionViewModel = new ConnectionViewModel(
                new FixtureDeviceEnumerator(),
                connection,
                localizer);
            foreach (var device in FixtureDeviceEnumerator.Devices)
            {
                connectionViewModel.Devices.Add(device);
            }

            connectionViewModel.SelectedDevice = connectionViewModel.Devices[0];
            connectionViewModel.SelectedMode = connectionMode;

            var monitor = new LiveMonitor();
            var monitorViewModel = new MonitorViewModel(
                monitor,
                connection,
                new FixtureClipboardService(),
                localizer);
            var repository = new FixtureNotebookRepository();
            var recordedAt = new DateTimeOffset(2026, 8, 9, 12, 30, 0, TimeSpan.Zero);
            var recorder = new NotebookRecorder(repository, monitor, () => recordedAt = recordedAt.AddSeconds(1));
            var interaction = new FixtureNotebookInteractionService();
            var notebookViewModel = new NotebookViewModel(recorder, interaction, localizer)
            {
                SessionName = "Datalogic and Zebra acceptance — A/B/A grouping"
            };

            notebookViewModel.StartCommand.ExecuteAsync().GetAwaiter().GetResult();
            var evidenceSessionId = recorder.CurrentSession!.Id;
            var analysis = CreateEvidenceAnalysis();
            var firstA = CreateScan(identity, 42, EvidencePayloadA);
            var middleB = CreateScan(identity, 43, EvidencePayloadB);
            var latestA = CreateScan(identity, 44, EvidencePayloadA);
            monitor.Append(firstA, CreateDecoded(firstA, EvidencePayloadA), [analysis]);
            monitor.Append(middleB, CreateDecoded(middleB, EvidencePayloadB), []);
            monitor.Append(latestA, CreateDecoded(latestA, EvidencePayloadA), [analysis]);
            notebookViewModel.StopCommand.ExecuteAsync().GetAwaiter().GetResult();

            var historyViewModel = new HistoryViewModel(repository, interaction, recorder, localizer);
            var settingsViewModel = new SettingsViewModel(
                settings,
                localizer,
                new FixturePlatformInteractionService(),
                true,
                @"C:\Users\Operator\AppData\Local\Scanio\Data\scanio-notebook.sqlite3",
                "0.5.0-beta.5",
                new Uri("https://github.com/thevladbog/scanio/releases"));
            var shell = new ShellViewModel(
                connectionViewModel,
                monitorViewModel,
                notebookViewModel,
                historyViewModel,
                settingsViewModel,
                recorder,
                connection);

            shell.ShowHistoryCommand.ExecuteAsync().GetAwaiter().GetResult();
            historyViewModel.OpenCommand.ExecuteAsync().GetAwaiter().GetResult();
            NavigateTo(shell, destination);

            SynchronizationContext.SetSynchronizationContext(previousContext);
            contextRestored = true;
            return new RenderedFixture(
                new Scanio.Presentation.MainWindow(shell),
                shell,
                recorder,
                repository,
                evidenceSessionId);
        }
        finally
        {
            if (!contextRestored)
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
    }

    internal static void ResetGlobalSources()
    {
        var settings = new MemorySettingsService(new AppSettings());
        var localizer = new UiLocalizer(settings);
        InitializeGlobalSources(settings, localizer);
    }

    private static void InitializeGlobalSources(MemorySettingsService settings, UiLocalizer localizer)
    {
        DisplaySettingsSource.Initialize(settings);
        LocalizationSource.Initialize(localizer);
    }

    private const string EvidencePayloadA =
        "010460123456789321SERIAL-000042\u001D91ABCD92LONG-CRYPTOGRAPHIC-TAIL";
    private const string EvidencePayloadB = "BATCH-ALTERNATE-000043";

    private static void NavigateTo(ShellViewModel shell, ShellDestination destination)
    {
        var command = destination switch
        {
            ShellDestination.Connection => shell.ShowConnectionCommand,
            ShellDestination.Monitor => shell.ShowMonitorCommand,
            ShellDestination.Notebook => shell.ShowNotebookCommand,
            ShellDestination.History => null,
            ShellDestination.Settings => shell.ShowSettingsCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, null)
        };
        command?.ExecuteAsync().GetAwaiter().GetResult();
    }

    private static AnalysisResult CreateEvidenceAnalysis() => AnalysisResult.Match(
        "HonestSign",
        "GS1 DataMatrix",
        AnalysisConfidence.Exact,
        "GTIN (01) and serial (21) are present; no online check was performed.",
        "GS1 DataMatrix data with a GTIN and serial number.",
        [
            new AnalysisField("01", "GTIN", "04601234567893"),
            new AnalysisField("21", "Serial number", "SERIAL-000042"),
            new AnalysisField("91", "Verification key", "ABCD"),
            new AnalysisField("92", "Cryptographic tail", "LONG-CRYPTOGRAPHIC-TAIL")
        ]);

    private static DecodedPayload CreateDecoded(CompletedScan scan, string payload) =>
        DecodedPayload.Create(
            scan.PayloadBytes.ToArray(),
            PayloadTextEncoding.Utf8,
            payload,
            payload.Replace("\u001D", "<GS>", StringComparison.Ordinal));

    private static CompletedScan CreateScan(
        TransportIdentity identity,
        long sequence,
        string payload)
    {
        var raw = Encoding.UTF8.GetBytes(payload + "\r");
        return CompletedScan.Create(
            sequence,
            raw,
            Encoding.UTF8.GetBytes(payload),
            [RawChunk.Create(sequence, raw, new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero), 10_000 + sequence, identity)],
            new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 9, 12, 34, 56, 18, TimeSpan.Zero),
            10_000 + sequence,
            10_018 + sequence,
            ScanCompletionReason.Terminator,
            ScanFramingSnapshot.Create([0x0D], TimeSpan.FromMilliseconds(100), 65_536),
            identity);
    }
}

internal sealed class KeyboardCaptureFixture(
    Window window,
    ConnectionViewModel connection,
    MonitorViewModel monitor,
    LiveMonitor sourceMonitor,
    ConnectionService connectionService) : IDisposable
{
    public Window Window { get; } = window;
    public ConnectionViewModel Connection { get; } = connection;
    public MonitorViewModel Monitor { get; } = monitor;
    public LiveMonitor SourceMonitor { get; } = sourceMonitor;
    public ConnectionService ConnectionService { get; } = connectionService;

    public void Dispose()
    {
        Window.Hide();
        CPlusFixtureFactory.ResetGlobalSources();
    }
}

internal sealed class RenderedFixture(
    Scanio.Presentation.MainWindow window,
    ShellViewModel shell,
    NotebookRecorder recorder,
    FixtureNotebookRepository repository,
    Guid evidenceSessionId) : IDisposable
{
    public Scanio.Presentation.MainWindow Window { get; } = window;
    public ConnectionViewModel Connection => shell.Connection;
    public NotebookViewModel Notebook => shell.Notebook;
    public HistoryViewModel History => shell.History;
    public IReadOnlyList<NotebookRecord> AuthoritativeRecords =>
        repository.GetRecords(evidenceSessionId);

    public void Dispose()
    {
        Window.Hide();
        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
        CPlusFixtureFactory.ResetGlobalSources();
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

internal sealed class FixtureConnectionService : IConnectionService
{
    public FixtureConnectionService(TransportIdentity? identity = null)
    {
        State = identity is null ? ConnectionState.Disconnected : ConnectionState.Connected;
        ActiveIdentity = identity;
        CurrentSnapshot = identity is null
            ? null
            : new ConnectionPresentationSnapshot(
                identity,
                ConnectionState.Connected,
                SerialConnectionOptions.Default(identity.Endpoint!));
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public ConnectionState State { get; private set; }
    public TransportIdentity? ActiveIdentity { get; private set; }
    public ConnectionPresentationSnapshot? CurrentSnapshot { get; private set; }
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
    private readonly List<NotebookSession> _sessions = [];
    private readonly List<NotebookRecord> _records = [];

    public FixtureNotebookRepository()
    {
        var first = NotebookSession.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Warehouse acceptance — Zebra DS3608", new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero))
            .WithSummary("Warehouse acceptance — Zebra DS3608", new DateTimeOffset(2026, 8, 8, 10, 42, 0, TimeSpan.Zero), 128);
        var second = NotebookSession.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Datalogic PowerScan long-run COM test", new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero))
            .WithSummary("Datalogic PowerScan long-run COM test", new DateTimeOffset(2026, 8, 7, 11, 37, 0, TimeSpan.Zero), 42);
        _sessions.AddRange([first, second]);
    }

    public void Initialize() { }

    public NotebookSession CreateSession(string name, DateTimeOffset startedAt)
    {
        var session = NotebookSession.Create(Guid.NewGuid(), name, startedAt);
        _sessions.Insert(0, session);
        return session;
    }

    public void Append(NotebookRecord record) => _records.Add(record);

    public void CompleteSession(Guid sessionId, DateTimeOffset endedAt)
    {
        var index = _sessions.FindIndex(session => session.Id == sessionId);
        var current = _sessions[index];
        _sessions[index] = current.WithSummary(
            current.Name,
            endedAt,
            _records.Count(record => record.SessionId == sessionId));
    }

    public IReadOnlyList<NotebookSession> GetSessions() => _sessions.ToArray();

    public IReadOnlyList<NotebookRecord> GetRecords(Guid sessionId) =>
        _records.Where(record => record.SessionId == sessionId).ToArray();

    public void RenameSession(Guid sessionId, string name) { }

    public void DeleteSession(Guid sessionId) => _sessions.RemoveAll(session => session.Id == sessionId);
}
