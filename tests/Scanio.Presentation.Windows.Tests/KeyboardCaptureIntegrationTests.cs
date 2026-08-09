using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Scanio.Presentation.Settings;
using Scanio.Presentation.Windows.Tests.Fixtures;

namespace Scanio.Presentation.Windows.Tests;

[TestClass]
public sealed class KeyboardCaptureIntegrationTests
{
    [TestMethod]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task FocusedSurface_ForwardsRepresentativeTextAndEnterToSelectedMonitorPayload()
    {
        var fixture = WpfTestHost.Run(() => CPlusFixtureFactory.CreateKeyboardCapture(UiLanguage.English));
        try
        {
            WpfTestHost.Run(() => RenderedLayoutTests.Prepare(fixture.Window, 1024, 700));
            await WpfTestHost.Run(() => fixture.Connection.StartKeyboardTestCommand.ExecuteAsync());
            await fixture.Window.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle).Task;

            WpfTestHost.Run(() =>
            {
                var input = RenderedLayoutTests.Descendants<TextBox>(fixture.Window)
                    .Single(element => element.Name == "KeyboardCaptureInput");
                Assert.IsTrue(input.IsKeyboardFocusWithin, "Start test must focus only the dedicated capture input.");

                var composition = new TextComposition(InputManager.Current, input, "ABC123");
                input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                {
                    RoutedEvent = TextCompositionManager.PreviewTextInputEvent
                });

                var presentationSource = PresentationSource.FromVisual(input);
                Assert.IsNotNull(presentationSource);
                input.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    presentationSource,
                    Environment.TickCount,
                    Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
            });

            await WaitUntilAsync(
                () => fixture.SourceMonitor.SelectedEvent?.Decoded.Text == "ABC123",
                TimeSpan.FromSeconds(2));
            await fixture.Window.Dispatcher.InvokeAsync(
                () => fixture.Window.UpdateLayout(),
                DispatcherPriority.ApplicationIdle).Task;

            Assert.AreEqual("ABC123", fixture.Monitor.SelectedEvent?.Payload);
        }
        finally
        {
            await fixture.ConnectionService.DisconnectAsync(CancellationToken.None);
            WpfTestHost.Run(fixture.Dispose);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("Timed out waiting for the keyboard scan to reach Monitor.");
            }

            await Task.Delay(10);
        }
    }
}
