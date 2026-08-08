using System.Globalization;

namespace Scanio.Transports.Serial;

public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum SerialStopBits
{
    One,
    OnePointFive,
    Two
}

public enum SerialHandshake
{
    None,
    XOnXOff,
    RequestToSend,
    RequestToSendXOnXOff
}

public sealed record SerialConnectionOptions
{
    public SerialConnectionOptions(
        string portName,
        int baudRate,
        int dataBits,
        SerialParity parity,
        SerialStopBits stopBits,
        SerialHandshake handshake,
        bool dtrEnable,
        bool rtsEnable)
    {
        var normalizedPortName = portName?.Trim();
        if (!IsComPortName(normalizedPortName))
        {
            throw new ArgumentException("A Windows COM port name is required.", nameof(portName));
        }

        if (baudRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baudRate), baudRate, "The baud rate must be positive.");
        }

        if (dataBits is < 5 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(dataBits), dataBits, "Data bits must be between 5 and 8.");
        }

        if (!Enum.IsDefined(parity))
        {
            throw new ArgumentOutOfRangeException(nameof(parity), parity, "The parity value is not supported.");
        }

        if (!Enum.IsDefined(stopBits))
        {
            throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, "The stop-bits value is not supported.");
        }

        if (!Enum.IsDefined(handshake))
        {
            throw new ArgumentOutOfRangeException(nameof(handshake), handshake, "The handshake value is not supported.");
        }

        PortName = normalizedPortName!;
        BaudRate = baudRate;
        DataBits = dataBits;
        Parity = parity;
        StopBits = stopBits;
        Handshake = handshake;
        DtrEnable = dtrEnable;
        RtsEnable = rtsEnable;
    }

    public string PortName { get; }

    public int BaudRate { get; }

    public int DataBits { get; }

    public SerialParity Parity { get; }

    public SerialStopBits StopBits { get; }

    public SerialHandshake Handshake { get; }

    public bool DtrEnable { get; }

    public bool RtsEnable { get; }

    public static SerialConnectionOptions Default(string portName) =>
        new(
            portName,
            baudRate: 9_600,
            dataBits: 8,
            SerialParity.None,
            SerialStopBits.One,
            SerialHandshake.None,
            dtrEnable: false,
            rtsEnable: false);

    private static bool IsComPortName(string? portName)
    {
        if (portName is null || portName.Length <= 3 ||
            !portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
                portName.AsSpan(3),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var portNumber) &&
            portNumber > 0;
    }
}
