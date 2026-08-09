using System.Collections.ObjectModel;
using System.IO;
using Scanio.Application.Notebook;
using Scanio.Presentation.Services;

namespace Scanio.Presentation.ViewModels;

public sealed class NotebookViewModel : ObservableObject
{
    private readonly NotebookRecorder _recorder;
    private readonly INotebookInteractionService _interaction;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private string _sessionName = $"Сессия {DateTime.Now:yyyy-MM-dd HH-mm}";

    public NotebookViewModel(NotebookRecorder recorder, INotebookInteractionService interaction)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(interaction);
        _recorder = recorder;
        _interaction = interaction;
        StartCommand = new AsyncCommand(_ => ExecuteSafelyAsync(StartAsync), () => CanStart);
        PauseCommand = new AsyncCommand(_ => ExecuteSafelyAsync(PauseAsync), () => CanPause);
        ResumeCommand = new AsyncCommand(_ => ExecuteSafelyAsync(ResumeAsync), () => CanResume);
        StopCommand = new AsyncCommand(_ => ExecuteSafelyAsync(StopAsync), () => CanStop);
        CopyAllCommand = RecordCommand(CopyAllAsync);
        CopyUniqueCommand = RecordCommand(CopyUniqueAsync);
        CopyEscapedCommand = RecordCommand(CopyEscapedAsync);
        ExportTextCommand = ExportCommand(NotebookExportFormat.Text);
        ExportCsvCommand = ExportCommand(NotebookExportFormat.Csv);
        ExportJsonCommand = ExportCommand(NotebookExportFormat.Json);
        _recorder.Changed += OnRecorderChanged;
        _recorder.RecordPersisted += OnRecordPersisted;
    }

    public ObservableCollection<NotebookRecordItemViewModel> Records { get; } = [];

    public string SessionName
    {
        get => _sessionName;
        set
        {
            if (SetProperty(ref _sessionName, value))
            {
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StateLabel => _recorder.State switch
    {
        NotebookRecordingState.Recording => "Запись идёт",
        NotebookRecordingState.Paused => "Пауза",
        _ => "Запись выключена"
    };

    public string? ErrorMessage => _recorder.LastError;
    public bool CanStart => _recorder.State == NotebookRecordingState.Off && !string.IsNullOrWhiteSpace(SessionName);
    public bool CanPause => _recorder.State == NotebookRecordingState.Recording;
    public bool CanResume => _recorder.State == NotebookRecordingState.Paused;
    public bool CanStop => _recorder.State != NotebookRecordingState.Off;
    public bool IsOff => _recorder.State == NotebookRecordingState.Off;
    public bool IsRecording => _recorder.State == NotebookRecordingState.Recording;
    public bool IsPaused => _recorder.State == NotebookRecordingState.Paused;
    public int TotalCount => Records.Count;
    public int UniqueCount => Records.Select(item => item.Record.Decoded.Text).Distinct(StringComparer.Ordinal).Count();
    public int DuplicateCount => Math.Max(0, TotalCount - UniqueCount);
    public string DeviceLabel => Records.FirstOrDefault()?.Record.Scan.Transport.DisplayName ?? "—";

    public AsyncCommand StartCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ResumeCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand CopyAllCommand { get; }
    public AsyncCommand CopyUniqueCommand { get; }
    public AsyncCommand CopyEscapedCommand { get; }
    public AsyncCommand ExportTextCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }

    private Task StartAsync()
    {
        _recorder.Start(SessionName);
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

    private Task CopyAllAsync()
    {
        _interaction.SetClipboardText(string.Join(
            Environment.NewLine,
            Records.Select(item => item.Record.Decoded.Text)));
        return Task.CompletedTask;
    }

    private Task CopyUniqueAsync()
    {
        _interaction.SetClipboardText(string.Join(
            Environment.NewLine,
            Records.Select(item => item.Record.Decoded.Text).Distinct(StringComparer.Ordinal)));
        return Task.CompletedTask;
    }

    private Task CopyEscapedAsync()
    {
        _interaction.SetClipboardText(NotebookExportService.BuildClipboardText(Records.Select(item => item.Record)));
        return Task.CompletedTask;
    }

    private Task ExportAsync(NotebookExportFormat format)
    {
        var path = _interaction.ChooseExportPath(format, SanitizeFileName(SessionName));
        if (path is not null)
        {
            NotebookExportService.Export(path, format, Records.Select(item => item.Record));
        }

        return Task.CompletedTask;
    }

    private async Task ExecuteSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _interaction.ShowError(exception.Message);
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
        Records.Add(new NotebookRecordItemViewModel(args.Record));
        RaiseSummaryProperties();
        RaiseRecordCommands();
    });

    private void RaiseRecordCommands()
    {
        CopyAllCommand.RaiseCanExecuteChanged();
        CopyUniqueCommand.RaiseCanExecuteChanged();
        CopyEscapedCommand.RaiseCanExecuteChanged();
        ExportTextCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
    }

    private AsyncCommand RecordCommand(Func<Task> action) =>
        new(_ => ExecuteSafelyAsync(action), () => Records.Count > 0);

    private AsyncCommand ExportCommand(NotebookExportFormat format) =>
        new(_ => ExecuteSafelyAsync(() => ExportAsync(format)), () => Records.Count > 0);

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
}
