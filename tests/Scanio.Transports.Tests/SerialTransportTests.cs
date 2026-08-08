using Scanio.Domain.Transport;
using Scanio.Transports.Serial;
using System.Collections.Concurrent;

namespace Scanio.Transports.Tests;

[TestClass]
public sealed class SerialTransportTests
{
    private static readonly TransportIdentity Identity =
        new(TransportKind.Serial, "serial-1", "Test serial scanner", "USB\\VID_05F9&PID_2214");

    [TestMethod]
    public async Task Construction_DoesNotOpenThePort()
    {
        var adapter = new FakeSerialPortAdapter();

        await using var transport = CreateTransport(adapter);

        Assert.AreEqual(ConnectionState.Detected, transport.State);
        Assert.AreEqual(0, adapter.OpenCount);
    }

    [TestMethod]
    public void DefaultOptions_UseTheDocumentedSerialPreset()
    {
        var options = SerialConnectionOptions.Default("COM7");

        Assert.AreEqual("COM7", options.PortName);
        Assert.AreEqual(9_600, options.BaudRate);
        Assert.AreEqual(8, options.DataBits);
        Assert.AreEqual(SerialParity.None, options.Parity);
        Assert.AreEqual(SerialStopBits.One, options.StopBits);
        Assert.AreEqual(SerialHandshake.None, options.Handshake);
        Assert.IsFalse(options.DtrEnable);
        Assert.IsFalse(options.RtsEnable);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(9)]
    public void Options_RejectInvalidDataBits(int dataBits)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateOptions(dataBits: dataBits));
    }

    [TestMethod]
    public void Options_RejectInvalidPortBaudAndEnumValues()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateOptions(portName: ""));
        Assert.ThrowsExactly<ArgumentException>(() => CreateOptions(portName: "tty.usbserial"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateOptions(baudRate: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateOptions(parity: (SerialParity)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateOptions(stopBits: (SerialStopBits)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateOptions(handshake: (SerialHandshake)99));
    }

    [TestMethod]
    public async Task OpenAsync_OpensExactlyOnceAndRejectsASecondOpen()
    {
        var adapter = new FakeSerialPortAdapter();
        await using var transport = CreateTransport(adapter);

        await transport.OpenAsync(CancellationToken.None);

        Assert.AreEqual(ConnectionState.Connected, transport.State);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await transport.OpenAsync(CancellationToken.None));
        Assert.AreEqual(1, adapter.OpenCount);
    }

    [TestMethod]
    public async Task ReadAllAsync_EmitsOrderedBytePreservingChunks()
    {
        var adapter = new FakeSerialPortAdapter(
            new byte[] { 0x31, 0x0D },
            new byte[] { 0x32, 0x00, 0x0A });
        await using var transport = CreateTransport(adapter);
        await transport.OpenAsync(CancellationToken.None);
        await using var chunks = transport.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.IsTrue(await chunks.MoveNextAsync());
        var first = chunks.Current;
        Assert.IsTrue(await chunks.MoveNextAsync());
        var second = chunks.Current;

        Assert.AreEqual(1L, first.SequenceNumber);
        CollectionAssert.AreEqual(new byte[] { 0x31, 0x0D }, first.Bytes.ToArray());
        Assert.AreEqual(2L, second.SequenceNumber);
        CollectionAssert.AreEqual(new byte[] { 0x32, 0x00, 0x0A }, second.Bytes.ToArray());
        Assert.AreEqual(Identity, first.TransportIdentity);
        Assert.AreEqual(Identity, second.TransportIdentity);
    }

    [TestMethod]
    public async Task ReadAllAsync_RejectsReadingBeforeOpen()
    {
        await using var transport = CreateTransport(new FakeSerialPortAdapter());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in transport.ReadAllAsync(CancellationToken.None))
            {
            }
        });
    }

    [TestMethod]
    public async Task OpenAsync_MapsUnauthorizedAccessWithoutRetrying()
    {
        var adapter = new FakeSerialPortAdapter
        {
            OpenException = new UnauthorizedAccessException("Access denied.")
        };
        await using var transport = CreateTransport(adapter);

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(async () =>
            await transport.OpenAsync(CancellationToken.None));

        Assert.AreEqual(ConnectionState.AccessDenied, transport.State);
        Assert.AreEqual(1, adapter.OpenCount);
    }

    [TestMethod]
    public async Task OpenAsync_MapsBusyOrSharingFailureWithoutRetrying()
    {
        var adapter = new FakeSerialPortAdapter
        {
            OpenException = new IOException("The port is already in use.")
        };
        await using var transport = CreateTransport(adapter);

        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await transport.OpenAsync(CancellationToken.None));

        Assert.AreEqual(ConnectionState.Busy, transport.State);
        Assert.AreEqual(1, adapter.OpenCount);
    }

    [TestMethod]
    public async Task ReadAllAsync_ChangesStateWhenTheDeviceIsRemoved()
    {
        var adapter = new FakeSerialPortAdapter
        {
            ReadException = new IOException("The device was removed.")
        };
        await using var transport = CreateTransport(adapter);
        await transport.OpenAsync(CancellationToken.None);
        await using var chunks = transport.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.IsFalse(await chunks.MoveNextAsync());

        Assert.AreEqual(ConnectionState.DeviceRemoved, transport.State);
    }

    [TestMethod]
    public async Task CloseThenDispose_ClosesTheHandleExactlyOnce()
    {
        var adapter = new FakeSerialPortAdapter();
        var transport = CreateTransport(adapter);
        await transport.OpenAsync(CancellationToken.None);

        await transport.CloseAsync(CancellationToken.None);
        await transport.DisposeAsync();
        await transport.DisposeAsync();

        Assert.AreEqual(ConnectionState.Disconnected, transport.State);
        Assert.AreEqual(1, adapter.CloseCount);
        Assert.AreEqual(1, adapter.DisposeCount);
    }

    [TestMethod]
    [Timeout(2_000, CooperativeCancellation = true)]
    public async Task DisposeAsync_CancelsABlockedReadAndClosesWithinOneSecond()
    {
        var adapter = new FakeSerialPortAdapter { BlockReads = true };
        var transport = CreateTransport(adapter);
        await transport.OpenAsync(CancellationToken.None);
        await using var chunks = transport.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        var pendingRead = chunks.MoveNextAsync().AsTask();
        await adapter.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await transport.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pendingRead);
        Assert.IsTrue(adapter.ReadWasCancelled);
        Assert.AreEqual(1, adapter.CloseCount);
        Assert.AreEqual(1, adapter.DisposeCount);
    }

    private static SerialConnectionOptions CreateOptions(
        string portName = "COM7",
        int baudRate = 9_600,
        int dataBits = 8,
        SerialParity parity = SerialParity.None,
        SerialStopBits stopBits = SerialStopBits.One,
        SerialHandshake handshake = SerialHandshake.None) =>
        new(portName, baudRate, dataBits, parity, stopBits, handshake, dtrEnable: false, rtsEnable: false);

    private static SerialTransport CreateTransport(FakeSerialPortAdapter adapter) =>
        new(Identity, SerialConnectionOptions.Default("COM7"), adapter);

    private sealed class FakeSerialPortAdapter : ISerialPortAdapter
    {
        private readonly ConcurrentQueue<byte[]> _reads;

        public FakeSerialPortAdapter(params byte[][] reads)
        {
            _reads = new ConcurrentQueue<byte[]>(reads);
        }

        public Exception? OpenException { get; init; }

        public Exception? ReadException { get; init; }

        public bool BlockReads { get; init; }

        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool ReadWasCancelled { get; private set; }

        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open()
        {
            OpenCount++;
            if (OpenException is not null)
            {
                throw OpenException;
            }
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult(true);

            if (ReadException is not null)
            {
                throw ReadException;
            }

            if (_reads.TryDequeue(out var bytes))
            {
                bytes.CopyTo(buffer);
                return bytes.Length;
            }

            if (!BlockReads)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
            catch (OperationCanceledException)
            {
                ReadWasCancelled = true;
                throw;
            }
        }

        public void Close() => CloseCount++;

        public void Dispose() => DisposeCount++;
    }
}
