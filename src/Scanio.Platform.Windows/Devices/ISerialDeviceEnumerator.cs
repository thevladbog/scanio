namespace Scanio.Platform.Windows.Devices;

public interface ISerialDeviceEnumerator
{
    Task<IReadOnlyList<SerialDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken);
}
