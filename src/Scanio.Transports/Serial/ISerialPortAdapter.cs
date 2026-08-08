namespace Scanio.Transports.Serial;

public enum SerialPortOpenFailureKind
{
    Busy,
    AccessDenied
}

public sealed class SerialPortOpenException : IOException
{
    public SerialPortOpenException(
        SerialPortOpenFailureKind failureKind,
        int nativeErrorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        NativeErrorCode = nativeErrorCode;
    }

    public SerialPortOpenFailureKind FailureKind { get; }

    public int NativeErrorCode { get; }
}

public interface ISerialPortAdapter : IDisposable
{
    void Open();

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    void Close();
}
