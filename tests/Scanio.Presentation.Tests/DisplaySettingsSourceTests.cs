using System.ComponentModel;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DisplaySettingsSourceTests
{
    [TestMethod]
    public void SettingsChanges_UpdateEveryDisplayValueImmediately()
    {
        var settings = new TestSettingsService();
        DisplaySettingsSource.Initialize(settings);
        var source = DisplaySettingsSource.Current;

        settings.Update(value => value with
        {
            ShowEscapedControls = false,
            ShowHexPreview = false,
            ShowChunkBoundaries = false,
            ListDensity = ListDensity.Compact
        });

        Assert.IsFalse(source.ShowEscapedControls);
        Assert.IsFalse(source.ShowHexPreview);
        Assert.IsFalse(source.ShowChunkBoundaries);
        Assert.AreEqual(54d, source.LedgerRowHeight);
    }

    [TestMethod]
    public void ComfortableDensity_RaisesOnlyChangedDisplayPropertiesWithExactHeight()
    {
        var settings = new TestSettingsService(new AppSettings(ListDensity: ListDensity.Compact));
        DisplaySettingsSource.Initialize(settings);
        var source = DisplaySettingsSource.Current;
        var notifications = new List<string?>();
        source.PropertyChanged += OnPropertyChanged;

        settings.Update(value => value with
        {
            ShowHexPreview = false,
            ListDensity = ListDensity.Comfortable
        });

        source.PropertyChanged -= OnPropertyChanged;
        Assert.AreEqual(66d, source.LedgerRowHeight);
        CollectionAssert.AreEquivalent(
            new[] { nameof(DisplaySettingsSource.ShowHexPreview), nameof(DisplaySettingsSource.LedgerRowHeight) },
            notifications);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => notifications.Add(args.PropertyName);
    }

    [TestMethod]
    public void Reinitialize_UnsubscribesThePriorServiceAndPublishesTheReplacementSnapshot()
    {
        var prior = new TestSettingsService(new AppSettings(ShowHexPreview: false));
        var replacement = new TestSettingsService(new AppSettings(ShowHexPreview: true));
        DisplaySettingsSource.Initialize(prior);
        DisplaySettingsSource.Initialize(replacement);
        var source = DisplaySettingsSource.Current;
        var notifications = new List<string?>();
        source.PropertyChanged += OnPropertyChanged;

        prior.Update(value => value with { ShowHexPreview = true });
        replacement.Update(value => value with { ShowHexPreview = false });

        source.PropertyChanged -= OnPropertyChanged;
        Assert.IsFalse(source.ShowHexPreview);
        CollectionAssert.AreEqual(new[] { nameof(DisplaySettingsSource.ShowHexPreview) }, notifications);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => notifications.Add(args.PropertyName);
    }

    private sealed class TestSettingsService : IAppSettingsService
    {
        public TestSettingsService(AppSettings? current = null) => Current = current ?? new AppSettings();

        public AppSettings Current { get; private set; }

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
}
