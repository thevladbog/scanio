using Scanio.Application.Notebook;

namespace Scanio.Presentation.Services;

public interface INotebookInteractionService
{
    void SetClipboardText(string text);

    string? ChooseExportPath(NotebookExportFormat format, string suggestedName);

    bool ConfirmDelete(string sessionName);

    void ShowError(string message);
}
