using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Gs1;

public sealed class Gs1Analyzer : IScanAnalyzer
{
    public const string AnalyzerName = "GS1";

    public string Name => AnalyzerName;

    public int Order => 200;

    public bool IsFallback => false;

    public AnalysisResult? Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var parsed = Gs1Parser.Parse(payload.Text);
        if (!parsed.IsRecognized)
        {
            return null;
        }

        return AnalysisResult.Match(
            Name,
            "GS1 element string",
            parsed.HasExplicitSyntax ? AnalysisConfidence.Exact : AnalysisConfidence.Inferred,
            parsed.HasExplicitSyntax
                ? "Explicit GS1 notation, AIM prefix, or FNC1 group-separator evidence was present."
                : "The payload follows supported GS1 application-identifier structure without transport symbology evidence.",
            parsed.Errors.Count == 0
                ? $"GS1 payload with {parsed.Elements.Count} structured field(s)."
                : $"GS1-shaped payload with {parsed.Errors.Count} validation error(s).",
            parsed.Elements.Select(element => new AnalysisField(element.Code, element.Identifier.Name, element.Value)),
            parsed.Errors,
            parsed.Warnings);
    }
}
