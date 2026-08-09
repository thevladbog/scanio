using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class IataBcbpAnalyzerTests
{
    private readonly IataBcbpAnalyzer _analyzer = new();

    [TestMethod]
    public void Analyze_ParsesOneLegMandatoryFields()
    {
        var result = Analyze(Build(1, Leg("ABC123", "SVO", "LED", "SU", "0123", "123", "C", "12A", "00001", "1")));

        Assert.IsNotNull(result);
        Assert.AreEqual("IATA BCBP", result.Format);
        Assert.AreEqual("DOE/IVAN", Field(result, "passenger-name"));
        Assert.AreEqual("SVO", Field(result, "leg-1-origin"));
        Assert.AreEqual("LED", Field(result, "leg-1-destination"));
        Assert.AreEqual("SU", Field(result, "leg-1-carrier"));
        Assert.AreEqual("00123", Field(result, "leg-1-flight-number"));
        Assert.AreEqual("12A", Field(result, "leg-1-seat"));
        Assert.IsEmpty(result.ValidationErrors);
        Assert.IsNull(result.PhysicalSymbology);
    }

    [TestMethod]
    public void Analyze_ParsesTwoMandatoryLegsInOrder()
    {
        var value = Build(
            2,
            Leg("ABC123", "SVO", "IST", "TK", "0416", "100", "Y", "18C", "00007", "1"),
            Leg("ABC123", "IST", "CDG", "TK", "1821", "101", "Y", "22A", "00008", "1"));

        var result = Analyze(value);

        Assert.IsNotNull(result);
        Assert.AreEqual("SVO", Field(result, "leg-1-origin"));
        Assert.AreEqual("IST", Field(result, "leg-2-origin"));
        Assert.AreEqual("CDG", Field(result, "leg-2-destination"));
    }

    [TestMethod]
    public void Analyze_TruncatedMandatorySection_PreservesBcbpMatchAndReportsError()
    {
        var result = Analyze("M1DOE/IVAN");

        Assert.IsNotNull(result);
        CollectionAssert.Contains(result.ValidationErrors.ToArray(), "BCBP mandatory header is incomplete.");
    }

    [TestMethod]
    public void Analyze_InvalidLegCount_PreservesBcbpMatchAndReportsError()
    {
        var result = Analyze("MX" + "DOE/IVAN".PadRight(20) + "E");

        Assert.IsNotNull(result);
        CollectionAssert.Contains(result.ValidationErrors.ToArray(), "BCBP number of legs must be a digit from 1 to 9.");
    }

    [TestMethod]
    public void Analyze_InvalidJulianDate_ReportsStructuralError()
    {
        var result = Analyze(Build(1, Leg("ABC123", "SVO", "LED", "SU", "0123", "367", "C", "12A", "00001", "1")));

        Assert.IsNotNull(result);
        CollectionAssert.Contains(result.ValidationErrors.ToArray(), "Leg 1 has an invalid Julian flight date.");
    }

    [TestMethod]
    public void Analyze_ConditionalLengthBeyondPayload_ReportsIncompleteSection()
    {
        var value = BuildMandatory(1, Leg("ABC123", "SVO", "LED", "SU", "0123", "123", "C", "12A", "00001", "1")) + "0AABC";

        var result = Analyze(value);

        Assert.IsNotNull(result);
        CollectionAssert.Contains(result.ValidationErrors.ToArray(), "BCBP declares 10 conditional character(s), but only 3 remain.");
    }

    [TestMethod]
    public void Analyze_ConditionalData_IsPreservedWithUnsupportedWarning()
    {
        var value = BuildMandatory(1, Leg("ABC123", "SVO", "LED", "SU", "0123", "123", "C", "12A", "00001", "1")) + "03XYZ";

        var result = Analyze(value);

        Assert.IsNotNull(result);
        Assert.AreEqual("XYZ", Field(result, "conditional-data"));
        CollectionAssert.Contains(result.ValidationWarnings.ToArray(), "Conditional BCBP data is preserved but not decoded in this version.");
    }

    [TestMethod]
    public void Analyze_UnrelatedText_DoesNotMatch()
    {
        Assert.IsNull(Analyze("scanner diagnostics"));
    }

    private AnalysisResult? Analyze(string value) =>
        _analyzer.Analyze(DecodedPayload.Create(
            System.Text.Encoding.ASCII.GetBytes(value),
            PayloadTextEncoding.Ascii,
            value,
            value));

    private static string Field(AnalysisResult result, string code) =>
        result.Fields.Single(field => field.Code == code).Value;

    private static string Build(int legs, params string[] segments) => BuildMandatory(legs, segments) + "00";

    private static string BuildMandatory(int legs, params string[] segments) =>
        "M" + legs + "DOE/IVAN".PadRight(20) + "E" + string.Concat(segments);

    private static string Leg(
        string pnr,
        string origin,
        string destination,
        string carrier,
        string flight,
        string julianDate,
        string compartment,
        string seat,
        string sequence,
        string status) =>
        pnr.PadRight(7) +
        origin.PadRight(3) +
        destination.PadRight(3) +
        carrier.PadRight(3) +
        flight.PadLeft(5, '0') +
        julianDate.PadLeft(3, '0') +
        compartment[0] +
        seat.PadRight(4) +
        sequence.PadLeft(5, '0') +
        status[0];
}
