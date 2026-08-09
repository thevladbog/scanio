using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.Localization;

public sealed class UiLocalizer : IUiLocalizer
{
    private static readonly ResourceManager ResourceManager = new(
        "Scanio.Presentation.Resources.Strings",
        typeof(UiLocalizer).Assembly);
    private readonly IAppSettingsService _settings;
    private UiLanguage _language;

    public UiLocalizer(IAppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _language = settings.Current.Language;
        ApplyCulture(_language);
        _settings.Changed += OnSettingsChanged;
    }

    public UiLanguage Language => _language;

    public string this[string key]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return ResourceManager.GetString(key, CultureFor(_language)) ?? key;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLanguage(UiLanguage language) =>
        _settings.Update(settings => settings with { Language = language });

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        var language = _settings.Current.Language;
        if (language == _language)
        {
            return;
        }

        _language = language;
        ApplyCulture(language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    private static CultureInfo CultureFor(UiLanguage language) =>
        CultureInfo.GetCultureInfo(language == UiLanguage.Russian ? "ru-RU" : "en-US");

    private static void ApplyCulture(UiLanguage language)
    {
        var culture = CultureFor(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
