using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Transports;

public interface IScannerTransport : IAsyncDisposable
{
    TransportIdentity Identity { get; }

    ConnectionState State { get; }

    ValueTask OpenAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<RawChunk> ReadAllAsync(CancellationToken cancellationToken);

    ValueTask CloseAsync(CancellationToken cancellationToken);
}
