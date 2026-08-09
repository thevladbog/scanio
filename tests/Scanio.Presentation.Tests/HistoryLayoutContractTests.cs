using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class HistoryLayoutContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [TestMethod]
    public void History_UsesAdaptiveRowsAndExplicitActions()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "HistoryView.xaml"));

        Assert.IsEmpty(document.Descendants(Presentation + "GridView"));
        var records = document.Descendants(Presentation + "ListBox")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding Records}");
        Assert.AreEqual("Disabled", (string?)records.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));

        foreach (var command in new[]
                 {
                     "CopyAllCommand", "CopyUniqueCommand", "CopyEscapedCommand",
                     "ExportTextCommand", "ExportCsvCommand", "ExportJsonCommand", "DeleteCommand"
                 })
        {
            Assert.IsNotNull(document.Descendants(Presentation + "Button")
                .SingleOrDefault(element => (string?)element.Attribute("Command") == $"{{Binding {command}}}"), command);
        }
    }

    [TestMethod]
    public void History_UsesLocalizedOccurrenceLabelInTheLastRecordColumn()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "HistoryView.xaml"));

        var lastColumn = document.Descendants(Presentation + "TextBlock")
            .Single(element => (string?)element.Attribute("Grid.Column") == "4");
        Assert.AreEqual("{Binding OccurrenceLabel}", (string?)lastColumn.Attribute("Text"));
        Assert.IsFalse(lastColumn.Attributes().Any(attribute => attribute.Value.Contains("StringFormat=×", StringComparison.Ordinal)));
    }
}
