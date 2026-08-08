using System.Collections.Immutable;

namespace Scanio.Domain.Analysis;

public enum PayloadTextEncoding
{
    Utf8,
    Ascii,
    Windows1251,
    Latin1
}

public enum AnalysisConfidence
{
    Exact,
    Inferred,
    Unknown
}

public sealed record DecodedPayload
{
    private DecodedPayload(
        ImmutableArray<byte> bytes,
        PayloadTextEncoding encoding,
        string text,
        string escapedDisplay,
        string? decodingWarning)
    {
        Bytes = ImmutableArray.Create(bytes.ToArray());
        Encoding = encoding;
        Text = text;
        EscapedDisplay = escapedDisplay;
        DecodingWarning = decodingWarning;
    }

    public ImmutableArray<byte> Bytes { get; }

    public PayloadTextEncoding Encoding { get; }

    public string Text { get; }

    public string EscapedDisplay { get; }

    public string? DecodingWarning { get; }

    public bool HasDecodingWarning => DecodingWarning is not null;

    public static DecodedPayload Create(
        ReadOnlySpan<byte> bytes,
        PayloadTextEncoding encoding,
        string text,
        string escapedDisplay,
        string? decodingWarning = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(escapedDisplay);

        return new DecodedPayload(
            ImmutableArray.Create(bytes.ToArray()),
            encoding,
            text,
            escapedDisplay,
            decodingWarning);
    }
}

public sealed record AnalysisField(string Name, string Value);

public sealed record AnalysisResult
{
    private AnalysisResult(
        string analyzerName,
        string format,
        bool isMatch,
        AnalysisConfidence confidence,
        string evidence,
        string summary,
        ImmutableArray<AnalysisField> fields,
        ImmutableArray<string> validationErrors,
        ImmutableArray<string> validationWarnings)
    {
        if (string.IsNullOrWhiteSpace(analyzerName))
        {
            throw new ArgumentException("An analyzer result must identify its analyzer.", nameof(analyzerName));
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            throw new ArgumentException("An analyzer result must identify its format.", nameof(format));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        AnalyzerName = analyzerName;
        Format = format;
        IsMatch = isMatch;
        Confidence = confidence;
        Evidence = evidence;
        Summary = summary;
        Fields = ImmutableArray.CreateRange(fields);
        ValidationErrors = ImmutableArray.CreateRange(validationErrors);
        ValidationWarnings = ImmutableArray.CreateRange(validationWarnings);
    }

    public string AnalyzerName { get; }

    public string Format { get; }

    public bool IsMatch { get; }

    public AnalysisConfidence Confidence { get; }

    public string Evidence { get; }

    public string Summary { get; }

    public ImmutableArray<AnalysisField> Fields { get; }

    public ImmutableArray<string> ValidationErrors { get; }

    public ImmutableArray<string> ValidationWarnings { get; }

    // Payload structure does not prove a physical barcode symbology. The initial
    // analyzers therefore never make such a claim without external evidence.
    public string? PhysicalSymbology => null;

    public static AnalysisResult Match(
        string analyzerName,
        string format,
        AnalysisConfidence confidence,
        string evidence,
        string summary,
        IEnumerable<AnalysisField>? fields = null,
        IEnumerable<string>? validationErrors = null,
        IEnumerable<string>? validationWarnings = null) =>
        new(
            analyzerName,
            format,
            true,
            confidence,
            evidence,
            summary,
            ImmutableArray.CreateRange(fields ?? Enumerable.Empty<AnalysisField>()),
            ImmutableArray.CreateRange(validationErrors ?? Enumerable.Empty<string>()),
            ImmutableArray.CreateRange(validationWarnings ?? Enumerable.Empty<string>()));

    public static AnalysisResult Failure(string analyzerName) =>
        new(
            analyzerName,
            "Analyzer failure",
            false,
            AnalysisConfidence.Unknown,
            "The analyzer threw an exception while evaluating the payload.",
            "Analysis continued with the remaining analyzers.",
            ImmutableArray<AnalysisField>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray.Create("This analyzer could not evaluate the payload."));
}
