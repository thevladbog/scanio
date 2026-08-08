namespace Scanio.Domain.Transport;

public enum ConnectionState
{
    Detected,
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    Busy,
    AccessDenied,
    DeviceRemoved,
    UnsupportedInterface,
    TransportError
}
