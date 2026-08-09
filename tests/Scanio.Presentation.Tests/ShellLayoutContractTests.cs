using System.Globalization;
using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class ShellLayoutContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void Header_LeavesEnoughVerticalRoomForCommandButtons()
    {
        var layoutDirectory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var window = XDocument.Load(Path.Combine(layoutDirectory, "MainWindow.xaml"));
        var theme = LoadResources(Path.Combine(layoutDirectory, "Theme.xaml"));
        var controls = XDocument.Load(Path.Combine(layoutDirectory, "Controls.xaml"));

        var header = window.Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute("Grid.Row") == "0");
        var headerHeight = ResolveNumber(
            window.Descendants(Presentation + "RowDefinition").First().Attribute("Height")!.Value,
            theme);
        var padding = ResolveThickness(header.Attribute("Padding")!.Value, theme);
        var border = ResolveThickness(header.Attribute("BorderThickness")!.Value, theme);
        var commandButton = controls.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "CommandButton");
        var minimumControlHeight = ResolveNumber(
            commandButton.Elements(Presentation + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "MinHeight")
                .Attribute("Value")!.Value,
            theme);

        var availableHeight = headerHeight - padding.Top - padding.Bottom - border.Top - border.Bottom;

        Assert.IsGreaterThanOrEqualTo(
            minimumControlHeight,
            availableHeight,
            $"The shell header exposes only {availableHeight}px for {minimumControlHeight}px command buttons, so their labels are clipped.");
    }

    private static Dictionary<string, string> LoadResources(string path) =>
        XDocument.Load(path).Root!.Elements()
            .Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(Xaml + "Key")!.Value,
                element => element.Value,
                StringComparer.Ordinal);

    private static double ResolveNumber(string value, IReadOnlyDictionary<string, string> resources) =>
        double.Parse(ResolveResource(value, resources), CultureInfo.InvariantCulture);

    private static ThicknessValues ResolveThickness(string value, IReadOnlyDictionary<string, string> resources)
    {
        var parts = ResolveResource(value, resources)
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();

        return parts.Length switch
        {
            1 => new ThicknessValues(parts[0], parts[0], parts[0], parts[0]),
            2 => new ThicknessValues(parts[0], parts[1], parts[0], parts[1]),
            4 => new ThicknessValues(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new InvalidDataException($"Unsupported Thickness value: {value}")
        };
    }

    private static string ResolveResource(string value, IReadOnlyDictionary<string, string> resources)
    {
        const string dynamicResourcePrefix = "{DynamicResource ";
        if (!value.StartsWith(dynamicResourcePrefix, StringComparison.Ordinal) || !value.EndsWith('}'))
        {
            return value;
        }

        var key = value[dynamicResourcePrefix.Length..^1];
        return resources[key];
    }

    private readonly record struct ThicknessValues(double Left, double Top, double Right, double Bottom);
}
