using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public sealed class EanUpcAnalyzer : IScanAnalyzer
{
    public const string AnalyzerName = "EAN/UPC";

    public string Name => AnalyzerName;

    public int Order => 300;

    public bool IsFallback => false;

    public AnalysisResult? Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var value = payload.Text;
        var format = value.Length switch
        {
            8 => "EAN-8",
            12 => "UPC-A",
            13 => "EAN-13",
            _ => null
        };

        if (format is null || value.Any(character => character is < '0' or > '9'))
        {
            return null;
        }

        var expectedCheckDigit = CalculateCheckDigit(value.AsSpan(0, value.Length - 1));
        var actualCheckDigit = value[^1] - '0';
        var errors = expectedCheckDigit == actualCheckDigit
            ? Array.Empty<string>()
            : new[] { $"{format} check digit is invalid." };

        return AnalysisResult.Match(
            Name,
            format,
            AnalysisConfidence.Exact,
            $"{value.Length} numeric payload characters match the {format} payload length; the modulo-10 check digit was evaluated.",
            errors.Length == 0 ? $"Valid {format} payload." : $"{format}-shaped payload with an invalid check digit.",
            new[]
            {
                new AnalysisField("data", "Data", value[..^1]),
                new AnalysisField("check-digit", "Check digit", value[^1].ToString())
            },
            errors);
    }

    private static int CalculateCheckDigit(ReadOnlySpan<char> data)
    {
        var sum = 0;
        var weight = 3;
        for (var index = data.Length - 1; index >= 0; index--)
        {
            sum += (data[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        return (10 - sum % 10) % 10;
    }
}
