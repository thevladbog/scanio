namespace Scanio.Presentation.Services;

public interface IPlatformInteractionService
{
    void OpenFolder(string path);

    void OpenUri(Uri uri);
}
