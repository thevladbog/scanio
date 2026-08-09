using Scanio.Analysis.Gs1;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class Gs1AnalyzerTests
{
    private readonly Gs1Analyzer _analyzer = new();

    [TestMethod]
    public void Analyze_MapsApplicationIdentifiersToStructuredFields()
    {
        var payload = TextDecoder.Decode("(01)04601234567893(10)LOT-7"u8, PayloadTextEncoding.Utf8);

        var result = _analyzer.Analyze(payload);

        Assert.IsNotNull(result);
        Assert.AreEqual("GS1 element string", result.Format);
        Assert.AreEqual(AnalysisConfidence.Exact, result.Confidence);
        Assert.AreEqual("01", result.Fields[0].Code);
        Assert.AreEqual("GTIN", result.Fields[0].Name);
        Assert.IsNull(result.PhysicalSymbology);
    }

    [TestMethod]
    public void Analyze_UnseparatedRawStructure_IsInferredRatherThanExact()
    {
        var payload = TextDecoder.Decode("0104601234567893"u8, PayloadTextEncoding.Utf8);

        var result = _analyzer.Analyze(payload);

        Assert.IsNotNull(result);
        Assert.AreEqual(AnalysisConfidence.Inferred, result.Confidence);
        Assert.IsNull(result.PhysicalSymbology);
    }

    [TestMethod]
    public void Analyze_ArbitraryText_DoesNotMatch()
    {
        var payload = TextDecoder.Decode("scanner diagnostics"u8, PayloadTextEncoding.Utf8);

        Assert.IsNull(_analyzer.Analyze(payload));
    }
}
