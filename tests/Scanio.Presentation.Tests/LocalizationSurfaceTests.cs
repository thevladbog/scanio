using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class LocalizationSurfaceTests
{
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

    private static string[] ResourceKeys(string path) => XDocument.Load(path)
        .Root!
        .Elements("data")
        .Select(element => element.Attribute("name")!.Value)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
