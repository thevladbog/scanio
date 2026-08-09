using System.Collections.ObjectModel;
using System.IO;
using Scanio.Application.Notebook;
using Scanio.Presentation.Services;
using Scanio.Presentation.Localization;

namespace Scanio.Presentation.ViewModels;

public sealed class NotebookViewModel : ObservableObject
{
    private readonly NotebookRecorder _recorder;
    private readonly INotebookInteractionService _interaction;
    private readonly IUiLocalizer? _localizer;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private readonly List<NotebookRecord> _occurrences = [];
    private readonly DateTime _defaultSessionTimestamp = DateTime.Now;
    private string _sessionName;
    private bool _usesAutomaticSessionName = true;
    private static readonly TimeSpan ArrivalPulseDuration = TimeSpan.FromMilliseconds(600);

    public NotebookViewModel(
        NotebookRecorder recorder,
        INotebookInteractionService interaction,
        IUiLocalizer? localizer = null,
        Func<TimeSpan, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(interaction);
        _recorder = recorder;
        _interaction = interaction;
        _localizer = localizer;
        _delay = delay ?? (duration => Task.Delay(duration));
        _sessionName = BuildDefaultSessionName();
        StartCommand = new AsyncCommand(_ => ExecuteSafelyAsync(StartAsync), () => CanStart);
        PauseCommand = new AsyncCommand(_ => ExecuteSafelyAsync(PauseAsync), () => CanPause);
        ResumeCommand = new AsyncCommand(_ => ExecuteSafelyAsync(ResumeAsync), () => CanResume);
        StopCommand = new AsyncCommand(_ => ExecuteSafelyAsync(StopAsync), () => CanStop);
        CopyAllCommand = RecordCommand(() => CopyExactAsync(unique: false));
        CopyUniqueCommand = RecordCommand(() => CopyExactAsync(unique: true));
        CopyEscapedCommand = RecordCommand(CopyReadableAsync);
        ExportTextCommand = ExportCommand(NotebookExportFormat.Text);
        ExportReadableTextCommand = ExportCommand(NotebookExportFormat.ReadableText);
        ExportCsvCommand = ExportCommand(NotebookExportFormat.Csv);
        ExportJsonCommand = ExportCommand(NotebookExportFormat.Json);
        _recorder.Changed += OnRecorderChanged;
        _recorder.RecordPersisted += OnRecordPersisted;
        if (_localizer is not null)
        {
            _localizer.PropertyChanged += (_, _) => RunOnUi(() =>
            {
                if (_usesAutomaticSessionName)
                {
                    SetProperty(ref _sessionName, BuildDefaultSessionName(), nameof(SessionName));
                }

                Records.Clear();
                foreach (var item in NotebookRecordGrouping.Build(_occurrences, _localizer))
                {
                    Records.Add(item);
                }

                OnPropertyChanged(nameof(StateLabel));
                RaiseSummaryProperties();
                RaiseRecordCommands();
            });
        }
    }

    public ObservableCollection<NotebookRecordItemViewModel> Records { get; } = [];

    public string SessionName
    {
        get => _sessionName;
        set
        {
            if (SetProperty(ref _sessionName, value))
            {
                _usesAutomaticSessionName = false;
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StateLabel => _localizer is null
        ? _recorder.State switch
        {
            NotebookRecordingState.Recording => "Запись идёт",
            NotebookRecordingState.Paused => "Пауза",
            _ => "Запись выключена"
        }
        : _localizer[$"Notebook.State.{_recorder.State}"];

    public string? ErrorMessage => _recorder.LastError;
    public bool CanStart => _recorder.State == NotebookRecordingState.Off && !string.IsNullOrWhiteSpace(SessionName);
    public bool CanPause => _recorder.State == NotebookRecordingState.Recording;
    public bool CanResume => _recorder.State == NotebookRecordingState.Paused;
    public bool CanStop => _recorder.State != NotebookRecordingState.Off;
    public bool IsOff => _recorder.State == NotebookRecordingState.Off;
    public bool IsRecording => _recorder.State == NotebookRecordingState.Recording;
    public bool IsPaused => _recorder.State == NotebookRecordingState.Paused;
    public int TotalCount => _occurrences.Count;
    public int UniqueCount => NotebookExportService.CountUniquePayloads(
        _occurrences);
    public int DuplicateCount => Math.Max(0, TotalCount - UniqueCount);
    public string DeviceLabel => Records.LastOrDefault()?.Transport ?? "—";

    public AsyncCommand StartCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ResumeCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand CopyAllCommand { get; }
    public AsyncCommand CopyUniqueCommand { get; }
    public AsyncCommand CopyEscapedCommand { get; }
    public AsyncCommand ExportTextCommand { get; }
    public AsyncCommand ExportReadableTextCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }

    private Task StartAsync()
    {
        _recorder.Start(SessionName);
        _occurrences.Clear();
        Records.Clear();
        RaiseSummaryProperties();
        RaiseRecordCommands();
        return Task.CompletedTask;
    }

    private Task PauseAsync()
    {
        _recorder.Pause();
        return Task.CompletedTask;
    }

    private Task ResumeAsync()
    {
        _recorder.Resume();
        return Task.CompletedTask;
    }

    private async Task StopAsync() => await _recorder.StopAsync();

    private Task CopyExactAsync(bool unique)
    {
        _interaction.SetClipboardText(NotebookExportService.BuildExactClipboardText(
            _occurrences,
            unique));
        return Task.CompletedTask;
    }

    private Task CopyReadableAsync()
    {
        _interaction.SetClipboardText(NotebookExportService.BuildReadableClipboardText(
            _occurrences));
        return Task.CompletedTask;
    }

    private Task ExportAsync(NotebookExportFormat format)
    {
        var path = _interaction.ChooseExportPath(format, SanitizeFileName(SessionName));
        if (path is not null)
        {
            NotebookExportService.Export(path, format, _occurrences);
        }

        return Task.CompletedTask;
    }

    private async Task ExecuteSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            _interaction.ShowError(_localizer?["Error.OperationFailed"] ?? "Operation failed.");
        }
    }

    private void OnRecorderChanged(object? sender, EventArgs args) => RunOnUi(() =>
    {
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsOff));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsPaused));
        StartCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    });

    private void OnRecordPersisted(object? sender, NotebookRecordPersistedEventArgs args) => RunOnUi(() =>
    {
        _occurrences.Add(args.Record);
        foreach (var visibleItem in Records)
        {
            visibleItem.ClearArrivalPulse();
        }

        var key = NotebookPayloadIdentity.Create(args.Record.Scan.PayloadBytes.AsSpan());
        var previous = Records.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(
                NotebookPayloadIdentity.Create(item.Record.Scan.PayloadBytes.AsSpan()),
                key));
        var occurrenceCount = previous?.OccurrenceCount + 1 ?? 1;
        if (previous is not null)
        {
            Records.Remove(previous);
        }

        var item = new NotebookRecordItemViewModel(
            args.Record,
            _localizer,
            pulseArrival: true,
            occurrenceCount: occurrenceCount);
        Records.Add(item);
        Observe(ClearArrivalPulseAsync(item));
        RaiseSummaryProperties();
        RaiseRecordCommands();
    });

    private void RaiseRecordCommands()
    {
        CopyAllCommand.RaiseCanExecuteChanged();
        CopyUniqueCommand.RaiseCanExecuteChanged();
        CopyEscapedCommand.RaiseCanExecuteChanged();
        ExportTextCommand.RaiseCanExecuteChanged();
        ExportReadableTextCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
    }

    private AsyncCommand RecordCommand(Func<Task> action) =>
        new(_ => ExecuteSafelyAsync(action), () => Records.Count > 0);

    private AsyncCommand ExportCommand(NotebookExportFormat format) =>
        new(_ => ExecuteSafelyAsync(() => ExportAsync(format)), () => Records.Count > 0);

    private async Task ClearArrivalPulseAsync(NotebookRecordItemViewModel item)
    {
        try
        {
            await _delay(ArrivalPulseDuration);
        }
        catch (Exception)
        {
            // Arrival feedback must never surface as an operation failure.
        }
        finally
        {
            RunOnUi(item.ClearArrivalPulse);
        }
    }

    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RaiseSummaryProperties()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(UniqueCount));
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(DeviceLabel));
    }

    private void RunOnUi(Action action)
    {
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            action();
        }
        else
        {
            _synchronizationContext.Post(_ => action(), null);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Trim().Select(character => invalid.Contains(character) ? '_' : character));
    }

    private string BuildDefaultSessionName() =>
        $"{(_localizer?["Notebook.DefaultSession"] ?? "Сессия")} {_defaultSessionTimestamp:yyyy-MM-dd HH-mm}";
}
