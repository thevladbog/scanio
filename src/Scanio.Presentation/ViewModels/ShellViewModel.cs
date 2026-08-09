using Scanio.Application.Notebook;
using Scanio.Presentation.Services;

namespace Scanio.Presentation.ViewModels;

public enum ShellDestination
{
    Connection,
    Monitor,
    Notebook,
    History,
    Settings
}

public sealed class ShellViewModel : ObservableObject
{
    private readonly IConnectionService _connection;
    private readonly NotebookRecorder _recorder;
    private ShellDestination _selectedDestination = ShellDestination.Connection;

    public ShellViewModel(
        ConnectionViewModel connection,
        MonitorViewModel monitor,
        NotebookViewModel notebook,
        HistoryViewModel history,
        SettingsViewModel settings,
        NotebookRecorder recorder,
        IConnectionService connectionService)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(connectionService);
        Connection = connection;
        Monitor = monitor;
        Notebook = notebook;
        History = history;
        Settings = settings;
        _recorder = recorder;
        _connection = connectionService;
        ShowConnectionCommand = NavigateCommand(ShellDestination.Connection);
        ShowMonitorCommand = new AsyncCommand(_ =>
        {
            Monitor.Activate();
            SelectedDestination = ShellDestination.Monitor;
            return Task.CompletedTask;
        });
        ShowNotebookCommand = NavigateCommand(ShellDestination.Notebook);
        ShowHistoryCommand = new AsyncCommand(async _ =>
        {
            SelectedDestination = ShellDestination.History;
            await History.RefreshCommand.ExecuteAsync();
        });
        ShowSettingsCommand = NavigateCommand(ShellDestination.Settings);
    }

    public ConnectionViewModel Connection { get; }

    public MonitorViewModel Monitor { get; }

    public NotebookViewModel Notebook { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    public ShellDestination SelectedDestination
    {
        get => _selectedDestination;
        private set
        {
            if (SetProperty(ref _selectedDestination, value))
            {
                OnPropertyChanged(nameof(CurrentScreen));
            }
        }
    }

    public object CurrentScreen => SelectedDestination switch
    {
        ShellDestination.Connection => Connection,
        ShellDestination.Monitor => Monitor,
        ShellDestination.Notebook => Notebook,
        ShellDestination.History => History,
        ShellDestination.Settings => Settings,
        _ => throw new ArgumentOutOfRangeException()
    };

    public AsyncCommand ShowConnectionCommand { get; }

    public AsyncCommand ShowMonitorCommand { get; }

    public AsyncCommand ShowNotebookCommand { get; }

    public AsyncCommand ShowHistoryCommand { get; }

    public AsyncCommand ShowSettingsCommand { get; }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _recorder.DisposeAsync();
        }
        finally
        {
            await _connection.ShutdownAsync(cancellationToken);
        }
    }

    private AsyncCommand NavigateCommand(ShellDestination destination) => new(_ =>
    {
        SelectedDestination = destination;
        return Task.CompletedTask;
    });
}
