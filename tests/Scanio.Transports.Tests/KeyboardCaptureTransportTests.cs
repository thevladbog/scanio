using System.Text;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;
using Scanio.Transports.Keyboard;

namespace Scanio.Transports.Tests;

[TestClass]
public sealed class KeyboardCaptureTransportTests
{
    private static readonly TransportIdentity Identity = new(
        TransportKind.KeyboardCapture,
        "keyboard-capture:focused-window",
        "Keyboard scanner",
        endpoint: "Keyboard");
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 8, 9, 12, 34, 56, TimeSpan.Zero);

    [TestMethod]
    public async Task CompleteInput_EmitsReconstructedUtf8AndCrFraming()
    {
        await using var transport = CreateTransport();
        await transport.OpenAsync(CancellationToken.None);
        transport.AppendText("01\u001D21");
        Assert.IsTrue(transport.CompleteInput());

        var chunk = await ReadNextAsync(transport);

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("01\u001D21\r"),
            chunk.Bytes.ToArray());
        Assert.AreEqual(1L, chunk.SequenceNumber);
        Assert.AreEqual(ReceivedAt, chunk.ReceivedAt);
        Assert.AreEqual(42_000L, chunk.MonotonicTimestamp);
        Assert.AreEqual(Identity, chunk.TransportIdentity);
    }

    [TestMethod]
    public async Task InputBeforeOpen_IsRejectedWithoutCreatingPendingInput()
    {
        await using var transport = CreateTransport();

        Assert.IsFalse(transport.AppendText("scan"));
        Assert.IsFalse(transport.CompleteInput());
        Assert.IsFalse(transport.HasPendingInput);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in transport.ReadAllAsync(CancellationToken.None))
            {
            }
        });
    }

    [TestMethod]
    public async Task CompleteInput_RejectsAnEmptyBufferWithoutEmittingAChunk()
    {
        await using var transport = CreateTransport();
        await transport.OpenAsync(CancellationToken.None);

        Assert.IsFalse(transport.CompleteInput());
        Assert.IsFalse(transport.HasPendingInput);
        Assert.IsTrue(transport.AppendText("next"));
        Assert.IsTrue(transport.CompleteInput());

        var chunk = await ReadNextAsync(transport);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("next\r"), chunk.Bytes.ToArray());
        Assert.AreEqual(1L, chunk.SequenceNumber);
    }

    [TestMethod]
    public async Task MultipleAppendTextCalls_PreserveUnicodeInputOrder()
    {
        await using var transport = CreateTransport();
        await transport.OpenAsync(CancellationToken.None);

        Assert.IsTrue(transport.AppendText("01"));
        Assert.IsTrue(transport.AppendText("\u001D"));
        Assert.IsTrue(transport.AppendText("21Ж"));
        Assert.IsTrue(transport.HasPendingInput);
        Assert.IsTrue(transport.CompleteInput());
        Assert.IsFalse(transport.HasPendingInput);

        var chunk = await ReadNextAsync(transport);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("01\u001D21Ж\r"), chunk.Bytes.ToArray());
    }

    [TestMethod]
    [Timeout(2_000, CooperativeCancellation = true)]
    public async Task CloseAsync_CompletesABlockedReader()
    {
        var transport = CreateTransport();
        await transport.OpenAsync(CancellationToken.None);
        await using var reader = transport.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        var pendingRead = reader.MoveNextAsync().AsTask();

        await transport.CloseAsync(CancellationToken.None);

        Assert.IsFalse(await pendingRead.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.AreEqual(ConnectionState.Disconnected, transport.State);
        await transport.DisposeAsync();
    }

    [TestMethod]
    [Timeout(2_000, CooperativeCancellation = true)]
    public async Task ReadAllAsync_ObservesCallerCancellation()
    {
        await using var transport = CreateTransport();
        await transport.OpenAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await using var reader = transport.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();
        var pendingRead = reader.MoveNextAsync().AsTask();

        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await pendingRead.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.AreEqual(ConnectionState.Connected, transport.State);
    }

    [TestMethod]
    public async Task OpenAsync_ObservesPreCancelledTokenWithoutChangingState()
    {
        await using var transport = CreateTransport();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await transport.OpenAsync(cancellation.Token));

        Assert.AreEqual(ConnectionState.Disconnected, transport.State);
    }

    [TestMethod]
    public async Task CloseAsync_IsIdempotentAndRejectsFurtherInput()
    {
        await using var transport = CreateTransport();
        await transport.OpenAsync(CancellationToken.None);
        Assert.IsTrue(transport.AppendText("discarded"));

        await transport.CloseAsync(CancellationToken.None);
        await transport.CloseAsync(CancellationToken.None);

        Assert.AreEqual(ConnectionState.Disconnected, transport.State);
        Assert.IsFalse(transport.HasPendingInput);
        Assert.IsFalse(transport.AppendText("after-close"));
        Assert.IsFalse(transport.CompleteInput());
    }

    [TestMethod]
    public async Task Reopen_StartsWithEmptyBufferAndResetSequence()
    {
        await using var transport = CreateTransport();
        await transport.OpenAsync(CancellationToken.None);
        Assert.IsTrue(transport.AppendText("old"));
        Assert.IsTrue(transport.CompleteInput());
        var first = await ReadNextAsync(transport);
        Assert.AreEqual(1L, first.SequenceNumber);
        await transport.CloseAsync(CancellationToken.None);

        await transport.OpenAsync(CancellationToken.None);

        Assert.IsFalse(transport.HasPendingInput);
        Assert.IsTrue(transport.AppendText("new"));
        Assert.IsTrue(transport.CompleteInput());
        var reopened = await ReadNextAsync(transport);
        Assert.AreEqual(1L, reopened.SequenceNumber);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("new\r"), reopened.Bytes.ToArray());
    }

    private static KeyboardCaptureTransport CreateTransport() =>
        new(Identity, () => ReceivedAt, () => 42_000);

    private static async Task<RawChunk> ReadNextAsync(KeyboardCaptureTransport transport)
    {
        await using var reader = transport.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.IsTrue(await reader.MoveNextAsync());
        return reader.Current;
    }
}
