using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Scanio.Transports.Serial;

public sealed class SerialTransport : IScannerTransport
{
    private const int ReadBufferSize = 4_096;
    private readonly ISerialPortAdapter _adapter;
    private readonly Action? _beforeReadRegistration;
    private readonly object _gate = new();
    private CancellationTokenSource? _lifetimeCancellation;
    private TaskCompletionSource<bool>? _activeReadCompletion;
    private Task? _closeTask;
    private Task? _disposeTask;
    private ConnectionState _state = ConnectionState.Detected;
    private bool _adapterOpened;
    private bool _readerActive;
    private bool _disposeStarted;
    private bool _adapterDisposed;

    public SerialTransport(TransportIdentity identity, SerialConnectionOptions options)
        : this(identity, options, new SystemSerialPortAdapter(options))
    {
    }

    public SerialTransport(
        TransportIdentity identity,
        SerialConnectionOptions options,
        ISerialPortAdapter adapter)
        : this(identity, options, adapter, beforeReadRegistration: null)
    {
    }

    internal SerialTransport(
        TransportIdentity identity,
        SerialConnectionOptions options,
        ISerialPortAdapter adapter,
        Action? beforeReadRegistration)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(adapter);

        if (identity.Kind != TransportKind.Serial)
        {
            throw new ArgumentException("A serial transport requires a serial identity.", nameof(identity));
        }

        Identity = identity;
        Options = options;
        _adapter = adapter;
        _beforeReadRegistration = beforeReadRegistration;
    }

    public TransportIdentity Identity { get; }

    public SerialConnectionOptions Options { get; }

    public ConnectionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
            if (_state is not (ConnectionState.Detected or ConnectionState.Disconnected))
            {
                throw new InvalidOperationException("The serial transport has already been opened.");
            }

            _state = ConnectionState.Connecting;
            try
            {
                _adapter.Open();
            }
            catch (SerialPortOpenException exception)
            {
                _state = exception.FailureKind switch
                {
                    SerialPortOpenFailureKind.AccessDenied => ConnectionState.AccessDenied,
                    SerialPortOpenFailureKind.Busy => ConnectionState.Busy,
                    _ => ConnectionState.TransportError
                };
                throw;
            }
            catch
            {
                _state = ConnectionState.TransportError;
                throw;
            }

            _lifetimeCancellation = new CancellationTokenSource();
            _adapterOpened = true;
            _closeTask = null;
            _state = ConnectionState.Connected;
            return ValueTask.CompletedTask;
        }
    }

    public async IAsyncEnumerable<RawChunk> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CancellationToken lifetimeToken;
        lock (_gate)
        {
            if (_state != ConnectionState.Connected || _lifetimeCancellation is null)
            {
                throw new InvalidOperationException("The serial transport is not open.");
            }

            if (_readerActive)
            {
                throw new InvalidOperationException("The serial transport already has an active reader.");
            }

            _readerActive = true;
            lifetimeToken = _lifetimeCancellation.Token;
        }

        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        var buffer = new byte[ReadBufferSize];
        long sequenceNumber = 0;

        try
        {
            while (true)
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                _beforeReadRegistration?.Invoke();
                var readCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    if (_state != ConnectionState.Connected || linkedCancellation.IsCancellationRequested)
                    {
                        linkedCancellation.Token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("The serial transport is not open.");
                    }

                    _activeReadCompletion = readCompletion;
                }

                int bytesRead;
                try
                {
                    bytesRead = await _adapter.ReadAsync(buffer, linkedCancellation.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    SetReadFailureState(ConnectionState.DeviceRemoved);
                    yield break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    SetReadFailureState(ConnectionState.TransportError);
                    throw;
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_activeReadCompletion, readCompletion))
                        {
                            _activeReadCompletion = null;
                        }
                    }

                    readCompletion.TrySetResult(true);
                }

                if (bytesRead <= 0)
                {
                    SetReadFailureState(ConnectionState.DeviceRemoved);
                    yield break;
                }

                yield return RawChunk.Create(
                    ++sequenceNumber,
                    buffer.AsSpan(0, bytesRead),
                    DateTimeOffset.UtcNow,
                    Stopwatch.GetTimestamp(),
                    Identity);
            }
        }
        finally
        {
            lock (_gate)
            {
                _readerActive = false;
            }
        }
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_closeTask is not null)
            {
                return new ValueTask(_closeTask);
            }

            if (!_adapterOpened)
            {
                if (_state is ConnectionState.Detected or ConnectionState.Connecting or ConnectionState.Connected or
                    ConnectionState.Disconnecting)
                {
                    _state = ConnectionState.Disconnected;
                }

                return ValueTask.CompletedTask;
            }

            var preserveDeviceRemoved = _state == ConnectionState.DeviceRemoved;
            if (!preserveDeviceRemoved)
            {
                _state = ConnectionState.Disconnecting;
            }

            _lifetimeCancellation!.Cancel();
            _closeTask = CloseOpenedAdapterAsync(_activeReadCompletion?.Task, preserveDeviceRemoved);
            return new ValueTask(_closeTask);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposeStarted = true;
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task CloseOpenedAdapterAsync(Task? activeRead, bool preserveDeviceRemoved)
    {
        if (activeRead is not null)
        {
            await activeRead.ConfigureAwait(false);
        }

        try
        {
            _adapter.Close();
        }
        catch
        {
            lock (_gate)
            {
                _state = ConnectionState.TransportError;
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                _adapterOpened = false;
                _lifetimeCancellation?.Dispose();
                _lifetimeCancellation = null;
                if (_state != ConnectionState.TransportError)
                {
                    _state = preserveDeviceRemoved
                        ? ConnectionState.DeviceRemoved
                        : ConnectionState.Disconnected;
                }
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (!_adapterDisposed)
                {
                    _adapter.Dispose();
                    _adapterDisposed = true;
                }
            }
        }
    }

    private void SetReadFailureState(ConnectionState state)
    {
        lock (_gate)
        {
            if (_state == ConnectionState.Connected)
            {
                _state = state;
            }
        }
    }
}
