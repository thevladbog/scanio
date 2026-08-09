using System.ComponentModel;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation;

public partial class MainWindow : System.Windows.Window
{
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(2);
    private readonly ShellViewModel _viewModel;
    private bool _shutdownComplete;

    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        try
        {
            using var cancellation = new CancellationTokenSource(ShutdownGracePeriod);
            var shutdown = _viewModel.ShutdownAsync(cancellation.Token);
            await WindowShutdownGuard.WaitAsync(shutdown, Task.Delay(ShutdownGracePeriod));
        }
        finally
        {
            _shutdownComplete = true;
            Close();
        }
    }

    private void ShowConnection(object sender, System.Windows.RoutedEventArgs e)
    {
        ShowOnly(ConnectionScreen);
    }

    private void ShowMonitor(object sender, System.Windows.RoutedEventArgs e)
    {
        ShowOnly(MonitorScreen);
    }

    private void ShowNotebook(object sender, System.Windows.RoutedEventArgs e) => ShowOnly(NotebookScreen);

    private async void ShowHistory(object sender, System.Windows.RoutedEventArgs e)
    {
        ShowOnly(HistoryScreen);
        await _viewModel.History.RefreshCommand.ExecuteAsync();
    }

    private void ShowOnly(System.Windows.UIElement visible)
    {
        ConnectionScreen.Visibility = System.Windows.Visibility.Collapsed;
        MonitorScreen.Visibility = System.Windows.Visibility.Collapsed;
        NotebookScreen.Visibility = System.Windows.Visibility.Collapsed;
        HistoryScreen.Visibility = System.Windows.Visibility.Collapsed;
        visible.Visibility = System.Windows.Visibility.Visible;
    }
}
