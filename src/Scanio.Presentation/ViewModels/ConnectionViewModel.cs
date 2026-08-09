using System.Collections.ObjectModel;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.ViewModels;

public sealed record LocalizedOption<T>(T Value, string Label) where T : struct, Enum;

public enum ConnectionMode
{
    Serial,
    Keyboard
}

public sealed class ConnectionViewModel : ObservableObject
{
    private static readonly TimeSpan KeyboardSilenceTimeout = TimeSpan.FromMilliseconds(100);
    private readonly ISerialDeviceEnumerator _deviceEnumerator;
    private readonly IConnectionService _connection;
    private readonly IUiLocalizer _localizer;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private readonly object _keyboardDeadlineGate = new();
    private SerialDeviceInfo? _selectedDevice;
    private ConnectionState _state;
    private ConnectionMode _selectedMode;
    private bool _isKeyboardSurfaceFocused;
    private long _keyboardDeadlineGeneration;
    private CancellationTokenSource? _keyboardDeadlineCancellation;
    private string _pendingKeyboardText = string.Empty;
    private string? _lastKeyboardScan;
    private string? _errorMessage;

    public ConnectionViewModel(
        ISerialDeviceEnumerator deviceEnumerator,
        IConnectionService connection,
        IUiLocalizer localizer)
        : this(deviceEnumerator, connection, localizer, Task.Delay)
    {
    }

    internal ConnectionViewModel(
        ISerialDeviceEnumerator deviceEnumerator,
        IConnectionService connection,
        IUiLocalizer localizer,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(deviceEnumerator);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(delay);
        _deviceEnumerator = deviceEnumerator;
        _connection = connection;
        _localizer = localizer;
        _delay = delay;
        _state = connection.State;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => IsEditingEnabled);
        ConnectCommand = new AsyncCommand(ConnectAsync, CanStartSerial);
        StartKeyboardTestCommand = new AsyncCommand(ConnectKeyboardAsync, CanStartKeyboard);
        DisconnectCommand = new AsyncCommand(DisconnectAsync, () => State is ConnectionState.Connected or ConnectionState.DeviceRemoved);
        _connection.StateChanged += OnConnectionStateChanged;
        _localizer.PropertyChanged += (_, _) => RunOnUi(RaiseLocalizedProperties);
        ConnectCommand.PropertyChanged += (_, _) => OnCommandStateChanged();
        StartKeyboardTestCommand.PropertyChanged += (_, _) => OnCommandStateChanged();
    }

    public event EventHandler? KeyboardFocusRequested;

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

    public ConnectionMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetProperty(ref _selectedMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSerialMode));
            OnPropertyChanged(nameof(IsKeyboardMode));
            OnPropertyChanged(nameof(IsKeyboardStartVisible));
            OnPropertyChanged(nameof(IsKeyboardStopVisible));
            ConnectCommand.RaiseCanExecuteChanged();
            StartKeyboardTestCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsSerialMode
    {
        get => SelectedMode == ConnectionMode.Serial;
        set
        {
            if (value)
            {
                SelectedMode = ConnectionMode.Serial;
            }
        }
    }

    public bool IsKeyboardMode
    {
        get => SelectedMode == ConnectionMode.Keyboard;
        set
        {
            if (value)
            {
                SelectedMode = ConnectionMode.Keyboard;
            }
        }
    }

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
                RaiseKeyboardProperties();
                RefreshCommand.RaiseCanExecuteChanged();
                ConnectCommand.RaiseCanExecuteChanged();
                StartKeyboardTestCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StateTitle => ConnectionLabels.State(State, _localizer);

    public bool IsEditingEnabled =>
        !ConnectCommand.IsRunning &&
        !StartKeyboardTestCommand.IsRunning &&
        State is not (ConnectionState.Connecting or ConnectionState.Connected or ConnectionState.Disconnecting);

    public bool CanChangeMode => IsEditingEnabled;

    public bool IsKeyboardCaptureActive =>
        State == ConnectionState.Connected &&
        (_connection.ActiveIdentity?.Kind == TransportKind.KeyboardCapture ||
         _connection.CurrentSnapshot?.Identity.Kind == TransportKind.KeyboardCapture);

    public bool IsKeyboardSurfaceFocused
    {
        get => _isKeyboardSurfaceFocused;
        private set
        {
            if (SetProperty(ref _isKeyboardSurfaceFocused, value))
            {
                OnPropertyChanged(nameof(KeyboardStatusTitle));
            }
        }
    }

    public bool IsKeyboardStartVisible => IsKeyboardMode && !IsKeyboardCaptureActive;

    public bool IsKeyboardStopVisible => IsKeyboardMode && IsKeyboardCaptureActive;

    public string KeyboardStatusTitle => IsKeyboardCaptureActive
        ? _localizer[IsKeyboardSurfaceFocused
            ? "Connection.Keyboard.Status.Active"
            : "Connection.Keyboard.Status.Paused"]
        : _localizer[(State == ConnectionState.Connecting || StartKeyboardTestCommand.IsRunning) && IsKeyboardMode
            ? "Connection.Keyboard.Status.Starting"
            : "Connection.Keyboard.Status.Ready"];

    public string? LastKeyboardScan
    {
        get => _lastKeyboardScan;
        private set => SetProperty(ref _lastKeyboardScan, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand ConnectCommand { get; }

    public AsyncCommand StartKeyboardTestCommand { get; }

    public AsyncCommand DisconnectCommand { get; }

    public AsyncCommand StopKeyboardTestCommand => DisconnectCommand;

    public void AcceptKeyboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        CancellationTokenSource current;
        long generation;
        lock (_keyboardDeadlineGate)
        {
            if (_connection.KeyboardInput?.AppendText(text) != true)
            {
                return;
            }

            CancelAndDisposeKeyboardDeadlineLocked();
            current = new CancellationTokenSource();
            _keyboardDeadlineCancellation = current;
            _pendingKeyboardText += text;
            generation = ++_keyboardDeadlineGeneration;
        }

        _ = CompleteKeyboardInputAfterSilenceAsync(generation, current);
    }

    public void CompleteKeyboardInput()
    {
        string? completed;
        lock (_keyboardDeadlineGate)
        {
            CancelAndDisposeKeyboardDeadlineLocked();
            completed = TryCompleteKeyboardInputLocked();
        }

        if (completed is not null)
        {
            PublishCompletedKeyboardInput(completed);
        }
    }

    public void SetKeyboardSurfaceFocused(bool focused) => IsKeyboardSurfaceFocused = focused;

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

    private async Task ConnectKeyboardAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            await _connection.ConnectKeyboardAsync(cancellationToken);
            RequestKeyboardFocus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ErrorMessage = _localizer["Error.ConnectKeyboard"];
        }
    }

    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        CancelKeyboardDeadline(discardPending: true);
        IsKeyboardSurfaceFocused = false;
        await _connection.DisconnectAsync(cancellationToken);
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs args) =>
        RunOnUi(() =>
        {
            State = args.State;
            if (args.State is not (ConnectionState.Connecting or ConnectionState.Connected or ConnectionState.Disconnecting))
            {
                CancelKeyboardDeadline(discardPending: true);
                IsKeyboardSurfaceFocused = false;
            }

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
        OnPropertyChanged(nameof(KeyboardStatusTitle));
    }

    private void OnCommandStateChanged()
    {
        OnPropertyChanged(nameof(IsEditingEnabled));
        OnPropertyChanged(nameof(CanChangeMode));
        OnPropertyChanged(nameof(KeyboardStatusTitle));
        RefreshCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        StartKeyboardTestCommand.RaiseCanExecuteChanged();
    }

    private bool CanStartSerial() =>
        IsSerialMode &&
        SelectedDevice is not null &&
        IsEditingEnabled &&
        _connection.ActiveIdentity is null;

    private bool CanStartKeyboard() =>
        IsKeyboardMode &&
        IsEditingEnabled &&
        _connection.ActiveIdentity is null;

    private void RaiseKeyboardProperties()
    {
        OnPropertyChanged(nameof(CanChangeMode));
        OnPropertyChanged(nameof(IsKeyboardCaptureActive));
        OnPropertyChanged(nameof(IsKeyboardStartVisible));
        OnPropertyChanged(nameof(IsKeyboardStopVisible));
        OnPropertyChanged(nameof(KeyboardStatusTitle));
    }

    private void CancelKeyboardDeadline(bool discardPending = false)
    {
        lock (_keyboardDeadlineGate)
        {
            CancelAndDisposeKeyboardDeadlineLocked();
            if (discardPending)
            {
                _pendingKeyboardText = string.Empty;
            }
        }
    }

    private async Task CompleteKeyboardInputAfterSilenceAsync(
        long generation,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await _delay(KeyboardSilenceTimeout, cancellationToken).ConfigureAwait(false);

            string? completed;
            lock (_keyboardDeadlineGate)
            {
                if (generation != _keyboardDeadlineGeneration ||
                    !ReferenceEquals(_keyboardDeadlineCancellation, cancellation) ||
                    cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _keyboardDeadlineCancellation = null;
                _keyboardDeadlineGeneration++;
                completed = TryCompleteKeyboardInputLocked();
                cancellation.Dispose();
            }

            if (completed is not null)
            {
                PublishCompletedKeyboardInput(completed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer fragment or explicit completion superseded this deadline.
        }
    }

    private void RequestKeyboardFocus() =>
        RunOnUi(() => KeyboardFocusRequested?.Invoke(this, EventArgs.Empty));

    private string? TryCompleteKeyboardInputLocked()
    {
        if (_connection.KeyboardInput?.CompleteInput() != true)
        {
            return null;
        }

        var completed = _pendingKeyboardText;
        _pendingKeyboardText = string.Empty;
        return completed;
    }

    private void PublishCompletedKeyboardInput(string completed) =>
        RunOnUi(() => LastKeyboardScan = completed);

    private void CancelAndDisposeKeyboardDeadlineLocked()
    {
        var cancellation = _keyboardDeadlineCancellation;
        _keyboardDeadlineCancellation = null;
        _keyboardDeadlineGeneration++;
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        finally
        {
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
