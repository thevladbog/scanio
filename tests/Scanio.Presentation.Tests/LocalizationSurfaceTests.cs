using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class LocalizationSurfaceTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> NeutralLiterals = new(StringComparer.Ordinal)
    {
        "RAW", "HEX", "PAYLOAD", "DTR", "RTS", "•••", "—"
    };

    [TestMethod]
    public void EveryVisibleXamlLiteral_IsNeutralOrResourceBound()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        foreach (var file in new[]
                 {
                     "MainWindow.xaml", "ConnectionView.xaml", "MonitorView.xaml",
                     "NotebookView.xaml", "HistoryView.xaml", "SettingsView.xaml"
                 })
        {
            var document = XDocument.Load(Path.Combine(directory, file));
            var offenders = document.Root!.DescendantsAndSelf()
                .Attributes()
                .Where(attribute => attribute.Name.LocalName is "Text" or "Content" or "Header" or "Title")
                .Select(attribute => attribute.Value)
                .Where(value =>
                    !value.StartsWith('{') &&
                    !NeutralLiterals.Contains(value))
                .ToArray();

            Assert.IsEmpty(offenders, $"{file} contains visible text that cannot switch language: {string.Join(", ", offenders)}");
        }
    }

    [TestMethod]
    public void RussianAndEnglishResources_HaveTheSameKeys()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var russian = ResourceKeys(Path.Combine(directory, "Strings.resx"));
        var english = ResourceKeys(Path.Combine(directory, "Strings.en.resx"));

        CollectionAssert.AreEquivalent(russian, english);
    }

    [TestMethod]
    public void Connection_ContainsTwoTransportSelectorsAndDedicatedKeyboardSurface()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var document = XDocument.Load(Path.Combine(directory, "ConnectionView.xaml"));
        var selectors = document.Descendants(Presentation + "RadioButton")
            .Where(element => (string?)element.Attribute("GroupName") == "ConnectionMode")
            .ToArray();

        Assert.HasCount(2, selectors);
        Assert.IsNotNull(document.Descendants().SingleOrDefault(element =>
            (string?)element.Attribute(Xaml + "Name") == "KeyboardCaptureSurface"));
        Assert.IsNotNull(document.Descendants().SingleOrDefault(element =>
            (string?)element.Attribute(Xaml + "Name") == "KeyboardCaptureInput"));
    }

    [TestMethod]
    public void KeyboardCaptureWarning_IsLocalizedAndDisclosesReconstructedFocusedInput()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var russian = ResourceValues(Path.Combine(directory, "Strings.resx"));
        var english = ResourceValues(Path.Combine(directory, "Strings.en.resx"));

        Assert.IsTrue(russian["Connection.Keyboard.Warning"].Contains("обычный ввод", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(russian["Connection.Keyboard.Warning"].Contains("USB", StringComparison.Ordinal));
        Assert.IsTrue(english["Connection.Keyboard.Warning"].Contains("normal typing", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(english["Connection.Keyboard.Warning"].Contains("raw USB", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(english["Connection.Keyboard.Warning"].Contains("focused", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] ResourceKeys(string path) => XDocument.Load(path)
        .Root!
        .Elements("data")
        .Select(element => element.Attribute("name")!.Value)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static Dictionary<string, string> ResourceValues(string path) => XDocument.Load(path)
        .Root!
        .Elements("data")
        .ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")!.Value,
            StringComparer.Ordinal);
}
