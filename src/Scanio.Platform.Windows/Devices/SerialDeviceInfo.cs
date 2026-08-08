using Scanio.Domain.Transport;

namespace Scanio.Platform.Windows.Devices;

public sealed record SerialDeviceInfo
{
    public SerialDeviceInfo(
        string portName,
        string friendlyName,
        string? manufacturer,
        ushort? vendorId,
        ushort? productId,
        string? serialNumber,
        string? hardwareId,
        string? stableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentException.ThrowIfNullOrWhiteSpace(friendlyName);

        PortName = portName;
        FriendlyName = friendlyName;
        Manufacturer = manufacturer;
        VendorId = vendorId;
        ProductId = productId;
        SerialNumber = serialNumber;
        HardwareId = hardwareId;
        StableId = stableId;
    }

    public string PortName { get; }

    public string FriendlyName { get; }

    public string? Manufacturer { get; }

    public ushort? VendorId { get; }

    public ushort? ProductId { get; }

    public string? SerialNumber { get; }

    public string? HardwareId { get; }

    public string? StableId { get; }

    public bool HasStableIdentity => StableId is not null;

    public ConnectionState State => ConnectionState.Detected;
}
