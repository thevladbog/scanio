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
            .Select(variant => (variant.Language, variant.Destination, variant.ListDensity))
            .ToArray();
        var expected = new[]
        {
            UiLanguage.Russian,
            UiLanguage.English
        }.SelectMany(language => new[]
        {
            (language, ShellDestination.Connection, ListDensity.Compact),
            (language, ShellDestination.Connection, ListDensity.Comfortable),
            (language, ShellDestination.Monitor, ListDensity.Compact),
            (language, ShellDestination.Monitor, ListDensity.Comfortable),
            (language, ShellDestination.Notebook, ListDensity.Compact),
            (language, ShellDestination.Notebook, ListDensity.Comfortable),
            (language, ShellDestination.History, ListDensity.Compact),
            (language, ShellDestination.History, ListDensity.Comfortable)
        }).ToArray();

        CollectionAssert.AreEquivalent(expected, actual);
    }

    [TestMethod]
    public void MonitorMatrix_CoversHexAndChunksTogetherOffAndOn()
    {
        var actual = RenderedEvidenceMatrix.Monitor
            .Select(variant => (variant.Language, variant.ShowHexPreview, variant.ShowChunkBoundaries))
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                (UiLanguage.Russian, false, false),
                (UiLanguage.Russian, true, true),
                (UiLanguage.English, false, false),
                (UiLanguage.English, true, true)
            },
            actual);
    }

    [TestMethod]
    public void SettingsMatrix_CoversCompactAndComfortableDensityEvidence()
    {
        var actual = RenderedEvidenceMatrix.Settings
            .Select(variant => (variant.Language, variant.Destination, variant.ListDensity, variant.Width, variant.Height))
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                (UiLanguage.Russian, ShellDestination.Settings, ListDensity.Compact, 1440, 900),
                (UiLanguage.Russian, ShellDestination.Settings, ListDensity.Comfortable, 1440, 900),
                (UiLanguage.English, ShellDestination.Settings, ListDensity.Compact, 1440, 900),
                (UiLanguage.English, ShellDestination.Settings, ListDensity.Comfortable, 1440, 900)
            },
            actual);
    }

    [TestMethod]
    public void EveryEvidenceVariant_HasAStableDistinctScreenshotName()
    {
        var variants = RenderedEvidenceMatrix.All.ToArray();
        var names = variants.Select(variant => variant.ScreenshotName).ToArray();

        Assert.HasCount(32, variants);
        Assert.AreEqual(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(names.All(name => name.EndsWith(".png", StringComparison.Ordinal)));
        Assert.IsTrue(names.All(name => name.Contains('x', StringComparison.Ordinal)));
    }
}
