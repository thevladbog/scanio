using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Services;
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
        var viewModel = new ConnectionViewModel(devices, connection);

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
        var viewModel = new ConnectionViewModel(new FakeDeviceEnumerator(Device("COM7")), connection);
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
        var viewModel = new ConnectionViewModel(new FakeDeviceEnumerator(), connection);

        await viewModel.DisconnectCommand.ExecuteAsync();

        Assert.AreEqual(1, connection.DisconnectCount);
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
}
