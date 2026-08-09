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
    private NotebookSessionItemViewModel? _selectedSession;
    private string _renameText = string.Empty;
    private NotebookExportFormat _selectedExportFormat = NotebookExportFormat.Csv;

    public HistoryViewModel(INotebookRepository repository, INotebookInteractionService interaction)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(interaction);
        _repository = repository;
        _interaction = interaction;
        RefreshCommand = new AsyncCommand(_ => ExecuteSafelyAsync(RefreshAsync));
        OpenCommand = new AsyncCommand(_ => ExecuteSafelyAsync(OpenAsync), () => SelectedSession is not null);
        RenameCommand = new AsyncCommand(_ => ExecuteSafelyAsync(RenameAsync), () =>
            SelectedSession is not null && !string.IsNullOrWhiteSpace(RenameText));
        DeleteCommand = new AsyncCommand(_ => ExecuteSafelyAsync(DeleteAsync), () => SelectedSession is not null);
        CopyCommand = new AsyncCommand(_ => ExecuteSafelyAsync(CopyAsync), () => Records.Count > 0);
        ExportCommand = new AsyncCommand(_ => ExecuteSafelyAsync(ExportAsync), () => Records.Count > 0);
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

    public NotebookExportFormat SelectedExportFormat
    {
        get => _selectedExportFormat;
        set => SetProperty(ref _selectedExportFormat, value);
    }

    public IReadOnlyList<NotebookExportFormat> ExportFormats { get; } = Enum.GetValues<NotebookExportFormat>();

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand RenameCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand CopyCommand { get; }
    public AsyncCommand ExportCommand { get; }

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
        await Task.Run(() => _repository.RenameSession(selected.Session.Id, RenameText));
        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        if (!_interaction.ConfirmDelete(selected.Name))
        {
            return;
        }

        await Task.Run(() => _repository.DeleteSession(selected.Session.Id));
        await RefreshAsync();
    }

    private Task CopyAsync()
    {
        _interaction.SetClipboardText(NotebookExportService.BuildClipboardText(Records.Select(item => item.Record)));
        return Task.CompletedTask;
    }

    private Task ExportAsync()
    {
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        var path = _interaction.ChooseExportPath(SelectedExportFormat, selected.Name);
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

    private void RaiseRecordCommands()
    {
        CopyCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
    }
}
