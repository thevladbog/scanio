using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class NotebookLayoutContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [TestMethod]
    public void Notebook_UsesAdaptiveRowsAndExplicitActions()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "NotebookView.xaml"));

        Assert.IsEmpty(document.Descendants(Presentation + "GridView"));
        var records = document.Descendants(Presentation + "ListBox")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding Records}");
        Assert.AreEqual("Disabled", (string?)records.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));

        foreach (var command in new[]
                 {
                     "CopyAllCommand", "CopyUniqueCommand", "CopyEscapedCommand",
                     "ExportTextCommand", "ExportReadableTextCommand", "ExportCsvCommand", "ExportJsonCommand"
                 })
        {
            Assert.IsNotNull(document.Descendants(Presentation + "Button")
                .SingleOrDefault(element => (string?)element.Attribute("Command") == $"{{Binding {command}}}"), command);
        }
    }

    [TestMethod]
    public void Notebook_RendersNonBlockingUniqueAndDuplicateArrivalPulses()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LayoutContracts", "NotebookView.xaml"));

        var pulse = document.Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute("IsHitTestVisible") == "False");
        Assert.AreEqual("0", (string?)pulse.Attribute("Opacity"));
        Assert.AreEqual("5", (string?)pulse.Attribute("Grid.ColumnSpan"));

        var triggers = pulse.Descendants(Presentation + "MultiDataTrigger").ToArray();
        Assert.HasCount(2, triggers);
        CollectionAssert.AreEquivalent(
            new[] { "{DynamicResource Brush.SignalSurface}", "{DynamicResource Brush.WarningSurface}" },
            triggers.Select(trigger => trigger.Elements(Presentation + "Setter")
                .Single(setter => (string?)setter.Attribute("Property") == "Background")
                .Attribute("Value")?.Value).ToArray());
        Assert.IsTrue(triggers.All(trigger => trigger.Descendants(Presentation + "Condition")
            .Any(condition => (string?)condition.Attribute("Binding") == "{Binding IsArrivalPulseActive}"
                              && (string?)condition.Attribute("Value") == "True")));
        CollectionAssert.AreEquivalent(
            new[] { "False", "True" },
            triggers.Select(trigger => trigger.Descendants(Presentation + "Condition")
                .Single(condition => (string?)condition.Attribute("Binding") == "{Binding IsDuplicate}")
                .Attribute("Value")?.Value).ToArray());
        Assert.IsTrue(triggers.All(trigger => trigger.Descendants(Presentation + "DoubleAnimation")
            .Any(animation => (string?)animation.Attribute("Duration") == "0:0:0.6")));
    }

    [TestMethod]
    public void NotebookAndHistory_ExposeReadableTextExportAction()
    {
        foreach (var fileName in new[] { "NotebookView.xaml", "HistoryView.xaml" })
        {
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LayoutContracts", fileName));
            Assert.IsNotNull(document.Descendants(Presentation + "Button")
                .SingleOrDefault(element =>
                    (string?)element.Attribute("Command") == "{Binding ExportReadableTextCommand}"), fileName);
        }
    }

    [TestMethod]
    public void Notebook_ReadableTextResourcesUseVisibleGsLabels()
    {
        AssertResourceValues(
            "Strings.resx",
            "Копировать как текст (<GS>)",
            "Экспорт TXT как текст (<GS>)");
        AssertResourceValues(
            "Strings.en.resx",
            "Copy as readable text (<GS>)",
            "Export readable TXT (<GS>)");
    }

    private static void AssertResourceValues(string fileName, string copy, string export)
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LayoutContracts", fileName));
        var values = document.Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")!.Value,
            StringComparer.Ordinal);

        Assert.AreEqual(copy, values["Notebook.CopyEscaped"]);
        Assert.AreEqual(export, values["Notebook.ExportReadableText"]);
    }
}
