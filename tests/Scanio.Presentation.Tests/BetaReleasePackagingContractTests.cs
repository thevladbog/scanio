using System.Xml.Linq;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class BetaReleasePackagingContractTests
{
    [TestMethod]
    public void BetaVersion_AndDownloadDocumentation_ArePublishedTogether()
    {
        var root = RepositoryRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));

        Assert.AreEqual("0.5.0-beta.1", props.Descendants("Version").Single().Value);

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        StringAssert.Contains(readme, "v0.5.0-beta.1");
        StringAssert.Contains(readme, "Scanio-0.5.0-beta.1-win-x64-setup.exe");
        StringAssert.Contains(readme, "Scanio-0.5.0-beta.1-win-x64-portable.zip");
        Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "releases", "v0.5.0-beta.1.md")));
    }

    [TestMethod]
    public void ReleaseWorkflow_BuildsAndVerifiesInstallerAndPortablePackages()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));
        string[] required =
        [
            "installer/Scanio.iss", "ISCC.exe", "Scanio-$version-win-x64-setup.exe",
            "Scanio-$version-win-x64-portable.zip", "SHA256SUMS.txt", "/VERYSILENT",
            "unins000.exe", "portable.flag", "installer-retention.marker"
        ];

        foreach (var token in required)
        {
            StringAssert.Contains(workflow, token);
        }

        StringAssert.Contains(workflow, "if (-not (Test-Path $installedExe))");
        StringAssert.Contains(workflow, "if (Test-Path (Join-Path $installDir \"portable.flag\"))");
        StringAssert.Contains(workflow, "if (-not (Test-Path $marker))");
        var uninstallerIndex = workflow.IndexOf("& $uninstaller", StringComparison.Ordinal);
        var retainedMarkerIndex = workflow.IndexOf(
            "if (-not (Test-Path $marker))",
            uninstallerIndex,
            StringComparison.Ordinal);
        Assert.IsLessThan(
            retainedMarkerIndex,
            uninstallerIndex,
            "The workflow must verify the retained marker after uninstalling.");
        StringAssert.Contains(workflow, "gh release create");
        StringAssert.Contains(workflow, "artifacts/Scanio-$version-win-x64-portable.zip");
        StringAssert.Contains(workflow, "artifacts/Scanio-$version-win-x64-setup.exe");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (true)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            if (directory.Parent is null)
            {
                throw new DirectoryNotFoundException("Could not locate the repository root.");
            }

            directory = directory.Parent;
        }
    }
}
