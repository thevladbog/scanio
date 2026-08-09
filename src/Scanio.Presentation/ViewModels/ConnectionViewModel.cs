using System.Collections.ObjectModel;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Services;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.ViewModels;

public sealed class ConnectionViewModel : ObservableObject
{
    private readonly ISerialDeviceEnumerator _deviceEnumerator;
    private readonly IConnectionService _connection;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private SerialDeviceInfo? _selectedDevice;
    private ConnectionState _state;
    private string? _errorMessage;

    public ConnectionViewModel(
        ISerialDeviceEnumerator deviceEnumerator,
        IConnectionService connection)
    {
        ArgumentNullException.ThrowIfNull(deviceEnumerator);
        ArgumentNullException.ThrowIfNull(connection);
        _deviceEnumerator = deviceEnumerator;
        _connection = connection;
        _state = connection.State;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => IsEditingEnabled);
        ConnectCommand = new AsyncCommand(ConnectAsync, () => SelectedDevice is not null && IsEditingEnabled);
        DisconnectCommand = new AsyncCommand(connection.DisconnectAsync, () => State is ConnectionState.Connected or ConnectionState.DeviceRemoved);
        _connection.StateChanged += OnConnectionStateChanged;
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

    public int BaudRate { get; set; } = 9_600;

    public int DataBits { get; set; } = 8;

    public SerialParity Parity { get; set; } = SerialParity.None;

    public IReadOnlyList<SerialParity> ParityOptions { get; } = Enum.GetValues<SerialParity>();

    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;

    public IReadOnlyList<SerialStopBits> StopBitOptions { get; } = Enum.GetValues<SerialStopBits>();

    public SerialHandshake Handshake { get; set; } = SerialHandshake.None;

    public IReadOnlyList<SerialHandshake> HandshakeOptions { get; } = Enum.GetValues<SerialHandshake>();

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

    public string StateTitle => State switch
    {
        ConnectionState.Connecting => "Подключение…",
        ConnectionState.Connected => "Подключено",
        ConnectionState.Busy => "Порт занят",
        ConnectionState.AccessDenied => "Доступ запрещён",
        ConnectionState.DeviceRemoved => "Устройство отключено",
        ConnectionState.TransportError => "Ошибка транспорта",
        ConnectionState.Disconnecting => "Отключение…",
        ConnectionState.Disconnected => "Отключено",
        _ => "Обнаружено"
    };

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
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var device = SelectedDevice ?? throw new InvalidOperationException("Select a serial device first.");
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
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs args) =>
        RunOnUi(() => State = args.State);

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
