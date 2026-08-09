using Scanio.Analysis.Gs1;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public sealed class HonestSignAnalyzer : IScanAnalyzer
{
    public const string AnalyzerName = "HonestSign";

    public string Name => AnalyzerName;

    public int Order => 100;

    public bool IsFallback => false;

    public AnalysisResult? Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var parsed = Gs1Parser.Parse(payload.Text);
        if (!parsed.IsRecognized || !Has(parsed, "01") || !Has(parsed, "21"))
        {
            return null;
        }

        var fields = parsed.Elements
            .Where(element => element.Code is "01" or "21" or "91" or "92" or "93" or "8005")
            .Select(element => new AnalysisField(element.Code, element.Identifier.Name, element.Value))
            .ToList();
        var candidates = HonestSignProductGroupClassifier.Classify(parsed.Elements);

        if (candidates.IsEmpty)
        {
            fields.Add(new AnalysisField("product-group", "Product group", "Not determined"));
        }
        else
        {
            fields.AddRange(candidates.Select(candidate =>
                new AnalysisField("product-group-candidate", "Product group candidate", candidate)));
        }

        var warnings = parsed.Warnings.ToList();
        var isTobaccoShape = candidates.Length == 1 && candidates[0] == "Tobacco unit pack";
        if (!isTobaccoShape && !Has(parsed, "91"))
        {
            warnings.Add("Verification key AI 91 is not present.");
        }

        if (!isTobaccoShape && !Has(parsed, "92"))
        {
            warnings.Add("Crypto tail AI 92 is not present.");
        }

        warnings.Add("Product-group candidates use bundled structural rules only; official online validity was not checked.");

        var summary = candidates.Length switch
        {
            0 => "Serialized marking payload; product group not determined.",
            1 => $"Serialized marking payload; local product-group candidate: {candidates[0]}.",
            _ => $"Serialized marking payload with multiple local candidates ({candidates.Length})."
        };

        return AnalysisResult.Match(
            Name,
            "Честный знак",
            parsed.HasExplicitSyntax ? AnalysisConfidence.Exact : AnalysisConfidence.Inferred,
            "GTIN AI 01 and serial AI 21 are present in a GS1-shaped payload. No network lookup or cryptographic verification was performed.",
            summary,
            fields,
            parsed.Errors,
            warnings);
    }

    private static bool Has(Gs1ParseResult parsed, string code) =>
        parsed.Elements.Any(element => element.Code == code);
}
