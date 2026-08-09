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

    [TestMethod]
    public void ConnectionPrimaryAction_ContrastsWithTheDarkStatusPanel()
    {
        var layoutDirectory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var connection = XDocument.Load(Path.Combine(layoutDirectory, "ConnectionView.xaml"));
        var theme = LoadResources(Path.Combine(layoutDirectory, "Theme.xaml"));
        var controls = XDocument.Load(Path.Combine(layoutDirectory, "Controls.xaml"));

        var statusPanel = connection.Root!.Elements(Presentation + "Grid")
            .Single()
            .Elements(Presentation + "Border")
            .Single(element => (string?)element.Attribute("Grid.Column") == "2");
        var connectButton = statusPanel.Descendants(Presentation + "Button")
            .Single(element => ((string?)element.Attribute("Command"))?.Contains("ConnectCommand", StringComparison.Ordinal) == true);
        var styleKey = ExtractResourceKey(connectButton.Attribute("Style")!.Value);
        var primaryStyle = controls.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == styleKey);
        var actionBoundary = primaryStyle.Elements(Presentation + "Setter")
            .Single(element => (string?)element.Attribute("Property") == "BorderBrush")
            .Attribute("Value")!.Value;

        var panelColor = ResolveColor(statusPanel.Attribute("Background")!.Value, theme);
        var actionColor = ResolveColor(actionBoundary, theme);
        var contrastRatio = ContrastRatio(panelColor, actionColor);

        Assert.IsGreaterThanOrEqualTo(
            3d,
            contrastRatio,
            $"The Connect action edge has only {contrastRatio:F2}:1 contrast against its status panel, so it is not visibly discoverable.");
    }

    [TestMethod]
    public void HeaderNavigation_ExposesExactlyOneActiveDestination()
    {
        var layoutDirectory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var window = XDocument.Load(Path.Combine(layoutDirectory, "MainWindow.xaml"));
        var controls = XDocument.Load(Path.Combine(layoutDirectory, "Controls.xaml"));

        var navigation = window.Descendants(Presentation + "StackPanel")
            .Single(element => (string?)element.Attribute("Grid.Column") == "1")
            .Elements()
            .ToArray();

        Assert.IsNotEmpty(navigation);
        Assert.IsTrue(
            navigation.All(element => element.Name == Presentation + "RadioButton"),
            "Header destinations must form one selectable navigation group instead of independent action buttons.");
        Assert.AreEqual(
            1,
            navigation.Count(element => string.Equals((string?)element.Attribute("IsChecked"), "True", StringComparison.OrdinalIgnoreCase)),
            "Exactly one destination must be visibly active when the window opens.");
        Assert.AreEqual(
            1,
            navigation.Select(element => (string?)element.Attribute("GroupName")).Distinct(StringComparer.Ordinal).Count(),
            "All header destinations must share one radio group so selecting another destination clears the old active state.");

        var styleKey = ExtractResourceKey(navigation[0].Attribute("Style")!.Value);
        var navigationStyle = controls.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == styleKey);
        var checkedTrigger = navigationStyle.Descendants(Presentation + "Trigger")
            .Single(element =>
                (string?)element.Attribute("Property") == "IsChecked" &&
                string.Equals((string?)element.Attribute("Value"), "True", StringComparison.OrdinalIgnoreCase));
        var checkedSetters = checkedTrigger.Elements(Presentation + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);

        Assert.AreEqual("{DynamicResource Brush.Signal}", checkedSetters["BorderBrush"]);
        Assert.AreEqual("0,0,0,4", checkedSetters["BorderThickness"]);
        Assert.AreEqual("{DynamicResource Brush.TextPrimary}", checkedSetters["Foreground"]);
        Assert.AreEqual("SemiBold", checkedSetters["FontWeight"]);
    }

    [TestMethod]
    public void FormControls_CenterTheirLabelsVertically()
    {
        var layoutDirectory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var controls = XDocument.Load(Path.Combine(layoutDirectory, "Controls.xaml"));

        var commandButton = FindStyle(controls, "Button", "CommandButton");
        var comboBox = FindStyle(controls, "ComboBox");
        var comboBoxItem = FindStyle(controls, "ComboBoxItem");

        AssertStyleSetter(commandButton, "VerticalContentAlignment", "Center");
        AssertStyleSetter(comboBox, "VerticalContentAlignment", "Center");
        AssertStyleSetter(comboBoxItem, "VerticalContentAlignment", "Center");
    }

    [TestMethod]
    public void PrimaryButton_UsesGraphiteAccessibleStatesAndReservesSignalForFocus()
    {
        var layoutDirectory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var theme = LoadResources(Path.Combine(layoutDirectory, "Theme.xaml"));
        var controls = XDocument.Load(Path.Combine(layoutDirectory, "Controls.xaml"));
        var primary = FindStyle(controls, "Button", "PrimaryButton");
        var normalSetters = primary.Elements(Presentation + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);

        Assert.AreEqual("{DynamicResource Brush.ActionPrimary}", normalSetters["Background"]);
        Assert.AreEqual("{DynamicResource Brush.SurfacePrimary}", normalSetters["Foreground"]);
        Assert.AreEqual("{DynamicResource Brush.SurfacePrimary}", normalSetters["BorderBrush"]);
        Assert.IsFalse(normalSetters.Values.Contains("{DynamicResource Brush.Signal}", StringComparer.Ordinal),
            "Cyan signal cannot be the normal primary-action fill.");

        var triggers = primary.Descendants(Presentation + "Trigger")
            .ToDictionary(
                element => (Property: element.Attribute("Property")!.Value, Value: element.Attribute("Value")!.Value),
                element => element.Elements(Presentation + "Setter")
                    .ToDictionary(
                        setter => setter.Attribute("Property")!.Value,
                        setter => setter.Attribute("Value")!.Value,
                        StringComparer.Ordinal));

        Assert.AreEqual("{DynamicResource Brush.ActionPrimaryHover}", triggers[("IsMouseOver", "True")]["Background"]);
        Assert.AreEqual("{DynamicResource Brush.ActionPrimaryPressed}", triggers[("IsPressed", "True")]["Background"]);
        Assert.AreEqual("{DynamicResource Brush.Signal}", triggers[("IsKeyboardFocused", "True")]["BorderBrush"]);
        Assert.AreEqual("2", triggers[("IsKeyboardFocused", "True")]["BorderThickness"]);
        Assert.AreEqual("{DynamicResource Brush.ActionDisabled}", triggers[("IsEnabled", "False")]["Background"]);
        Assert.AreEqual("{DynamicResource Brush.ActionTextDisabled}", triggers[("IsEnabled", "False")]["Foreground"]);

        var foreground = ResolveColor(normalSetters["Foreground"], theme);
        foreach (var stateBackground in new[]
                 {
                     normalSetters["Background"],
                     triggers[("IsMouseOver", "True")]["Background"],
                     triggers[("IsPressed", "True")]["Background"]
                 })
        {
            var ratio = ContrastRatio(foreground, ResolveColor(stateBackground, theme));
            Assert.IsGreaterThanOrEqualTo(4.5d, ratio, $"Primary action state has only {ratio:F2}:1 text contrast.");
        }
    }

    private static XElement FindStyle(XDocument document, string targetType, string? key = null) =>
        document.Descendants(Presentation + "Style")
            .Single(element =>
                (string?)element.Attribute("TargetType") == targetType &&
                (string?)element.Attribute(Xaml + "Key") == key);

    private static void AssertStyleSetter(XElement style, string property, string expectedValue)
    {
        var actualValue = style.Elements(Presentation + "Setter")
            .SingleOrDefault(element => (string?)element.Attribute("Property") == property)
            ?.Attribute("Value")
            ?.Value;

        Assert.AreEqual(expectedValue, actualValue);
    }

    private static Dictionary<string, string> LoadResources(string path) =>
        XDocument.Load(path).Root!.Elements()
            .Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(Xaml + "Key")!.Value,
                element => (string?)element.Attribute("Color") ?? element.Value,
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

    private static string ExtractResourceKey(string value)
    {
        const string dynamicResourcePrefix = "{DynamicResource ";
        if (!value.StartsWith(dynamicResourcePrefix, StringComparison.Ordinal) || !value.EndsWith('}'))
        {
            throw new InvalidDataException($"Expected a DynamicResource reference, got: {value}");
        }

        return value[dynamicResourcePrefix.Length..^1];
    }

    private static RgbColor ResolveColor(string value, IReadOnlyDictionary<string, string> resources)
    {
        while (value.StartsWith("{DynamicResource ", StringComparison.Ordinal))
        {
            value = resources[ExtractResourceKey(value)];
        }

        if (value.Length != 7 || value[0] != '#')
        {
            throw new InvalidDataException($"Unsupported color value: {value}");
        }

        return new RgbColor(
            byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static double ContrastRatio(RgbColor first, RgbColor second)
    {
        var lighter = Math.Max(first.RelativeLuminance, second.RelativeLuminance);
        var darker = Math.Min(first.RelativeLuminance, second.RelativeLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private readonly record struct ThicknessValues(double Left, double Top, double Right, double Bottom);

    private readonly record struct RgbColor(byte Red, byte Green, byte Blue)
    {
        public double RelativeLuminance =>
            0.2126 * Linearize(Red) + 0.7152 * Linearize(Green) + 0.0722 * Linearize(Blue);

        private static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}
