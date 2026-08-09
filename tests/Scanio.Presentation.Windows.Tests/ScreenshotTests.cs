using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;
using Scanio.Presentation.Windows.Tests.Fixtures;

namespace Scanio.Presentation.Windows.Tests;

[TestClass]
public sealed class ScreenshotTests
{
    [TestMethod]
    public void CaptureAllRussianAndEnglishCPlusWorkspaces()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var language in Enum.GetValues<UiLanguage>())
            foreach (var destination in Enum.GetValues<ShellDestination>())
            foreach (var size in new[] { (Width: 1440, Height: 900), (Width: 1024, Height: 700) })
            {
                using var fixture = CPlusFixtureFactory.Create(language, destination);
                RenderedLayoutTests.Prepare(fixture.Window, size.Width, size.Height);
                var directory = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestResults",
                    "screenshots",
                    language == UiLanguage.Russian ? "ru" : "en",
                    $"{size.Width}x{size.Height}");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"{destination.ToString().ToLowerInvariant()}.png");

                var visual = (Visual)fixture.Window.Content;
                var width = Math.Max(1, (int)Math.Ceiling(fixture.Window.ActualWidth));
                var height = Math.Max(1, (int)Math.Ceiling(fixture.Window.ActualHeight));
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(path);
                encoder.Save(output);

                Assert.IsGreaterThan(10_000, new FileInfo(path).Length, $"Rendered screenshot is unexpectedly empty: {path}");
            }
        });
    }
}
