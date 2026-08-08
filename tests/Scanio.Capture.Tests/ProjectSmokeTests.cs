namespace Scanio.Capture.Tests;

[TestClass]
public sealed class ProjectSmokeTests
{
    [TestMethod]
    public void TestsRunOnDotNetTen() => Assert.AreEqual(10, Environment.Version.Major);
}
