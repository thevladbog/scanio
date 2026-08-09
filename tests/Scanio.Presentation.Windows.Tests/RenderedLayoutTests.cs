using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Globalization;
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
                AssertReferenceColumnsRemainUsable(fixture.Window, label);
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
                var readableActions = Descendants<Button>(fixture.Window)
                    .Where(button => button.IsVisible)
                    .Where(button => ButtonText(button)?.Contains("<GS>", StringComparison.Ordinal) == true)
                    .ToArray();

                Assert.HasCount(2, readableActions, $"{label} must expose both readable-text actions.");
                foreach (var button in readableActions)
                {
                    AssertButtonContentFits(button, fixture.Window, language, label);
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
        string label)
    {
        var text = ButtonText(button);
        Assert.IsNotNull(text);
        var presenter = Descendants<ContentPresenter>(button).Single();
        var presenterBounds = Bounds(presenter, window);
        var buttonBounds = Bounds(button, window);
        Assert.IsTrue(
            buttonBounds.Contains(presenterBounds.TopLeft) && buttonBounds.Contains(presenterBounds.BottomRight),
            $"{label} clips the content presenter for '{text}' at {presenterBounds} inside {buttonBounds}.");

        var textBlock = button.Content as TextBlock;
        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo(language == UiLanguage.English ? "en-US" : "ru-RU"),
            button.FlowDirection,
            new Typeface(
                textBlock?.FontFamily ?? button.FontFamily,
                textBlock?.FontStyle ?? button.FontStyle,
                textBlock?.FontWeight ?? button.FontWeight,
                textBlock?.FontStretch ?? button.FontStretch),
            textBlock?.FontSize ?? button.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(button).PixelsPerDip);
        if (textBlock?.TextWrapping != TextWrapping.NoWrap)
        {
            formatted.MaxTextWidth = presenter.ActualWidth;
        }

        Assert.IsGreaterThanOrEqualTo(
            formatted.WidthIncludingTrailingWhitespace - 0.5,
            presenter.ActualWidth,
            $"{label} clips readable label '{text}' horizontally.");
        Assert.IsGreaterThanOrEqualTo(
            formatted.Height - 0.5,
            presenter.ActualHeight,
            $"{label} clips readable label '{text}' vertically.");
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

    private static void AssertReferenceColumnsRemainUsable(Window window, string label)
    {
        var screen = Descendants<UserControl>(window).LastOrDefault();
        var rootGrid = screen?.Content as Grid;
        if (rootGrid is null || rootGrid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        foreach (var column in rootGrid.ColumnDefinitions)
        {
            Assert.IsGreaterThanOrEqualTo(200, column.ActualWidth,
                $"{label} collapses a primary workspace column to {column.ActualWidth:0.#} px.");
        }
    }

    private static Rect Bounds(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static string? ButtonText(Button button) => button.Content switch
    {
        string value => value,
        TextBlock textBlock => textBlock.Text,
        AccessText accessText => accessText.Text,
        _ => null
    };
}
