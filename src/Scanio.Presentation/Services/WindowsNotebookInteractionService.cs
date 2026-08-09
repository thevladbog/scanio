using Microsoft.Win32;
using Scanio.Application.Notebook;

namespace Scanio.Presentation.Services;

public sealed class WindowsNotebookInteractionService : INotebookInteractionService
{
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
            $"Удалить сессию «{sessionName}» и все её сканы?",
            "Сканио",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    public void ShowError(string message) =>
        System.Windows.MessageBox.Show(
            message,
            "Сканио",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
}
