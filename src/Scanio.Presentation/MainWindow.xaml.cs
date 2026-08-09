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

}
