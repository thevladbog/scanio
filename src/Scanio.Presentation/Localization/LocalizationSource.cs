namespace Scanio.Presentation.Localization;

public static class LocalizationSource
{
    private static IUiLocalizer? _current;

    public static IUiLocalizer Current => _current
        ?? throw new InvalidOperationException("LocalizationSource must be initialized before the application UI is created.");

    public static void Initialize(IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _current = localizer;
    }
}
