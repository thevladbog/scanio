using System.Threading.Channels;
using Scanio.Application.Monitor;

namespace Scanio.Application.Notebook;

public sealed class NotebookRecorder : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly INotebookRepository _repository;
    private readonly LiveMonitor _monitor;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action? _afterAppendAccepted;
    private readonly Channel<WorkItem> _queue;
    private readonly Dictionary<string, int> _sessionOccurrences = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private NotebookRecordingState _state;
    private NotebookSession? _session;
    private long _nextSequence = 1;
    private string? _lastError;
    private bool _transitioning;
    private bool _disposed;

    public NotebookRecorder(
        INotebookRepository repository,
        LiveMonitor monitor,
        Func<DateTimeOffset>? clock = null)
        : this(repository, monitor, clock, afterAppendAccepted: null)
    {
    }

    internal NotebookRecorder(
        INotebookRepository repository,
        LiveMonitor monitor,
        Func<DateTimeOffset>? clock,
        Action? afterAppendAccepted)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(monitor);
        _repository = repository;
        _monitor = monitor;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _afterAppendAccepted = afterAppendAccepted;
        _queue = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _monitor.EventAppended += OnEventAppended;
        _worker = Task.Run(ProcessQueueAsync);
    }

    public event EventHandler? Changed;

    public event EventHandler<NotebookRecordPersistedEventArgs>? RecordPersisted;

    public NotebookRecordingState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public NotebookSession? CurrentSession
    {
        get
        {
            lock (_gate)
            {
                return _session;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public NotebookSession Start(string name)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_state != NotebookRecordingState.Off || _transitioning)
            {
                throw new InvalidOperationException("A notebook session is already active.");
            }

            _transitioning = true;
        }

        try
        {
            _repository.Initialize();
            var session = _repository.CreateSession(name, _clock());
            lock (_gate)
            {
                _session = session;
                _sessionOccurrences.Clear();
                _nextSequence = 1;
                _lastError = null;
                _state = NotebookRecordingState.Recording;
                _transitioning = false;
            }

            NotifyChanged();
            return session;
        }
        catch
        {
            lock (_gate)
            {
                _transitioning = false;
            }

            throw;
        }
    }

    public void Pause()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_state != NotebookRecordingState.Recording)
            {
                throw new InvalidOperationException("Only an active recording can be paused.");
            }

            _state = NotebookRecordingState.Paused;
        }

        NotifyChanged();
    }

    public void Resume()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_state != NotebookRecordingState.Paused)
            {
                throw new InvalidOperationException("Only a paused recording can be resumed.");
            }

            _state = NotebookRecordingState.Recording;
        }

        NotifyChanged();
    }

    public async Task StopAsync()
    {
        ThrowIfDisposed();
        CompletionWorkItem completion;
        lock (_gate)
        {
            if (_state == NotebookRecordingState.Off || _session is null)
            {
                throw new InvalidOperationException("There is no active notebook session to stop.");
            }

            completion = new CompletionWorkItem(_session.Id, _clock());
            _state = NotebookRecordingState.Off;
            _session = null;
            _transitioning = true;
        }

        if (!_queue.Writer.TryWrite(completion))
        {
            lock (_gate)
            {
                _transitioning = false;
            }

            throw new ObjectDisposedException(nameof(NotebookRecorder));
        }

        NotifyChanged();
        await completion.Done.Task.ConfigureAwait(false);
        lock (_gate)
        {
            _transitioning = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        bool shouldStop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            shouldStop = _state != NotebookRecordingState.Off;
        }

        if (shouldStop)
        {
            await StopAsync().ConfigureAwait(false);
        }

        lock (_gate)
        {
            _disposed = true;
        }

        _monitor.EventAppended -= OnEventAppended;
        _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }

    private void OnEventAppended(object? sender, LiveScanEventAppendedEventArgs args)
    {
        bool appendAccepted;
        lock (_gate)
        {
            if (_disposed || _state != NotebookRecordingState.Recording || _session is null)
            {
                return;
            }

            var key = NotebookPayloadIdentity.Create(args.ScanEvent.Scan.PayloadBytes.AsSpan());
            var count = _sessionOccurrences.TryGetValue(key, out var previous) ? previous + 1 : 1;
            _sessionOccurrences[key] = count;
            var record = NotebookRecord.Create(
                _nextSequence++,
                _session.Id,
                args.ScanEvent.Scan,
                args.ScanEvent.Decoded,
                args.ScanEvent.Analyses,
                count,
                _clock());
            appendAccepted = _queue.Writer.TryWrite(new AppendWorkItem(record));
        }

        if (appendAccepted)
        {
            _afterAppendAccepted?.Invoke();
        }
        else
        {
            ReportFailure(new ObjectDisposedException(nameof(NotebookRecorder)));
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            switch (item)
            {
                case AppendWorkItem append:
                    try
                    {
                        _repository.Append(append.Record);
                        NotifyRecordPersisted(append.Record);
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(exception);
                    }

                    break;

                case CompletionWorkItem completion:
                    try
                    {
                        _repository.CompleteSession(completion.SessionId, completion.EndedAt);
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(exception);
                    }
                    finally
                    {
                        completion.Done.TrySetResult();
                    }

                    break;
            }
        }
    }

    private void ReportFailure(Exception exception)
    {
        lock (_gate)
        {
            _lastError = $"Notebook persistence failed: {exception.Message}";
        }

        NotifyChanged();
    }

    private void NotifyChanged()
    {
        var subscribers = Changed;
        if (subscribers is null)
        {
            return;
        }

        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch
            {
                // A presentation observer must not interrupt recording or persistence.
            }
        }
    }

    private void NotifyRecordPersisted(NotebookRecord record)
    {
        var subscribers = RecordPersisted;
        if (subscribers is null)
        {
            return;
        }

        var args = new NotebookRecordPersistedEventArgs(record);
        foreach (EventHandler<NotebookRecordPersistedEventArgs> subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, args);
            }
            catch
            {
                // Presentation observers must not interrupt the persistence worker.
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private abstract record WorkItem;

    private sealed record AppendWorkItem(NotebookRecord Record) : WorkItem;

    private sealed record CompletionWorkItem(Guid SessionId, DateTimeOffset EndedAt) : WorkItem
    {
        public TaskCompletionSource Done { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class NotebookRecordPersistedEventArgs : EventArgs
{
    public NotebookRecordPersistedEventArgs(NotebookRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Record = record;
    }

    public NotebookRecord Record { get; }
}
