using Scanio.Application.Connection;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Transports;
using Scanio.Transports.Serial;
using Scanio.Transports.Keyboard;

namespace Scanio.Presentation.Services;

public sealed class ConnectionService : IConnectionService
{
    private readonly ConnectionCoordinator _coordinator;
    private readonly Func<TransportIdentity, SerialConnectionOptions, IScannerTransport> _transportFactory;
    private ConnectionState _state = ConnectionState.Detected;
    private ConnectionPresentationSnapshot? _currentSnapshot;
    private IKeyboardCaptureInput? _keyboardInput;

    public ConnectionService(
        ConnectionCoordinator coordinator,
        Func<TransportIdentity, SerialConnectionOptions, IScannerTransport>? transportFactory = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        _coordinator = coordinator;
        _transportFactory = transportFactory ?? ((identity, options) => new SerialTransport(identity, options));
        _coordinator.StatusChanged += OnCoordinatorStatusChanged;
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public ConnectionState State => _state;

    public TransportIdentity? ActiveIdentity => _coordinator.ActiveIdentity;

    public ConnectionPresentationSnapshot? CurrentSnapshot => _currentSnapshot;

    public IKeyboardCaptureInput? KeyboardInput => Volatile.Read(ref _keyboardInput);

    public Task ConnectAsync(
        SerialDeviceInfo device,
        SerialConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);

        var identity = new TransportIdentity(
            TransportKind.Serial,
            device.StableId ?? $"port-session:{device.PortName.ToUpperInvariant()}",
            device.FriendlyName,
            device.HardwareId,
            device.PortName);
        _currentSnapshot = new ConnectionPresentationSnapshot(identity, ConnectionState.Connecting, options);
        return _coordinator.ConnectAsync(_transportFactory(identity, options), cancellationToken);
    }

    public async Task ConnectKeyboardAsync(CancellationToken cancellationToken)
    {
        var identity = new TransportIdentity(
            TransportKind.KeyboardCapture,
            "keyboard-capture:focused-window",
            "Keyboard scanner",
            endpoint: "Keyboard");
        var transport = new KeyboardCaptureTransport(identity);
        var previousSnapshot = _currentSnapshot;
        _currentSnapshot = new ConnectionPresentationSnapshot(identity, ConnectionState.Connecting, Options: null);
        Volatile.Write(ref _keyboardInput, transport);

        try
        {
            await _coordinator.ConnectAsync(transport, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(KeyboardInput, transport))
            {
                Volatile.Write(ref _keyboardInput, null);
                _currentSnapshot = previousSnapshot;
            }

            throw;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        _coordinator.DisconnectAsync(cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        _coordinator.ShutdownAsync(cancellationToken);

    private void OnCoordinatorStatusChanged(object? sender, ConnectionStatusEvent status)
    {
        _state = status.State;
        if (status.Identity.Kind == TransportKind.KeyboardCapture && IsTerminal(status.State))
        {
            Volatile.Write(ref _keyboardInput, null);
        }

        if (status.State is ConnectionState.Disconnected or ConnectionState.Detected)
        {
            _currentSnapshot = null;
        }
        else if (_currentSnapshot is not null)
        {
            _currentSnapshot = _currentSnapshot with
            {
                Identity = status.Identity ?? _currentSnapshot.Identity,
                State = status.State
            };
        }

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(status.State, status.Identity));
    }

    private static bool IsTerminal(ConnectionState state) =>
        state is not (ConnectionState.Connecting or ConnectionState.Connected or ConnectionState.Disconnecting);
}
