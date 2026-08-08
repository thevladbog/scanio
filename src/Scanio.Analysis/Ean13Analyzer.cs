using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public sealed class Ean13Analyzer : IScanAnalyzer
{
    public const string AnalyzerName = "EAN-13";

    public string Name => AnalyzerName;

    public int Order => 100;

    public bool IsFallback => false;

    public AnalysisResult? Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var value = payload.Text;
        if (value.Length != 13 || value.Any(character => character is < '0' or > '9'))
        {
            return null;
        }

        var expectedCheckDigit = CalculateCheckDigit(value.AsSpan(0, 12));
        var actualCheckDigit = value[12] - '0';
        var errors = expectedCheckDigit == actualCheckDigit
            ? Array.Empty<string>()
            : new[] { "EAN-13 check digit is invalid." };

        return AnalysisResult.Match(
            Name,
            "EAN-13",
            AnalysisConfidence.Exact,
            "13 numeric payload characters with an EAN-13 modulo-10 check digit.",
            errors.Length == 0 ? "Valid EAN-13 payload." : "EAN-13-shaped payload with an invalid check digit.",
            new[]
            {
                new AnalysisField("Data", value[..12]),
                new AnalysisField("Check digit", value[12].ToString())
            },
            errors);
    }

    private static int CalculateCheckDigit(ReadOnlySpan<char> data)
    {
        var sum = 0;
        for (var index = 0; index < data.Length; index++)
        {
            var digit = data[index] - '0';
            sum += index % 2 == 0 ? digit : digit * 3;
        }

        return (10 - (sum % 10)) % 10;
    }
}
