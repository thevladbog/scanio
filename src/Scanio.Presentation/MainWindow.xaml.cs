using System.ComponentModel;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation;

public partial class MainWindow : System.Windows.Window
{
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
            await _viewModel.ShutdownAsync(CancellationToken.None);
            _shutdownComplete = true;
            Close();
        }
        finally
        {
            if (!_shutdownComplete)
            {
                IsEnabled = true;
            }
        }
    }

    private void ShowConnection(object sender, System.Windows.RoutedEventArgs e)
    {
        ConnectionScreen.Visibility = System.Windows.Visibility.Visible;
        MonitorScreen.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void ShowMonitor(object sender, System.Windows.RoutedEventArgs e)
    {
        ConnectionScreen.Visibility = System.Windows.Visibility.Collapsed;
        MonitorScreen.Visibility = System.Windows.Visibility.Visible;
    }
}
