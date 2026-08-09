using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public void LanguageAndDisplaySettings_ApplyImmediately()
    {
        var settings = new TestSettingsService();
        var localizer = new UiLocalizer(settings);
        var viewModel = CreateViewModel(settings, localizer, new FakePlatformInteraction());

        viewModel.IsEnglish = true;
        viewModel.ShowHexPreview = false;
        viewModel.ShowChunkBoundaries = false;
        viewModel.IsCompact = true;

        Assert.AreEqual(UiLanguage.English, localizer.Language);
        Assert.IsTrue(viewModel.IsEnglish);
        Assert.IsFalse(viewModel.IsRussian);
        Assert.IsFalse(settings.Current.ShowHexPreview);
        Assert.IsFalse(settings.Current.ShowChunkBoundaries);
        Assert.AreEqual(ListDensity.Compact, settings.Current.ListDensity);
    }

    [TestMethod]
    public async Task LocalDataActions_OpenOnlyTheRealFolderAndReleasePage()
    {
        var settings = new TestSettingsService();
        var platform = new FakePlatformInteraction();
        var viewModel = CreateViewModel(settings, new UiLocalizer(settings), platform);

        await viewModel.OpenDataFolderCommand.ExecuteAsync();
        await viewModel.OpenReleasesCommand.ExecuteAsync();

        Assert.AreEqual(Path.Combine(Path.DirectorySeparatorChar.ToString(), "Scanio", "Data"), platform.OpenedFolder);
        Assert.AreEqual("https://github.com/thevladbog/scanio/releases", platform.OpenedUri?.AbsoluteUri.TrimEnd('/'));
        Assert.IsTrue(viewModel.IsPortable);
        Assert.AreEqual(Path.Combine(Path.DirectorySeparatorChar.ToString(), "Scanio", "Data", "scanio.db"), viewModel.DatabasePath);
    }

    private static SettingsViewModel CreateViewModel(
        IAppSettingsService settings,
        IUiLocalizer localizer,
        IPlatformInteractionService platform) =>
        new(
            settings,
            localizer,
            platform,
            isPortable: true,
            databasePath: Path.Combine(Path.DirectorySeparatorChar.ToString(), "Scanio", "Data", "scanio.db"),
            applicationVersion: "0.4.0-alpha.1",
            releasesUri: new Uri("https://github.com/thevladbog/scanio/releases"));

    private sealed class TestSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler? Changed;

        public void Update(Func<AppSettings, AppSettings> update)
        {
            var next = update(Current);
            if (next == Current)
            {
                return;
            }

            Current = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakePlatformInteraction : IPlatformInteractionService
    {
        public string? OpenedFolder { get; private set; }
        public Uri? OpenedUri { get; private set; }

        public void OpenFolder(string path) => OpenedFolder = path;

        public void OpenUri(Uri uri) => OpenedUri = uri;
    }
}
