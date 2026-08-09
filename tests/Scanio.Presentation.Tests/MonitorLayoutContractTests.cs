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

    private static XDocument Load() => XDocument.Load(
        Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "MonitorView.xaml"));
}
