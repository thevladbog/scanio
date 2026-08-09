using System.IO;
using System.Reflection;
using Scanio.Analysis;
using Scanio.Application.Connection;
using Scanio.Application.Monitor;
using Scanio.Application.Notebook;
using Scanio.Capture;
using Scanio.Domain.Analysis;
using Scanio.Platform.Windows.Devices;
using Scanio.Presentation.Services;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Settings;
using Scanio.Presentation.ViewModels;
using Scanio.Storage;

namespace Scanio.Presentation;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var databasePath = NotebookDatabasePath.Resolve(portable, AppContext.BaseDirectory, localApplicationData);
        var settingsPath = JsonAppSettingsStore.ResolvePath(portable, AppContext.BaseDirectory, localApplicationData);
        var settingsService = new AppSettingsService(new JsonAppSettingsStore(settingsPath));
        DisplaySettingsSource.Initialize(settingsService);
        var localizer = new UiLocalizer(settingsService);
        LocalizationSource.Initialize(localizer);

        var monitor = new LiveMonitor();
        var assembler = new ScanAssembler();
        var analyzers = BuiltInAnalyzers.CreatePipeline();
        var pipeline = new ScanProcessingPipeline(assembler, PayloadTextEncoding.Utf8, analyzers, monitor);
        var coordinator = new ConnectionCoordinator(pipeline);
        var connection = new ConnectionService(coordinator);
        var connectionViewModel = new ConnectionViewModel(new WindowsSerialDeviceEnumerator(), connection, localizer);
        var monitorViewModel = new MonitorViewModel(
            monitor,
            connection,
            new WindowsClipboardService(),
            localizer);
        var repository = new SqliteNotebookRepository(databasePath);
        repository.Initialize();
        var recorder = new NotebookRecorder(repository, monitor);
        var interaction = new WindowsNotebookInteractionService(localizer);
        var notebookViewModel = new NotebookViewModel(recorder, interaction, localizer);
        var historyViewModel = new HistoryViewModel(repository, interaction, recorder, localizer);
        var applicationVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "unknown";
        var settingsViewModel = new SettingsViewModel(
            settingsService,
            localizer,
            new WindowsPlatformInteractionService(),
            portable,
            databasePath,
            applicationVersion,
            new Uri("https://github.com/thevladbog/scanio/releases"));
        var shell = new ShellViewModel(
            connectionViewModel,
            monitorViewModel,
            notebookViewModel,
            historyViewModel,
            settingsViewModel,
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
