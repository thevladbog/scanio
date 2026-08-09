namespace Scanio.Presentation.Settings;

public interface IAppSettingsStore
{
    string SettingsPath { get; }

    AppSettings Load();

    void Save(AppSettings settings);
}

public interface IAppSettingsService
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    void Update(Func<AppSettings, AppSettings> update);
}
