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
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _presentationGate = new();
    private ConnectionState _state = ConnectionState.Detected;
    private ConnectionPresentationSnapshot? _currentSnapshot;
    private IKeyboardCaptureInput? _keyboardInput;
    private object? _snapshotOwner;
    private long _lastStatusSequence;

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

    public ConnectionState State
    {
        get
        {
            lock (_presentationGate)
            {
                return _state;
            }
        }
    }

    public TransportIdentity? ActiveIdentity => _coordinator.ActiveIdentity;

    public ConnectionPresentationSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_presentationGate)
            {
                return _currentSnapshot;
            }
        }
    }

    public IKeyboardCaptureInput? KeyboardInput
    {
        get
        {
            lock (_presentationGate)
            {
                return _keyboardInput;
            }
        }
    }

    public async Task ConnectAsync(
        SerialDeviceInfo device,
        SerialConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var identity = new TransportIdentity(
                TransportKind.Serial,
                device.StableId ?? $"port-session:{device.PortName.ToUpperInvariant()}",
                device.FriendlyName,
                device.HardwareId,
                device.PortName);
            var transport = _transportFactory(identity, options);
            var attemptOwner = new object();
            ConnectionPresentationSnapshot? previousSnapshot;
            object? previousSnapshotOwner;
            lock (_presentationGate)
            {
                previousSnapshot = _currentSnapshot;
                previousSnapshotOwner = _snapshotOwner;
                _currentSnapshot = new ConnectionPresentationSnapshot(identity, ConnectionState.Connecting, options);
                _snapshotOwner = attemptOwner;
            }

            try
            {
                await _coordinator.ConnectAsync(transport, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_presentationGate)
                {
                    if (ReferenceEquals(_snapshotOwner, attemptOwner))
                    {
                        _currentSnapshot = previousSnapshot;
                        _snapshotOwner = previousSnapshotOwner;
                    }
                }

                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task ConnectKeyboardAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var identity = new TransportIdentity(
                TransportKind.KeyboardCapture,
                "keyboard-capture:focused-window",
                "Keyboard scanner",
                endpoint: "Keyboard");
            var transport = new KeyboardCaptureTransport(identity);
            var attemptOwner = new object();
            ConnectionPresentationSnapshot? previousSnapshot;
            object? previousSnapshotOwner;
            IKeyboardCaptureInput? previousKeyboardInput;
            lock (_presentationGate)
            {
                previousSnapshot = _currentSnapshot;
                previousSnapshotOwner = _snapshotOwner;
                previousKeyboardInput = _keyboardInput;
                _currentSnapshot = new ConnectionPresentationSnapshot(identity, ConnectionState.Connecting, Options: null);
                _snapshotOwner = attemptOwner;
                _keyboardInput = transport;
            }

            try
            {
                await _coordinator.ConnectAsync(transport, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_presentationGate)
                {
                    if (ReferenceEquals(_snapshotOwner, attemptOwner))
                    {
                        _currentSnapshot = previousSnapshot;
                        _snapshotOwner = previousSnapshotOwner;
                    }

                    if (ReferenceEquals(_keyboardInput, transport))
                    {
                        _keyboardInput = previousKeyboardInput;
                    }
                }

                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _coordinator.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _coordinator.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void OnCoordinatorStatusChanged(object? sender, ConnectionStatusEvent status)
    {
        lock (_presentationGate)
        {
            if (status.Sequence <= _lastStatusSequence)
            {
                return;
            }

            _lastStatusSequence = status.Sequence;
            _state = status.State;
            if (status.Identity.Kind == TransportKind.KeyboardCapture && IsTerminal(status.State))
            {
                _keyboardInput = null;
            }

            if (status.State is ConnectionState.Disconnected or ConnectionState.Detected)
            {
                _currentSnapshot = null;
                _snapshotOwner = null;
            }
            else if (_currentSnapshot is not null)
            {
                _currentSnapshot = _currentSnapshot with
                {
                    Identity = status.Identity ?? _currentSnapshot.Identity,
                    State = status.State
                };
                if (IsTerminal(status.State))
                {
                    _snapshotOwner = null;
                }
            }
        }

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(status.State, status.Identity));
    }

    private static bool IsTerminal(ConnectionState state) =>
        state is not (ConnectionState.Connecting or ConnectionState.Connected or ConnectionState.Disconnecting);
}
