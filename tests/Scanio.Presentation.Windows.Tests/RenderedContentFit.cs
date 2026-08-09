namespace Scanio.Presentation.Windows.Tests;

internal static class RenderedContentFit
{
    public static bool Fits(
        double availableWidth,
        double availableHeight,
        double requiredWidth,
        double requiredHeight,
        double tolerance = 0.5) =>
        availableWidth + tolerance >= requiredWidth &&
        availableHeight + tolerance >= requiredHeight;
}
