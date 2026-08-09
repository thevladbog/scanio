using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scanio.Presentation.Settings;
using Scanio.Presentation.Tests.Infrastructure;
using Scanio.Presentation.ViewModels;
using Scanio.Presentation.Windows.Tests.Fixtures;

namespace Scanio.Presentation.Windows.Tests;

[TestClass]
public sealed class ScreenshotTests
{
    [TestMethod]
    public void CaptureGeneralRussianAndEnglishCPlusWorkspaces()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var language in Enum.GetValues<UiLanguage>())
            foreach (var destination in Enum.GetValues<ShellDestination>().Where(value => value != ShellDestination.Connection))
            foreach (var size in new[] { (Width: 1440, Height: 900), (Width: 1024, Height: 700) })
            {
                using var fixture = CPlusFixtureFactory.Create(language, destination);
                RenderedLayoutTests.AssertFixtureSemanticEvidence(
                    fixture,
                    language,
                    $"screenshot/{language}/{destination}");
                RenderedLayoutTests.Prepare(fixture.Window, size.Width, size.Height);
                Capture(
                    fixture.Window,
                    Path.Combine(
                        language == UiLanguage.Russian ? "ru" : "en",
                        $"{size.Width}x{size.Height}",
                        $"{destination.ToString().ToLowerInvariant()}.png"));
            }
        });
    }

    [TestMethod]
    public void CaptureRequiredRenderedEvidenceVariants()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var variant in RenderedEvidenceMatrix.All)
            {
                using var fixture = CPlusFixtureFactory.Create(variant);
                RenderedLayoutTests.AssertFixtureSemanticEvidence(
                    fixture,
                    variant.Language,
                    $"screenshot/{variant.ScreenshotName}");
                RenderedLayoutTests.Prepare(fixture.Window, variant.Width, variant.Height);
                Capture(fixture.Window, Path.Combine("evidence", variant.ScreenshotName));
            }
        });
    }

    private static void Capture(Scanio.Presentation.MainWindow window, string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestResults", "screenshots", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var visual = (Visual)window.Content;
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);

        Assert.IsGreaterThan(10_000, new FileInfo(path).Length, $"Rendered screenshot is unexpectedly empty: {path}");
    }
}
