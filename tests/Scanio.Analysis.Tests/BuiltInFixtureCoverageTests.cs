using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class BuiltInFixtureCoverageTests
{
    private static readonly ScanAnalyzerPipeline Pipeline = BuiltInAnalyzers.CreatePipeline();

    [TestMethod]
    [DataRow("GS1 DataMatrix", "(01)04601234567893(21)ABC1234567890(91)ABCD(92)CRYPTO")]
    [DataRow("GS1 element string", "(01)04601234567893(10)LOT-7")]
    [DataRow("EAN-8", "96385074")]
    [DataRow("UPC-A", "036000291452")]
    [DataRow("EAN-13", "4601234567893")]
    [DataRow("IATA BCBP", "M1DOE/IVAN           EABC123 SVOLEDSU 00123123C12A 00001100")]
    [DataRow("URL", "https://scanio.example/monitor?q=scanner")]
    [DataRow("Plain text", "scanner diagnostics")]
    public void BuiltInFixture_ProducesExpectedFormat(string expectedFormat, string value)
    {
        var results = Analyze(value);

        Assert.IsTrue(results.Any(result => result.Format == expectedFormat), $"Missing {expectedFormat} for fixture {value}.");
        Assert.IsTrue(results.All(result => result.PhysicalSymbology is null));
    }

    [TestMethod]
    [DataRow("(01)04601234567890", "GS1 element string")]
    [DataRow("96385075", "EAN-8")]
    [DataRow("M1DOE/IVAN", "IATA BCBP")]
    public void MalformedStructuredSibling_RemainsStructuredWithValidationError(string value, string expectedFormat)
    {
        var result = Analyze(value).Single(item => item.Format == expectedFormat);

        Assert.IsNotEmpty(result.ValidationErrors);
    }

    [TestMethod]
    [DataRow("javascript:alert(1)")]
    [DataRow("file:///C:/scanner.txt")]
    public void UnsafeUriSibling_FallsBackToPlainText(string value)
    {
        var results = Analyze(value);
        Assert.HasCount(1, results);
        var result = results.Single();

        Assert.AreEqual("Plain text", result.Format);
    }

    private static IReadOnlyList<AnalysisResult> Analyze(string value) =>
        Pipeline.Analyze(DecodedPayload.Create(
            System.Text.Encoding.UTF8.GetBytes(value),
            PayloadTextEncoding.Utf8,
            value,
            value));
}
