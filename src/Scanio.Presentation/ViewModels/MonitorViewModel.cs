using System.Collections.ObjectModel;
using Scanio.Application.Monitor;
using Scanio.Domain.Transport;
using Scanio.Presentation.Services;

namespace Scanio.Presentation.ViewModels;

public sealed class MonitorViewModel : ObservableObject
{
    private readonly LiveMonitor _monitor;
    private readonly IConnectionService _connection;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private ScanLedgerItemViewModel? _selectedEvent;

    public MonitorViewModel(LiveMonitor monitor, IConnectionService connection)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(connection);
        _monitor = monitor;
        _connection = connection;
        ReturnToLatestCommand = new AsyncCommand(_ =>
        {
            _monitor.ReturnToLatest();
            return Task.CompletedTask;
        }, () => ShowReturnToLatest);
        DisconnectCommand = new AsyncCommand(
            connection.DisconnectAsync,
            () => _connection.ActiveIdentity is not null &&
                  _connection.State is (ConnectionState.Connected or ConnectionState.DeviceRemoved));
        _monitor.Changed += OnMonitorChanged;
        _connection.StateChanged += (_, _) => RaiseConnectionProperties();
        Rebuild();
    }

    public ObservableCollection<ScanLedgerItemViewModel> Events { get; } = [];

    public ScanLedgerItemViewModel? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (value is not null && value.Id != _monitor.SelectedEvent?.Id)
            {
                _monitor.Select(value.Id);
                return;
            }

            SetProperty(ref _selectedEvent, value);
        }
    }

    public bool ShowReturnToLatest => !_monitor.IsFollowingLatest && Events.Count > 0;

    public string ConnectionLabel => _connection.ActiveIdentity is null
        ? "Нет подключения"
        : $"{_connection.ActiveIdentity.DisplayName} · {_connection.State}";

    public AsyncCommand ReturnToLatestCommand { get; }

    public AsyncCommand DisconnectCommand { get; }

    private void OnMonitorChanged(object? sender, EventArgs args) => RunOnUi(Rebuild);

    private void Rebuild()
    {
        var selectedId = _monitor.SelectedEvent?.Id;
        Events.Clear();
        foreach (var scanEvent in _monitor.Events)
        {
            Events.Add(new ScanLedgerItemViewModel(scanEvent));
        }

        SetProperty(ref _selectedEvent, Events.FirstOrDefault(item => item.Id == selectedId), nameof(SelectedEvent));
        OnPropertyChanged(nameof(ShowReturnToLatest));
        ReturnToLatestCommand.RaiseCanExecuteChanged();
    }

    private void RaiseConnectionProperties() => RunOnUi(() =>
    {
        OnPropertyChanged(nameof(ConnectionLabel));
        DisconnectCommand.RaiseCanExecuteChanged();
    });

    private void RunOnUi(Action action)
    {
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            action();
            return;
        }

        _synchronizationContext.Post(_ => action(), null);
    }
}
