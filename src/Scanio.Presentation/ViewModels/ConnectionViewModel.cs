using System.Collections.ObjectModel;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.ViewModels;

public sealed record LocalizedOption<T>(T Value, string Label) where T : struct, Enum;

public sealed class ConnectionViewModel : ObservableObject
{
    private readonly ISerialDeviceEnumerator _deviceEnumerator;
    private readonly IConnectionService _connection;
    private readonly IUiLocalizer _localizer;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private SerialDeviceInfo? _selectedDevice;
    private ConnectionState _state;
    private string? _errorMessage;

    public ConnectionViewModel(
        ISerialDeviceEnumerator deviceEnumerator,
        IConnectionService connection,
        IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(deviceEnumerator);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(localizer);
        _deviceEnumerator = deviceEnumerator;
        _connection = connection;
        _localizer = localizer;
        _state = connection.State;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => IsEditingEnabled);
        ConnectCommand = new AsyncCommand(ConnectAsync, () => SelectedDevice is not null && IsEditingEnabled);
        DisconnectCommand = new AsyncCommand(connection.DisconnectAsync, () => State is ConnectionState.Connected or ConnectionState.DeviceRemoved);
        _connection.StateChanged += OnConnectionStateChanged;
        _localizer.PropertyChanged += (_, _) => RunOnUi(RaiseLocalizedProperties);
        ConnectCommand.PropertyChanged += (_, _) => OnCommandStateChanged();
    }

    public ObservableCollection<SerialDeviceInfo> Devices { get; } = [];

    public SerialDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedDevice is not null;

    public ConnectionSnapshotViewModel? ConnectionSnapshot =>
        ConnectionSnapshotViewModel.From(_connection.CurrentSnapshot, _localizer);

    public string HeaderConnectionLabel => ConnectionSnapshot is { } snapshot
        ? $"{snapshot.Endpoint} · {snapshot.StateLabel}"
        : _localizer[UiTextKeys.ConnectionNotConnected];

    public int BaudRate { get; set; } = 9_600;

    public int DataBits { get; set; } = 8;

    public SerialParity Parity { get; set; } = SerialParity.None;

    public IReadOnlyList<LocalizedOption<SerialParity>> ParityOptions =>
        Enum.GetValues<SerialParity>()
            .Select(value => new LocalizedOption<SerialParity>(value, ConnectionLabels.Parity(value, _localizer)))
            .ToArray();

    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;

    public IReadOnlyList<LocalizedOption<SerialStopBits>> StopBitOptions =>
        Enum.GetValues<SerialStopBits>()
            .Select(value => new LocalizedOption<SerialStopBits>(value, ConnectionLabels.StopBits(value, _localizer)))
            .ToArray();

    public SerialHandshake Handshake { get; set; } = SerialHandshake.None;

    public IReadOnlyList<LocalizedOption<SerialHandshake>> HandshakeOptions =>
        Enum.GetValues<SerialHandshake>()
            .Select(value => new LocalizedOption<SerialHandshake>(value, ConnectionLabels.Handshake(value, _localizer)))
            .ToArray();

    public bool DtrEnable { get; set; }

    public bool RtsEnable { get; set; }

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateTitle));
                OnPropertyChanged(nameof(IsEditingEnabled));
                RefreshCommand.RaiseCanExecuteChanged();
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StateTitle => ConnectionLabels.State(State, _localizer);

    public bool IsEditingEnabled =>
        !ConnectCommand.IsRunning && State is not (ConnectionState.Connecting or ConnectionState.Connected or ConnectionState.Disconnecting);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand ConnectCommand { get; }

    public AsyncCommand DisconnectCommand { get; }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            var detected = await _deviceEnumerator.EnumerateAsync(cancellationToken);
            Devices.Clear();
            foreach (var device in detected.OrderBy(device => device.PortName, StringComparer.OrdinalIgnoreCase))
            {
                Devices.Add(device);
            }

            SelectedDevice = Devices.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ErrorMessage = _localizer["Error.RefreshDevices"];
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var device = SelectedDevice ?? throw new InvalidOperationException(_localizer["Error.SelectDevice"]);
        ErrorMessage = null;
        try
        {
            var options = new SerialConnectionOptions(
                device.PortName,
                BaudRate,
                DataBits,
                Parity,
                StopBits,
                Handshake,
                DtrEnable,
                RtsEnable);
            await _connection.ConnectAsync(device, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ErrorMessage = _localizer["Error.Connect"];
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs args) =>
        RunOnUi(() =>
        {
            State = args.State;
            OnPropertyChanged(nameof(ConnectionSnapshot));
            OnPropertyChanged(nameof(HeaderConnectionLabel));
        });

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(StateTitle));
        OnPropertyChanged(nameof(ConnectionSnapshot));
        OnPropertyChanged(nameof(HeaderConnectionLabel));
        OnPropertyChanged(nameof(ParityOptions));
        OnPropertyChanged(nameof(StopBitOptions));
        OnPropertyChanged(nameof(HandshakeOptions));
    }

    private void OnCommandStateChanged()
    {
        OnPropertyChanged(nameof(IsEditingEnabled));
        RefreshCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
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
