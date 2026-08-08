using System.IO.Ports;

namespace Scanio.Transports.Serial;

public sealed class SystemSerialPortAdapter : ISerialPortAdapter
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorBusy = 170;
    private readonly SerialPort _serialPort;

    public SystemSerialPortAdapter(SerialConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _serialPort = new SerialPort
        {
            PortName = options.PortName,
            BaudRate = options.BaudRate,
            DataBits = options.DataBits,
            Parity = MapParity(options.Parity),
            StopBits = MapStopBits(options.StopBits),
            DtrEnable = options.DtrEnable,
            RtsEnable = options.RtsEnable,
            Handshake = MapHandshake(options.Handshake)
        };
    }

    public void Open()
    {
        try
        {
            _serialPort.Open();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw ClassifyOpenException(exception);
        }
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _serialPort.BaseStream.ReadAsync(buffer, cancellationToken);

    public void Close() => _serialPort.Close();

    public void Dispose() => _serialPort.Dispose();

    internal static SerialPortOpenException ClassifyOpenException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var nativeErrorCode = exception.HResult & 0xFFFF;
        var failureKind = nativeErrorCode switch
        {
            ErrorSharingViolation or ErrorLockViolation or ErrorBusy => SerialPortOpenFailureKind.Busy,
            ErrorAccessDenied => SerialPortOpenFailureKind.AccessDenied,
            _ when exception is UnauthorizedAccessException => SerialPortOpenFailureKind.AccessDenied,
            _ => SerialPortOpenFailureKind.Busy
        };

        return new SerialPortOpenException(
            failureKind,
            nativeErrorCode,
            exception.Message,
            exception);
    }

    private static Parity MapParity(SerialParity parity) => parity switch
    {
        SerialParity.None => Parity.None,
        SerialParity.Odd => Parity.Odd,
        SerialParity.Even => Parity.Even,
        SerialParity.Mark => Parity.Mark,
        SerialParity.Space => Parity.Space,
        _ => throw new ArgumentOutOfRangeException(nameof(parity), parity, null)
    };

    private static StopBits MapStopBits(SerialStopBits stopBits) => stopBits switch
    {
        SerialStopBits.One => StopBits.One,
        SerialStopBits.OnePointFive => StopBits.OnePointFive,
        SerialStopBits.Two => StopBits.Two,
        _ => throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, null)
    };

    private static Handshake MapHandshake(SerialHandshake handshake) => handshake switch
    {
        SerialHandshake.None => Handshake.None,
        SerialHandshake.XOnXOff => Handshake.XOnXOff,
        SerialHandshake.RequestToSend => Handshake.RequestToSend,
        SerialHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        _ => throw new ArgumentOutOfRangeException(nameof(handshake), handshake, null)
    };
}
