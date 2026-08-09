using Scanio.Analysis;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class AnalyzerPipelineTests
{
    private static readonly ScanAnalyzerPipeline Pipeline = BuiltInAnalyzers.CreatePipeline();

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

    [TestMethod]
    public void BuiltIns_RecognizeEverySupportedFormatWithoutClaimingPhysicalSymbology()
    {
        var fixtures = new Dictionary<string, string>
        {
            ["GS1 element string"] = "(01)04601234567893(10)LOT-7",
            ["EAN-8"] = "96385074",
            ["IATA BCBP"] = "M1DOE/IVAN".PadRight(22) + "E" + "ABC123 " + "SVO" + "LED" + "SU " + "00123" + "123" + "C" + "12A " + "00001" + "1" + "00",
            ["URL"] = "https://scanio.example/monitor",
            ["Plain text"] = "scanner diagnostics"
        };

        foreach (var fixture in fixtures)
        {
            var results = Pipeline.Analyze(DecodedPayload.Create(
                System.Text.Encoding.UTF8.GetBytes(fixture.Value),
                PayloadTextEncoding.Utf8,
                fixture.Value,
                fixture.Value));

            Assert.IsTrue(results.Any(result => result.Format == fixture.Key), $"Missing {fixture.Key} result.");
            Assert.IsTrue(results.All(result => result.PhysicalSymbology is null));
        }
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
