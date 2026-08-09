using Scanio.Application.Notebook;
using Scanio.Presentation.Services;

namespace Scanio.Presentation.Windows.Tests;

[TestClass]
public sealed class WindowsNotebookInteractionServiceTests
{
    [TestMethod]
    public void ReadableText_UsesATxtFileType()
    {
        var fileType = WindowsNotebookInteractionService.GetExportFileType(
            NotebookExportFormat.ReadableText);

        Assert.AreEqual("txt", fileType.Extension);
        Assert.AreEqual("Text files (*.txt)|*.txt", fileType.Filter);
    }
}
