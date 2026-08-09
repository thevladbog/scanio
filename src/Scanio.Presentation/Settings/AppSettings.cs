namespace Scanio.Presentation.Settings;

public enum UiLanguage
{
    Russian,
    English
}

public enum ListDensity
{
    Compact,
    Comfortable
}

public sealed record AppSettings(
    UiLanguage Language = UiLanguage.Russian,
    bool ShowEscapedControls = true,
    bool ShowHexPreview = true,
    bool ShowChunkBoundaries = true,
    ListDensity ListDensity = ListDensity.Comfortable);
