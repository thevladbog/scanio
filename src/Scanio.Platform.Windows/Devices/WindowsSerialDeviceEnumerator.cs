using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Scanio.Platform.Windows.Devices;

public sealed class WindowsSerialDeviceEnumerator : ISerialDeviceEnumerator
{
    private static readonly Regex ComPortPattern = new(
        "^COM(?<number>[1-9][0-9]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex UsbIdPattern = new(
        @"(?:^|[\\&+])VID_(?<vid>[0-9A-F]{4})[&+]PID_(?<pid>[0-9A-F]{4})(?:$|[\\&+])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IWindowsSerialDeviceNativeAdapter _nativeAdapter;

    public WindowsSerialDeviceEnumerator()
        : this(new SetupApiSerialDeviceNativeAdapter())
    {
    }

    public WindowsSerialDeviceEnumerator(IWindowsSerialDeviceNativeAdapter nativeAdapter)
    {
        ArgumentNullException.ThrowIfNull(nativeAdapter);
        _nativeAdapter = nativeAdapter;
    }

    public async Task<IReadOnlyList<SerialDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run<IReadOnlyList<SerialDeviceInfo>>(
                () => MapDevices(_nativeAdapter.EnumeratePresentDevices(cancellationToken), cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<SerialDeviceInfo> MapDevices(
        IReadOnlyList<WindowsSerialDeviceProperties> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var devices = new List<SerialDeviceInfo>(properties.Count);
        foreach (var deviceProperties in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var portName = NormalizePortName(deviceProperties.PortName);
            if (portName is null)
            {
                continue;
            }

            var hardwareId = deviceProperties.HardwareIds.FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value));
            var (vendorId, productId) = deviceProperties.HardwareIds
                .Select(ParseUsbIds)
                .FirstOrDefault(ids => ids.VendorId is not null && ids.ProductId is not null);
            var serialNumber = NormalizeOptional(deviceProperties.SerialNumber);
            var stableId = CreateStableId(serialNumber, vendorId, productId);

            devices.Add(
                new SerialDeviceInfo(
                    portName,
                    NormalizeOptional(deviceProperties.FriendlyName) ?? portName,
                    NormalizeOptional(deviceProperties.Manufacturer),
                    vendorId,
                    productId,
                    serialNumber,
                    hardwareId,
                    stableId));
        }

        return devices.ToArray();
    }

    private static string? NormalizePortName(string? portName)
    {
        var normalized = NormalizeOptional(portName)?.ToUpperInvariant();
        return normalized is not null && ComPortPattern.IsMatch(normalized) ? normalized : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static (ushort? VendorId, ushort? ProductId) ParseUsbIds(string? hardwareId)
    {
        if (hardwareId is null)
        {
            return (null, null);
        }

        var match = UsbIdPattern.Match(hardwareId);
        if (!match.Success ||
            !ushort.TryParse(match.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendorId) ||
            !ushort.TryParse(match.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var productId))
        {
            return (null, null);
        }

        return (vendorId, productId);
    }

    private static string? CreateStableId(
        string? serialNumber,
        ushort? vendorId,
        ushort? productId)
    {
        if (serialNumber is not null)
        {
            var normalizedSerial = serialNumber.ToUpperInvariant();
            return vendorId is not null && productId is not null
                ? $"serial:{vendorId:X4}:{productId:X4}:{normalizedSerial}"
                : $"serial:{normalizedSerial}";
        }

        return null;
    }
}

public interface IWindowsSerialDeviceNativeAdapter
{
    IReadOnlyList<WindowsSerialDeviceProperties> EnumeratePresentDevices(
        CancellationToken cancellationToken);
}

public sealed record WindowsSerialDeviceProperties
{
    public WindowsSerialDeviceProperties(
        string? PortName,
        string? FriendlyName,
        string? Manufacturer,
        IEnumerable<string> HardwareIds,
        string? InstanceId,
        string? SerialNumber)
    {
        ArgumentNullException.ThrowIfNull(HardwareIds);

        this.PortName = PortName;
        this.FriendlyName = FriendlyName;
        this.Manufacturer = Manufacturer;
        this.HardwareIds = ImmutableArray.CreateRange(HardwareIds);
        this.InstanceId = InstanceId;
        this.SerialNumber = SerialNumber;
    }

    public string? PortName { get; }

    public string? FriendlyName { get; }

    public string? Manufacturer { get; }

    public ImmutableArray<string> HardwareIds { get; }

    public string? InstanceId { get; }

    public string? SerialNumber { get; }
}

internal sealed partial class SetupApiSerialDeviceNativeAdapter : IWindowsSerialDeviceNativeAdapter
{
    private const uint DigcfPresent = 0x00000002;
    private const uint SpdrpDeviceDescription = 0x00000000;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpManufacturer = 0x0000000B;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint DicsFlagGlobal = 0x00000001;
    private const uint DiregDevice = 0x00000001;
    private const uint KeyQueryValue = 0x00000001;
    private const uint RrfRtRegSz = 0x00000002;
    private const int ErrorFileNotFound = 2;
    private const int ErrorInvalidData = 13;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorNotFound = 1168;
    private static readonly IntPtr InvalidHandleValue = new(-1);
    private static readonly Guid PortsClassGuid = new("4D36E978-E325-11CE-BFC1-08002BE10318");
    private static readonly Regex FriendlyNamePortPattern = new(
        @"\((?<port>COM[1-9][0-9]*)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FtdiSerialPattern = new(
        @"^FTDIBUS\\VID_[0-9A-F]{4}\+PID_[0-9A-F]{4}\+(?<serial>[^\\+]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public IReadOnlyList<WindowsSerialDeviceProperties> EnumeratePresentDevices(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows SetupAPI enumeration requires Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var classGuid = PortsClassGuid;
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevsW(
            ref classGuid,
            enumerator: null,
            parentWindow: IntPtr.Zero,
            DigcfPresent);
        if (deviceInfoSet == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var devices = new List<WindowsSerialDeviceProperties>();
            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deviceInfo = DeviceInfoData.Create();
                if (!NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error);
                }

                var friendlyName = GetDevicePropertyString(
                    deviceInfoSet,
                    ref deviceInfo,
                    SpdrpFriendlyName) ??
                    GetDevicePropertyString(deviceInfoSet, ref deviceInfo, SpdrpDeviceDescription);
                var portName = GetPortName(deviceInfoSet, ref deviceInfo) ??
                    ParsePortName(friendlyName);
                if (portName is null)
                {
                    continue;
                }

                var instanceId = GetDeviceInstanceId(deviceInfoSet, ref deviceInfo);
                devices.Add(
                    new WindowsSerialDeviceProperties(
                        portName,
                        friendlyName,
                        GetDevicePropertyString(deviceInfoSet, ref deviceInfo, SpdrpManufacturer),
                        GetDevicePropertyStrings(deviceInfoSet, ref deviceInfo, SpdrpHardwareId),
                        instanceId,
                        ExtractSerialNumber(instanceId)));
            }

            return devices;
        }
        finally
        {
            _ = NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static string? GetPortName(IntPtr deviceInfoSet, ref DeviceInfoData deviceInfo)
    {
        var deviceRegistryKey = NativeMethods.SetupDiOpenDevRegKey(
            deviceInfoSet,
            ref deviceInfo,
            DicsFlagGlobal,
            hardwareProfile: 0,
            DiregDevice,
            KeyQueryValue);
        if (deviceRegistryKey == InvalidHandleValue)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is ErrorFileNotFound or ErrorInvalidData or ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        try
        {
            uint byteCount = 0;
            var result = NativeMethods.RegGetValueW(
                deviceRegistryKey,
                subKey: null,
                valueName: "PortName",
                RrfRtRegSz,
                out _,
                data: null,
                ref byteCount);
            if (result is ErrorFileNotFound or ErrorNotFound)
            {
                return null;
            }

            if (result != 0)
            {
                throw new Win32Exception(result);
            }

            var data = new byte[byteCount];
            result = NativeMethods.RegGetValueW(
                deviceRegistryKey,
                subKey: null,
                valueName: "PortName",
                RrfRtRegSz,
                out _,
                data,
                ref byteCount);
            if (result != 0)
            {
                throw new Win32Exception(result);
            }

            return DecodeRegistryString(data, byteCount);
        }
        finally
        {
            _ = NativeMethods.RegCloseKey(deviceRegistryKey);
        }
    }

    private static string? GetDevicePropertyString(
        IntPtr deviceInfoSet,
        ref DeviceInfoData deviceInfo,
        uint property) =>
        GetDevicePropertyStrings(deviceInfoSet, ref deviceInfo, property).FirstOrDefault();

    private static IReadOnlyList<string> GetDevicePropertyStrings(
        IntPtr deviceInfoSet,
        ref DeviceInfoData deviceInfo,
        uint property)
    {
        if (!NativeMethods.SetupDiGetDeviceRegistryPropertyW(
                deviceInfoSet,
                ref deviceInfo,
                property,
                out _,
                propertyBuffer: null,
                propertyBufferSize: 0,
                out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is ErrorInvalidData or ErrorNotFound)
            {
                return [];
            }

            if (error != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(error);
            }
        }

        if (requiredSize == 0)
        {
            return [];
        }

        var buffer = new byte[requiredSize];
        if (!NativeMethods.SetupDiGetDeviceRegistryPropertyW(
                deviceInfoSet,
                ref deviceInfo,
                property,
                out _,
                buffer,
                (uint)buffer.Length,
                out requiredSize))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var text = Encoding.Unicode.GetString(buffer, 0, checked((int)requiredSize));
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? GetDeviceInstanceId(IntPtr deviceInfoSet, ref DeviceInfoData deviceInfo)
    {
        if (!NativeMethods.SetupDiGetDeviceInstanceIdW(
                deviceInfoSet,
                ref deviceInfo,
                instanceId: null,
                instanceIdSize: 0,
                out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is ErrorInvalidData or ErrorNotFound)
            {
                return null;
            }

            if (error != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(error);
            }
        }

        if (requiredSize == 0)
        {
            return null;
        }

        var instanceId = new StringBuilder(checked((int)requiredSize));
        if (!NativeMethods.SetupDiGetDeviceInstanceIdW(
                deviceInfoSet,
                ref deviceInfo,
                instanceId,
                requiredSize,
                out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return instanceId.ToString();
    }

    private static string? ParsePortName(string? friendlyName)
    {
        if (friendlyName is null)
        {
            return null;
        }

        var match = FriendlyNamePortPattern.Match(friendlyName);
        return match.Success ? match.Groups["port"].Value : null;
    }

    private static string? ExtractSerialNumber(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        var ftdiMatch = FtdiSerialPattern.Match(instanceId);
        if (ftdiMatch.Success)
        {
            return ftdiMatch.Groups["serial"].Value;
        }

        if (!instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var separatorIndex = instanceId.LastIndexOf('\\');
        var candidate = separatorIndex >= 0 ? instanceId[(separatorIndex + 1)..] : string.Empty;
        return candidate.Length > 0 && !candidate.Contains('&', StringComparison.Ordinal)
            ? candidate
            : null;
    }

    private static string? DecodeRegistryString(byte[] data, uint byteCount)
    {
        if (byteCount == 0)
        {
            return null;
        }

        var value = Encoding.Unicode
            .GetString(data, 0, checked((int)byteCount))
            .TrimEnd('\0')
            .Trim();
        return value.Length == 0 ? null : value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DeviceInstance;
        public UIntPtr Reserved;

        public static DeviceInfoData Create() =>
            new() { Size = checked((uint)Marshal.SizeOf<DeviceInfoData>()) };
    }

    private static partial class NativeMethods
    {
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevsW(
            ref Guid classGuid,
            string? enumerator,
            IntPtr parentWindow,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref DeviceInfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr deviceInfoSet,
            ref DeviceInfoData deviceInfoData,
            uint property,
            out uint propertyRegistryDataType,
            byte[]? propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr deviceInfoSet,
            ref DeviceInfoData deviceInfoData,
            StringBuilder? instanceId,
            uint instanceIdSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern IntPtr SetupDiOpenDevRegKey(
            IntPtr deviceInfoSet,
            ref DeviceInfoData deviceInfoData,
            uint scope,
            uint hardwareProfile,
            uint keyType,
            uint samDesired);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        internal static extern int RegGetValueW(
            IntPtr key,
            string? subKey,
            string valueName,
            uint flags,
            out uint valueType,
            byte[]? data,
            ref uint dataSize);

        [DllImport("advapi32.dll")]
        internal static extern int RegCloseKey(IntPtr key);
    }
}
