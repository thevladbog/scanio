using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class ConnectionViewModelTests
{
    [TestMethod]
    public async Task Refresh_OnlyEnumeratesAndNeverConnects()
    {
        var devices = new FakeDeviceEnumerator(Device("COM7"));
        var connection = new FakeConnectionService();
        var viewModel = CreateViewModel(devices, connection);

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(1, devices.Count);
        Assert.AreEqual(0, connection.ConnectCount);
        Assert.HasCount(1, viewModel.Devices);
        Assert.AreEqual("COM7", viewModel.SelectedDevice?.PortName);
    }

    [TestMethod]
    public async Task Connect_IsExplicitSingleShotAndDisabledWhileRunning()
    {
        var connection = new FakeConnectionService { BlockConnect = true };
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(Device("COM7")), connection);
        await viewModel.RefreshCommand.ExecuteAsync();

        var connecting = viewModel.ConnectCommand.ExecuteAsync();
        await connection.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(viewModel.ConnectCommand.CanExecute(null));
        Assert.IsFalse(viewModel.IsEditingEnabled);
        connection.AllowConnect();
        await connecting;

        Assert.AreEqual(1, connection.ConnectCount);
        Assert.AreEqual("COM7", connection.LastDevice?.PortName);
        Assert.AreEqual(9_600, connection.LastOptions?.BaudRate);
    }

    [TestMethod]
    public async Task Disconnect_InvokesTheConnectionService()
    {
        var connection = new FakeConnectionService { State = ConnectionState.Connected };
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection);

        await viewModel.DisconnectCommand.ExecuteAsync();

        Assert.AreEqual(1, connection.DisconnectCount);
    }

    [TestMethod]
    public void ConnectionSnapshot_ExposesEndpointAndFullFriendlyNameSeparately()
    {
        var identity = new TransportIdentity(
            TransportKind.Serial,
            "serial:05f9:2214:abc",
            "Datalogic Barcode Scanner with a deliberately long diagnostic name",
            "USB\\VID_05F9&PID_2214",
            "COM18");
        var options = SerialConnectionOptions.Default("COM18");
        var connection = new FakeConnectionService
        {
            State = ConnectionState.Connected,
            CurrentSnapshot = new ConnectionPresentationSnapshot(identity, ConnectionState.Connected, options)
        };

        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection);

        Assert.AreEqual("COM18", viewModel.ConnectionSnapshot?.Endpoint);
        Assert.AreEqual(identity.DisplayName, viewModel.ConnectionSnapshot?.FriendlyName);
        Assert.AreEqual("Подключено", viewModel.ConnectionSnapshot?.StateLabel);
        Assert.AreEqual("9600 · 8 · Нет · 1", viewModel.ConnectionSnapshot?.ParametersLabel);
        Assert.AreEqual("COM18 · Подключено", viewModel.HeaderConnectionLabel);
        Assert.AreEqual("Нет", viewModel.ParityOptions.Single(option => option.Value == SerialParity.None).Label);

        var disconnected = CreateViewModel(new FakeDeviceEnumerator(), new FakeConnectionService());
        Assert.AreEqual("Нет подключения", disconnected.HeaderConnectionLabel);
    }

    private static ConnectionViewModel CreateViewModel(
        ISerialDeviceEnumerator enumerator,
        IConnectionService connection)
    {
        var settings = new TestSettingsService();
        return new ConnectionViewModel(enumerator, connection, new UiLocalizer(settings));
    }

    private static SerialDeviceInfo Device(string port) =>
        new(port, $"Scanner ({port})", "Vendor", 0x05F9, 0x2214, "SERIAL", "USB\\VID_05F9&PID_2214", "serial:05F9:2214:SERIAL");

    private sealed class FakeDeviceEnumerator(params SerialDeviceInfo[] devices) : ISerialDeviceEnumerator
    {
        public int Count { get; private set; }

        public Task<IReadOnlyList<SerialDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult<IReadOnlyList<SerialDeviceInfo>>(devices);
        }
    }

    private sealed class FakeConnectionService : IConnectionService
    {
        private readonly TaskCompletionSource _allowConnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }
        public int ConnectCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public bool BlockConnect { get; init; }
        public SerialDeviceInfo? LastDevice { get; private set; }
        public SerialConnectionOptions? LastOptions { get; private set; }
        public ConnectionState State { get; init; } = ConnectionState.Detected;
        public TransportIdentity? ActiveIdentity => null;
        public ConnectionPresentationSnapshot? CurrentSnapshot { get; init; }
        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConnectAsync(SerialDeviceInfo device, SerialConnectionOptions options, CancellationToken cancellationToken)
        {
            ConnectCount++;
            LastDevice = device;
            LastOptions = options;
            ConnectStarted.TrySetResult();
            if (BlockConnect)
            {
                await _allowConnect.Task.WaitAsync(cancellationToken);
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCount++;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void AllowConnect() => _allowConnect.TrySetResult();
    }

    private sealed class TestSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler? Changed;

        public void Update(Func<AppSettings, AppSettings> update)
        {
            Current = update(Current);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
