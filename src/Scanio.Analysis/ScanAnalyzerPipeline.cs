using System.Collections.Immutable;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public sealed class ScanAnalyzerPipeline
{
    private readonly ImmutableArray<IScanAnalyzer> _analyzers;

    public ScanAnalyzerPipeline(IEnumerable<IScanAnalyzer> analyzers)
    {
        ArgumentNullException.ThrowIfNull(analyzers);

        _analyzers = analyzers
            .Select(analyzer => analyzer ?? throw new ArgumentException("An analyzer pipeline cannot contain null.", nameof(analyzers)))
            .OrderBy(analyzer => analyzer.IsFallback)
            .ThenBy(analyzer => analyzer.Order)
            .ThenBy(analyzer => analyzer.Name, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public ImmutableArray<AnalysisResult> Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var results = ImmutableArray.CreateBuilder<AnalysisResult>();
        var hasMatch = false;

        foreach (var analyzer in _analyzers)
        {
            if (analyzer.IsFallback && hasMatch)
            {
                continue;
            }

            try
            {
                var result = analyzer.Analyze(payload);
                if (result is null)
                {
                    continue;
                }

                results.Add(result);
                hasMatch |= result.IsMatch;
            }
            catch (Exception)
            {
                results.Add(AnalysisResult.Failure(analyzer.Name));
            }
        }

        return results.ToImmutable();
    }
}
