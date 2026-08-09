using Scanio.Application.Notebook;
using Scanio.Presentation.Services;

namespace Scanio.Presentation.ViewModels;

public sealed class ShellViewModel
{
    private readonly IConnectionService _connection;
    private readonly NotebookRecorder _recorder;

    public ShellViewModel(
        ConnectionViewModel connection,
        MonitorViewModel monitor,
        NotebookViewModel notebook,
        HistoryViewModel history,
        NotebookRecorder recorder,
        IConnectionService connectionService)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(connectionService);
        Connection = connection;
        Monitor = monitor;
        Notebook = notebook;
        History = history;
        _recorder = recorder;
        _connection = connectionService;
    }

    public ConnectionViewModel Connection { get; }

    public MonitorViewModel Monitor { get; }

    public NotebookViewModel Notebook { get; }

    public HistoryViewModel History { get; }

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
}
