namespace Scanio.Presentation.Settings;

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IAppSettingsStore _store;

    public AppSettingsService(IAppSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Update(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var next = update(Current) ?? throw new InvalidOperationException("Settings updates cannot return null.");
        if (next == Current)
        {
            return;
        }

        _store.Save(next);
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
