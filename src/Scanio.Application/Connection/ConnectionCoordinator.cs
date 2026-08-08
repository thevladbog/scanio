using System.Collections.Immutable;
using Scanio.Application.Monitor;
using Scanio.Domain.Transport;
using Scanio.Transports;

namespace Scanio.Application.Connection;

public sealed record ConnectionStatusEvent(
    long Sequence,
    DateTimeOffset OccurredAt,
    ConnectionState State,
    TransportIdentity Identity);

public sealed class ConnectionCoordinator
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _snapshotGate = new();
    private readonly IScanProcessingPipeline _pipeline;
    private ImmutableArray<ConnectionStatusEvent> _events = ImmutableArray<ConnectionStatusEvent>.Empty;
    private ActiveSession? _active;
    private TransportIdentity? _activeIdentity;
    private long _nextEventSequence = 1;
    private bool _isShutdown;

    public ConnectionCoordinator(IScanProcessingPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    public event EventHandler<ConnectionStatusEvent>? StatusChanged;

    public ImmutableArray<ConnectionStatusEvent> Events
    {
        get
        {
            lock (_snapshotGate)
            {
                return _events;
            }
        }
    }

    public TransportIdentity? ActiveIdentity
    {
        get
        {
            lock (_snapshotGate)
            {
                return _activeIdentity;
            }
        }
    }

    public void ReportDetected(TransportIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Publish(ConnectionState.Detected, identity);
    }

    public async Task ConnectAsync(IScannerTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isShutdown, this);
            if (_active is not null)
            {
                throw new InvalidOperationException("Disconnect the active scanner before connecting another transport.");
            }

            Publish(ConnectionState.Connecting, transport.Identity);
            try
            {
                await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                Publish(ConnectionState.Disconnected, transport.Identity);
                throw;
            }
            catch
            {
                var failureState = IsConnectionFailure(transport.State)
                    ? transport.State
                    : ConnectionState.TransportError;
                await transport.DisposeAsync().ConfigureAwait(false);
                Publish(failureState, transport.Identity);
                throw;
            }

            var lifetime = new CancellationTokenSource();
            Task processing;
            try
            {
                processing = _pipeline.ProcessAsync(transport, lifetime.Token);
            }
            catch
            {
                lifetime.Dispose();
                await CloseAndDisposeAsync(transport).ConfigureAwait(false);
                Publish(ConnectionState.TransportError, transport.Identity);
                throw;
            }

            var session = new ActiveSession(transport, lifetime, processing);
            _active = session;
            SetActiveIdentity(transport.Identity);
            Publish(ConnectionState.Connected, transport.Identity);
            _ = ObserveCompletionAsync(session);
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
            if (_active is not null)
            {
                await DisconnectActiveAsync(_active).ConfigureAwait(false);
            }
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
            if (_isShutdown)
            {
                return;
            }

            _isShutdown = true;
            if (_active is not null)
            {
                await DisconnectActiveAsync(_active).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task DisconnectActiveAsync(ActiveSession session)
    {
        Publish(ConnectionState.Disconnecting, session.Transport.Identity);
        session.Lifetime.Cancel();
        try
        {
            await session.Processing.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested)
        {
            // Manual disconnect deliberately cancels the active read.
        }
        catch
        {
            Publish(ConnectionState.TransportError, session.Transport.Identity);
        }

        try
        {
            await CloseAndDisposeAsync(session.Transport).ConfigureAwait(false);
        }
        catch
        {
            ClearActive(session);
            session.Lifetime.Dispose();
            Publish(ConnectionState.TransportError, session.Transport.Identity);
            throw;
        }

        ClearActive(session);
        session.Lifetime.Dispose();
        Publish(ConnectionState.Disconnected, session.Transport.Identity);
    }

    private async Task ObserveCompletionAsync(ActiveSession session)
    {
        Exception? processingFailure = null;
        try
        {
            await session.Processing.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            processingFailure = exception;
        }

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_active, session) || session.Lifetime.IsCancellationRequested)
            {
                return;
            }

            var terminalState = processingFailure is null && session.Transport.State == ConnectionState.DeviceRemoved
                ? ConnectionState.DeviceRemoved
                : ConnectionState.TransportError;

            try
            {
                await CloseAndDisposeAsync(session.Transport).ConfigureAwait(false);
            }
            catch
            {
                terminalState = ConnectionState.TransportError;
            }

            ClearActive(session);
            session.Lifetime.Dispose();
            Publish(terminalState, session.Transport.Identity);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private static bool IsConnectionFailure(ConnectionState state) =>
        state is ConnectionState.Busy or ConnectionState.AccessDenied or ConnectionState.TransportError;

    private static async Task CloseAndDisposeAsync(IScannerTransport transport)
    {
        Exception? closeFailure = null;
        try
        {
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            closeFailure = exception;
        }

        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch when (closeFailure is not null)
        {
            throw closeFailure;
        }

        if (closeFailure is not null)
        {
            throw closeFailure;
        }
    }

    private void ClearActive(ActiveSession session)
    {
        if (ReferenceEquals(_active, session))
        {
            _active = null;
            SetActiveIdentity(null);
        }
    }

    private void SetActiveIdentity(TransportIdentity? identity)
    {
        lock (_snapshotGate)
        {
            _activeIdentity = identity;
        }
    }

    private void Publish(ConnectionState state, TransportIdentity identity)
    {
        ConnectionStatusEvent status;
        lock (_snapshotGate)
        {
            status = new ConnectionStatusEvent(
                _nextEventSequence++,
                DateTimeOffset.UtcNow,
                state,
                identity);
            _events = _events.Add(status);
        }

        var subscribers = StatusChanged;
        if (subscribers is null)
        {
            return;
        }

        foreach (EventHandler<ConnectionStatusEvent> subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, status);
            }
            catch
            {
                // Observer failures must not interrupt device cleanup or schedule retries.
            }
        }
    }

    private sealed record ActiveSession(
        IScannerTransport Transport,
        CancellationTokenSource Lifetime,
        Task Processing);
}
