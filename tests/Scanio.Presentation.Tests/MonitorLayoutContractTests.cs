using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class MonitorLayoutContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void Monitor_UsesAdaptiveLedgerRowsAndNoGridView()
    {
        var monitor = Load();

        Assert.IsEmpty(monitor.Descendants(Presentation + "GridView"));
        Assert.IsEmpty(monitor.Descendants(Presentation + "GridViewColumn"));

        var ledger = monitor.Descendants(Presentation + "ListBox")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding Events}");
        Assert.AreEqual("Disabled", (string?)ledger.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));
        Assert.AreEqual("{DynamicResource LedgerListBox}", (string?)ledger.Attribute("Style"));
    }

    [TestMethod]
    public void Monitor_KeepsPrimaryEvidenceAndCopyOutsideAnalysisScroll()
    {
        var monitor = Load();
        var copy = monitor.Descendants(Presentation + "Button")
            .Single(element => (string?)element.Attribute("Command") == "{Binding CopyCodeCommand}");
        var analysisScroll = monitor.Descendants(Presentation + "ScrollViewer")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "AnalysisScroll");

        Assert.IsFalse(copy.Ancestors(Presentation + "ScrollViewer").Any());
        Assert.IsNotNull(monitor.Descendants(Presentation + "Border")
            .SingleOrDefault(element => (string?)element.Attribute(Xaml + "Name") == "RawEvidence"));
        Assert.IsFalse(monitor.Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "RawEvidence")
            .Ancestors().Contains(analysisScroll));
    }

    [TestMethod]
    public void Monitor_TitleWrapsInsteadOfClippingBesideTheCopyAction()
    {
        var monitor = Load();
        var title = monitor.Descendants(Presentation + "TextBlock")
            .Single(element => ((string?)element.Attribute("Text"))?.Contains("Monitor.Title", StringComparison.Ordinal) == true);

        Assert.AreEqual("Wrap", (string?)title.Attribute("TextWrapping"));
    }

    [TestMethod]
    public void Monitor_LedgerUsesPlainSequenceNumbers()
    {
        var monitor = Load();
        var sequence = monitor.Descendants(Presentation + "ListBox")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding Events}")
            .Descendants(Presentation + "TextBlock")
            .Single(element => ((string?)element.Attribute("Text"))?.Contains("Binding Sequence", StringComparison.Ordinal) == true);

        var text = (string?)sequence.Attribute("Text") ?? string.Empty;
        var formatStart = text.IndexOf("StringFormat=", StringComparison.Ordinal);
        var stringFormat = formatStart < 0
            ? null
            : text[(formatStart + "StringFormat=".Length)..(text.IndexOf('}', formatStart) + 1)];

        Assert.IsTrue(stringFormat is null or "{0}");
    }

    [TestMethod]
    public void Monitor_ExposesStructuredInspectorSections()
    {
        var monitor = Load();
        var names = monitor.Descendants()
            .Select(element => (string?)element.Attribute(Xaml + "Name"))
            .Where(name => name is not null)
            .ToArray();

        CollectionAssert.Contains(names, "ConnectionInspector");
        CollectionAssert.Contains(names, "MeasurementInspector");
        CollectionAssert.Contains(names, "ChunksInspector");
    }

    [TestMethod]
    public void Monitor_DisplaySectionsBindVisibilityToTheSharedSettingsSource()
    {
        var monitor = Load();
        var rawEvidence = monitor.Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "RawEvidence");
        var hex = rawEvidence.Descendants(Presentation + "StackPanel")
            .Single(element => (string?)element.Attribute("Grid.Column") == "2");
        var chunks = monitor.Descendants(Presentation + "Grid")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "ChunksInspector");

        Assert.AreEqual(
            "{Binding Source={x:Static settings:DisplaySettingsSource.Current}, Path=ShowHexPreview, Converter={StaticResource BooleanToVisibility}}",
            (string?)hex.Attribute("Visibility"));
        Assert.AreEqual(
            "{Binding Source={x:Static settings:DisplaySettingsSource.Current}, Path=ShowChunkBoundaries, Converter={StaticResource BooleanToVisibility}}",
            (string?)chunks.Attribute("Visibility"));
    }

    [TestMethod]
    public void WorkspaceListsUseDensityAwareContainerStyles()
    {
        var layoutDirectory = Path.Combine(AppContext.BaseDirectory, "LayoutContracts");
        AssertContainerStyle("MonitorView.xaml", "ListBox", "ItemsSource", "{Binding Events}", "{DynamicResource DensityAwareLedgerRow}");
        AssertContainerStyle("NotebookView.xaml", "ListBox", "ItemsSource", "{Binding Records}", "{DynamicResource DensityAwareLedgerRow}");
        AssertContainerStyle("HistoryView.xaml", "ListBox", "ItemsSource", "{Binding Sessions}", "{DynamicResource DensityAwareLedgerRow}");
        AssertContainerStyle("HistoryView.xaml", "ListBox", "ItemsSource", "{Binding Records}", "{DynamicResource DensityAwareLedgerRow}");
        AssertContainerStyle("ConnectionView.xaml", "ListView", "ItemsSource", "{Binding Devices}", "{DynamicResource DensityAwareDeviceRow}");

        void AssertContainerStyle(string fileName, string elementName, string selectorAttribute, string selectorValue, string expectedStyle)
        {
            var document = XDocument.Load(Path.Combine(layoutDirectory, fileName));
            var list = document.Descendants(Presentation + elementName)
                .Single(element => (string?)element.Attribute(selectorAttribute) == selectorValue);
            Assert.AreEqual(expectedStyle, (string?)list.Attribute("ItemContainerStyle"), fileName);
        }
    }

    [TestMethod]
    public void DensityAwareStylesBindExactRowHeightFromTheSharedSettingsSource()
    {
        var controls = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "Controls.xaml"));

        foreach (var styleName in new[] { "DensityAwareLedgerRow", "DensityAwareDeviceRow" })
        {
            var style = controls.Descendants(Presentation + "Style")
                .Single(element => (string?)element.Attribute(Xaml + "Key") == styleName);
            var minHeight = style.Elements(Presentation + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "MinHeight");
            Assert.AreEqual(
                "{Binding Source={x:Static settings:DisplaySettingsSource.Current}, Path=LedgerRowHeight}",
                (string?)minHeight.Attribute("Value"),
                styleName);
        }
    }

    private static XDocument Load() => XDocument.Load(
        Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "MonitorView.xaml"));
}
