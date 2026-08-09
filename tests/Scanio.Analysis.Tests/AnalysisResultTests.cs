using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class AnalysisResultTests
{
    [TestMethod]
    public void AnalysisField_PreservesCodeNameAndValue()
    {
        var field = new AnalysisField("01", "GTIN", "04601234567890");

        Assert.AreEqual("01", field.Code);
        Assert.AreEqual("GTIN", field.Name);
        Assert.AreEqual("04601234567890", field.Value);
    }

    [TestMethod]
    public void AnalysisField_TwoArgumentConstructorRemainsCompatible()
    {
        var field = new AnalysisField("Text", "scanner diagnostics");

        Assert.AreEqual(string.Empty, field.Code);
        Assert.AreEqual("Text", field.Name);
        Assert.AreEqual("scanner diagnostics", field.Value);
    }

    [TestMethod]
    public void Match_FromPayloadEvidenceNeverClaimsPhysicalSymbology()
    {
        var result = AnalysisResult.Match(
            "Fixture",
            "Fixture format",
            AnalysisConfidence.Inferred,
            "Payload structure only.",
            "Fixture summary.");

        Assert.IsNull(result.PhysicalSymbology);
    }
}
