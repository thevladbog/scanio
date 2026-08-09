using System.ComponentModel;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.Localization;

public interface IUiLocalizer : INotifyPropertyChanged
{
    UiLanguage Language { get; }

    string this[string key] { get; }

    void SetLanguage(UiLanguage language);
}
