using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public sealed class PlainTextAnalyzer : IScanAnalyzer
{
    public const string AnalyzerName = "PlainText";

    public string Name => AnalyzerName;

    public int Order => 10_000;

    public bool IsFallback => true;

    public AnalysisResult Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return AnalysisResult.Match(
            Name,
            "Plain text",
            AnalysisConfidence.Unknown,
            "No preceding structured analyzer matched the decoded payload.",
            "Unstructured decoded text.",
            new[] { new AnalysisField("Text", payload.Text) });
    }
}
