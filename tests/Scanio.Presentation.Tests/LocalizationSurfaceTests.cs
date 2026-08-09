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

    private static readonly HashSet<string> RussianNeutralWords = new(StringComparer.Ordinal)
    {
        "AI", "BCBP", "COM", "CSV", "EAN/UPC", "English", "Enter", "GS", "GS1", "GTIN",
        "GitHub", "HEX", "HTTP", "HTTPS", "IATA", "JSON", "RAW", "Releases", "RTS", "Scanio", "SQLite",
        "SSCC", "Tab", "TXT", "URL", "USB", "UTF-8", "Windows", "XON/XOFF"
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

    [TestMethod]
    public void RussianDisplayResources_ContainOnlyRussianCopyAndApprovedTechnicalTokens()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        var russian = ResourceValues(Path.Combine(directory, "Strings.resx"));
        var retiredCopy = new[]
        {
            "Терминатор",
            "Пауза чтения",
            "Точный формат данных",
            "Предположение по структуре",
            "Строка элементов GS1"
        };

        var leakedWords = russian
            .SelectMany(pair => LatinWords(pair.Value).Select(word => $"{pair.Key}: {word}"))
            .Where(entry => !RussianNeutralWords.Contains(entry[(entry.LastIndexOf(' ') + 1)..]))
            .ToArray();
        var retiredValues = russian
            .Where(pair => retiredCopy.Contains(pair.Value, StringComparer.Ordinal))
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToArray();

        Assert.IsEmpty(
            leakedWords,
            $"Russian display copy leaks non-approved English words: {string.Join(", ", leakedWords)}");
        Assert.IsFalse(
            russian.Values.Any(value => value.Contains("Termination", StringComparison.Ordinal) ||
                                         value.Contains("Portable", StringComparison.Ordinal)),
            "Russian display copy must translate known Termination and Portable leaks.");
        Assert.IsEmpty(
            retiredValues,
            $"Russian display copy still contains retired literal phrasing: {string.Join(", ", retiredValues)}");
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

    private static IEnumerable<string> LatinWords(string value)
    {
        var start = -1;
        for (var index = 0; index <= value.Length; index++)
        {
            var character = index < value.Length ? value[index] : '\0';
            var belongsToWord = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or
                '/' or '+' or '-' or '.';
            if (belongsToWord && start < 0)
            {
                start = index;
            }
            else if (!belongsToWord && start >= 0)
            {
                var word = value[start..index].TrimEnd('.', '-', '/');
                if (word.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
                {
                    yield return word;
                }

                start = -1;
            }
        }
    }
}
