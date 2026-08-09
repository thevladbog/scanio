using Scanio.Analysis.Gs1;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class Gs1ParserTests
{
    [TestMethod]
    public void Parse_ParenthesizedElementString_PreservesOrderedFields()
    {
        var result = Gs1Parser.Parse("(01)04601234567893(17)271231(10)LOT-7(21)SERIAL42");

        Assert.IsTrue(result.IsRecognized);
        Assert.IsTrue(result.HasExplicitSyntax);
        CollectionAssert.AreEqual(
            new[] { "01", "17", "10", "21" },
            result.Elements.Select(element => element.Code).ToArray());
        Assert.AreEqual("LOT-7", result.Elements[2].Value);
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    public void Parse_RawElementString_HonorsGroupSeparatorForVariableFields()
    {
        var result = Gs1Parser.Parse("010460123456789321SERIAL42\u001D17271231");

        Assert.IsTrue(result.IsRecognized);
        Assert.IsTrue(result.HasExplicitSyntax);
        CollectionAssert.AreEqual(
            new[] { "01", "21", "17" },
            result.Elements.Select(element => element.Code).ToArray());
        Assert.AreEqual("SERIAL42", result.Elements[1].Value);
    }

    [TestMethod]
    public void Parse_RawVariableFieldWithoutSeparator_ReportsAmbiguousBoundary()
    {
        var result = Gs1Parser.Parse("010460123456789321SERIAL4217271231");

        Assert.IsTrue(result.IsRecognized);
        CollectionAssert.Contains(
            result.Warnings.ToArray(),
            "Variable-length AI 21 reaches the end of the payload; a missing GS separator may make following fields ambiguous.");
    }

    [TestMethod]
    public void Parse_InvalidGtinCheckDigit_ReportsValidationError()
    {
        var result = Gs1Parser.Parse("(01)04601234567890");

        CollectionAssert.Contains(result.Errors.ToArray(), "AI 01 has an invalid GS1 check digit.");
    }

    [TestMethod]
    public void Parse_InvalidCalendarDate_ReportsValidationError()
    {
        var result = Gs1Parser.Parse("(17)271332");

        CollectionAssert.Contains(result.Errors.ToArray(), "AI 17 is not a valid YYMMDD date.");
    }

    [TestMethod]
    public void Parse_UnknownParenthesizedAi_PreservesEvidenceAndReportsError()
    {
        var result = Gs1Parser.Parse("(01)04601234567893(88)VALUE");

        Assert.IsTrue(result.IsRecognized);
        CollectionAssert.Contains(result.Errors.ToArray(), "Unsupported GS1 application identifier 88.");
    }

    [TestMethod]
    public void Parse_NonGs1Text_IsNotRecognized()
    {
        var result = Gs1Parser.Parse("scanner diagnostics");

        Assert.IsFalse(result.IsRecognized);
        Assert.IsEmpty(result.Elements);
    }
}
