using System.ComponentModel;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class LocalizationTests
{
    [TestMethod]
    public void FirstRun_IsRussianAndStandardsRemainNeutral()
    {
        var settings = new InMemorySettingsService(new AppSettings());
        var localizer = new UiLocalizer(settings);

        Assert.AreEqual("Подключение", localizer[UiTextKeys.NavConnection]);
        Assert.AreEqual("HEX", localizer[UiTextKeys.StandardHex]);
        Assert.AreEqual(UiLanguage.Russian, localizer.Language);
    }

    [TestMethod]
    public void SetLanguage_SwitchesImmediatelyPersistsAndRaisesOneIndexerNotification()
    {
        var settings = new InMemorySettingsService(new AppSettings());
        var localizer = new UiLocalizer(settings);
        var notifications = new List<string?>();
        localizer.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        localizer.SetLanguage(UiLanguage.English);

        Assert.AreEqual("Connection", localizer[UiTextKeys.NavConnection]);
        Assert.AreEqual(UiLanguage.English, settings.Current.Language);
        CollectionAssert.AreEqual(new[] { "Item[]", nameof(UiLocalizer.Language) }, notifications);
    }

    [TestMethod]
    public void MissingResource_ReturnsItsStableKey()
    {
        var localizer = new UiLocalizer(new InMemorySettingsService(new AppSettings()));

        Assert.AreEqual("Missing.Resource", localizer["Missing.Resource"]);
    }

    private sealed class InMemorySettingsService : IAppSettingsService
    {
        public InMemorySettingsService(AppSettings current) => Current = current;

        public AppSettings Current { get; private set; }

        public event EventHandler? Changed;

        public void Update(Func<AppSettings, AppSettings> update)
        {
            Current = update(Current);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
