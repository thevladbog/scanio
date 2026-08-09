using System.Collections.ObjectModel;
using Scanio.Application.Monitor;
using Scanio.Domain.Transport;
using Scanio.Presentation.Services;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Settings;
using System.ComponentModel;

namespace Scanio.Presentation.ViewModels;

public sealed class MonitorViewModel : ObservableObject
{
    private readonly LiveMonitor _monitor;
    private readonly IConnectionService _connection;
    private readonly IClipboardService _clipboard;
    private readonly IUiLocalizer _localizer;
    private readonly DisplaySettingsSource _displaySettings = DisplaySettingsSource.Current;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private ScanLedgerItemViewModel? _selectedEvent;
    private string? _copyFeedback;
    private CancellationTokenSource? _copyFeedbackCancellation;

    public MonitorViewModel(
        LiveMonitor monitor,
        IConnectionService connection,
        IClipboardService clipboard,
        IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(localizer);
        _monitor = monitor;
        _connection = connection;
        _clipboard = clipboard;
        _localizer = localizer;
        ReturnToLatestCommand = new AsyncCommand(_ =>
        {
            _monitor.ReturnToLatest();
            return Task.CompletedTask;
        }, () => ShowReturnToLatest);
        DisconnectCommand = new AsyncCommand(
            connection.DisconnectAsync,
            () => _connection.ActiveIdentity is not null &&
                  _connection.State is (ConnectionState.Connected or ConnectionState.DeviceRemoved));
        CopyCodeCommand = CopyCommand(item => item.Payload);
        CopyRawCommand = CopyCommand(item => item.Raw);
        CopyHexCommand = CopyCommand(item => item.Hex);
        CopyDiagnosticJsonCommand = CopyCommand(item => ScanDiagnosticJsonSerializer.Serialize(item.Source));
        _monitor.Changed += OnMonitorChanged;
        _connection.StateChanged += (_, _) => RaiseConnectionProperties();
        _displaySettings.PropertyChanged += OnDisplaySettingsChanged;
        _localizer.PropertyChanged += (_, _) => RunOnUi(() =>
        {
            Rebuild();
            RaiseConnectionProperties();
        });
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

            if (SetProperty(ref _selectedEvent, value))
            {
                RaiseCopyCommands();
            }
        }
    }

    public bool ShowReturnToLatest => !_monitor.IsFollowingLatest && Events.Count > 0;

    public string ConnectionLabel => _connection.CurrentSnapshot is { } snapshot
        ? $"{snapshot.Endpoint} · {ConnectionLabels.State(snapshot.State, _localizer)}"
        : _localizer[UiTextKeys.ConnectionNotConnected];

    public string? ConnectionFriendlyName =>
        _connection.CurrentSnapshot?.Identity.DisplayName ?? _connection.ActiveIdentity?.DisplayName;

    public ConnectionSnapshotViewModel? ConnectionSnapshot =>
        ConnectionSnapshotViewModel.From(_connection.CurrentSnapshot, _localizer);

    public string? CopyFeedback
    {
        get => _copyFeedback;
        private set => SetProperty(ref _copyFeedback, value);
    }

    public AsyncCommand ReturnToLatestCommand { get; }

    public AsyncCommand DisconnectCommand { get; }

    public AsyncCommand CopyCodeCommand { get; }

    public AsyncCommand CopyRawCommand { get; }

    public AsyncCommand CopyHexCommand { get; }

    public AsyncCommand CopyDiagnosticJsonCommand { get; }

    public void Activate() => _monitor.ReturnToLatest();

    private void OnMonitorChanged(object? sender, EventArgs args) => RunOnUi(Rebuild);

    private void OnDisplaySettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DisplaySettingsSource.ShowEscapedControls))
        {
            RunOnUi(Rebuild);
        }
    }

    private void Rebuild()
    {
        var selectedId = _monitor.SelectedEvent?.Id;
        Events.Clear();
        foreach (var scanEvent in _monitor.Events)
        {
            Events.Add(new ScanLedgerItemViewModel(scanEvent, _localizer, _displaySettings.ShowEscapedControls));
        }

        SetProperty(ref _selectedEvent, Events.FirstOrDefault(item => item.Id == selectedId), nameof(SelectedEvent));
        OnPropertyChanged(nameof(ShowReturnToLatest));
        ReturnToLatestCommand.RaiseCanExecuteChanged();
        RaiseCopyCommands();
    }

    private void RaiseConnectionProperties() => RunOnUi(() =>
    {
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(ConnectionFriendlyName));
        OnPropertyChanged(nameof(ConnectionSnapshot));
        DisconnectCommand.RaiseCanExecuteChanged();
    });

    private AsyncCommand CopyCommand(Func<ScanLedgerItemViewModel, string> selectText) => new(_ =>
    {
        var selected = SelectedEvent ?? throw new InvalidOperationException("Select a scan before copying.");
        _clipboard.SetText(selectText(selected));
        ShowCopyFeedback();
        return Task.CompletedTask;
    }, () => SelectedEvent is not null);

    private void RaiseCopyCommands()
    {
        CopyCodeCommand.RaiseCanExecuteChanged();
        CopyRawCommand.RaiseCanExecuteChanged();
        CopyHexCommand.RaiseCanExecuteChanged();
        CopyDiagnosticJsonCommand.RaiseCanExecuteChanged();
    }

    private void ShowCopyFeedback()
    {
        _copyFeedbackCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _copyFeedbackCancellation = cancellation;
        CopyFeedback = _localizer[UiTextKeys.MonitorCopied];
        _ = ClearCopyFeedbackAsync(cancellation);
    }

    private async Task ClearCopyFeedbackAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            RunOnUi(() => CopyFeedback = null);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_copyFeedbackCancellation, cancellation))
            {
                _copyFeedbackCancellation = null;
            }

            cancellation.Dispose();
        }
    }

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
