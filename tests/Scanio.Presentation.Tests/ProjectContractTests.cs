using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class ProjectContractTests
{
    [TestMethod]
    public void WindowsTarget_EmbedsTheScanioExecutableIcon()
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Scanio.Presentation",
            "Scanio.Presentation.csproj"));
        var project = XDocument.Load(projectPath);

        var applicationIcon = project.Descendants("ApplicationIcon").SingleOrDefault();

        Assert.IsNotNull(applicationIcon, "The Windows application must embed an icon in Scanio.exe.");
        Assert.AreEqual("Assets\\scanio.ico", applicationIcon.Value);
        Assert.AreEqual(
            "'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'",
            (string?)applicationIcon.Attribute("Condition"));
    }
}
