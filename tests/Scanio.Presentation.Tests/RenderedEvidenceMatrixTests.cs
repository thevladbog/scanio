using Scanio.Presentation.Settings;
using Scanio.Presentation.Tests.Infrastructure;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class RenderedEvidenceMatrixTests
{
    [TestMethod]
    public void ConnectionMatrix_CoversEveryLanguageModeAndRequiredSize()
    {
        var actual = RenderedEvidenceMatrix.Connection
            .Select(variant => (
                variant.Language,
                variant.ConnectionMode,
                variant.Width,
                variant.Height))
            .ToArray();
        var expected = new[]
        {
            (UiLanguage.Russian, ConnectionMode.Serial, 1024, 700),
            (UiLanguage.Russian, ConnectionMode.Serial, 1440, 900),
            (UiLanguage.Russian, ConnectionMode.Keyboard, 1024, 700),
            (UiLanguage.Russian, ConnectionMode.Keyboard, 1440, 900),
            (UiLanguage.English, ConnectionMode.Serial, 1024, 700),
            (UiLanguage.English, ConnectionMode.Serial, 1440, 900),
            (UiLanguage.English, ConnectionMode.Keyboard, 1024, 700),
            (UiLanguage.English, ConnectionMode.Keyboard, 1440, 900)
        };

        CollectionAssert.AreEquivalent(expected, actual);
    }

    [TestMethod]
    public void DensityMatrix_CoversBothDensitiesOnEveryMainListSurface()
    {
        var actual = RenderedEvidenceMatrix.Density
            .Select(variant => (variant.Destination, variant.ListDensity))
            .ToArray();
        var expected = new[]
        {
            (ShellDestination.Connection, ListDensity.Compact),
            (ShellDestination.Connection, ListDensity.Comfortable),
            (ShellDestination.Monitor, ListDensity.Compact),
            (ShellDestination.Monitor, ListDensity.Comfortable),
            (ShellDestination.Notebook, ListDensity.Compact),
            (ShellDestination.Notebook, ListDensity.Comfortable),
            (ShellDestination.History, ListDensity.Compact),
            (ShellDestination.History, ListDensity.Comfortable)
        };

        CollectionAssert.AreEquivalent(expected, actual);
    }

    [TestMethod]
    public void MonitorMatrix_CoversHexAndChunksTogetherOffAndOn()
    {
        var actual = RenderedEvidenceMatrix.Monitor
            .Select(variant => (variant.ShowHexPreview, variant.ShowChunkBoundaries))
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { (false, false), (true, true) },
            actual);
    }

    [TestMethod]
    public void EveryEvidenceVariant_HasAStableDistinctScreenshotName()
    {
        var variants = RenderedEvidenceMatrix.All.ToArray();
        var names = variants.Select(variant => variant.ScreenshotName).ToArray();

        Assert.HasCount(18, variants);
        Assert.AreEqual(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(names.All(name => name.EndsWith(".png", StringComparison.Ordinal)));
        Assert.IsTrue(names.All(name => name.Contains('x', StringComparison.Ordinal)));
    }
}
