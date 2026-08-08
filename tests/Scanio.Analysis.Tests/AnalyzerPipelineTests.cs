using Scanio.Analysis;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class AnalyzerPipelineTests
{
    private static readonly ScanAnalyzerPipeline Pipeline = new(new IScanAnalyzer[]
    {
        new PlainTextAnalyzer(),
        new Ean13Analyzer()
    });

    [TestMethod]
    public void Analyze_RecognizesAValidEan13WithoutClaimingPhysicalSymbology()
    {
        var results = Pipeline.Analyze(TextDecoder.Decode("4601234567893"u8, PayloadTextEncoding.Utf8));

        Assert.HasCount(1, results);
        var result = results[0];
        Assert.AreEqual("EAN-13", result.Format);
        Assert.IsTrue(result.IsMatch);
        Assert.IsEmpty(result.ValidationErrors);
        Assert.AreEqual(AnalysisConfidence.Exact, result.Confidence);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Evidence));
        Assert.IsNull(result.PhysicalSymbology);
    }

    [TestMethod]
    public void Analyze_ReportsAnInvalidEan13CheckDigit()
    {
        var results = Pipeline.Analyze(TextDecoder.Decode("4601234567894"u8, PayloadTextEncoding.Utf8));

        Assert.HasCount(1, results);
        Assert.AreEqual("EAN-13", results[0].Format);
        Assert.IsTrue(results[0].IsMatch);
        CollectionAssert.Contains(results[0].ValidationErrors.ToArray(), "EAN-13 check digit is invalid.");
    }

    [TestMethod]
    public void Analyze_RoutesArbitraryTextToTheFinalPlainTextFallback()
    {
        var results = Pipeline.Analyze(TextDecoder.Decode("scanner diagnostics"u8, PayloadTextEncoding.Utf8));

        Assert.HasCount(1, results);
        Assert.AreEqual("Plain text", results[0].Format);
        Assert.AreEqual(PlainTextAnalyzer.AnalyzerName, results[0].AnalyzerName);
        Assert.AreEqual(AnalysisConfidence.Unknown, results[0].Confidence);
    }

    [TestMethod]
    public void Analyze_IsolatesAnalyzerFailuresAndContinuesToThePlainTextFallback()
    {
        var pipeline = new ScanAnalyzerPipeline(new IScanAnalyzer[]
        {
            new ThrowingAnalyzer(),
            new PlainTextAnalyzer()
        });

        var results = pipeline.Analyze(TextDecoder.Decode("anything"u8, PayloadTextEncoding.Utf8));

        Assert.HasCount(2, results);
        Assert.IsFalse(results[0].IsMatch);
        Assert.AreEqual("Throwing", results[0].AnalyzerName);
        Assert.AreEqual("Plain text", results[1].Format);
    }

    [TestMethod]
    public void Analyze_RunsStructuredAnalyzersBeforeFallbackRegardlessOfTheirOrderValue()
    {
        var pipeline = new ScanAnalyzerPipeline(new IScanAnalyzer[]
        {
            new PlainTextAnalyzer(),
            new LateStructuredAnalyzer()
        });

        var results = pipeline.Analyze(TextDecoder.Decode("late format"u8, PayloadTextEncoding.Utf8));

        Assert.HasCount(1, results);
        Assert.AreEqual("Late structured", results[0].Format);
    }

    private sealed class ThrowingAnalyzer : IScanAnalyzer
    {
        public string Name => "Throwing";

        public int Order => 1;

        public bool IsFallback => false;

        public AnalysisResult? Analyze(DecodedPayload payload) => throw new InvalidOperationException("Expected test failure.");
    }

    private sealed class LateStructuredAnalyzer : IScanAnalyzer
    {
        public string Name => "Late structured";

        public int Order => 10_001;

        public bool IsFallback => false;

        public AnalysisResult Analyze(DecodedPayload payload) => AnalysisResult.Match(
            Name,
            "Late structured",
            AnalysisConfidence.Exact,
            "The test analyzer matched after the conventional fallback order.",
            "Structured result.");
    }
}
