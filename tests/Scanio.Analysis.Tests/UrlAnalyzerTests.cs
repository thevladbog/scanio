using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class UrlAnalyzerTests
{
    private readonly UrlAnalyzer _analyzer = new();

    [TestMethod]
    [DataRow("https://scanio.example/path?q=scanner#result", "https", "scanio.example")]
    [DataRow("http://localhost:8080/monitor", "http", "localhost")]
    public void Analyze_AcceptsAbsoluteHttpAndHttpsUris(string value, string scheme, string host)
    {
        var result = Analyze(value);

        Assert.IsNotNull(result);
        Assert.AreEqual("URL", result.Format);
        Assert.AreEqual(scheme, Field(result, "scheme"));
        Assert.AreEqual(host, Field(result, "host"));
        Assert.AreEqual(value, Field(result, "uri"));
        StringAssert.Contains(result.Evidence, "does not open");
    }

    [TestMethod]
    [DataRow("/relative/path")]
    [DataRow("file:///C:/scanner.txt")]
    [DataRow("javascript:alert(1)")]
    [DataRow("scanner diagnostics")]
    public void Analyze_RejectsRelativeAndUnsafeSchemes(string value)
    {
        Assert.IsNull(Analyze(value));
    }

    [TestMethod]
    public void Analyze_PreservesUnicodeDisplayValue()
    {
        const string value = "https://пример.рф/сканер?код=да";

        var result = Analyze(value);

        Assert.IsNotNull(result);
        Assert.AreEqual(value, Field(result, "uri"));
    }

    private AnalysisResult? Analyze(string value) =>
        _analyzer.Analyze(DecodedPayload.Create(
            System.Text.Encoding.UTF8.GetBytes(value),
            PayloadTextEncoding.Utf8,
            value,
            value));

    private static string Field(AnalysisResult result, string code) =>
        result.Fields.Single(field => field.Code == code).Value;
}
