using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public interface IScanAnalyzer
{
    string Name { get; }

    int Order { get; }

    bool IsFallback { get; }

    AnalysisResult? Analyze(DecodedPayload payload);
}
