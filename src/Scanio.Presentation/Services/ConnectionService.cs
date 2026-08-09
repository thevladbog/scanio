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
    private PendingConnectionAttempt? _pendingAttempt;
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
            var attempt = new PendingConnectionAttempt(
                new ConnectionPresentationSnapshot(identity, ConnectionState.Connecting, options),
                KeyboardInput: null,
                transport.Identity);
            lock (_presentationGate)
            {
                _pendingAttempt = attempt;
            }

            try
            {
                await _coordinator.ConnectAsync(transport, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_presentationGate)
                {
                    if (ReferenceEquals(_pendingAttempt, attempt))
                    {
                        _pendingAttempt = null;
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
            var attempt = new PendingConnectionAttempt(
                new ConnectionPresentationSnapshot(identity, ConnectionState.Connecting, Options: null),
                transport,
                transport.Identity);
            lock (_presentationGate)
            {
                _pendingAttempt = attempt;
            }

            try
            {
                await _coordinator.ConnectAsync(transport, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_presentationGate)
                {
                    if (ReferenceEquals(_pendingAttempt, attempt))
                    {
                        _pendingAttempt = null;
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
            if (status.State == ConnectionState.Connecting &&
                _pendingAttempt is { } pending &&
                pending.CoordinatorIdentity == status.Identity)
            {
                _currentSnapshot = pending.Snapshot with
                {
                    Identity = status.Identity,
                    State = status.State
                };
                _keyboardInput = pending.KeyboardInput;
                _pendingAttempt = null;
            }

            if (status.Identity.Kind == TransportKind.KeyboardCapture && IsTerminal(status.State))
            {
                _keyboardInput = null;
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
        }

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(status.State, status.Identity));
    }

    private static bool IsTerminal(ConnectionState state) =>
        state is not (ConnectionState.Connecting or ConnectionState.Connected or ConnectionState.Disconnecting);

    private sealed record PendingConnectionAttempt(
        ConnectionPresentationSnapshot Snapshot,
        IKeyboardCaptureInput? KeyboardInput,
        TransportIdentity CoordinatorIdentity);
}
