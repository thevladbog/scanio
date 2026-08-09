using System.Globalization;
using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class SettingsLayoutContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void SettingsLayout_IsFullBleedTwoColumnWithOneNeutralDivider()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "SettingsView.xaml");
        var document = XDocument.Load(path);
        var root = document.Root!.Elements(Presentation + "Grid").Single();
        var columnDefinitions = root.Element(Presentation + "Grid.ColumnDefinitions");
        Assert.IsNotNull(columnDefinitions,
            "The full-bleed Settings root must define the two content columns and their divider.");
        var columns = columnDefinitions
            .Elements(Presentation + "ColumnDefinition")
            .Select(element => element.Attribute("Width")!.Value)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "*", "1", "*" }, columns);
        Assert.IsTrue(root.Attribute("Margin") is null || IsZero(root.Attribute("Margin")!.Value),
            "The Settings root must reach the workspace edges instead of exposing an outer page rectangle.");

        var left = NamedElement(root, "SettingsLeftColumn");
        var divider = NamedElement(root, "SettingsDivider");
        var localData = NamedElement(root, "SettingsLocalDataColumn");

        Assert.AreEqual("0", (string?)left.Attribute("Grid.Column") ?? "0");
        Assert.AreEqual("1", divider.Attribute("Grid.Column")!.Value);
        Assert.AreEqual("2", localData.Attribute("Grid.Column")!.Value);
        Assert.AreEqual("{DynamicResource Brush.BorderDefault}", divider.Attribute("Background")!.Value);
        Assert.IsGreaterThanOrEqualTo(24d, MinimumInset(left.Attribute("Padding")!.Value));
        Assert.IsGreaterThanOrEqualTo(24d, MinimumInset(localData.Attribute("Padding")!.Value));
        Assert.IsNull(localData.Attribute("BorderBrush"),
            "The local-data rail must be flat, without a four-sided outline.");
        Assert.IsTrue(localData.Attribute("BorderThickness") is null || IsZero(localData.Attribute("BorderThickness")!.Value),
            "The local-data rail must be flat, without a four-sided outline.");
    }

    private static XElement NamedElement(XContainer root, string name) => root
        .Descendants()
        .Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static bool IsZero(string value) => ParseThickness(value).All(part => part == 0d);

    private static double MinimumInset(string value) => ParseThickness(value).Min();

    private static double[] ParseThickness(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
        return parts.Length switch
        {
            1 => [parts[0], parts[0], parts[0], parts[0]],
            2 => [parts[0], parts[1], parts[0], parts[1]],
            4 => parts,
            _ => throw new InvalidDataException($"Unsupported Thickness value: {value}")
        };
    }
}
