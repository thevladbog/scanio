namespace Scanio.Transports.Serial;

public interface ISerialPortAdapter : IDisposable
{
    void Open();

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    void Close();
}
