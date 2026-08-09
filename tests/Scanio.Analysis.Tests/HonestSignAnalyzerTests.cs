using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class HonestSignAnalyzerTests
{
    private readonly HonestSignAnalyzer _analyzer = new();

    [TestMethod]
    public void Analyze_ExtractsStandardSerializedMarkingFields()
    {
        var result = Analyze("(01)04601234567893(21)ABC1234567890(91)ABCD(92)CRYPTO-SIGNATURE");

        Assert.IsNotNull(result);
        Assert.AreEqual("Честный знак", result.Format);
        Assert.AreEqual("04601234567893", Field(result, "01").Value);
        Assert.AreEqual("ABC1234567890", Field(result, "21").Value);
        Assert.AreEqual("ABCD", Field(result, "91").Value);
        Assert.AreEqual("CRYPTO-SIGNATURE", Field(result, "92").Value);
        StringAssert.Contains(result.Evidence, "No network lookup");
        Assert.IsNull(result.PhysicalSymbology);
    }

    [TestMethod]
    public void Analyze_AmbiguousSerialStructure_ReturnsMultipleProductGroupCandidates()
    {
        var result = Analyze("(01)04601234567893(21)ABC1234567890(91)ABCD(92)CRYPTO-SIGNATURE");

        Assert.IsNotNull(result);
        var candidates = result.Fields.Where(field => field.Code == "product-group-candidate").ToArray();
        Assert.IsGreaterThanOrEqualTo(2, candidates.Length);
        StringAssert.Contains(result.Summary, "multiple local candidates");
    }

    [TestMethod]
    public void Analyze_TobaccoShape_ReturnsSingleLocalCandidate()
    {
        var result = Analyze("(01)04601234567893(21)ABC1234(8005)012345(93)KEY1");

        Assert.IsNotNull(result);
        var candidates = result.Fields.Where(field => field.Code == "product-group-candidate").ToArray();
        Assert.HasCount(1, candidates);
        Assert.AreEqual("Tobacco unit pack", candidates[0].Value);
    }

    [TestMethod]
    public void Analyze_MissingCryptoFields_PreservesMatchWithWarnings()
    {
        var result = Analyze("(01)04601234567893(21)SERIAL42");

        Assert.IsNotNull(result);
        CollectionAssert.Contains(result.ValidationWarnings.ToArray(), "Verification key AI 91 is not present.");
        CollectionAssert.Contains(result.ValidationWarnings.ToArray(), "Crypto tail AI 92 is not present.");
        Assert.AreEqual("Not determined", Field(result, "product-group").Value);
    }

    [TestMethod]
    public void Analyze_InvalidGtin_PropagatesGs1ValidationError()
    {
        var result = Analyze("(01)04601234567890(21)SERIAL42(91)ABCD(92)CRYPTO");

        Assert.IsNotNull(result);
        CollectionAssert.Contains(result.ValidationErrors.ToArray(), "AI 01 has an invalid GS1 check digit.");
    }

    [TestMethod]
    public void Analyze_GenericGs1Payload_DoesNotMatchHonestSign()
    {
        Assert.IsNull(Analyze("(01)04601234567893(10)LOT-7"));
    }

    private AnalysisResult? Analyze(string value) =>
        _analyzer.Analyze(DecodedPayload.Create(
            System.Text.Encoding.UTF8.GetBytes(value),
            PayloadTextEncoding.Utf8,
            value,
            value));

    private static AnalysisField Field(AnalysisResult result, string code) =>
        result.Fields.Single(field => field.Code == code);
}
