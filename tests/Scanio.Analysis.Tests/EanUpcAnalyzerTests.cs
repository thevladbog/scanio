using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class EanUpcAnalyzerTests
{
    private readonly EanUpcAnalyzer _analyzer = new();

    [TestMethod]
    [DataRow("96385074", "EAN-8", "9638507", "4")]
    [DataRow("036000291452", "UPC-A", "03600029145", "2")]
    [DataRow("4601234567893", "EAN-13", "460123456789", "3")]
    public void Analyze_RecognizesSupportedRetailPayloads(
        string value,
        string format,
        string data,
        string checkDigit)
    {
        var result = Analyze(value);

        Assert.IsNotNull(result);
        Assert.AreEqual(format, result.Format);
        Assert.IsEmpty(result.ValidationErrors);
        Assert.AreEqual(data, result.Fields.Single(field => field.Name == "Data").Value);
        Assert.AreEqual(checkDigit, result.Fields.Single(field => field.Name == "Check digit").Value);
        Assert.IsNull(result.PhysicalSymbology);
    }

    [TestMethod]
    [DataRow("96385075", "EAN-8")]
    [DataRow("036000291453", "UPC-A")]
    [DataRow("4601234567894", "EAN-13")]
    public void Analyze_ShapedPayloadWithInvalidCheckDigit_PreservesMatchAndReportsError(string value, string format)
    {
        var result = Analyze(value);

        Assert.IsNotNull(result);
        Assert.AreEqual(format, result.Format);
        CollectionAssert.Contains(result.ValidationErrors.ToArray(), $"{format} check digit is invalid.");
    }

    [TestMethod]
    [DataRow("1234567")]
    [DataRow("123456789")]
    [DataRow("12345678901234")]
    [DataRow("03600029145X")]
    public void Analyze_UnsupportedShape_DoesNotMatch(string value)
    {
        Assert.IsNull(Analyze(value));
    }

    private AnalysisResult? Analyze(string value) =>
        _analyzer.Analyze(DecodedPayload.Create(
            System.Text.Encoding.ASCII.GetBytes(value),
            PayloadTextEncoding.Ascii,
            value,
            value));
}
