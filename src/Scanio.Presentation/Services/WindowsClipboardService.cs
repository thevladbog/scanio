namespace Scanio.Presentation.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        System.Windows.Clipboard.SetText(text);
    }
}
