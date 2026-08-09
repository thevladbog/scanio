using System.Text.Json;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class SettingsTests
{
    [TestMethod]
    public void MissingFile_UsesRussianComfortableDefaults()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonAppSettingsStore(Path.Combine(directory.Path, "settings.json"));

        var settings = store.Load();

        Assert.AreEqual(UiLanguage.Russian, settings.Language);
        Assert.AreEqual(ListDensity.Comfortable, settings.ListDensity);
        Assert.IsTrue(settings.ShowEscapedControls);
        Assert.IsTrue(settings.ShowHexPreview);
        Assert.IsTrue(settings.ShowChunkBoundaries);
        Assert.IsTrue(settings.FollowLatestByDefault);
    }

    [TestMethod]
    public void Save_RoundTripsAllSettingsAndLeavesNoTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new JsonAppSettingsStore(path);
        var expected = new AppSettings(
            UiLanguage.English,
            ShowEscapedControls: false,
            ShowHexPreview: false,
            ShowChunkBoundaries: false,
            FollowLatestByDefault: false,
            ListDensity.Compact);

        store.Save(expected);

        Assert.AreEqual(expected, store.Load());
        Assert.IsFalse(File.Exists(path + ".tmp"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual("English", document.RootElement.GetProperty("language").GetString());
    }

    [TestMethod]
    public void CorruptFile_FallsBackToDefaultsWithoutOverwritingEvidence()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{ not-json");
        var store = new JsonAppSettingsStore(path);

        var settings = store.Load();

        Assert.AreEqual(new AppSettings(), settings);
        Assert.AreEqual("{ not-json", File.ReadAllText(path));
    }

    [TestMethod]
    public void ResolvePath_SeparatesPortableAndInstalledModes()
    {
        Assert.AreEqual(
            Path.Combine("C:\\Scanio", "Data", "settings.json"),
            JsonAppSettingsStore.ResolvePath(true, "C:\\Scanio", "C:\\Users\\User\\AppData\\Local"));
        Assert.AreEqual(
            Path.Combine("C:\\Users\\User\\AppData\\Local", "Scanio", "settings.json"),
            JsonAppSettingsStore.ResolvePath(false, "C:\\Scanio", "C:\\Users\\User\\AppData\\Local"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scanio-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
