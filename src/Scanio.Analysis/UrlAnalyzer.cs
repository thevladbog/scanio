using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public sealed class UrlAnalyzer : IScanAnalyzer
{
    public const string AnalyzerName = "URL";

    public string Name => AnalyzerName;

    public int Order => 500;

    public bool IsFallback => false;

    public AnalysisResult? Analyze(DecodedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!Uri.TryCreate(payload.Text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var fields = new List<AnalysisField>
        {
            new("uri", "URI", payload.Text),
            new("scheme", "Scheme", uri.Scheme),
            new("host", "Host", uri.DnsSafeHost),
            new("path", "Path", uri.AbsolutePath)
        };

        if (!uri.IsDefaultPort)
        {
            fields.Add(new("port", "Port", uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            fields.Add(new("query", "Query", uri.Query[1..]));
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            fields.Add(new("fragment", "Fragment", uri.Fragment[1..]));
        }

        return AnalysisResult.Match(
            Name,
            "URL",
            AnalysisConfidence.Exact,
            "The complete decoded payload is an absolute HTTP or HTTPS URI. Scanio validates and displays it but does not open it automatically.",
            $"{uri.Scheme.ToUpperInvariant()} URL for {uri.DnsSafeHost}.",
            fields);
    }
}
