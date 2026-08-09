using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation.Tests.Infrastructure;

internal readonly record struct RenderedEvidenceVariant(
    string ScreenshotName,
    UiLanguage Language,
    ShellDestination Destination,
    int Width,
    int Height,
    ConnectionMode ConnectionMode,
    ListDensity ListDensity,
    bool ShowHexPreview,
    bool ShowChunkBoundaries);

internal static class RenderedEvidenceMatrix
{
    private static readonly UiLanguage[] Languages =
        [UiLanguage.Russian, UiLanguage.English];

    public static IReadOnlyList<RenderedEvidenceVariant> Connection { get; } =
    [
        ConnectionVariant(UiLanguage.Russian, ConnectionMode.Serial, 1024, 700, "connection-serial-ru-1024x700.png"),
        ConnectionVariant(UiLanguage.Russian, ConnectionMode.Serial, 1440, 900, "connection-serial-ru-1440x900.png"),
        ConnectionVariant(UiLanguage.Russian, ConnectionMode.Keyboard, 1024, 700, "connection-keyboard-ru-1024x700.png"),
        ConnectionVariant(UiLanguage.Russian, ConnectionMode.Keyboard, 1440, 900, "connection-keyboard-ru-1440x900.png"),
        ConnectionVariant(UiLanguage.English, ConnectionMode.Serial, 1024, 700, "connection-serial-en-1024x700.png"),
        ConnectionVariant(UiLanguage.English, ConnectionMode.Serial, 1440, 900, "connection-serial-en-1440x900.png"),
        ConnectionVariant(UiLanguage.English, ConnectionMode.Keyboard, 1024, 700, "connection-keyboard-en-1024x700.png"),
        ConnectionVariant(UiLanguage.English, ConnectionMode.Keyboard, 1440, 900, "connection-keyboard-en-1440x900.png")
    ];

    public static IReadOnlyList<RenderedEvidenceVariant> Density { get; } =
        Languages.SelectMany(language => new[]
        {
            DensityVariant(language, ShellDestination.Connection, ListDensity.Compact, $"density-connection-compact-{LanguageCode(language)}-1024x700.png"),
            DensityVariant(language, ShellDestination.Connection, ListDensity.Comfortable, $"density-connection-comfortable-{LanguageCode(language)}-1024x700.png"),
            DensityVariant(language, ShellDestination.Monitor, ListDensity.Compact, $"density-monitor-compact-{LanguageCode(language)}-1024x700.png"),
            DensityVariant(language, ShellDestination.Monitor, ListDensity.Comfortable, $"density-monitor-comfortable-{LanguageCode(language)}-1024x700.png"),
            DensityVariant(language, ShellDestination.Notebook, ListDensity.Compact, $"density-notebook-compact-{LanguageCode(language)}-1024x700.png"),
            DensityVariant(language, ShellDestination.Notebook, ListDensity.Comfortable, $"density-notebook-comfortable-{LanguageCode(language)}-1024x700.png"),
            DensityVariant(language, ShellDestination.History, ListDensity.Compact, $"density-history-compact-{LanguageCode(language)}-1024x700.png"),
            DensityVariant(language, ShellDestination.History, ListDensity.Comfortable, $"density-history-comfortable-{LanguageCode(language)}-1024x700.png")
        }).ToArray();

    public static IReadOnlyList<RenderedEvidenceVariant> Monitor { get; } =
        Languages.SelectMany(language => new[]
        {
            MonitorVariant(language, false, $"monitor-evidence-off-{LanguageCode(language)}-1440x900.png"),
            MonitorVariant(language, true, $"monitor-evidence-on-{LanguageCode(language)}-1440x900.png")
        }).ToArray();

    public static IReadOnlyList<RenderedEvidenceVariant> Settings { get; } =
        Languages.SelectMany(language => new[]
        {
            SettingsVariant(language, ListDensity.Compact, $"settings-density-compact-{LanguageCode(language)}-1440x900.png"),
            SettingsVariant(language, ListDensity.Comfortable, $"settings-density-comfortable-{LanguageCode(language)}-1440x900.png")
        }).ToArray();

    public static IEnumerable<RenderedEvidenceVariant> All =>
        Connection.Concat(Density).Concat(Monitor).Concat(Settings);

    private static RenderedEvidenceVariant ConnectionVariant(
        UiLanguage language,
        ConnectionMode connectionMode,
        int width,
        int height,
        string screenshotName) =>
        new(
            screenshotName,
            language,
            ShellDestination.Connection,
            width,
            height,
            connectionMode,
            ListDensity.Comfortable,
            ShowHexPreview: true,
            ShowChunkBoundaries: true);

    private static RenderedEvidenceVariant DensityVariant(
        UiLanguage language,
        ShellDestination destination,
        ListDensity listDensity,
        string screenshotName) =>
        new(
            screenshotName,
            language,
            destination,
            1024,
            700,
            ConnectionMode.Serial,
            listDensity,
            ShowHexPreview: true,
            ShowChunkBoundaries: true);

    private static RenderedEvidenceVariant MonitorVariant(
        UiLanguage language,
        bool evidenceVisible,
        string screenshotName) =>
        new(
            screenshotName,
            language,
            ShellDestination.Monitor,
            1440,
            900,
            ConnectionMode.Serial,
            ListDensity.Comfortable,
            ShowHexPreview: evidenceVisible,
            ShowChunkBoundaries: evidenceVisible);

    private static RenderedEvidenceVariant SettingsVariant(
        UiLanguage language,
        ListDensity listDensity,
        string screenshotName) =>
        new(
            screenshotName,
            language,
            ShellDestination.Settings,
            1440,
            900,
            ConnectionMode.Serial,
            listDensity,
            ShowHexPreview: true,
            ShowChunkBoundaries: true);

    private static string LanguageCode(UiLanguage language) =>
        language == UiLanguage.Russian ? "ru" : "en";
}
