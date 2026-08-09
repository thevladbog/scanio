using Scanio.Application.Monitor;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class MonitorViewModelTests
{
    [TestMethod]
    public void DisconnectCommand_IsEnabledOnlyWhileAConnectionIsActive()
    {
        var connection = new FakeConnectionService(ConnectionState.Detected, activeIdentity: null);
        var viewModel = CreateViewModel(new LiveMonitor(), connection);

        Assert.IsFalse(viewModel.DisconnectCommand.CanExecute(null));

        connection.SetState(ConnectionState.Connected, Identity);
        Assert.IsTrue(viewModel.DisconnectCommand.CanExecute(null));

        connection.SetState(ConnectionState.Disconnected, activeIdentity: null);
        Assert.IsFalse(viewModel.DisconnectCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task OlderSelectionRemainsFixedUntilReturnToLatest()
    {
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "first"), Decoded("first"), []);
        monitor.Append(Scan(2, "second"), Decoded("second"), []);
        var viewModel = CreateViewModel(monitor, new FakeConnectionService());

        viewModel.SelectedEvent = viewModel.Events[0];
        monitor.Append(Scan(3, "third"), Decoded("third"), []);

        Assert.AreEqual("first", viewModel.SelectedEvent?.Payload);
        Assert.IsTrue(viewModel.ShowReturnToLatest);

        await viewModel.ReturnToLatestCommand.ExecuteAsync();

        Assert.AreEqual("third", viewModel.SelectedEvent?.Payload);
        Assert.IsFalse(viewModel.ShowReturnToLatest);
    }

    [TestMethod]
    public void Activate_ReturnsSelectionToTheLatestRetainedScan()
    {
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "first"), Decoded("first"), []);
        monitor.Append(Scan(2, "second"), Decoded("second"), []);
        var viewModel = CreateViewModel(monitor, new FakeConnectionService());
        viewModel.SelectedEvent = viewModel.Events[0];
        monitor.Append(Scan(3, "third"), Decoded("third"), []);

        viewModel.Activate();

        Assert.AreEqual("third", viewModel.SelectedEvent?.Payload);
        Assert.IsFalse(viewModel.ShowReturnToLatest);
    }

    [TestMethod]
    public void SelectedEventExposesPersistentRawAndHexEvidence()
    {
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "A\r"), Decoded("A"), []);
        var viewModel = CreateViewModel(monitor, new FakeConnectionService());

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

        var viewModel = CreateViewModel(monitor, new FakeConnectionService());

        Assert.HasCount(1, viewModel.SelectedEvent!.Analyses);
        var displayed = viewModel.SelectedEvent.Analyses.Single();
        Assert.AreEqual("Предположение по структуре", displayed.Confidence);
        Assert.AreEqual("04601234567893", displayed.Fields.Single().Value);
        CollectionAssert.Contains(displayed.Errors.ToArray(), "Ошибка проверки: Fixture error.");
        CollectionAssert.Contains(displayed.Warnings.ToArray(), "Предупреждение проверки: Fixture warning.");
    }

    [TestMethod]
    public void RussianHonestSignWarnings_ExplainTheActualValidationConcern()
    {
        var analysis = AnalysisResult.Match(
            "HonestSign",
            "Честный знак",
            AnalysisConfidence.Inferred,
            "Serialized GS1 structure.",
            "Marking payload.",
            validationWarnings:
            [
                "Variable-length AI 21 reaches the end of the payload; a missing GS separator may make following fields ambiguous.",
                "Verification key AI 91 is not present.",
                "Crypto tail AI 92 is not present.",
                "Product-group candidates use bundled structural rules only; official online validity was not checked."
            ]);
        var localizer = new UiLocalizer(new TestSettingsService());

        var displayed = new AnalysisItemViewModel(analysis, localizer);

        CollectionAssert.AreEqual(
            new[]
            {
                "Поле переменной длины AI 21 дошло до конца данных. Возможно, пропущен разделитель GS, поэтому следующие поля могут быть разобраны неоднозначно.",
                "В данных нет ключа проверки с идентификатором применения AI 91.",
                "В данных нет криптографического хвоста с идентификатором применения AI 92.",
                "Товарная группа определена только по встроенным структурным правилам; официальная онлайн-проверка не выполнялась."
            },
            displayed.Warnings.ToArray());
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

        var viewModel = CreateViewModel(monitor, new FakeConnectionService());

        var selected = viewModel.SelectedEvent!;
        CollectionAssert.AreEqual(
            new[] { "Честный знак", "Строка элементов GS1" },
            selected.Analyses.Select(item => item.Format).ToArray());
        Assert.AreEqual("Честный знак", selected.Format);
    }

    [TestMethod]
    public void ChangingLanguage_RebuildsTheCompleteAnalyzerPresentationImmediately()
    {
        var analysis = AnalysisResult.Match(
            "GS1",
            "GS1 element string",
            AnalysisConfidence.Exact,
            "Application identifier structure.",
            "GS1 payload.",
            [new AnalysisField("10", "Batch or lot", "LOT-42")]);
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "value"), Decoded("value"), [analysis]);
        var settings = new TestSettingsService();
        var localizer = new UiLocalizer(settings);
        var viewModel = new MonitorViewModel(
            monitor,
            new FakeConnectionService(),
            new FakeClipboardService(),
            localizer);

        Assert.AreEqual("Строка элементов GS1", viewModel.SelectedEvent!.Format);
        Assert.AreEqual("Партия", viewModel.SelectedEvent.Analyses.Single().Fields.Single().Name);

        localizer.SetLanguage(UiLanguage.English);

        Assert.AreEqual("GS1 element string", viewModel.SelectedEvent!.Format);
        Assert.AreEqual("Batch or lot", viewModel.SelectedEvent.Analyses.Single().Fields.Single().Name);
        Assert.AreEqual("Application identifier structure.", viewModel.SelectedEvent.Analyses.Single().Evidence);
    }

    [TestMethod]
    public void KnownAnalyzerFormat_DoesNotLeakItsRussianDomainLabelIntoEnglishUi()
    {
        var analysis = AnalysisResult.Match(
            "HonestSign",
            "Честный знак",
            AnalysisConfidence.Exact,
            "Serialized marking structure.",
            "Marking code.");
        var monitor = new LiveMonitor();
        monitor.Append(Scan(1, "value"), Decoded("value"), [analysis]);
        var settings = new TestSettingsService();
        var localizer = new UiLocalizer(settings);
        localizer.SetLanguage(UiLanguage.English);
        var viewModel = new MonitorViewModel(
            monitor,
            new FakeConnectionService(),
            new FakeClipboardService(),
            localizer);

        Assert.AreEqual("Honest Sign", viewModel.SelectedEvent!.Format);
        Assert.AreEqual("Honest Sign", viewModel.SelectedEvent.Analyses.Single().Format);
    }

    [TestMethod]
    public async Task CopyActions_PreservePayloadSeparatorsAndExposeRawHexAndJson()
    {
        var monitor = new LiveMonitor();
        var payload = "A\u001DB";
        monitor.Append(Scan(1, payload + "\r"), Decoded(payload), []);
        var clipboard = new FakeClipboardService();
        var viewModel = CreateViewModel(monitor, new FakeConnectionService(), clipboard);

        await viewModel.CopyCodeCommand.ExecuteAsync();
        Assert.AreEqual(payload, clipboard.Text);

        await viewModel.CopyRawCommand.ExecuteAsync();
        Assert.AreEqual("A<GS>B<CR>", clipboard.Text);

        await viewModel.CopyHexCommand.ExecuteAsync();
        Assert.AreEqual("41 1D 42 0D", clipboard.Text);

        await viewModel.CopyDiagnosticJsonCommand.ExecuteAsync();
        StringAssert.Contains(clipboard.Text!, "\"payload\": \"A\\u001DB\"");
        StringAssert.Contains(clipboard.Text!, "\"completionReason\": \"Terminator\"");
        StringAssert.Contains(clipboard.Text!, "\"transport\"");
        Assert.AreEqual("Скопировано", viewModel.CopyFeedback);
    }

    [TestMethod]
    public void CopyActions_AreDisabledWithoutASelectedScan()
    {
        var viewModel = CreateViewModel(new LiveMonitor(), new FakeConnectionService());

        Assert.IsFalse(viewModel.CopyCodeCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CopyRawCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CopyHexCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CopyDiagnosticJsonCommand.CanExecute(null));
    }

    [TestMethod]
    public void HeaderConnectionLabel_IsCompactButSnapshotRetainsFullName()
    {
        var identity = new TransportIdentity(
            TransportKind.Serial,
            "serial:test",
            "Datalogic Barcode Scanner (COM18) with a deliberately long name",
            endpoint: "COM18");
        var connection = new FakeConnectionService(ConnectionState.Connected, identity)
        {
            Snapshot = new ConnectionPresentationSnapshot(
                identity,
                ConnectionState.Connected,
                Scanio.Transports.Serial.SerialConnectionOptions.Default("COM18"))
        };

        var viewModel = CreateViewModel(new LiveMonitor(), connection);

        Assert.AreEqual("COM18 · Подключено", viewModel.ConnectionLabel);
        Assert.AreEqual(identity.DisplayName, viewModel.ConnectionFriendlyName);
    }

    private static MonitorViewModel CreateViewModel(
        LiveMonitor monitor,
        IConnectionService connection,
        IClipboardService? clipboard = null)
    {
        var settings = new TestSettingsService();
        return new MonitorViewModel(
            monitor,
            connection,
            clipboard ?? new FakeClipboardService(),
            new UiLocalizer(settings));
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

    private sealed class FakeConnectionService(
        ConnectionState state = ConnectionState.Connected,
        TransportIdentity? activeIdentity = null) : IConnectionService
    {
        private ConnectionState _state = state;
        private TransportIdentity? _activeIdentity = activeIdentity ?? (state == ConnectionState.Connected ? Identity : null);

        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
        public ConnectionState State => _state;
        public TransportIdentity? ActiveIdentity => _activeIdentity;
        public ConnectionPresentationSnapshot? Snapshot { get; init; }
        public ConnectionPresentationSnapshot? CurrentSnapshot => Snapshot;
        public Scanio.Transports.Keyboard.IKeyboardCaptureInput? KeyboardInput => null;
        public Task ConnectAsync(Scanio.Platform.Windows.Devices.SerialDeviceInfo device, Scanio.Transports.Serial.SerialConnectionOptions options, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ConnectKeyboardAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void SetState(ConnectionState state, TransportIdentity? activeIdentity)
        {
            _state = state;
            _activeIdentity = activeIdentity;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, activeIdentity));
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
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
