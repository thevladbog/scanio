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
[DoNotParallelize]
public sealed class ConnectionViewModelTests
{
    [TestMethod]
    public async Task KeyboardMode_HidesSerialEditingAndExposesManualStart()
    {
        var connection = new FakeConnectionService();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(Device("COM7")), connection);
        await viewModel.RefreshCommand.ExecuteAsync();

        viewModel.SelectedMode = ConnectionMode.Keyboard;

        Assert.IsFalse(viewModel.IsSerialMode);
        Assert.IsTrue(viewModel.IsKeyboardMode);
        Assert.IsFalse(viewModel.ConnectCommand.CanExecute(null));
        Assert.IsTrue(viewModel.StartKeyboardTestCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task StartKeyboardTest_ConnectsRequestsFocusAndChangesStatusCopy()
    {
        var connection = new FakeConnectionService();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        var initialStatus = viewModel.KeyboardStatusTitle;
        var focusRequests = 0;
        viewModel.KeyboardFocusRequested += (_, _) => focusRequests++;

        await viewModel.StartKeyboardTestCommand.ExecuteAsync();

        Assert.AreEqual(1, connection.ConnectKeyboardCount);
        Assert.AreEqual(1, focusRequests);
        Assert.AreNotEqual(initialStatus, viewModel.KeyboardStatusTitle);
        Assert.IsTrue(viewModel.IsKeyboardCaptureActive);
    }

    [TestMethod]
    public void AcceptedKeyboardFragments_ResetTheExactHundredMillisecondDeadline()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delays = new ControlledDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delays.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;

        viewModel.AcceptKeyboardText("ABC");
        viewModel.AcceptKeyboardText("123");

        Assert.HasCount(2, delays.Requests);
        Assert.IsTrue(delays.Requests[0].Token.IsCancellationRequested);
        Assert.IsFalse(delays.Requests[1].Token.IsCancellationRequested);
        Assert.IsTrue(delays.Requests.All(request => request.Delay == TimeSpan.FromMilliseconds(100)));
        CollectionAssert.AreEqual(new[] { "ABC", "123" }, connection.KeyboardCaptureInput.AcceptedFragments);
    }

    [TestMethod]
    public void ReplacingKeyboardDeadline_DoesNotDisposeSourceInsideCancellationBoundary()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delay = new CancellationBoundaryDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delay.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.AcceptKeyboardText("A");

        viewModel.AcceptKeyboardText("B");

        Assert.AreEqual(1, delay.CompletedBoundaryProbes);
    }

    [TestMethod]
    public void ExplicitKeyboardCompletion_DoesNotDisposeSourceInsideCancellationBoundary()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delay = new CancellationBoundaryDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delay.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.AcceptKeyboardText("ABC123");

        viewModel.CompleteKeyboardInput();

        Assert.AreEqual(1, delay.CompletedBoundaryProbes);
        Assert.AreEqual(1, connection.KeyboardCaptureInput.CompletedScans);
    }

    [TestMethod]
    public async Task DisconnectKeyboard_DoesNotDisposeSourceInsideCancellationBoundary()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delay = new CancellationBoundaryDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delay.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.AcceptKeyboardText("ABC123");

        await viewModel.StopKeyboardTestCommand.ExecuteAsync();

        Assert.AreEqual(1, delay.CompletedBoundaryProbes);
        Assert.AreEqual(1, connection.DisconnectCount);
    }

    [TestMethod]
    public async Task OnlyNewestKeyboardDeadline_CompletesPendingInput()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delays = new ControlledDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delays.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;

        viewModel.AcceptKeyboardText("ABC");
        viewModel.AcceptKeyboardText("123");
        delays.Release(0);
        await Task.Yield();

        Assert.AreEqual(0, connection.KeyboardCaptureInput.CompletedScans);

        delays.Release(1);
        await connection.KeyboardCaptureInput.CompletionObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, connection.KeyboardCaptureInput.CompletedScans);
        Assert.AreEqual("ABC123", connection.KeyboardCaptureInput.LastCompletedScan);
    }

    [TestMethod]
    public void ExplicitKeyboardCompletion_IsImmediateCancelsDeadlineAndIgnoresEmptyInput()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delays = new ControlledDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delays.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.AcceptKeyboardText("ABC123");

        viewModel.CompleteKeyboardInput();

        Assert.IsTrue(delays.Requests.Single().Token.IsCancellationRequested);
        Assert.AreEqual(1, connection.KeyboardCaptureInput.CompletedScans);
        Assert.AreEqual("ABC123", connection.KeyboardCaptureInput.LastCompletedScan);

        viewModel.CompleteKeyboardInput();

        Assert.AreEqual(1, connection.KeyboardCaptureInput.CompletedScans);
    }

    [TestMethod]
    public void ExplicitKeyboardCompletion_DoesNotRequestFocusAgain()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.SetKeyboardSurfaceFocused(true);
        var focusRequests = 0;
        viewModel.KeyboardFocusRequested += (_, _) => focusRequests++;
        viewModel.AcceptKeyboardText("ABC123");

        viewModel.CompleteKeyboardInput();

        Assert.AreEqual(1, connection.KeyboardCaptureInput.CompletedScans);
        Assert.AreEqual(0, focusRequests);
    }

    [TestMethod]
    public void FocusLostThenNewestSilenceCompletion_UpdatesScanWithoutRequestingFocus()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delays = new ControlledDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delays.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.SetKeyboardSurfaceFocused(false);
        var focusRequests = 0;
        viewModel.KeyboardFocusRequested += (_, _) => focusRequests++;
        viewModel.AcceptKeyboardText("ABC123");

        delays.Release(0);

        Assert.AreEqual(1, connection.KeyboardCaptureInput.CompletedScans);
        Assert.AreEqual("ABC123", viewModel.LastKeyboardScan);
        Assert.AreEqual(0, focusRequests);
        Assert.IsFalse(viewModel.IsKeyboardSurfaceFocused);
    }

    [TestMethod]
    public async Task NaturalKeyboardDeadline_ObservesCompletionFailureDisposesSourceAndPublishesLocalizedErrorWithoutReclaimingFocus()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        connection.KeyboardCaptureInput.CompletionException = new InvalidOperationException("Completion failed.");
        var delays = new ControlledDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delays.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.SetKeyboardSurfaceFocused(false);
        var focusRequests = 0;
        viewModel.KeyboardFocusRequested += (_, _) => focusRequests++;
        viewModel.AcceptKeyboardText("ABC123");
        var waitHandle = delays.Requests.Single().Token.WaitHandle;

        delays.Release(0);
        await Task.Yield();

        var sourceWasDisposed = waitHandle.SafeWaitHandle.IsClosed;
        waitHandle.Dispose();
        Assert.IsTrue(sourceWasDisposed, "The winning silence deadline must dispose its CTS even when completion throws.");
        Assert.AreEqual("Не удалось выполнить операцию.", viewModel.ErrorMessage);
        Assert.IsNull(viewModel.LastKeyboardScan);
        Assert.AreEqual(0, focusRequests);
        Assert.IsFalse(viewModel.IsKeyboardSurfaceFocused);
    }

    [TestMethod]
    public async Task StopThenStaleSilenceCompletion_EmitsNoScanAndNoFocusRequest()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var delays = new ControlledDelay();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection, delays.WaitAsync);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        var focusRequests = 0;
        viewModel.KeyboardFocusRequested += (_, _) => focusRequests++;
        viewModel.AcceptKeyboardText("STALE");

        await viewModel.StopKeyboardTestCommand.ExecuteAsync();
        delays.Release(0);

        Assert.AreEqual(0, connection.KeyboardCaptureInput.CompletedScans);
        Assert.IsNull(viewModel.LastKeyboardScan);
        Assert.AreEqual(0, focusRequests);
    }

    [TestMethod]
    public void CompletedKeyboardInput_IsExposedAsImmediateSurfaceConfirmation()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.AcceptKeyboardText("ABC123");

        viewModel.CompleteKeyboardInput();

        Assert.AreEqual("ABC123", viewModel.LastKeyboardScan);
    }

    [TestMethod]
    public void LosingKeyboardSurfaceFocus_ReportsPausedWithoutDisconnecting()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection);
        viewModel.SelectedMode = ConnectionMode.Keyboard;
        viewModel.SetKeyboardSurfaceFocused(true);
        var activeStatus = viewModel.KeyboardStatusTitle;

        viewModel.SetKeyboardSurfaceFocused(false);

        Assert.IsFalse(viewModel.IsKeyboardSurfaceFocused);
        Assert.AreNotEqual(activeStatus, viewModel.KeyboardStatusTitle);
        Assert.AreEqual(0, connection.DisconnectCount);
        Assert.IsTrue(viewModel.IsKeyboardCaptureActive);
    }

    [TestMethod]
    public async Task StopKeyboardTest_UsesExistingDisconnectService()
    {
        var connection = FakeConnectionService.ConnectedKeyboard();
        var viewModel = CreateViewModel(new FakeDeviceEnumerator(), connection);
        viewModel.SelectedMode = ConnectionMode.Keyboard;

        await viewModel.StopKeyboardTestCommand.ExecuteAsync();

        Assert.AreEqual(1, connection.DisconnectCount);
    }

    [TestMethod]
    public async Task SerialAndKeyboardStarts_AreDisabledByTheOtherActiveTransport()
    {
        var keyboardConnection = FakeConnectionService.ConnectedKeyboard();
        var serialViewModel = CreateViewModel(
            new FakeDeviceEnumerator(Device("COM7")),
            keyboardConnection);
        await serialViewModel.RefreshCommand.ExecuteAsync();
        serialViewModel.SelectedMode = ConnectionMode.Serial;

        Assert.IsFalse(serialViewModel.ConnectCommand.CanExecute(null));

        var serialConnection = FakeConnectionService.ConnectedSerial();
        var keyboardViewModel = CreateViewModel(new FakeDeviceEnumerator(), serialConnection);
        keyboardViewModel.SelectedMode = ConnectionMode.Keyboard;

        Assert.IsFalse(keyboardViewModel.StartKeyboardTestCommand.CanExecute(null));
    }

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
    public void ConnectionSnapshot_LocalizesKeyboardIdentityWithoutChangingTechnicalIdentity()
    {
        var identity = new TransportIdentity(
            TransportKind.KeyboardCapture,
            "keyboard-capture:focused-window",
            "Keyboard scanner",
            endpoint: "Keyboard");
        var snapshot = ConnectionSnapshotViewModel.From(
            new ConnectionPresentationSnapshot(identity, ConnectionState.Connected, Options: null),
            new UiLocalizer(new TestSettingsService()));

        Assert.AreEqual("Клавиатура", snapshot?.Endpoint);
        Assert.AreEqual("Сканер-клавиатура", snapshot?.FriendlyName);
        Assert.AreEqual("Реконструированный ввод Windows · UTF-8", snapshot?.ParametersLabel);
        Assert.AreEqual("keyboard-capture:focused-window", identity.StableId);
        Assert.AreEqual("Keyboard scanner", identity.DisplayName);
        Assert.AreEqual("Keyboard", identity.Endpoint);
    }

    [TestMethod]
    public void ConnectionSnapshot_DoesNotDescribeOtherOptionlessTransportsAsKeyboardInput()
    {
        var identity = new TransportIdentity(
            TransportKind.HidPos,
            "hid-pos:test",
            "HID POS scanner",
            endpoint: "HID POS");

        var snapshot = ConnectionSnapshotViewModel.From(
            new ConnectionPresentationSnapshot(identity, ConnectionState.Connected, Options: null),
            new UiLocalizer(new TestSettingsService()));

        Assert.AreEqual(string.Empty, snapshot?.ParametersLabel);
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
    public async Task ConnectionService_CleanupFailureBeforeTerminalRestoresPriorPresentation()
    {
        var coordinator = new ConnectionCoordinator(new BlockingPipeline());
        var service = new ConnectionService(
            coordinator,
            (identity, _) => new CleanupFailingTransport(identity));
        var observedStates = new List<ConnectionState>();
        service.StateChanged += (_, args) => observedStates.Add(args.State);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.ConnectAsync(
                Device("COM7"),
                SerialConnectionOptions.Default("COM7"),
                CancellationToken.None));

        Assert.AreEqual("Cleanup failed before terminal publication.", exception.Message);
        Assert.AreEqual(ConnectionState.Detected, service.State);
        Assert.IsNull(service.CurrentSnapshot);
        Assert.IsNull(service.KeyboardInput);
        Assert.IsNull(service.ActiveIdentity);
        CollectionAssert.AreEqual(
            new[] { ConnectionState.Connecting, ConnectionState.Detected },
            observedStates);
    }

    [TestMethod]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task ConnectionService_AutomaticOldKeyboardTerminalCannotClearQueuedNewGeneration()
    {
        var pipeline = new TwoGenerationPipeline();
        var coordinator = new ConnectionCoordinator(pipeline);
        var oldTerminalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseOldTerminal = new ManualResetEventSlim();
        coordinator.StatusChanged += (_, status) =>
        {
            if (status.State == ConnectionState.TransportError && oldTerminalEntered.TrySetResult())
            {
                releaseOldTerminal.Wait(TimeSpan.FromSeconds(4));
            }
        };
        var service = new ConnectionService(coordinator);
        await service.ConnectKeyboardAsync(CancellationToken.None);
        var oldInput = service.KeyboardInput;
        pipeline.CompleteFirstGeneration();
        await oldTerminalEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var reconnecting = service.ConnectKeyboardAsync(CancellationToken.None);

        try
        {
            Assert.IsFalse(reconnecting.IsCompleted);

            releaseOldTerminal.Set();
            await reconnecting.WaitAsync(TimeSpan.FromSeconds(1));
            var newInput = service.KeyboardInput;
            var newSnapshot = service.CurrentSnapshot;

            Assert.IsNotNull(newInput);
            Assert.AreNotSame(oldInput, newInput);
            Assert.IsTrue(newInput.AppendText("new-generation"));
            Assert.IsNotNull(newSnapshot);
            Assert.AreEqual(TransportKind.KeyboardCapture, newSnapshot.Identity.Kind);
            Assert.AreEqual(ConnectionState.Connected, newSnapshot.State);
        }
        finally
        {
            releaseOldTerminal.Set();
            await service.DisconnectAsync(CancellationToken.None);
        }
    }

    private static ConnectionViewModel CreateViewModel(
        ISerialDeviceEnumerator enumerator,
        IConnectionService connection,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var settings = new TestSettingsService();
        var localizer = new UiLocalizer(settings);
        return delay is null
            ? new ConnectionViewModel(enumerator, connection, localizer)
            : new ConnectionViewModel(enumerator, connection, localizer, delay);
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
        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
        public int ConnectCount { get; private set; }
        public int ConnectKeyboardCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public bool BlockConnect { get; init; }
        public SerialDeviceInfo? LastDevice { get; private set; }
        public SerialConnectionOptions? LastOptions { get; private set; }
        public ConnectionState State { get; set; } = ConnectionState.Detected;
        public TransportIdentity? ActiveIdentity { get; set; }
        public ConnectionPresentationSnapshot? CurrentSnapshot { get; set; }
        public IKeyboardCaptureInput? KeyboardInput =>
            State == ConnectionState.Connected && ActiveIdentity?.Kind == TransportKind.KeyboardCapture
                ? KeyboardCaptureInput
                : null;
        public FakeKeyboardCaptureInput KeyboardCaptureInput { get; } = new();
        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static FakeConnectionService ConnectedKeyboard()
        {
            var identity = KeyboardIdentity();
            var connection = new FakeConnectionService
            {
                State = ConnectionState.Connected,
                ActiveIdentity = identity,
                CurrentSnapshot = new ConnectionPresentationSnapshot(identity, ConnectionState.Connected, Options: null)
            };
            connection.KeyboardCaptureInput.IsConnected = true;
            return connection;
        }

        public static FakeConnectionService ConnectedSerial()
        {
            var identity = new TransportIdentity(TransportKind.Serial, "serial:test", "Serial scanner", endpoint: "COM7");
            return new FakeConnectionService
            {
                State = ConnectionState.Connected,
                ActiveIdentity = identity,
                CurrentSnapshot = new ConnectionPresentationSnapshot(
                    identity,
                    ConnectionState.Connected,
                    SerialConnectionOptions.Default("COM7"))
            };
        }

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
            var priorIdentity = ActiveIdentity;
            State = ConnectionState.Disconnected;
            ActiveIdentity = null;
            KeyboardCaptureInput.IsConnected = false;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(State, priorIdentity));
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ConnectKeyboardAsync(CancellationToken cancellationToken)
        {
            ConnectKeyboardCount++;
            var identity = KeyboardIdentity();
            State = ConnectionState.Connected;
            ActiveIdentity = identity;
            CurrentSnapshot = new ConnectionPresentationSnapshot(identity, State, Options: null);
            KeyboardCaptureInput.IsConnected = true;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(State, identity));
            return Task.CompletedTask;
        }

        private static TransportIdentity KeyboardIdentity() => new(
            TransportKind.KeyboardCapture,
            "keyboard-capture:focused-window",
            "Keyboard scanner",
            endpoint: "Keyboard");

        public void AllowConnect() => _allowConnect.TrySetResult();
    }

    private sealed class FakeKeyboardCaptureInput : IKeyboardCaptureInput
    {
        private readonly System.Text.StringBuilder _pending = new();

        public List<string> AcceptedFragments { get; } = [];
        public int CompletedScans { get; private set; }
        public string? LastCompletedScan { get; private set; }
        public Exception? CompletionException { get; set; }
        public bool IsConnected { get; set; }
        public bool HasPendingInput => IsConnected && _pending.Length > 0;
        public TaskCompletionSource CompletionObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool AppendText(string text)
        {
            if (!IsConnected || string.IsNullOrEmpty(text))
            {
                return false;
            }

            AcceptedFragments.Add(text);
            _pending.Append(text);
            return true;
        }

        public bool CompleteInput()
        {
            if (CompletionException is not null)
            {
                throw CompletionException;
            }

            if (!HasPendingInput)
            {
                return false;
            }

            LastCompletedScan = _pending.ToString();
            _pending.Clear();
            CompletedScans++;
            CompletionObserved.TrySetResult();
            return true;
        }
    }

    private sealed class ControlledDelay
    {
        public List<Request> Requests { get; } = [];

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var request = new Request(delay, cancellationToken);
            Requests.Add(request);
            return request.Completion.Task;
        }

        public void Release(int index) => Requests[index].Completion.TrySetResult();

        public sealed record Request(TimeSpan Delay, CancellationToken Token)
        {
            public TaskCompletionSource Completion { get; } =
                new();
        }
    }

    private sealed class CancellationBoundaryDelay
    {
        public int CompletedBoundaryProbes { get; private set; }

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource();
            cancellationToken.Register(() =>
            {
                completion.TrySetResult();
                _ = cancellationToken.WaitHandle.SafeWaitHandle.IsClosed;
                CompletedBoundaryProbes++;
            });
            return completion.Task;
        }
    }

    private sealed class BlockingPipeline : IScanProcessingPipeline
    {
        public async Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class TwoGenerationPipeline : IScanProcessingPipeline
    {
        private readonly TaskCompletionSource _firstGenerationCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _generation;

        public Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _generation) == 1
                ? _firstGenerationCompletion.Task
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public void CompleteFirstGeneration() => _firstGenerationCompletion.TrySetResult();
    }

    private sealed class CleanupFailingTransport(TransportIdentity identity) : IScannerTransport
    {
        public TransportIdentity Identity { get; } = identity;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            State = ConnectionState.TransportError;
            throw new IOException("Open failed.");
        }

        public async IAsyncEnumerable<Scanio.Domain.Capture.RawChunk> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("Cleanup failed before terminal publication."));
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
