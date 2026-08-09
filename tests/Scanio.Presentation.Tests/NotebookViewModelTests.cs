using Scanio.Application.Monitor;
using Scanio.Application.Notebook;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NotebookViewModelTests
{
    [TestMethod]
    public async Task Notebook_RecordsOccurrencesAndCopiesTheVisibleSession()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        var interaction = new FakeInteraction();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(recorder, interaction) { SessionName = "Shift" };

        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(CreateScan(1, "one"), CreateDecoded("one"), []);
        monitor.Append(CreateScan(2, "one"), CreateDecoded("one"), []);
        await viewModel.StopCommand.ExecuteAsync();
        await viewModel.CopyAllCommand.ExecuteAsync();

        Assert.AreEqual("Запись выключена", viewModel.StateLabel);
        Assert.HasCount(1, viewModel.Records);
        Assert.AreEqual(2, viewModel.Records.Single().OccurrenceCount);
        Assert.AreEqual("2 раза", viewModel.Records.Single().OccurrenceLabel);
        Assert.AreEqual("one" + Environment.NewLine + "one", interaction.ClipboardText);
        Assert.AreEqual(2, viewModel.TotalCount);
        Assert.AreEqual(1, viewModel.UniqueCount);
        Assert.AreEqual(1, viewModel.DuplicateCount);

        await viewModel.CopyUniqueCommand.ExecuteAsync();
        Assert.AreEqual("one", interaction.ClipboardText);

        await viewModel.CopyEscapedCommand.ExecuteAsync();
        Assert.AreEqual("one" + Environment.NewLine + "one", interaction.ClipboardText);

        await viewModel.ExportTextCommand.ExecuteAsync();
        await viewModel.ExportReadableTextCommand.ExecuteAsync();
        await viewModel.ExportCsvCommand.ExecuteAsync();
        await viewModel.ExportJsonCommand.ExecuteAsync();
        CollectionAssert.AreEqual(
            new[]
            {
                NotebookExportFormat.Text,
                NotebookExportFormat.ReadableText,
                NotebookExportFormat.Csv,
                NotebookExportFormat.Json
            },
            interaction.RequestedFormats.ToArray());
    }

    [TestMethod]
    public async Task Notebook_CopyCommandsPreserveExactControlsAndOfferReadableText()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        var interaction = new FakeInteraction();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(recorder, interaction) { SessionName = "Shift" };

        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(
            CreateScan(1, "01\u001D21"),
            CreateDecoded("01\u001D21", "01<GS>21"),
            []);
        await viewModel.StopCommand.ExecuteAsync();

        await viewModel.CopyAllCommand.ExecuteAsync();
        Assert.AreEqual($"01\u001D21", interaction.ClipboardText);

        await viewModel.CopyEscapedCommand.ExecuteAsync();
        Assert.AreEqual("01<GS>21", interaction.ClipboardText);

        await viewModel.ExportReadableTextCommand.ExecuteAsync();
        CollectionAssert.Contains(
            interaction.RequestedFormats,
            NotebookExportFormat.ReadableText);
    }

    [TestMethod]
    public async Task EnglishLanguage_ReplacesUntouchedDefaultSessionInTheExportFileName()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        var interaction = new FakeInteraction();
        var settings = new TestSettingsService();
        var localizer = new UiLocalizer(settings);
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(recorder, interaction, localizer);
        Assert.StartsWith("Сессия ", viewModel.SessionName);

        localizer.SetLanguage(UiLanguage.English);

        Assert.StartsWith("Session ", viewModel.SessionName);
        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(CreateScan(1, "one"), CreateDecoded("one"), []);
        await viewModel.StopCommand.ExecuteAsync();
        await viewModel.ExportTextCommand.ExecuteAsync();
        Assert.StartsWith("Session ", interaction.SuggestedNames.Single());
    }

    [TestMethod]
    public async Task LanguageChange_DoesNotReplaceACustomSessionName()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        var interaction = new FakeInteraction();
        var settings = new TestSettingsService();
        var localizer = new UiLocalizer(settings);
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(recorder, interaction, localizer) { SessionName = "Line A" };

        localizer.SetLanguage(UiLanguage.English);

        Assert.AreEqual("Line A", viewModel.SessionName);
    }

    [TestMethod]
    public async Task Notebook_SummaryAndUniqueCopyUseByteExactPayloadIdentity()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        var interaction = new FakeInteraction();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(recorder, interaction) { SessionName = "Shift" };

        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(CreateScan(1, [0x31]), CreateDecoded("same"), []);
        monitor.Append(CreateScan(2, [0x32]), CreateDecoded("same"), []);
        await viewModel.StopCommand.ExecuteAsync();
        await viewModel.CopyUniqueCommand.ExecuteAsync();

        Assert.HasCount(2, viewModel.Records);
        Assert.IsTrue(viewModel.Records.All(item => item.OccurrenceCount == 1));
        Assert.IsTrue(viewModel.Records.All(item => item.OccurrenceLabel == string.Empty));
        Assert.AreEqual(2, viewModel.UniqueCount);
        Assert.AreEqual(0, viewModel.DuplicateCount);
        Assert.AreEqual($"same{Environment.NewLine}same", interaction.ClipboardText);
    }

    [TestMethod]
    public async Task Notebook_RepeatedGroupMovesToLatestPositionAndOwnsTheArrivalPulse()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        var interaction = new FakeInteraction();
        var releasePulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedPulseDurations = new List<TimeSpan>();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(
            recorder,
            interaction,
            localizer: null,
            delay: duration =>
            {
                requestedPulseDurations.Add(duration);
                return releasePulse.Task;
            })
        {
            SessionName = "Shift"
        };

        Assert.IsFalse(viewModel.ExportReadableTextCommand.CanExecute(null));
        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(CreateScan(1, "A"), CreateDecoded("A"), []);
        monitor.Append(CreateScan(2, "B"), CreateDecoded("B"), []);
        monitor.Append(CreateScan(3, "A"), CreateDecoded("A"), []);
        await viewModel.StopCommand.ExecuteAsync();

        Assert.HasCount(2, viewModel.Records);
        Assert.AreEqual("B", viewModel.Records[0].Payload);
        Assert.AreEqual(2, viewModel.Records[0].Sequence);
        Assert.IsFalse(viewModel.Records[0].IsArrivalPulseActive);
        Assert.IsFalse(viewModel.Records[0].IsDuplicate);
        Assert.AreEqual("A", viewModel.Records[1].Payload);
        Assert.AreEqual(3, viewModel.Records[1].Sequence);
        Assert.AreEqual(2, viewModel.Records[1].OccurrenceCount);
        Assert.AreEqual("2 раза", viewModel.Records[1].OccurrenceLabel);
        Assert.IsTrue(viewModel.Records[1].IsArrivalPulseActive);
        Assert.IsTrue(viewModel.Records[1].IsDuplicate);
        Assert.IsTrue(viewModel.ExportReadableTextCommand.CanExecute(null));
        CollectionAssert.AreEqual(
            new[]
            {
                TimeSpan.FromMilliseconds(600),
                TimeSpan.FromMilliseconds(600),
                TimeSpan.FromMilliseconds(600)
            },
            requestedPulseDurations);

        var clearedProperties = new List<string?>();
        foreach (var item in viewModel.Records)
        {
            item.PropertyChanged += (_, args) => clearedProperties.Add(args.PropertyName);
        }

        releasePulse.SetResult();
        await WaitUntilAsync(() => viewModel.Records.All(item => !item.IsArrivalPulseActive));

        Assert.AreEqual(1, clearedProperties.Count(name => name == nameof(NotebookRecordItemViewModel.IsArrivalPulseActive)));

        var historical = new NotebookRecordItemViewModel(
            CreateRecord(Guid.NewGuid(), 3, "historical"));
        Assert.IsFalse(historical.IsArrivalPulseActive);
    }

    [TestMethod]
    public async Task Notebook_FailedPulseDelayClearsWithoutShowingAnOperationError()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        var interaction = new FakeInteraction();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(
            recorder,
            interaction,
            localizer: null,
            delay: _ => Task.FromException(new InvalidOperationException("Pulse failed")))
        {
            SessionName = "Shift"
        };

        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(CreateScan(1, "one"), CreateDecoded("one"), []);
        await viewModel.StopCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.Records.Count == 1 && !viewModel.Records[0].IsArrivalPulseActive);

        Assert.IsEmpty(interaction.Errors);
    }

    [TestMethod]
    public async Task History_RefreshOpenRenameAndConfirmedDeleteUseRepository()
    {
        var repository = new FakeRepository();
        var session = repository.CreateSession("Original", DateTimeOffset.UnixEpoch);
        repository.Append(CreateRecord(session.Id, 1, "one"));
        var interaction = new FakeInteraction { ConfirmDeleteResult = true };
        var viewModel = new HistoryViewModel(repository, interaction);

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.OpenCommand.ExecuteAsync();
        Assert.HasCount(1, viewModel.Records);
        await viewModel.CopyAllCommand.ExecuteAsync();
        await viewModel.CopyUniqueCommand.ExecuteAsync();
        await viewModel.CopyEscapedCommand.ExecuteAsync();
        await viewModel.ExportTextCommand.ExecuteAsync();
        await viewModel.ExportReadableTextCommand.ExecuteAsync();
        await viewModel.ExportCsvCommand.ExecuteAsync();
        await viewModel.ExportJsonCommand.ExecuteAsync();
        CollectionAssert.AreEqual(
            new[]
            {
                NotebookExportFormat.Text,
                NotebookExportFormat.ReadableText,
                NotebookExportFormat.Csv,
                NotebookExportFormat.Json
            },
            interaction.RequestedFormats.ToArray());
        Assert.IsFalse(viewModel.Records.Single().IsArrivalPulseActive);
        viewModel.RenameText = "Renamed";
        await viewModel.RenameCommand.ExecuteAsync();
        Assert.AreEqual("Renamed", viewModel.Sessions.Single().Name);
        await viewModel.DeleteCommand.ExecuteAsync();
        Assert.IsEmpty(viewModel.Sessions);
    }

    [TestMethod]
    public async Task History_GroupsLatestPayloadRowsButCopiesEveryOccurrence()
    {
        var repository = new FakeRepository();
        var session = repository.CreateSession("Grouped", DateTimeOffset.UnixEpoch);
        repository.Append(CreateRecord(session.Id, 1, "A"));
        repository.Append(CreateRecord(session.Id, 2, "B"));
        repository.Append(CreateRecord(session.Id, 3, "A"));
        var interaction = new FakeInteraction();
        var viewModel = new HistoryViewModel(repository, interaction);

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.OpenCommand.ExecuteAsync();
        await viewModel.CopyAllCommand.ExecuteAsync();

        Assert.HasCount(2, viewModel.Records);
        CollectionAssert.AreEqual(new[] { "B", "A" }, viewModel.Records.Select(item => item.Payload).ToArray());
        Assert.AreEqual(3, viewModel.Records[1].Sequence);
        Assert.AreEqual(2, viewModel.Records[1].OccurrenceCount);
        Assert.AreEqual($"A{Environment.NewLine}B{Environment.NewLine}A", interaction.ClipboardText);
    }

    [TestMethod]
    public async Task KeyboardRecord_UsesRussianPresentationLabelInNotebook()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(
            recorder,
            new FakeInteraction(),
            RussianLocalizer())
        {
            SessionName = "Keyboard"
        };

        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(
            CreateScan(1, "A", KeyboardIdentity),
            CreateDecoded("A"),
            []);
        await viewModel.StopCommand.ExecuteAsync();

        Assert.AreEqual("Сканер-клавиатура", viewModel.Records.Single().Transport);
        Assert.AreEqual("Сканер-клавиатура", viewModel.DeviceLabel);
        Assert.AreEqual("Keyboard scanner", viewModel.Records.Single().Record.Scan.Transport.DisplayName);
    }

    [TestMethod]
    public async Task DeviceLabel_FollowsTheLatestGroupedOccurrenceAcrossTransports()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        var viewModel = new NotebookViewModel(
            recorder,
            new FakeInteraction(),
            RussianLocalizer())
        {
            SessionName = "Mixed transports"
        };

        await viewModel.StartCommand.ExecuteAsync();
        monitor.Append(CreateScan(1, "COM"), CreateDecoded("COM"), []);
        monitor.Append(CreateScan(2, "Keyboard", KeyboardIdentity), CreateDecoded("Keyboard"), []);
        await viewModel.StopCommand.ExecuteAsync();

        Assert.AreEqual("Сканер-клавиатура", viewModel.DeviceLabel);
    }

    [TestMethod]
    public async Task KeyboardRecord_UsesRussianPresentationLabelInHistory()
    {
        var repository = new FakeRepository();
        var session = repository.CreateSession("Keyboard", DateTimeOffset.UnixEpoch);
        repository.Append(CreateRecord(session.Id, 1, "A", KeyboardIdentity));
        var viewModel = new HistoryViewModel(
            repository,
            new FakeInteraction(),
            localizer: RussianLocalizer());

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.OpenCommand.ExecuteAsync();

        Assert.AreEqual("Сканер-клавиатура", viewModel.Records.Single().Transport);
        Assert.AreEqual("Keyboard scanner", viewModel.Records.Single().Record.Scan.Transport.DisplayName);
    }

    [TestMethod]
    [DataRow(1, Scanio.Presentation.Settings.UiLanguage.Russian, "")]
    [DataRow(2, Scanio.Presentation.Settings.UiLanguage.Russian, "2 раза")]
    [DataRow(3, Scanio.Presentation.Settings.UiLanguage.Russian, "3 раза")]
    [DataRow(5, Scanio.Presentation.Settings.UiLanguage.Russian, "5 раз")]
    [DataRow(11, Scanio.Presentation.Settings.UiLanguage.Russian, "11 раз")]
    [DataRow(21, Scanio.Presentation.Settings.UiLanguage.Russian, "21 раз")]
    [DataRow(22, Scanio.Presentation.Settings.UiLanguage.Russian, "22 раза")]
    [DataRow(2, Scanio.Presentation.Settings.UiLanguage.English, "2 scans")]
    public void Occurrence_FormatsLocalizedCountLabel(
        int count,
        Scanio.Presentation.Settings.UiLanguage language,
        string expected)
    {
        Assert.AreEqual(expected, NotebookRecordItemViewModel.FormatOccurrenceCount(count, language));
    }

    [TestMethod]
    public async Task History_CannotRenameOrDeleteTheActiveRecordingSession()
    {
        var monitor = new LiveMonitor();
        var repository = new FakeRepository();
        await using var recorder = new NotebookRecorder(repository, monitor, () => DateTimeOffset.UnixEpoch);
        recorder.Start("Active");
        var viewModel = new HistoryViewModel(repository, new FakeInteraction { ConfirmDeleteResult = true }, recorder);

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.IsFalse(viewModel.RenameCommand.CanExecute(null));
        Assert.IsFalse(viewModel.DeleteCommand.CanExecute(null));
        await recorder.StopAsync();
        Assert.IsTrue(viewModel.RenameCommand.CanExecute(null));
        Assert.IsTrue(viewModel.DeleteCommand.CanExecute(null));
    }

    private static readonly TransportIdentity KeyboardIdentity = new(
        TransportKind.KeyboardCapture,
        "keyboard-capture:focused-window",
        "Keyboard scanner",
        endpoint: "Keyboard");

    private static CompletedScan CreateScan(
        long sequence,
        string value,
        TransportIdentity? identity = null)
        => CreateScan(sequence, System.Text.Encoding.UTF8.GetBytes(value), identity);

    private static CompletedScan CreateScan(
        long sequence,
        byte[] payload,
        TransportIdentity? identity = null)
    {
        return CompletedScan.Create(
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
            identity ?? new TransportIdentity(TransportKind.Serial, "COM7", "COM7"));
    }

    private static DecodedPayload CreateDecoded(string value, string? escapedDisplay = null) =>
        DecodedPayload.Create(
            System.Text.Encoding.UTF8.GetBytes(value),
            PayloadTextEncoding.Utf8,
            value,
            escapedDisplay ?? value);

    private static NotebookRecord CreateRecord(
        Guid sessionId,
        long sequence,
        string value,
        TransportIdentity? identity = null) =>
        NotebookRecord.Create(
            sequence,
            sessionId,
            CreateScan(sequence, value, identity),
            CreateDecoded(value),
            [],
            1,
            DateTimeOffset.UnixEpoch);

    private static IUiLocalizer RussianLocalizer() =>
        new UiLocalizer(new TestSettingsService());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeInteraction : INotebookInteractionService
    {
        public string? ClipboardText { get; private set; }
        public bool ConfirmDeleteResult { get; init; }
        public List<string> Errors { get; } = [];
        public List<NotebookExportFormat> RequestedFormats { get; } = [];
        public List<string> SuggestedNames { get; } = [];
        public void SetClipboardText(string text) => ClipboardText = text;
        public string? ChooseExportPath(NotebookExportFormat format, string suggestedName)
        {
            RequestedFormats.Add(format);
            SuggestedNames.Add(suggestedName);
            return null;
        }
        public bool ConfirmDelete(string sessionName) => ConfirmDeleteResult;
        public void ShowError(string message) => Errors.Add(message);
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

    private sealed class FakeRepository : INotebookRepository
    {
        private readonly List<NotebookSession> _sessions = [];
        private readonly List<NotebookRecord> _records = [];

        public void Initialize()
        {
        }

        public NotebookSession CreateSession(string name, DateTimeOffset startedAt)
        {
            var session = NotebookSession.Create(Guid.NewGuid(), name, startedAt);
            _sessions.Add(session);
            return session;
        }

        public void Append(NotebookRecord record) => _records.Add(record);

        public void CompleteSession(Guid sessionId, DateTimeOffset endedAt)
        {
            var index = _sessions.FindIndex(item => item.Id == sessionId);
            var current = _sessions[index];
            _sessions[index] = current.WithSummary(
                current.Name,
                endedAt,
                _records.Count(item => item.SessionId == sessionId));
        }

        public IReadOnlyList<NotebookSession> GetSessions() => _sessions.ToArray();
        public IReadOnlyList<NotebookRecord> GetRecords(Guid sessionId) =>
            _records.Where(item => item.SessionId == sessionId).ToArray();

        public void RenameSession(Guid sessionId, string name)
        {
            var index = _sessions.FindIndex(item => item.Id == sessionId);
            var current = _sessions[index];
            _sessions[index] = current.WithSummary(name, current.EndedAt, current.RecordCount);
        }

        public void DeleteSession(Guid sessionId)
        {
            _sessions.RemoveAll(item => item.Id == sessionId);
            _records.RemoveAll(item => item.SessionId == sessionId);
        }
    }
}
