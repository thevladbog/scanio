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
    [
        DensityVariant(ShellDestination.Connection, ListDensity.Compact, "density-connection-compact-en-1024x700.png"),
        DensityVariant(ShellDestination.Connection, ListDensity.Comfortable, "density-connection-comfortable-en-1024x700.png"),
        DensityVariant(ShellDestination.Monitor, ListDensity.Compact, "density-monitor-compact-en-1024x700.png"),
        DensityVariant(ShellDestination.Monitor, ListDensity.Comfortable, "density-monitor-comfortable-en-1024x700.png"),
        DensityVariant(ShellDestination.Notebook, ListDensity.Compact, "density-notebook-compact-en-1024x700.png"),
        DensityVariant(ShellDestination.Notebook, ListDensity.Comfortable, "density-notebook-comfortable-en-1024x700.png"),
        DensityVariant(ShellDestination.History, ListDensity.Compact, "density-history-compact-en-1024x700.png"),
        DensityVariant(ShellDestination.History, ListDensity.Comfortable, "density-history-comfortable-en-1024x700.png")
    ];

    public static IReadOnlyList<RenderedEvidenceVariant> Monitor { get; } =
    [
        MonitorVariant(false, "monitor-evidence-off-en-1440x900.png"),
        MonitorVariant(true, "monitor-evidence-on-en-1440x900.png")
    ];

    public static IReadOnlyList<RenderedEvidenceVariant> Settings { get; } =
    [
        SettingsVariant(ListDensity.Compact, "settings-density-compact-en-1440x900.png"),
        SettingsVariant(ListDensity.Comfortable, "settings-density-comfortable-en-1440x900.png")
    ];

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
        ShellDestination destination,
        ListDensity listDensity,
        string screenshotName) =>
        new(
            screenshotName,
            UiLanguage.English,
            destination,
            1024,
            700,
            ConnectionMode.Serial,
            listDensity,
            ShowHexPreview: true,
            ShowChunkBoundaries: true);

    private static RenderedEvidenceVariant MonitorVariant(bool evidenceVisible, string screenshotName) =>
        new(
            screenshotName,
            UiLanguage.English,
            ShellDestination.Monitor,
            1440,
            900,
            ConnectionMode.Serial,
            ListDensity.Comfortable,
            ShowHexPreview: evidenceVisible,
            ShowChunkBoundaries: evidenceVisible);

    private static RenderedEvidenceVariant SettingsVariant(ListDensity listDensity, string screenshotName) =>
        new(
            screenshotName,
            UiLanguage.English,
            ShellDestination.Settings,
            1440,
            900,
            ConnectionMode.Serial,
            listDensity,
            ShowHexPreview: true,
            ShowChunkBoundaries: true);
}
