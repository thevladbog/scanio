using Scanio.Presentation.Layout;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class WorkspaceLayoutClassifierTests
{
    [TestMethod]
    [DataRow(1024d, WorkspaceLayoutMode.Compact)]
    [DataRow(1179d, WorkspaceLayoutMode.Compact)]
    [DataRow(1180d, WorkspaceLayoutMode.Medium)]
    [DataRow(1319d, WorkspaceLayoutMode.Medium)]
    [DataRow(1320d, WorkspaceLayoutMode.Wide)]
    [DataRow(1440d, WorkspaceLayoutMode.Wide)]
    public void Classify_UsesApprovedDipBoundaries(double width, WorkspaceLayoutMode expected) =>
        Assert.AreEqual(expected, WorkspaceLayoutClassifier.Classify(width));

    [TestMethod]
    public void Classify_RejectsUnsupportedWidths() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceLayoutClassifier.Classify(1023.99));
}
