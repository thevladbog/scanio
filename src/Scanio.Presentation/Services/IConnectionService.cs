using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.Services;

public sealed class ConnectionStateChangedEventArgs(
    ConnectionState state,
    TransportIdentity? identity) : EventArgs
{
    public ConnectionState State { get; } = state;

    public TransportIdentity? Identity { get; } = identity;
}

public sealed record ConnectionPresentationSnapshot(
    TransportIdentity Identity,
    ConnectionState State,
    SerialConnectionOptions Options)
{
    public string Endpoint => Identity.Endpoint ?? Options.PortName;
}

public interface IConnectionService
{
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    ConnectionState State { get; }

    TransportIdentity? ActiveIdentity { get; }

    ConnectionPresentationSnapshot? CurrentSnapshot { get; }

    Task ConnectAsync(
        SerialDeviceInfo device,
        SerialConnectionOptions options,
        CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}
