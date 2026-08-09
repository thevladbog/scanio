using Scanio.Application.Connection;
using Scanio.Application.Monitor;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;
using Scanio.Transports.Serial;
using Scanio.Transports;
using Scanio.Transports.Keyboard;

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

    [TestMethod]
    public async Task ConnectionService_KeepsSerialAndKeyboardSnapshotsDistinct()
    {
        var coordinator = new ConnectionCoordinator(new BlockingPipeline());
        var serialTransport = new FakeScannerTransport();
        var service = new ConnectionService(coordinator, (_, _) => serialTransport);

        await service.ConnectAsync(
            Device("COM7"),
            SerialConnectionOptions.Default("COM7"),
            CancellationToken.None);

        Assert.AreEqual(TransportKind.Serial, service.CurrentSnapshot?.Identity.Kind);
        Assert.AreEqual("COM7", service.CurrentSnapshot?.Endpoint);
        Assert.IsNotNull(service.CurrentSnapshot?.Options);
        Assert.IsNull(service.KeyboardInput);
        await service.DisconnectAsync(CancellationToken.None);

        await service.ConnectKeyboardAsync(CancellationToken.None);

        Assert.AreEqual(TransportKind.KeyboardCapture, service.CurrentSnapshot?.Identity.Kind);
        Assert.AreEqual("keyboard-capture:focused-window", service.CurrentSnapshot?.Identity.StableId);
        Assert.AreEqual("Keyboard scanner", service.CurrentSnapshot?.Identity.DisplayName);
        Assert.AreEqual("Keyboard", service.CurrentSnapshot?.Endpoint);
        Assert.IsNull(service.CurrentSnapshot?.Options);
        Assert.IsNotNull(service.KeyboardInput);
        var keyboardSnapshot = ConnectionSnapshotViewModel.From(
            service.CurrentSnapshot,
            new UiLocalizer(new TestSettingsService()));
        Assert.AreEqual("Реконструированный ввод Windows · UTF-8", keyboardSnapshot?.ParametersLabel);
        await service.DisconnectAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ConnectionService_ExposesKeyboardInputOnlyWhileKeyboardTransportIsActive()
    {
        var coordinator = new ConnectionCoordinator(new BlockingPipeline());
        var service = new ConnectionService(coordinator);

        Assert.IsNull(service.KeyboardInput);

        await service.ConnectKeyboardAsync(CancellationToken.None);
        var activeInput = service.KeyboardInput;

        Assert.IsNotNull(activeInput);
        Assert.IsTrue(activeInput.AppendText("scan"));

        await service.DisconnectAsync(CancellationToken.None);

        Assert.IsNull(service.KeyboardInput);
        Assert.IsFalse(activeInput.AppendText("after-disconnect"));
    }

    [TestMethod]
    public async Task ConnectionService_RejectedKeyboardRetryPreservesActiveKeyboardOwnership()
    {
        var coordinator = new ConnectionCoordinator(new BlockingPipeline());
        var service = new ConnectionService(coordinator);
        await service.ConnectKeyboardAsync(CancellationToken.None);
        var activeInput = service.KeyboardInput;
        var activeSnapshot = service.CurrentSnapshot;

        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await service.ConnectKeyboardAsync(CancellationToken.None));

            Assert.AreSame(activeInput, service.KeyboardInput);
            Assert.AreSame(activeSnapshot, service.CurrentSnapshot);
            Assert.AreEqual(ConnectionState.Connected, service.CurrentSnapshot?.State);
        }
        finally
        {
            await service.DisconnectAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task ConnectionService_RejectedSerialAttemptPreservesActiveKeyboardOwnership()
    {
        var coordinator = new ConnectionCoordinator(new BlockingPipeline());
        var service = new ConnectionService(coordinator, (_, _) => new FakeScannerTransport());
        await service.ConnectKeyboardAsync(CancellationToken.None);
        var activeInput = service.KeyboardInput;
        var activeSnapshot = service.CurrentSnapshot;

        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await service.ConnectAsync(
                    Device("COM7"),
                    SerialConnectionOptions.Default("COM7"),
                    CancellationToken.None));

            Assert.AreSame(activeInput, service.KeyboardInput);
            Assert.AreSame(activeSnapshot, service.CurrentSnapshot);
            Assert.AreEqual(TransportKind.KeyboardCapture, service.CurrentSnapshot?.Identity.Kind);
            Assert.AreEqual(ConnectionState.Connected, service.CurrentSnapshot?.State);
        }
        finally
        {
            await service.DisconnectAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task ConnectionService_DelayedOldKeyboardTerminalCannotClearNewGeneration()
    {
        var coordinator = new ConnectionCoordinator(new BlockingPipeline());
        var delayedStatusEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseDelayedStatus = new ManualResetEventSlim();
        var delayOldDetectedStatus = false;
        coordinator.StatusChanged += (_, status) =>
        {
            if (delayOldDetectedStatus && status.State == ConnectionState.Detected)
            {
                delayedStatusEntered.TrySetResult();
                releaseDelayedStatus.Wait(TimeSpan.FromSeconds(4));
            }
        };
        var service = new ConnectionService(coordinator);
        await service.ConnectKeyboardAsync(CancellationToken.None);
        var oldIdentity = service.CurrentSnapshot!.Identity;
        await service.DisconnectAsync(CancellationToken.None);
        delayOldDetectedStatus = true;
        var delayedNotification = Task.Run(() => coordinator.ReportDetected(oldIdentity));
        await delayedStatusEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            await service.ConnectKeyboardAsync(CancellationToken.None);
            var newInput = service.KeyboardInput;
            var newSnapshot = service.CurrentSnapshot;

            releaseDelayedStatus.Set();
            await delayedNotification.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.IsNotNull(newInput);
            Assert.AreSame(newInput, service.KeyboardInput);
            Assert.AreSame(newSnapshot, service.CurrentSnapshot);
            Assert.AreEqual(ConnectionState.Connected, service.CurrentSnapshot?.State);
        }
        finally
        {
            releaseDelayedStatus.Set();
            await delayedNotification.WaitAsync(TimeSpan.FromSeconds(1));
            await service.DisconnectAsync(CancellationToken.None);
        }
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
        public IKeyboardCaptureInput? KeyboardInput => null;
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
        public Task ConnectKeyboardAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void AllowConnect() => _allowConnect.TrySetResult();
    }

    private sealed class BlockingPipeline : IScanProcessingPipeline
    {
        public async Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FakeScannerTransport : IScannerTransport
    {
        public TransportIdentity Identity { get; } = new(
            TransportKind.Serial,
            "serial:test",
            "Test serial scanner",
            endpoint: "COM7");

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = ConnectionState.Connected;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<Scanio.Domain.Capture.RawChunk> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            State = ConnectionState.Disconnected;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
