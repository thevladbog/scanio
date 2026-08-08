using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Transports;
using System.Runtime.CompilerServices;

namespace Scanio.Transports.Tests;

[TestClass]
public sealed class TransportContractTests
{
    private static readonly TransportIdentity Identity = new(TransportKind.Serial, "scanner-1", "Test scanner");

    [TestMethod]
    public void TransportIdentity_UsesAClassifiedNonEmptyStableIdentity()
    {
        var identity = new TransportIdentity(TransportKind.Serial, "scanner-1", "Test scanner");

        Assert.AreEqual(TransportKind.Serial, identity.Kind);
        Assert.AreEqual("scanner-1", identity.StableId);
        Assert.ThrowsExactly<ArgumentException>(() => new TransportIdentity(TransportKind.Serial, "", "Test scanner"));
        Assert.ThrowsExactly<ArgumentException>(() => new TransportIdentity(TransportKind.Serial, "scanner-1", ""));
    }

    [TestMethod]
    public void ConnectionState_DescribesTheCompleteTransportLifecycle()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                ConnectionState.Detected,
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Disconnecting,
                ConnectionState.Disconnected,
                ConnectionState.Busy,
                ConnectionState.AccessDenied,
                ConnectionState.DeviceRemoved,
                ConnectionState.UnsupportedInterface,
                ConnectionState.TransportError
            },
            Enum.GetValues<ConnectionState>());
    }

    [TestMethod]
    public void RawChunk_CreateCopiesTheSourceBytes()
    {
        var source = new byte[] { 0x31, 0x0D };

        var chunk = RawChunk.Create(1, source, DateTimeOffset.UnixEpoch, 10, Identity);
        source[0] = 0x39;

        CollectionAssert.AreEqual(new byte[] { 0x31, 0x0D }, chunk.Bytes.ToArray());
    }

    [TestMethod]
    public void ScanFramingSnapshot_CreateCopiesTheTerminatorAndAcceptsTerminatorCompletion()
    {
        var terminator = new byte[] { 0x0D, 0x0A };

        var framing = ScanFramingSnapshot.Create(terminator, TimeSpan.FromMilliseconds(100), 65_536);
        terminator[0] = 0x03;

        CollectionAssert.AreEqual(new byte[] { 0x0D, 0x0A }, framing.Terminator.ToArray());
        Assert.IsTrue(Enum.IsDefined(ScanCompletionReason.Terminator));
    }

    [TestMethod]
    public async Task ScannerTransport_RejectsReadsBeforeOpenAndDuplicateOpens()
    {
        await using var transport = new ContractProbe();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in transport.ReadAllAsync(CancellationToken.None))
            {
            }
        });

        await transport.OpenAsync(CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await transport.OpenAsync(CancellationToken.None));
    }

    private sealed class ContractProbe : IScannerTransport
    {
        public TransportIdentity Identity => TransportContractTests.Identity;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            if (State != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException("The transport is already open.");
            }

            State = ConnectionState.Connected;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<RawChunk> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException("The transport is not open.");
            }

            await Task.CompletedTask;
            yield break;
        }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            State = ConnectionState.Disconnected;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
