namespace Scanio.Domain.Transport;

public sealed record TransportIdentity
{
    public TransportIdentity(TransportKind kind, string stableId, string displayName, string? hardwareId = null)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException("A transport must have a stable identifier.", nameof(stableId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A transport must have a display name.", nameof(displayName));
        }

        Kind = kind;
        StableId = stableId;
        DisplayName = displayName;
        HardwareId = hardwareId;
    }

    public TransportKind Kind { get; }

    public string StableId { get; }

    public string DisplayName { get; }

    public string? HardwareId { get; }
}
