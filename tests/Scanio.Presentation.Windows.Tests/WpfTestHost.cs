using System.Windows;
using System.Windows.Threading;

namespace Scanio.Presentation.Windows.Tests;

internal static class WpfTestHost
{
    private static readonly TaskCompletionSource<Dispatcher> Ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    static WpfTestHost()
    {
        var thread = new Thread(() =>
        {
            var application = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Scanio;component/Resources/Theme.xaml")
            });
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Scanio;component/Resources/Controls.xaml")
            });
            Ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Scanio WPF test host"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static T Run<T>(Func<T> action) =>
        Ready.Task.GetAwaiter().GetResult().Invoke(action);

    public static void Run(Action action) =>
        Ready.Task.GetAwaiter().GetResult().Invoke(action);
}
