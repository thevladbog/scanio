using Scanio.Domain.Transport;
using Scanio.Platform.Windows.Devices;
using System.Reflection;
using System.Runtime.InteropServices;

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
        Assert.AreEqual("serial:05E0:1200:ZBR-001", afterDevice.StableId);
    }

    [TestMethod]
    public async Task EnumerateAsync_DoesNotTreatLocationDerivedInstanceIdAsStableIdentity()
    {
        var enumerator = new WindowsSerialDeviceEnumerator(CreateLocationDerivedZebraSource("COM7"));

        var device = (await enumerator.EnumerateAsync(CancellationToken.None))[0];

        Assert.IsNull(device.StableId);
        Assert.IsFalse(device.HasStableIdentity);
    }

    [TestMethod]
    public async Task EnumerateAsync_FindsUsbIdsInAnyHardwareId()
    {
        var enumerator = new WindowsSerialDeviceEnumerator(
            new RecordingSerialDeviceSource(
                new WindowsSerialDeviceProperties(
                    PortName: "COM9",
                    FriendlyName: "Scanner (COM9)",
                    Manufacturer: null,
                    HardwareIds: ["USB\\Class_02&SubClass_02", "USB\\VID_05F9&PID_2214"],
                    InstanceId: null,
                    SerialNumber: "DLG-9")));

        var device = (await enumerator.EnumerateAsync(CancellationToken.None))[0];

        Assert.AreEqual((ushort)0x05F9, device.VendorId);
        Assert.AreEqual((ushort)0x2214, device.ProductId);
        Assert.AreEqual("serial:05F9:2214:DLG-9", device.StableId);
    }

    [TestMethod]
    public void PlatformAssembly_HasNoPortOpeningDependencyOrCreateFileImport()
    {
        var assembly = typeof(WindowsSerialDeviceEnumerator).Assembly;

        Assert.IsFalse(assembly.GetReferencedAssemblies().Any(reference =>
            string.Equals(reference.Name, "System.IO.Ports", StringComparison.Ordinal)));

        var importedEntryPoints = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<DllImportAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.EntryPoint ?? string.Empty)
            .ToArray();

        CollectionAssert.DoesNotContain(importedEntryPoints, "CreateFileW");
        CollectionAssert.DoesNotContain(importedEntryPoints, "CreateFileA");
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
    }

    private static RecordingSerialDeviceSource CreateZebraSource(string portName, string friendlyName) =>
        new(
            new WindowsSerialDeviceProperties(
                PortName: portName,
                FriendlyName: friendlyName,
                Manufacturer: "Zebra Technologies",
                HardwareIds: ["USB\\VID_05E0&PID_1200"],
                InstanceId: "USB\\VID_05E0&PID_1200\\ZBR-001",
                SerialNumber: "ZBR-001"));

    private static RecordingSerialDeviceSource CreateLocationDerivedZebraSource(string portName) =>
        new(
            new WindowsSerialDeviceProperties(
                PortName: portName,
                FriendlyName: $"Zebra CDC Scanner ({portName})",
                Manufacturer: "Zebra Technologies",
                HardwareIds: ["USB\\VID_05E0&PID_1200"],
                InstanceId: "USB\\VID_05E0&PID_1200\\6&2A95EF5&0&2",
                SerialNumber: null));

    private sealed class RecordingSerialDeviceSource(
        params WindowsSerialDeviceProperties[] devices) : IWindowsSerialDeviceNativeAdapter
    {
        private readonly IReadOnlyList<WindowsSerialDeviceProperties> _devices = devices;

        public int MetadataEnumerationCount { get; private set; }

        public IReadOnlyList<WindowsSerialDeviceProperties> EnumeratePresentDevices(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataEnumerationCount++;
            return _devices;
        }

    }
}
