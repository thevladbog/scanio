using Microsoft.Win32;
using Scanio.Application.Notebook;
using Scanio.Presentation.Localization;

namespace Scanio.Presentation.Services;

public sealed class WindowsNotebookInteractionService : INotebookInteractionService
{
    private readonly IUiLocalizer _localizer;

    public WindowsNotebookInteractionService(IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
    }

    public void SetClipboardText(string text) => System.Windows.Clipboard.SetText(text);

    public string? ChooseExportPath(NotebookExportFormat format, string suggestedName)
    {
        var (extension, filter) = format switch
        {
            NotebookExportFormat.Text => ("txt", "Text files (*.txt)|*.txt"),
            NotebookExportFormat.Csv => ("csv", "CSV files (*.csv)|*.csv"),
            NotebookExportFormat.Json => ("json", "JSON files (*.json)|*.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = extension,
            FileName = suggestedName,
            Filter = filter,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool ConfirmDelete(string sessionName) =>
        System.Windows.MessageBox.Show(
            string.Format(_localizer["Dialog.DeleteSession"], sessionName),
            _localizer["App.Title"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    public void ShowError(string message) =>
        System.Windows.MessageBox.Show(
            message,
            _localizer["Dialog.ErrorTitle"],
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
}
