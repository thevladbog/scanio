using System.Collections.ObjectModel;
using Scanio.Application.Notebook;
using Scanio.Presentation.Services;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.ViewModels;

public sealed record NotebookSessionItemViewModel(
    NotebookSession Session,
    string Name,
    string StartedAt,
    string Duration,
    int RecordCount)
{
    public static NotebookSessionItemViewModel From(NotebookSession session, IUiLocalizer? localizer = null)
    {
        var duration = session.EndedAt is null
            ? localizer?["History.NotFinished"] ?? "Не завершена"
            : FormatDuration(session.EndedAt.Value - session.StartedAt, localizer);
        return new NotebookSessionItemViewModel(
            session,
            session.Name,
            session.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            duration,
            session.RecordCount);
    }

    private static string FormatDuration(TimeSpan value, IUiLocalizer? localizer) =>
        localizer?.Language == UiLanguage.English
            ? value.TotalHours >= 1
                ? $"{(int)value.TotalHours} h {value.Minutes} min"
                : $"{Math.Max(0, value.Minutes)} min"
            : value.TotalHours >= 1
                ? $"{(int)value.TotalHours} ч {value.Minutes} мин"
                : $"{Math.Max(0, value.Minutes)} мин";
}

public sealed class HistoryViewModel : ObservableObject
{
    private readonly INotebookRepository _repository;
    private readonly INotebookInteractionService _interaction;
    private readonly NotebookRecorder? _recorder;
    private readonly IUiLocalizer? _localizer;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private readonly List<NotebookRecord> _occurrences = [];
    private NotebookSessionItemViewModel? _selectedSession;
    private string _renameText = string.Empty;

    public HistoryViewModel(
        INotebookRepository repository,
        INotebookInteractionService interaction,
        NotebookRecorder? recorder = null,
        IUiLocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(interaction);
        _repository = repository;
        _interaction = interaction;
        _recorder = recorder;
        _localizer = localizer;
        RefreshCommand = new AsyncCommand(_ => ExecuteSafelyAsync(RefreshAsync));
        OpenCommand = new AsyncCommand(_ => ExecuteSafelyAsync(OpenAsync), () => SelectedSession is not null);
        RenameCommand = new AsyncCommand(_ => ExecuteSafelyAsync(RenameAsync), () =>
            CanMutateSelectedSession && !string.IsNullOrWhiteSpace(RenameText));
        DeleteCommand = new AsyncCommand(_ => ExecuteSafelyAsync(DeleteAsync), () => CanMutateSelectedSession);
        CopyAllCommand = RecordCommand(() => CopyExactAsync(unique: false));
        CopyUniqueCommand = RecordCommand(() => CopyExactAsync(unique: true));
        CopyEscapedCommand = RecordCommand(CopyReadableAsync);
        ExportTextCommand = ExportCommand(NotebookExportFormat.Text);
        ExportReadableTextCommand = ExportCommand(NotebookExportFormat.ReadableText);
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
        if (_localizer is not null)
        {
            _localizer.PropertyChanged += (_, _) => RunOnUi(() => _ = ExecuteSafelyAsync(RefreshAsync));
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
    public AsyncCommand ExportReadableTextCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }
    public int RecordCount => _occurrences.Count;

    private bool CanMutateSelectedSession =>
        SelectedSession is not null && _recorder?.CurrentSession?.Id != SelectedSession.Session.Id;

    private async Task RefreshAsync()
    {
        _repository.Initialize();
        var sessions = await Task.Run(_repository.GetSessions);
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(NotebookSessionItemViewModel.From(session, _localizer));
        }

        SelectedSession = Sessions.FirstOrDefault();
        _occurrences.Clear();
        Records.Clear();
        RaiseRecordCommands();
    }

    private async Task OpenAsync()
    {
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        var records = await Task.Run(() => _repository.GetRecords(selected.Session.Id));
        _occurrences.Clear();
        _occurrences.AddRange(records);
        Records.Clear();
        foreach (var item in NotebookRecordGrouping.Build(_occurrences, _localizer))
        {
            Records.Add(item);
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
        var selected = SelectedSession ?? throw new InvalidOperationException("Select a notebook session first.");
        var path = _interaction.ChooseExportPath(format, selected.Name);
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

    private void RaiseRecordCommands()
    {
        OnPropertyChanged(nameof(RecordCount));
        CopyAllCommand.RaiseCanExecuteChanged();
        CopyUniqueCommand.RaiseCanExecuteChanged();
        CopyEscapedCommand.RaiseCanExecuteChanged();
        ExportTextCommand.RaiseCanExecuteChanged();
        ExportReadableTextCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
    }

    private AsyncCommand RecordCommand(Func<Task> action) =>
        new(_ => ExecuteSafelyAsync(action), () => _occurrences.Count > 0);

    private AsyncCommand ExportCommand(NotebookExportFormat format) =>
        new(_ => ExecuteSafelyAsync(() => ExportAsync(format)), () => _occurrences.Count > 0);

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
