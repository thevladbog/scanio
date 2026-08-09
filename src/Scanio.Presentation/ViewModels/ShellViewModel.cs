using Scanio.Presentation.Services;

namespace Scanio.Presentation.ViewModels;

public sealed class ShellViewModel
{
    private readonly IConnectionService _connection;

    public ShellViewModel(
        ConnectionViewModel connection,
        MonitorViewModel monitor,
        IConnectionService connectionService)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(connectionService);
        Connection = connection;
        Monitor = monitor;
        _connection = connectionService;
    }

    public ConnectionViewModel Connection { get; }

    public MonitorViewModel Monitor { get; }

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        _connection.ShutdownAsync(cancellationToken);
}
