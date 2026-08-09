using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Transports.Keyboard;

public interface IKeyboardCaptureInput
{
    bool AppendText(string text);

    bool CompleteInput();

    bool HasPendingInput { get; }
}

public sealed class KeyboardCaptureTransport : IScannerTransport, IKeyboardCaptureInput
{
    private const byte CrFramingByte = 0x0D;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _wallClock;
    private readonly Func<long> _monotonicTimestamp;
    private ConnectionState _state = ConnectionState.Disconnected;
    private Session? _session;
    private bool _disposed;

    public KeyboardCaptureTransport(TransportIdentity identity)
        : this(identity, () => DateTimeOffset.UtcNow, Stopwatch.GetTimestamp)
    {
    }

    internal KeyboardCaptureTransport(
        TransportIdentity identity,
        Func<DateTimeOffset> wallClock,
        Func<long> monotonicTimestamp)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(wallClock);
        ArgumentNullException.ThrowIfNull(monotonicTimestamp);

        if (identity.Kind != TransportKind.KeyboardCapture)
        {
            throw new ArgumentException("A keyboard capture transport requires a keyboard capture identity.", nameof(identity));
        }

        Identity = identity;
        _wallClock = wallClock;
        _monotonicTimestamp = monotonicTimestamp;
    }

    public TransportIdentity Identity { get; }

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

    public bool HasPendingInput
    {
        get
        {
            lock (_gate)
            {
                return _state == ConnectionState.Connected && _session?.Buffer.Length > 0;
            }
        }
    }

    public ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException("The keyboard capture transport has already been opened.");
            }

            _state = ConnectionState.Connecting;
            _session = new Session();
            _state = ConnectionState.Connected;
            return ValueTask.CompletedTask;
        }
    }

    public async IAsyncEnumerable<RawChunk> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Session session;
        lock (_gate)
        {
            if (_state != ConnectionState.Connected || _session is null)
            {
                throw new InvalidOperationException("The keyboard capture transport is not open.");
            }

            if (_session.ReaderActive)
            {
                throw new InvalidOperationException("The keyboard capture transport already has an active reader.");
            }

            session = _session;
            session.ReaderActive = true;
        }

        try
        {
            await foreach (var chunk in session.Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
        finally
        {
            lock (_gate)
            {
                session.ReaderActive = false;
            }
        }
    }

    public bool AppendText(string text)
    {
        lock (_gate)
        {
            if (_state != ConnectionState.Connected || _session is null || string.IsNullOrEmpty(text))
            {
                return false;
            }

            _session.Buffer.Append(text);
            return true;
        }
    }

    public bool CompleteInput()
    {
        lock (_gate)
        {
            if (_state != ConnectionState.Connected || _session is null || _session.Buffer.Length == 0)
            {
                return false;
            }

            var payload = Encoding.UTF8.GetBytes(_session.Buffer.ToString());
            var framedPayload = new byte[payload.Length + 1];
            payload.CopyTo(framedPayload, 0);
            framedPayload[^1] = CrFramingByte;
            var sequenceNumber = _session.SequenceNumber + 1;
            var chunk = RawChunk.Create(
                sequenceNumber,
                framedPayload,
                _wallClock(),
                _monotonicTimestamp(),
                Identity);

            if (!_session.Channel.Writer.TryWrite(chunk))
            {
                return false;
            }

            _session.SequenceNumber = sequenceNumber;
            _session.Buffer.Clear();
            return true;
        }
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            CloseLocked();
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            CloseLocked();
            return ValueTask.CompletedTask;
        }
    }

    private void CloseLocked()
    {
        if (_state is ConnectionState.Disconnected or ConnectionState.Disconnecting)
        {
            return;
        }

        _state = ConnectionState.Disconnecting;
        _session?.Buffer.Clear();
        _session?.Channel.Writer.TryComplete();
        _state = ConnectionState.Disconnected;
    }

    private sealed class Session
    {
        public Channel<RawChunk> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<RawChunk>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

        public StringBuilder Buffer { get; } = new();

        public long SequenceNumber { get; set; }

        public bool ReaderActive { get; set; }
    }
}
