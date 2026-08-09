using Scanio.Storage;

namespace Scanio.Storage.Tests;

[TestClass]
public sealed class NotebookDatabasePathTests
{
    [TestMethod]
    public void Resolve_PortableModeUsesApplicationDataDirectory()
    {
        var path = NotebookDatabasePath.Resolve(true, "/app", "/local");

        Assert.AreEqual(Path.Combine("/app", "Data", "scanio.db"), path);
    }

    [TestMethod]
    public void Resolve_InstalledModeUsesLocalApplicationData()
    {
        var path = NotebookDatabasePath.Resolve(false, "/app", "/local");

        Assert.AreEqual(Path.Combine("/local", "Scanio", "scanio.db"), path);
    }
}
