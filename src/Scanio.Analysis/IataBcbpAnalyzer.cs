using System.Globalization;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public sealed class IataBcbpAnalyzer : IScanAnalyzer
{
    private const int HeaderLength = 23;
    private const int LegLength = 35;

    public const string AnalyzerName = "IATA BCBP";

    public string Name => AnalyzerName;

    public int Order => 400;

    public bool IsFallback => false;

    public AnalysisResult? Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var text = payload.Text;
        if (!text.StartsWith('M'))
        {
            return null;
        }

        var fields = new List<AnalysisField>();
        var errors = new List<string>();
        var warnings = new List<string>();

        if (text.Length < 2 || text[1] is < '1' or > '9')
        {
            errors.Add("BCBP number of legs must be a digit from 1 to 9.");
            return Result(fields, errors, warnings);
        }

        var legCount = text[1] - '0';
        fields.Add(new("legs", "Number of legs", legCount.ToString(CultureInfo.InvariantCulture)));

        if (text.Length < HeaderLength)
        {
            errors.Add("BCBP mandatory header is incomplete.");
            return Result(fields, errors, warnings);
        }

        fields.Add(new("passenger-name", "Passenger name", Clean(text.Substring(2, 20))));
        fields.Add(new("electronic-ticket", "Electronic ticket indicator", text[22].ToString()));

        var position = HeaderLength;
        for (var leg = 1; leg <= legCount; leg++)
        {
            if (text.Length - position < LegLength)
            {
                errors.Add($"BCBP mandatory section for leg {leg} is incomplete.");
                return Result(fields, errors, warnings);
            }

            var segment = text.AsSpan(position, LegLength);
            AddLegFields(fields, leg, segment);
            ValidateLeg(errors, leg, segment);
            position += LegLength;
        }

        if (text.Length - position < 2)
        {
            errors.Add("BCBP conditional-field length is missing.");
            return Result(fields, errors, warnings);
        }

        var lengthText = text.AsSpan(position, 2);
        position += 2;
        if (!int.TryParse(lengthText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var conditionalLength))
        {
            errors.Add("BCBP conditional-field length is not hexadecimal.");
            return Result(fields, errors, warnings);
        }

        var remaining = text.Length - position;
        if (remaining < conditionalLength)
        {
            errors.Add($"BCBP declares {conditionalLength} conditional character(s), but only {remaining} remain.");
            if (remaining > 0)
            {
                fields.Add(new("conditional-data", "Conditional data", text[position..]));
            }

            return Result(fields, errors, warnings);
        }

        if (conditionalLength > 0)
        {
            fields.Add(new("conditional-data", "Conditional data", text.Substring(position, conditionalLength)));
            warnings.Add("Conditional BCBP data is preserved but not decoded in this version.");
            position += conditionalLength;
        }

        if (position < text.Length)
        {
            fields.Add(new("trailing-data", "Trailing data", text[position..]));
            warnings.Add("Trailing BCBP data is preserved but not decoded in this version.");
        }

        return Result(fields, errors, warnings);
    }

    private AnalysisResult Result(
        IReadOnlyCollection<AnalysisField> fields,
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<string> warnings) =>
        AnalysisResult.Match(
            Name,
            "IATA BCBP",
            AnalysisConfidence.Exact,
            "The decoded payload uses IATA BCBP format code M and a fixed-width mandatory structure.",
            errors.Count == 0
                ? "IATA Bar Coded Boarding Pass payload."
                : $"IATA BCBP-shaped payload with {errors.Count} structural error(s).",
            fields,
            errors,
            warnings);

    private static void AddLegFields(List<AnalysisField> fields, int leg, ReadOnlySpan<char> segment)
    {
        var prefix = $"leg-{leg}";
        fields.Add(new($"{prefix}-pnr", $"Leg {leg} operating carrier PNR", Clean(segment[..7])));
        fields.Add(new($"{prefix}-origin", $"Leg {leg} origin", Clean(segment.Slice(7, 3))));
        fields.Add(new($"{prefix}-destination", $"Leg {leg} destination", Clean(segment.Slice(10, 3))));
        fields.Add(new($"{prefix}-carrier", $"Leg {leg} operating carrier", Clean(segment.Slice(13, 3))));
        fields.Add(new($"{prefix}-flight-number", $"Leg {leg} flight number", Clean(segment.Slice(16, 5))));
        fields.Add(new($"{prefix}-flight-date", $"Leg {leg} Julian flight date", Clean(segment.Slice(21, 3))));
        fields.Add(new($"{prefix}-compartment", $"Leg {leg} compartment", Clean(segment.Slice(24, 1))));
        fields.Add(new($"{prefix}-seat", $"Leg {leg} seat", Clean(segment.Slice(25, 4))));
        fields.Add(new($"{prefix}-check-in-sequence", $"Leg {leg} check-in sequence", Clean(segment.Slice(29, 5))));
        fields.Add(new($"{prefix}-passenger-status", $"Leg {leg} passenger status", Clean(segment.Slice(34, 1))));
    }

    private static void ValidateLeg(List<string> errors, int leg, ReadOnlySpan<char> segment)
    {
        var date = segment.Slice(21, 3);
        if (!int.TryParse(date, NumberStyles.None, CultureInfo.InvariantCulture, out var julianDay) ||
            julianDay is < 1 or > 366)
        {
            errors.Add($"Leg {leg} has an invalid Julian flight date.");
        }
    }

    private static string Clean(ReadOnlySpan<char> value) => value.ToString().Trim();

    private static string Clean(string value) => value.Trim();
}
