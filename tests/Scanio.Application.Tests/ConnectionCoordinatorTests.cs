using System.Runtime.CompilerServices;
using Scanio.Application.Connection;
using Scanio.Application.Monitor;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Transports;

namespace Scanio.Application.Tests;

[TestClass]
public sealed class ConnectionCoordinatorTests
{
    [TestMethod]
    public void ReportDetected_IsPassiveAndNeverOpensTheTransport()
    {
        var pipeline = new ControllablePipeline();
        var transport = new FakeTransport("COM7");
        var coordinator = new ConnectionCoordinator(pipeline);

        coordinator.ReportDetected(transport.Identity);

        Assert.AreEqual(0, transport.OpenCount);
        Assert.AreEqual(ConnectionState.Detected, coordinator.Events.Single().State);
        Assert.IsNull(coordinator.ActiveIdentity);
    }

    [TestMethod]
    public async Task ConnectAsync_RequiresAnExplicitDisconnectBeforeSwitchingTransport()
    {
        var pipeline = new ControllablePipeline();
        var first = new FakeTransport("COM7");
        var second = new FakeTransport("COM8");
        var coordinator = new ConnectionCoordinator(pipeline);

        await coordinator.ConnectAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.ConnectAsync(second, CancellationToken.None));
        Assert.AreEqual(0, second.OpenCount);

        await coordinator.DisconnectAsync(CancellationToken.None);
        await coordinator.ConnectAsync(second, CancellationToken.None);

        Assert.AreEqual(1, first.CloseCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.OpenCount);

        await coordinator.ShutdownAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task PhysicalRemoval_DisposesOnceAndNeverReconnects()
    {
        var pipeline = new ControllablePipeline();
        var transport = new FakeTransport("COM7");
        var coordinator = new ConnectionCoordinator(pipeline);
        var removalPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StatusChanged += (_, status) =>
        {
            if (status.State == ConnectionState.DeviceRemoved)
            {
                removalPublished.TrySetResult();
            }
        };

        await coordinator.ConnectAsync(transport, CancellationToken.None);
        transport.State = ConnectionState.DeviceRemoved;
        pipeline.Complete();
        await removalPublished.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, transport.OpenCount);
        Assert.AreEqual(1, transport.CloseCount);
        Assert.AreEqual(1, transport.DisposeCount);
        Assert.IsNull(coordinator.ActiveIdentity);
        CollectionAssert.DoesNotContain(
            coordinator.Events.SkipWhile(status => status.State != ConnectionState.Connected).Skip(1)
                .Select(status => status.State).ToArray(),
            ConnectionState.Connecting);

        await coordinator.ShutdownAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ShutdownAsync_DoesNotReturnUntilTheTransportHasClosed()
    {
        var pipeline = new ControllablePipeline();
        var transport = new FakeTransport("COM7") { BlockClose = true };
        var coordinator = new ConnectionCoordinator(pipeline);
        await coordinator.ConnectAsync(transport, CancellationToken.None);

        var shutdown = coordinator.ShutdownAsync(CancellationToken.None);
        await transport.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(shutdown.IsCompleted);
        transport.AllowClose();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, transport.CloseCount);
        Assert.AreEqual(1, transport.DisposeCount);
        Assert.AreEqual(ConnectionState.Disconnected, coordinator.Events[^1].State);
    }

    [TestMethod]
    [DataRow(ConnectionState.Busy)]
    [DataRow(ConnectionState.AccessDenied)]
    [DataRow(ConnectionState.TransportError)]
    public async Task ConnectAsync_PublishesTheTransportFailureState(ConnectionState failureState)
    {
        var transport = new FakeTransport("COM7") { OpenFailureState = failureState };
        var coordinator = new ConnectionCoordinator(new ControllablePipeline());

        await Assert.ThrowsAsync<IOException>(async () =>
            await coordinator.ConnectAsync(transport, CancellationToken.None));

        Assert.AreEqual(failureState, coordinator.Events[^1].State);
        Assert.IsNull(coordinator.ActiveIdentity);
        Assert.AreEqual(1, transport.DisposeCount);
    }

    private sealed class ControllablePipeline : IScanProcessingPipeline
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken) =>
            _completion.Task.WaitAsync(cancellationToken);

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class FakeTransport(string stableId) : IScannerTransport
    {
        private readonly TaskCompletionSource _closePermission =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TransportIdentity Identity { get; } =
            new(TransportKind.Serial, stableId, stableId);

        public ConnectionState State { get; set; } = ConnectionState.Detected;

        public ConnectionState? OpenFailureState { get; init; }

        public bool BlockClose { get; init; }

        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public TaskCompletionSource CloseStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            if (OpenFailureState is { } failure)
            {
                State = failure;
                throw new IOException("Expected open failure.");
            }

            State = ConnectionState.Connected;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<RawChunk> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public async ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            CloseStarted.TrySetResult();
            if (BlockClose)
            {
                await _closePermission.Task.WaitAsync(cancellationToken);
            }

            State = ConnectionState.Disconnected;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public void AllowClose() => _closePermission.TrySetResult();
    }
}
