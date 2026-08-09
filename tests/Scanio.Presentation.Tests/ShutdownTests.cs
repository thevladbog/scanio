using System.Runtime.CompilerServices;
using Scanio.Application.Connection;
using Scanio.Application.Monitor;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Services;
using Scanio.Transports;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class ShutdownTests
{
    [TestMethod]
    public async Task Shutdown_DoesNotReturnUntilBlockedTransportIsClosedAndDisposed()
    {
        var pipeline = new BlockingPipeline();
        var transport = new BlockingCloseTransport();
        var coordinator = new ConnectionCoordinator(pipeline);
        var service = new ConnectionService(coordinator, (_, _) => transport);
        var device = new SerialDeviceInfo("COM7", "Scanner", null, null, null, null, null, null);

        await service.ConnectAsync(device, SerialConnectionOptions.Default("COM7"), CancellationToken.None);
        var shutdown = service.ShutdownAsync(CancellationToken.None);
        await transport.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(shutdown.IsCompleted);
        transport.AllowClose();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, transport.CloseCount);
        Assert.AreEqual(1, transport.DisposeCount);
    }

    private sealed class BlockingPipeline : IScanProcessingPipeline
    {
        public async Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class BlockingCloseTransport : IScannerTransport
    {
        private readonly TaskCompletionSource _allowClose = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TransportIdentity Identity { get; } = new(TransportKind.Serial, "port-session:COM7", "Scanner");
        public ConnectionState State { get; private set; } = ConnectionState.Detected;
        public int CloseCount { get; private set; }
        public int DisposeCount { get; private set; }
        public TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            State = ConnectionState.Connected;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<RawChunk> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public async ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            CloseStarted.TrySetResult();
            await _allowClose.Task.WaitAsync(cancellationToken);
            State = ConnectionState.Disconnected;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public void AllowClose() => _allowClose.TrySetResult();
    }
}
