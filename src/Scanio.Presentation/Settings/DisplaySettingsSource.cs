using System.ComponentModel;

namespace Scanio.Presentation.Settings;

public sealed class DisplaySettingsSource : INotifyPropertyChanged
{
    private IAppSettingsService? _settings;
    private bool _showEscapedControls = true;
    private bool _showHexPreview = true;
    private bool _showChunkBoundaries = true;
    private bool _isCompact;
    private double _ledgerRowHeight = 66d;

    private DisplaySettingsSource()
    {
    }

    public static DisplaySettingsSource Current { get; } = new();

    public bool ShowEscapedControls => _showEscapedControls;

    public bool ShowHexPreview => _showHexPreview;

    public bool ShowChunkBoundaries => _showChunkBoundaries;

    public bool IsCompact => _isCompact;

    public double LedgerRowHeight => _ledgerRowHeight;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static void Initialize(IAppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current.InitializeCore(settings);
    }

    private void InitializeCore(IAppSettingsService settings)
    {
        if (!ReferenceEquals(_settings, settings))
        {
            if (_settings is not null)
            {
                _settings.Changed -= OnSettingsChanged;
            }

            _settings = settings;
            _settings.Changed += OnSettingsChanged;
        }

        Apply(settings.Current);
    }

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        if (_settings is not null)
        {
            Apply(_settings.Current);
        }
    }

    private void Apply(AppSettings settings)
    {
        Publish(ref _showEscapedControls, settings.ShowEscapedControls, nameof(ShowEscapedControls));
        Publish(ref _showHexPreview, settings.ShowHexPreview, nameof(ShowHexPreview));
        Publish(ref _showChunkBoundaries, settings.ShowChunkBoundaries, nameof(ShowChunkBoundaries));
        Publish(ref _isCompact, settings.ListDensity == ListDensity.Compact, nameof(IsCompact));
        Publish(
            ref _ledgerRowHeight,
            settings.ListDensity == ListDensity.Compact ? 48d : 66d,
            nameof(LedgerRowHeight));
    }

    private void Publish<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
