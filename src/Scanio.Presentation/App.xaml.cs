using System.IO;
using Scanio.Analysis;
using Scanio.Application.Connection;
using Scanio.Application.Monitor;
using Scanio.Application.Notebook;
using Scanio.Capture;
using Scanio.Domain.Analysis;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Services;
using Scanio.Presentation.ViewModels;
using Scanio.Storage;

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
        var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
        var databasePath = NotebookDatabasePath.Resolve(
            portable,
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var repository = new SqliteNotebookRepository(databasePath);
        repository.Initialize();
        var recorder = new NotebookRecorder(repository, monitor);
        var interaction = new WindowsNotebookInteractionService();
        var notebookViewModel = new NotebookViewModel(recorder, interaction);
        var historyViewModel = new HistoryViewModel(repository, interaction, recorder);
        var shell = new ShellViewModel(
            connectionViewModel,
            monitorViewModel,
            notebookViewModel,
            historyViewModel,
            recorder,
            connection);

        var window = new MainWindow(shell);
        MainWindow = window;
        window.Show();
        await Task.WhenAll(
            connectionViewModel.RefreshCommand.ExecuteAsync(),
            historyViewModel.RefreshCommand.ExecuteAsync());
    }
}
