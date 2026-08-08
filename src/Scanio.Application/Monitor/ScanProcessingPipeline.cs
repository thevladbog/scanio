using Scanio.Analysis;
using Scanio.Capture;
using Scanio.Domain.Analysis;
using Scanio.Transports;

namespace Scanio.Application.Monitor;

public interface IScanProcessingPipeline
{
    Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken);
}

public sealed class ScanProcessingPipeline : IScanProcessingPipeline
{
    private readonly ScanAssembler _assembler;
    private readonly PayloadTextEncoding _encoding;
    private readonly ScanAnalyzerPipeline _analyzers;
    private readonly LiveMonitor _monitor;

    public ScanProcessingPipeline(
        ScanAssembler assembler,
        PayloadTextEncoding encoding,
        ScanAnalyzerPipeline analyzers,
        LiveMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(assembler);
        ArgumentNullException.ThrowIfNull(analyzers);
        ArgumentNullException.ThrowIfNull(monitor);

        _assembler = assembler;
        _encoding = encoding;
        _analyzers = analyzers;
        _monitor = monitor;
    }

    public async Task ProcessAsync(IScannerTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);

        try
        {
            await foreach (var chunk in transport.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var scan in _assembler.Push(chunk))
                {
                    var decoded = TextDecoder.Decode(scan.PayloadBytes.AsSpan(), _encoding);
                    var analyses = _analyzers.Analyze(decoded);
                    _monitor.Append(scan, decoded, analyses);
                }
            }
        }
        finally
        {
            // A partial scan belongs to this connection only. Disconnecting or
            // removing the device must never merge its bytes into a later session.
            _assembler.DiscardPending();
        }
    }
}
