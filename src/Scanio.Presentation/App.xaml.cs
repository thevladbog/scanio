using Scanio.Analysis;
using Scanio.Application.Connection;
using Scanio.Application.Monitor;
using Scanio.Capture;
using Scanio.Domain.Analysis;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Services;
using Scanio.Presentation.ViewModels;

namespace Scanio.Presentation;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var monitor = new LiveMonitor();
        var assembler = new ScanAssembler();
        var analyzers = BuiltInAnalyzers.CreatePipeline();
        var pipeline = new ScanProcessingPipeline(assembler, PayloadTextEncoding.Utf8, analyzers, monitor);
        var coordinator = new ConnectionCoordinator(pipeline);
        var connection = new ConnectionService(coordinator);
        var connectionViewModel = new ConnectionViewModel(new WindowsSerialDeviceEnumerator(), connection);
        var monitorViewModel = new MonitorViewModel(monitor, connection);
        var shell = new ShellViewModel(connectionViewModel, monitorViewModel, connection);

        var window = new MainWindow(shell);
        MainWindow = window;
        window.Show();
        await connectionViewModel.RefreshCommand.ExecuteAsync();
    }
}
