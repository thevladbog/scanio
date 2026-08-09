using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;
using Scanio.Presentation.Windows.Tests.Fixtures;

namespace Scanio.Presentation.Windows.Tests;

[TestClass]
public sealed class RenderedLayoutTests
{
    private static readonly (double Width, double Height)[] Sizes = [(1440, 900), (1024, 700)];

    [TestMethod]
    public void EveryWorkspace_KeepsActionsVisibleAndAvoidsHorizontalScrolling()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var language in Enum.GetValues<UiLanguage>())
            foreach (var destination in Enum.GetValues<ShellDestination>())
            foreach (var size in Sizes)
            {
                using var fixture = CPlusFixtureFactory.Create(language, destination);
                Prepare(fixture.Window, size.Width, size.Height);
                var label = $"{language}/{destination}/{size.Width}x{size.Height}";

                var horizontalScrollbars = Descendants<ScrollBar>(fixture.Window)
                    .Where(scrollbar => scrollbar.Orientation == Orientation.Horizontal && scrollbar.IsVisible)
                    .ToArray();
                Assert.IsEmpty(horizontalScrollbars, $"{label} exposes a horizontal scrollbar.");

                foreach (var button in Descendants<Button>(fixture.Window).Where(button => button.IsVisible))
                {
                    Assert.IsGreaterThanOrEqualTo(1, button.ActualWidth, $"{label} has a zero-width button: {button.Content}");
                    Assert.IsGreaterThanOrEqualTo(32, button.ActualHeight, $"{label} has an undersized action: {button.Content}");
                    AssertInsideWindow(button, fixture.Window, label);
                }

                AssertSiblingControlsDoNotOverlap(fixture.Window, label);
                AssertReferenceColumnsRemainUsable(fixture.Window, destination, label);
            }
        });
    }

    [TestMethod]
    public void ReadableNotebookActions_KeepFullLabelsAtMinimumSizeInBothLanguages()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var language in Enum.GetValues<UiLanguage>())
            foreach (var destination in new[] { ShellDestination.Notebook, ShellDestination.History })
            {
                using var fixture = CPlusFixtureFactory.Create(language, destination);
                Prepare(fixture.Window, 1024, 700);
                var label = $"{language}/{destination}/1024x700";
                var expectedLabels = language == UiLanguage.English
                    ? new[] { "Copy as readable text (<GS>)", "Export readable TXT (<GS>)" }
                    : new[] { "Копировать как текст (<GS>)", "Экспорт TXT как текст (<GS>)" };
                var visibleButtons = Descendants<Button>(fixture.Window)
                    .Where(button => button.IsVisible)
                    .ToArray();

                foreach (var expectedLabel in expectedLabels)
                {
                    var button = visibleButtons.SingleOrDefault(candidate =>
                        candidate.Content is TextBlock textBlock &&
                        string.Equals(textBlock.Text, expectedLabel, StringComparison.Ordinal));
                    Assert.IsNotNull(button, $"{label} must expose the full label '{expectedLabel}'.");
                    AssertButtonContentFits(button, fixture.Window, language, label, expectedLabel);
                }
            }
        });
    }

    [TestMethod]
    public void ConnectionTransportModes_KeepSelectorsAndFocusedCaptureSurfaceUsableAtMinimumSize()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var language in Enum.GetValues<UiLanguage>())
            foreach (var mode in Enum.GetValues<ConnectionMode>())
            {
                using var fixture = CPlusFixtureFactory.Create(language, ShellDestination.Connection, mode);
                Prepare(fixture.Window, 1024, 700);
                var label = $"{language}/{mode}/1024x700";
                var selectors = Descendants<RadioButton>(fixture.Window)
                    .Where(button => button.GroupName == "ConnectionMode" && button.IsVisible)
                    .ToArray();

                Assert.HasCount(2, selectors, $"{label} must expose both transport selectors.");
                Assert.IsTrue(selectors.All(selector => selector.ActualHeight >= 32),
                    $"{label} exposes an undersized transport selector.");

                var captureSurface = Descendants<FrameworkElement>(fixture.Window)
                    .Single(element => element.Name == "KeyboardCaptureSurface");
                var captureInput = Descendants<FrameworkElement>(fixture.Window)
                    .Single(element => element.Name == "KeyboardCaptureInput");
                Assert.AreEqual(mode == ConnectionMode.Keyboard, captureSurface.IsVisible, label);
                Assert.AreEqual(mode == ConnectionMode.Keyboard, captureInput.IsVisible, label);
                if (mode == ConnectionMode.Keyboard)
                {
                    Assert.IsGreaterThanOrEqualTo(120, captureSurface.ActualHeight, label);
                    AssertInsideWindow(captureSurface, fixture.Window, label);
                    AssertInsideWindow(captureInput, fixture.Window, label);
                }
            }
        });
    }

    internal static void Prepare(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.Left = -20_000;
        window.Top = 0;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Show();
        window.UpdateLayout();
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void AssertInsideWindow(FrameworkElement element, Window window, string label)
    {
        var bounds = element.TransformToAncestor(window)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var viewport = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
        Assert.IsTrue(viewport.Contains(bounds.TopLeft) && viewport.Contains(bounds.BottomRight),
            $"{label} clips action '{(element as Button)?.Content}' at {bounds} inside {viewport}.");
    }

    private static void AssertButtonContentFits(
        Button button,
        Window window,
        UiLanguage language,
        string label,
        string expectedText)
    {
        var textBlock = button.Content as TextBlock;
        Assert.IsNotNull(textBlock);
        Assert.AreEqual(expectedText, textBlock.Text, $"{label} measures the wrong readable label.");
        var textBounds = Bounds(textBlock, window);
        var buttonBounds = Bounds(button, window);
        Assert.IsTrue(
            buttonBounds.Contains(textBounds.TopLeft) && buttonBounds.Contains(textBounds.BottomRight),
            $"{label} clips readable label '{expectedText}' at {textBounds} inside {buttonBounds}.");
        Assert.IsTrue(textBlock.ActualWidth > 0 && textBlock.ActualHeight > 0,
            $"{label} gives readable label '{expectedText}' empty content bounds.");

        var formatted = new FormattedText(
            expectedText,
            CultureInfo.GetCultureInfo(language == UiLanguage.English ? "en-US" : "ru-RU"),
            button.FlowDirection,
            new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch),
            textBlock.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(textBlock).PixelsPerDip)
        {
            MaxTextWidth = textBlock.ActualWidth,
            Trimming = TextTrimming.None
        };

        Assert.IsTrue(
            RenderedContentFit.Fits(
                textBlock.ActualWidth,
                textBlock.ActualHeight,
                formatted.WidthIncludingTrailingWhitespace,
                formatted.Height),
            $"{label} clips readable label '{expectedText}' inside " +
            $"{textBlock.ActualWidth:0.#}x{textBlock.ActualHeight:0.#}; " +
            $"required {formatted.WidthIncludingTrailingWhitespace:0.#}x{formatted.Height:0.#}.");
    }

    private static void AssertSiblingControlsDoNotOverlap(Window window, string label)
    {
        var controls = Descendants<Control>(window)
            .Where(control => control.IsVisible && control is Button or TextBox or ComboBox or RadioButton)
            .GroupBy(VisualTreeHelper.GetParent);
        foreach (var siblings in controls)
        {
            var items = siblings.ToArray();
            for (var left = 0; left < items.Length; left++)
            for (var right = left + 1; right < items.Length; right++)
            {
                var first = Bounds(items[left], window);
                var second = Bounds(items[right], window);
                var intersection = Rect.Intersect(first, second);
                Assert.IsTrue(intersection.IsEmpty || intersection.Width < 1 || intersection.Height < 1,
                    $"{label} overlaps sibling controls at {first} and {second}.");
            }
        }
    }

    private static void AssertReferenceColumnsRemainUsable(
        Window window,
        ShellDestination destination,
        string label)
    {
        var screen = Descendants<UserControl>(window).LastOrDefault();
        var rootGrid = screen?.Content as Grid;
        if (rootGrid is null || rootGrid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        if (destination == ShellDestination.Settings)
        {
            AssertSettingsColumnsRemainUsable(rootGrid, label);
            return;
        }

        foreach (var column in rootGrid.ColumnDefinitions)
        {
            Assert.IsGreaterThanOrEqualTo(200, column.ActualWidth,
                $"{label} collapses a primary workspace column to {column.ActualWidth:0.#} px.");
        }
    }

    private static void AssertSettingsColumnsRemainUsable(Grid rootGrid, string label)
    {
        Assert.AreEqual(3, rootGrid.ColumnDefinitions.Count,
            $"{label} must keep two content columns separated by one divider.");

        foreach (var index in new[] { 0, 2 })
        {
            var content = rootGrid.ColumnDefinitions[index];
            Assert.IsTrue(content.Width.IsStar,
                $"{label} content column {index} must remain star-sized.");
            Assert.IsGreaterThanOrEqualTo(200, content.ActualWidth,
                $"{label} collapses Settings content column {index} to {content.ActualWidth:0.#} px.");
        }

        var divider = rootGrid.ColumnDefinitions[1];
        Assert.IsTrue(divider.Width.IsAbsolute,
            $"{label} Settings divider must remain fixed-width.");
        Assert.AreEqual(1d, divider.Width.Value, 0.01d,
            $"{label} declares a {divider.Width.Value:0.##}-DIP Settings divider instead of 1 DIP.");
        Assert.AreEqual(1d, divider.ActualWidth, 0.5d,
            $"{label} renders a {divider.ActualWidth:0.##}-DIP Settings divider instead of 1 DIP.");
    }

    private static Rect Bounds(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

}
