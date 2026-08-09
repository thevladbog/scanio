using System.Collections.ObjectModel;
using Scanio.Application.Notebook;
using Scanio.Presentation.Services;

namespace Scanio.Presentation.ViewModels;

public sealed record NotebookSessionItemViewModel(
    NotebookSession Session,
    string Name,
    string StartedAt,
    string Duration,
    int RecordCount)
{
    public static NotebookSessionItemViewModel From(NotebookSession session)
    {
        var duration = session.EndedAt is null
            ? "Не завершена"
            : FormatDuration(session.EndedAt.Value - session.StartedAt);
        return new NotebookSessionItemViewModel(
            session,
            session.Name,
            session.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            duration,
            session.RecordCount);
    }

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1 ? $"{(int)value.TotalHours} ч {value.Minutes} мин" : $"{Math.Max(0, value.Minutes)} мин";
}

public sealed class HistoryViewModel : ObservableObject
{
    private readonly INotebookRepository _repository;
    private readonly INotebookInteractionService _interaction;
    private readonly NotebookRecorder? _recorder;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private NotebookSessionItemViewModel? _selectedSession;
    private string _renameText = string.Empty;

    public HistoryViewModel(
        INotebookRepository repository,
        INotebookInteractionService interaction,
        NotebookRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(interaction);
        _repository = repository;
        _interaction = interaction;
        _recorder = recorder;
        RefreshCommand = new AsyncCommand(_ => ExecuteSafelyAsync(RefreshAsync));
        OpenCommand = new AsyncCommand(_ => ExecuteSafelyAsync(OpenAsync), () => SelectedSession is not null);
        RenameCommand = new AsyncCommand(_ => ExecuteSafelyAsync(RenameAsync), () =>
            CanMutateSelectedSession && !string.IsNullOrWhiteSpace(RenameText));
        DeleteCommand = new AsyncCommand(_ => ExecuteSafelyAsync(DeleteAsync), () => CanMutateSelectedSession);
        CopyAllCommand = RecordCommand(CopyAllAsync);
        CopyUniqueCommand = RecordCommand(CopyUniqueAsync);
        CopyEscapedCommand = RecordCommand(CopyEscapedAsync);
        ExportTextCommand = ExportCommand(NotebookExportFormat.Text);
        ExportCsvCommand = ExportCommand(NotebookExportFormat.Csv);
        ExportJsonCommand = ExportCommand(NotebookExportFormat.Json);
        if (_recorder is not null)
        {
            _recorder.Changed += (_, _) => RunOnUi(() =>
            {
                RenameCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            });
        }
    }

    public ObservableCollection<NotebookSessionItemViewModel> Sessions { get; } = [];
    public ObservableCollection<NotebookRecordItemViewModel> Records { get; } = [];

    public NotebookSessionItemViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                RenameText = value?.Name ?? string.Empty;
                OpenCommand.RaiseCanExecuteChanged();
                RenameCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RenameText
    {
        get => _renameText;
        set
        {
            if (SetProperty(ref _renameText, value))
            {
                RenameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand RenameCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand CopyAllCommand { get; }
    public AsyncCommand CopyUniqueCommand { get; }
    public AsyncCommand CopyEscapedCommand { get; }
    public AsyncCommand ExportTextCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }
    public int RecordCount => Records.Count;

    private bool CanMutateSelectedSession =>
        SelectedSession is not null && _recorder?.CurrentSession?.Id != SelectedSession.Session.Id;

    private async Task RefreshAsync()
    {
        _repository.Initialize();
        var sessions = await Task.Run(_repository.GetSessions);
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(NotebookSessionItemViewModel.From(session));
        }

        SelectedSession = Sessions.FirstOrDefault();
        Records.Clear();
        RaiseRecordCommands();
    }

    private async Task OpenAsync()
    {
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        var records = await Task.Run(() => _repository.GetRecords(selected.Session.Id));
        Records.Clear();
        foreach (var record in records)
        {
            Records.Add(new NotebookRecordItemViewModel(record));
        }

        RaiseRecordCommands();
    }

    private async Task RenameAsync()
    {
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        EnsureSessionIsNotActive(selected);
        await Task.Run(() => _repository.RenameSession(selected.Session.Id, RenameText));
        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        EnsureSessionIsNotActive(selected);
        if (!_interaction.ConfirmDelete(selected.Name))
        {
            return;
        }

        await Task.Run(() => _repository.DeleteSession(selected.Session.Id));
        await RefreshAsync();
    }

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
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        var path = _interaction.ChooseExportPath(format, selected.Name);
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

    private void RaiseRecordCommands()
    {
        OnPropertyChanged(nameof(RecordCount));
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

    private void EnsureSessionIsNotActive(NotebookSessionItemViewModel selected)
    {
        if (_recorder?.CurrentSession?.Id == selected.Session.Id)
        {
            throw new InvalidOperationException("Stop the active recording before renaming or deleting its session.");
        }
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
}
