using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;

namespace Scanio.Platform.Windows.Tests;

[TestClass]
[TestCategory("PortableFixture")]
public sealed class WindowsSerialDeviceEnumeratorTests
{
    [TestMethod]
    public async Task EnumerateAsync_MapsAvailableSetupApiMetadata()
    {
        var source = new RecordingSerialDeviceSource(
            new WindowsSerialDeviceProperties(
                PortName: "COM7",
                FriendlyName: "Datalogic Barcode Scanner (COM7)",
                Manufacturer: "Datalogic",
                HardwareIds: ["USB\\VID_05F9&PID_2214&REV_0100", "USB\\VID_05F9&PID_2214"],
                InstanceId: "USB\\VID_05F9&PID_2214\\DLG-00042",
                SerialNumber: "DLG-00042"));
        var enumerator = new WindowsSerialDeviceEnumerator(source);

        var devices = await enumerator.EnumerateAsync(CancellationToken.None);

        Assert.HasCount(1, devices);
        var device = devices[0];
        Assert.AreEqual("COM7", device.PortName);
        Assert.AreEqual("Datalogic Barcode Scanner (COM7)", device.FriendlyName);
        Assert.AreEqual("Datalogic", device.Manufacturer);
        Assert.AreEqual((ushort)0x05F9, device.VendorId);
        Assert.AreEqual((ushort)0x2214, device.ProductId);
        Assert.AreEqual("DLG-00042", device.SerialNumber);
        Assert.AreEqual("USB\\VID_05F9&PID_2214&REV_0100", device.HardwareId);
        Assert.AreEqual("serial:05F9:2214:DLG-00042", device.StableId);
        Assert.AreEqual(ConnectionState.Detected, device.State);
    }

    [TestMethod]
    public async Task EnumerateAsync_DoesNotInventStableIdentityWhenHardwareIdentityIsMissing()
    {
        var source = new RecordingSerialDeviceSource(
            new WindowsSerialDeviceProperties(
                PortName: "COM12",
                FriendlyName: null,
                Manufacturer: null,
                HardwareIds: [],
                InstanceId: null,
                SerialNumber: null));
        var enumerator = new WindowsSerialDeviceEnumerator(source);

        var devices = await enumerator.EnumerateAsync(CancellationToken.None);

        Assert.HasCount(1, devices);
        Assert.AreEqual("COM12", devices[0].FriendlyName);
        Assert.IsNull(devices[0].StableId);
        Assert.IsFalse(devices[0].HasStableIdentity);
    }

    [TestMethod]
    public async Task EnumerateAsync_KeepsStableIdentityWhenWindowsChangesTheComNumber()
    {
        var before = new WindowsSerialDeviceEnumerator(
            CreateZebraSource("COM4", "Zebra CDC Scanner (COM4)"));
        var after = new WindowsSerialDeviceEnumerator(
            CreateZebraSource("COM18", "Zebra CDC Scanner (COM18)"));

        var beforeDevice = (await before.EnumerateAsync(CancellationToken.None))[0];
        var afterDevice = (await after.EnumerateAsync(CancellationToken.None))[0];

        Assert.AreEqual("COM4", beforeDevice.PortName);
        Assert.AreEqual("COM18", afterDevice.PortName);
        Assert.AreEqual(beforeDevice.StableId, afterDevice.StableId);
        Assert.AreEqual("pnp:USB\\VID_05E0&PID_1200\\6&2A95EF5&0&2", afterDevice.StableId);
    }

    [TestMethod]
    public async Task EnumerateAsync_UsesOnlyPassiveMetadataEnumeration()
    {
        var source = new RecordingSerialDeviceSource(
            new WindowsSerialDeviceProperties(
                PortName: "COM7",
                FriendlyName: "Scanner (COM7)",
                Manufacturer: null,
                HardwareIds: [],
                InstanceId: null,
                SerialNumber: null));
        var enumerator = new WindowsSerialDeviceEnumerator(source);

        _ = await enumerator.EnumerateAsync(CancellationToken.None);

        Assert.AreEqual(1, source.MetadataEnumerationCount);
        Assert.AreEqual(0, source.PortOpenAttemptCount);
    }

    [TestMethod]
    public async Task EnumerateAsync_PropagatesCancellationToTheNativeSource()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new RecordingSerialDeviceSource();
        var enumerator = new WindowsSerialDeviceEnumerator(source);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await enumerator.EnumerateAsync(cancellation.Token));

        Assert.AreEqual(0, source.MetadataEnumerationCount);
        Assert.AreEqual(0, source.PortOpenAttemptCount);
    }

    private static RecordingSerialDeviceSource CreateZebraSource(string portName, string friendlyName) =>
        new(
            new WindowsSerialDeviceProperties(
                PortName: portName,
                FriendlyName: friendlyName,
                Manufacturer: "Zebra Technologies",
                HardwareIds: ["USB\\VID_05E0&PID_1200"],
                InstanceId: "USB\\VID_05E0&PID_1200\\6&2A95EF5&0&2",
                SerialNumber: null));

    private sealed class RecordingSerialDeviceSource(
        params WindowsSerialDeviceProperties[] devices) : IWindowsSerialDeviceNativeAdapter
    {
        private readonly IReadOnlyList<WindowsSerialDeviceProperties> _devices = devices;

        public int MetadataEnumerationCount { get; private set; }

        public int PortOpenAttemptCount { get; private set; }

        public IReadOnlyList<WindowsSerialDeviceProperties> EnumeratePresentDevices(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataEnumerationCount++;
            return _devices;
        }

        // Deliberately outside the injected passive interface. If production code ever
        // grows a probing dependency, this recording seam is where the contract test
        // must observe it.
        public void RecordPortOpenAttempt() => PortOpenAttemptCount++;
    }
}
