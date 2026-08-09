using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Presentation.Settings;
using System.IO;

namespace Scanio.Presentation.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly IUiLocalizer _localizer;
    private readonly IPlatformInteractionService _platform;
    private readonly Uri _releasesUri;

    public SettingsViewModel(
        IAppSettingsService settings,
        IUiLocalizer localizer,
        IPlatformInteractionService platform,
        bool isPortable,
        string databasePath,
        string applicationVersion,
        Uri releasesUri)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ArgumentNullException.ThrowIfNull(releasesUri);
        _settings = settings;
        _localizer = localizer;
        _platform = platform;
        _releasesUri = releasesUri;
        IsPortable = isPortable;
        DatabasePath = databasePath;
        ApplicationVersion = applicationVersion;
        DataFolder = Path.GetDirectoryName(databasePath)
            ?? throw new ArgumentException("The database path must have a parent directory.", nameof(databasePath));
        OpenDataFolderCommand = new AsyncCommand(_ =>
        {
            _platform.OpenFolder(DataFolder);
            return Task.CompletedTask;
        });
        OpenReleasesCommand = new AsyncCommand(_ =>
        {
            _platform.OpenUri(_releasesUri);
            return Task.CompletedTask;
        });
        _settings.Changed += OnSettingsChanged;
        _localizer.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(ModeLabel));
        };
    }

    public bool IsPortable { get; }

    public string DatabasePath { get; }

    public string DataFolder { get; }

    public string ApplicationVersion { get; }

    public string ModeLabel => _localizer[IsPortable ? "Settings.Portable" : "Settings.Installed"];

    public bool IsRussian
    {
        get => _settings.Current.Language == UiLanguage.Russian;
        set
        {
            if (value)
            {
                _localizer.SetLanguage(UiLanguage.Russian);
            }
        }
    }

    public bool IsEnglish
    {
        get => _settings.Current.Language == UiLanguage.English;
        set
        {
            if (value)
            {
                _localizer.SetLanguage(UiLanguage.English);
            }
        }
    }

    public bool ShowEscapedControls
    {
        get => _settings.Current.ShowEscapedControls;
        set => Update(settings => settings with { ShowEscapedControls = value });
    }

    public bool ShowHexPreview
    {
        get => _settings.Current.ShowHexPreview;
        set => Update(settings => settings with { ShowHexPreview = value });
    }

    public bool ShowChunkBoundaries
    {
        get => _settings.Current.ShowChunkBoundaries;
        set => Update(settings => settings with { ShowChunkBoundaries = value });
    }

    public bool IsCompact
    {
        get => _settings.Current.ListDensity == ListDensity.Compact;
        set
        {
            if (value)
            {
                Update(settings => settings with { ListDensity = ListDensity.Compact });
            }
        }
    }

    public bool IsComfortable
    {
        get => _settings.Current.ListDensity == ListDensity.Comfortable;
        set
        {
            if (value)
            {
                Update(settings => settings with { ListDensity = ListDensity.Comfortable });
            }
        }
    }

    public AsyncCommand OpenDataFolderCommand { get; }

    public AsyncCommand OpenReleasesCommand { get; }

    private void Update(Func<AppSettings, AppSettings> update) => _settings.Update(update);

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsRussian));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(ShowEscapedControls));
        OnPropertyChanged(nameof(ShowHexPreview));
        OnPropertyChanged(nameof(ShowChunkBoundaries));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(IsComfortable));
    }
}
