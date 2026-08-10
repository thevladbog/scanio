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

        var fixture = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Scanio.Presentation.Windows.Tests",
            "Fixtures",
            "CPlusFixtureFactory.cs"));
        StringAssert.Contains(fixture, "0.5.0-beta.1");
        Assert.AreEqual(-1, fixture.IndexOf("0.5.0-alpha.2", StringComparison.Ordinal));
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
        StringAssert.Contains(workflow, "$portableDatabase = Join-Path $portable \"Data/scanio.db\"");
        StringAssert.Contains(workflow, "if (-not (Test-Path $portableDatabase))");
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

    [TestMethod]
    public void ReleaseWorkflow_PinsTheExactInnoSetupCompilerVersion()
    {
        var workflow = ReleaseWorkflow();
        var compileStep = WorkflowStep(workflow, "Compile per-user installer", "Verify silent installer and retained local data");

        StringAssert.Contains(compileStep, "$expectedIsccVersion = [Version]\"6.7.1.0\"");
        StringAssert.Contains(compileStep, "[System.Diagnostics.FileVersionInfo]::GetVersionInfo($iscc)");
        StringAssert.Contains(compileStep, "$isccFileVersion = [Version]::new(");
        StringAssert.Contains(compileStep, "$isccProductVersion = [Version]::new(");
        StringAssert.Contains(
            compileStep,
            "if ($isccFileVersion -ne $expectedIsccVersion -or $isccProductVersion -ne $expectedIsccVersion)");
        StringAssert.Contains(compileStep, "Inno Setup compiler version mismatch");
        AssertAppearsInOrder(
            compileStep,
            "$expectedIsccVersion = [Version]\"6.7.1.0\"",
            "[System.Diagnostics.FileVersionInfo]::GetVersionInfo($iscc)",
            "if ($isccFileVersion -ne $expectedIsccVersion -or $isccProductVersion -ne $expectedIsccVersion)",
            "& $iscc");
    }

    [TestMethod]
    public void ReleaseWorkflow_WaitsForAllSmokeProcessesBeforeConsumingTheirFiles()
    {
        var workflow = ReleaseWorkflow();
        var publishStep = WorkflowStep(workflow, "Publish self-contained Windows application", "Build portable package");
        var portableStep = WorkflowStep(workflow, "Build portable package", "Compile per-user installer");
        var installerStep = WorkflowStep(workflow, "Verify silent installer and retained local data", "Write package checksums");

        Assert.AreEqual(3, CountOccurrences(workflow, "$process.WaitForExit(10_000)"));
        StringAssert.Contains(publishStep, "Published Scanio process did not exit within 10 seconds after forced stop.");
        StringAssert.Contains(portableStep, "Portable Scanio process did not exit within 10 seconds after forced stop.");
        StringAssert.Contains(installerStep, "Installed Scanio process did not exit within 10 seconds after forced stop.");
        AssertAppearsInOrder(publishStep, "Stop-Process -Id $process.Id -Force", "$process.WaitForExit(10_000)");
        AssertAppearsInOrder(portableStep, "Stop-Process -Id $process.Id -Force", "$process.WaitForExit(10_000)", "Compress-Archive");
        AssertAppearsInOrder(installerStep, "Stop-Process -Id $process.Id -Force", "$process.WaitForExit(10_000)", "& $uninstaller");
    }

    [TestMethod]
    public void ReleaseWorkflow_VerifiesPortableDatabaseBeforeCompression()
    {
        var portableStep = WorkflowStep(ReleaseWorkflow(), "Build portable package", "Compile per-user installer");

        AssertAppearsInOrder(
            portableStep,
            "portable.flag",
            "$portableDatabase = Join-Path $portable \"Data/scanio.db\"",
            "Start-Process (Join-Path $portable \"Scanio.exe\")",
            "if (-not (Test-Path $portableDatabase))",
            "Compress-Archive");
    }

    [TestMethod]
    public void ReleaseWorkflow_HashesExactlyTheTwoReleasePackages()
    {
        var checksumStep = WorkflowStep(ReleaseWorkflow(), "Write package checksums", "Create GitHub release");

        StringAssert.Contains(checksumStep, "$packages = @(");
        StringAssert.Contains(checksumStep, "artifacts/Scanio-$version-win-x64-portable.zip");
        StringAssert.Contains(checksumStep, "artifacts/Scanio-$version-win-x64-setup.exe");
        StringAssert.Contains(checksumStep, "if ($packages.Count -ne 2)");
        StringAssert.Contains(checksumStep, "Expected exactly two release packages for SHA256SUMS.txt");
    }

    [TestMethod]
    public void ReleaseWorkflow_PreservesUnexpectedInstallerProbeLeftovers()
    {
        var workflow = ReleaseWorkflow();
        var installerStep = WorkflowStep(workflow, "Verify silent installer and retained local data", "Write package checksums");

        Assert.AreEqual(1, CountOccurrences(workflow, "Remove-Item -LiteralPath $installDir -Recurse -Force"));
        StringAssert.Contains(installerStep, "$leftovers = @(Get-ChildItem -LiteralPath $installDir -Force)");
        StringAssert.Contains(installerStep, "if ($leftovers.Count -gt 0)");
        StringAssert.Contains(installerStep, "$leftoverNames = $leftovers.Name | Sort-Object");
        StringAssert.Contains(installerStep, "Installer probe retained files after uninstall: $($leftoverNames -join ', ')");
        StringAssert.Contains(installerStep, "Remove-Item -LiteralPath $installDir -Force");
        AssertAppearsInOrder(
            installerStep,
            "& $uninstaller",
            "$leftovers = @(Get-ChildItem -LiteralPath $installDir -Force)",
            "if ($leftovers.Count -gt 0)",
            "Remove-Item -LiteralPath $installDir -Force");
    }

    private static string ReleaseWorkflow() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));

    private static string WorkflowStep(string workflow, string name, string nextName)
    {
        var marker = $"      - name: {name}";
        var nextMarker = $"      - name: {nextName}";
        var start = workflow.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            Assert.Fail($"Workflow step not found: {name}");
        }

        var end = workflow.IndexOf(nextMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            Assert.Fail($"Workflow step not found after {name}: {nextName}");
        }

        return workflow[start..end];
    }

    private static void AssertAppearsInOrder(string source, params string[] fragments)
    {
        var searchFrom = 0;
        foreach (var fragment in fragments)
        {
            var index = source.IndexOf(fragment, searchFrom, StringComparison.Ordinal);
            if (index < 0)
            {
                Assert.Fail($"Expected fragment after position {searchFrom}: {fragment}");
            }

            searchFrom = index + fragment.Length;
        }
    }

    private static int CountOccurrences(string source, string token) =>
        source.Split(token, StringSplitOptions.None).Length - 1;

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
