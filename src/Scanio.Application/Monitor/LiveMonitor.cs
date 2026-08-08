using System.Collections.Immutable;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;

namespace Scanio.Application.Monitor;

public sealed record LiveScanEvent
{
    internal LiveScanEvent(
        long id,
        CompletedScan scan,
        DecodedPayload decoded,
        ImmutableArray<AnalysisResult> analyses,
        int duplicateCount)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(decoded);

        Id = id;
        Scan = scan;
        Decoded = decoded;
        Analyses = ImmutableArray.CreateRange(analyses);
        DuplicateCount = duplicateCount;
    }

    public long Id { get; }

    public CompletedScan Scan { get; }

    public DecodedPayload Decoded { get; }

    public ImmutableArray<AnalysisResult> Analyses { get; }

    public int DuplicateCount { get; init; }
}

public sealed class LiveMonitor
{
    public const int Capacity = 1_000;

    private readonly object _gate = new();
    private ImmutableArray<LiveScanEvent> _events = ImmutableArray<LiveScanEvent>.Empty;
    private long _nextId = 1;
    private long? _selectedId;
    private bool _isFollowingLatest = true;

    public event EventHandler? Changed;

    public ImmutableArray<LiveScanEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events;
            }
        }
    }

    public LiveScanEvent? SelectedEvent
    {
        get
        {
            lock (_gate)
            {
                return FindById(_selectedId);
            }
        }
    }

    public bool IsFollowingLatest
    {
        get
        {
            lock (_gate)
            {
                return _isFollowingLatest;
            }
        }
    }

    public LiveScanEvent Append(
        CompletedScan scan,
        DecodedPayload decoded,
        IEnumerable<AnalysisResult> analyses)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(analyses);

        var ownedAnalyses = ImmutableArray.CreateRange(
            analyses.Select(result => result ?? throw new ArgumentException(
                "Monitor analyses cannot contain null.", nameof(analyses))));

        LiveScanEvent appended;
        lock (_gate)
        {
            var added = new LiveScanEvent(_nextId++, scan, decoded, ownedAnalyses, duplicateCount: 1);
            _events = _events.Add(added);
            NormalizeDuplicateCounts(scan.PayloadBytes);

            if (_events.Length > Capacity)
            {
                var evictedPayload = _events[0].Scan.PayloadBytes;
                _events = _events.RemoveAt(0);
                NormalizeDuplicateCounts(evictedPayload);
            }

            if (_isFollowingLatest)
            {
                _selectedId = added.Id;
            }
            else if (FindById(_selectedId) is null)
            {
                _selectedId = _events.IsEmpty ? null : _events[0].Id;
            }

            appended = FindById(added.Id)!;
        }

        NotifyChanged();
        return appended;
    }

    public void Select(long id)
    {
        lock (_gate)
        {
            if (FindById(id) is null)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "The live event is no longer retained.");
            }

            _selectedId = id;
            _isFollowingLatest = false;
        }

        NotifyChanged();
    }

    public void ReturnToLatest()
    {
        lock (_gate)
        {
            _isFollowingLatest = true;
            _selectedId = _events.IsEmpty ? null : _events[^1].Id;
        }

        NotifyChanged();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events = ImmutableArray<LiveScanEvent>.Empty;
            _selectedId = null;
            _isFollowingLatest = true;
        }

        NotifyChanged();
    }

    private LiveScanEvent? FindById(long? id)
    {
        if (id is null)
        {
            return null;
        }

        foreach (var scanEvent in _events)
        {
            if (scanEvent.Id == id.Value)
            {
                return scanEvent;
            }
        }

        return null;
    }

    private void NormalizeDuplicateCounts(ImmutableArray<byte> payload)
    {
        var duplicateCount = 0;
        foreach (var scanEvent in _events)
        {
            if (scanEvent.Scan.PayloadBytes.AsSpan().SequenceEqual(payload.AsSpan()))
            {
                duplicateCount++;
            }
        }

        for (var index = 0; index < _events.Length; index++)
        {
            if (_events[index].Scan.PayloadBytes.AsSpan().SequenceEqual(payload.AsSpan()) &&
                _events[index].DuplicateCount != duplicateCount)
            {
                _events = _events.SetItem(index, _events[index] with { DuplicateCount = duplicateCount });
            }
        }
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
                // A presentation observer must not interrupt capture processing.
            }
        }
    }
}
