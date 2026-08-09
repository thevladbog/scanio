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
    private NotebookExportFormat _selectedExportFormat = NotebookExportFormat.Text;

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
        CopyCommand = new AsyncCommand(_ => ExecuteSafelyAsync(CopyAsync), () => Records.Count > 0);
        ExportCommand = new AsyncCommand(_ => ExecuteSafelyAsync(ExportAsync), () => Records.Count > 0);
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

    public NotebookExportFormat SelectedExportFormat
    {
        get => _selectedExportFormat;
        set => SetProperty(ref _selectedExportFormat, value);
    }

    public IReadOnlyList<NotebookExportFormat> ExportFormats { get; } = Enum.GetValues<NotebookExportFormat>();

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

    public AsyncCommand StartCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ResumeCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand CopyCommand { get; }
    public AsyncCommand ExportCommand { get; }

    private Task StartAsync()
    {
        _recorder.Start(SessionName);
        Records.Clear();
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

    private Task CopyAsync()
    {
        _interaction.SetClipboardText(NotebookExportService.BuildClipboardText(Records.Select(item => item.Record)));
        return Task.CompletedTask;
    }

    private Task ExportAsync()
    {
        var path = _interaction.ChooseExportPath(SelectedExportFormat, SanitizeFileName(SessionName));
        if (path is not null)
        {
            NotebookExportService.Export(path, SelectedExportFormat, Records.Select(item => item.Record));
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
        StartCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    });

    private void OnRecordPersisted(object? sender, NotebookRecordPersistedEventArgs args) => RunOnUi(() =>
    {
        Records.Add(new NotebookRecordItemViewModel(args.Record));
        RaiseRecordCommands();
    });

    private void RaiseRecordCommands()
    {
        CopyCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
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
