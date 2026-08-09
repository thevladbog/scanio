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
}
