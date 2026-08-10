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

        Assert.AreEqual("0.5.0-beta.4", props.Descendants("Version").Single().Value);

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        StringAssert.Contains(readme, "v0.5.0-beta.4");
        StringAssert.Contains(readme, "Scanio-0.5.0-beta.4-win-x64-setup.exe");
        StringAssert.Contains(readme, "Scanio-0.5.0-beta.4-win-x64-portable.zip");
        Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "releases", "v0.5.0-beta.4.md")));

        var fixture = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Scanio.Presentation.Windows.Tests",
            "Fixtures",
            "CPlusFixtureFactory.cs"));
        StringAssert.Contains(fixture, "0.5.0-beta.4");
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
        var uninstallerIndex = workflow.IndexOf("Start-Process -FilePath $uninstaller", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, uninstallerIndex, "The positive installer probe must start the uninstaller as a process.");
        var retainedMarkerIndex = workflow.IndexOf(
            "if (-not (Test-Path $marker))",
            uninstallerIndex,
            StringComparison.Ordinal);
        if (uninstallerIndex >= retainedMarkerIndex)
        {
            Assert.Fail("The workflow must verify the retained marker after uninstalling.");
        }
        StringAssert.Contains(workflow, "gh release create");
        StringAssert.Contains(workflow, "artifacts/Scanio-$version-win-x64-portable.zip");
        StringAssert.Contains(workflow, "artifacts/Scanio-$version-win-x64-setup.exe");
    }

    [TestMethod]
    public void ReleaseWorkflow_PinsTheExactInstalledInnoSetupProductBeforeInvokingCompiler()
    {
        var workflow = ReleaseWorkflow();
        var compileStep = WorkflowStep(workflow, "Compile per-user installer", "Verify silent installer and retained local data");

        StringAssert.Contains(compileStep, "$expectedInnoSetupVersion = \"6.7.1\"");
        StringAssert.Contains(compileStep, "[Microsoft.Win32.RegistryView]::Registry32");
        StringAssert.Contains(compileStep, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Inno Setup 6_is1");
        StringAssert.Contains(compileStep, "GetValue(\"DisplayVersion\")");
        StringAssert.Contains(compileStep, "GetValue(\"InstallLocation\")");
        StringAssert.Contains(compileStep, "[System.IO.Path]::GetFullPath");
        StringAssert.Contains(compileStep, "[StringComparison]::OrdinalIgnoreCase");
        StringAssert.Contains(compileStep, "$innoSetupKey.Dispose()");
        StringAssert.Contains(compileStep, "$uninstallRoot.Dispose()");
        StringAssert.Contains(compileStep, "Expected version '$expectedInnoSetupVersion'");
        StringAssert.Contains(compileStep, "selected compiler '$iscc'");
        Assert.AreEqual(-1, compileStep.IndexOf("FileVersionInfo", StringComparison.Ordinal));
        Assert.AreEqual(-1, compileStep.IndexOf("FileMajorPart", StringComparison.Ordinal));
        Assert.AreEqual(-1, compileStep.IndexOf("ProductMajorPart", StringComparison.Ordinal));
        AssertAppearsInOrder(
            compileStep,
            "$iscc = $isccCandidates",
            "$expectedInnoSetupVersion = \"6.7.1\"",
            "[Microsoft.Win32.RegistryView]::Registry32",
            "GetValue(\"DisplayVersion\")",
            "GetValue(\"InstallLocation\")",
            "$uninstallRoot.Dispose()",
            "& $iscc");
    }

    [TestMethod]
    public void ReleaseWorkflow_WaitsForAllSmokeProcessesBeforeConsumingTheirFiles()
    {
        var workflow = ReleaseWorkflow();
        var publishStep = WorkflowStep(workflow, "Publish self-contained Windows application", "Build portable package");
        var portableStep = WorkflowStep(workflow, "Build portable package", "Compile per-user installer");
        var installerStep = WorkflowStep(workflow, "Verify silent installer and retained local data", "Write package checksums");

        Assert.IsFalse(
            System.Text.RegularExpressions.Regex.IsMatch(
                workflow,
                @"(?<![\w])\d[\d_]*_\d[\d_]*(?![\w])",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            "PowerShell workflow scripts must not contain numeric separators.");
        Assert.AreEqual(3, CountOccurrences(workflow, "$process.WaitForExit(10000)"));
        StringAssert.Contains(publishStep, "Published Scanio process did not exit within 10 seconds after forced stop.");
        StringAssert.Contains(portableStep, "Portable Scanio process did not exit within 10 seconds after forced stop.");
        StringAssert.Contains(installerStep, "Installed Scanio process did not exit within 10 seconds after forced stop.");
        AssertAppearsInOrder(publishStep, "Stop-Process -Id $process.Id -Force", "$process.WaitForExit(10000)");
        AssertAppearsInOrder(portableStep, "Stop-Process -Id $process.Id -Force", "$process.WaitForExit(10000)", "Compress-Archive");
        AssertAppearsInOrder(installerStep, "Stop-Process -Id $process.Id -Force", "$process.WaitForExit(10000)", "Start-Process -FilePath $uninstaller");
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
    public void ReleaseWorkflow_PassesTheReleaseTagToPowerShellThroughStepEnvironment()
    {
        var workflow = ReleaseWorkflow();
        var expressionLines = workflow
            .Split('\n')
            .Where(line => line.Contains("${{ github.ref_name }}", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();

        Assert.HasCount(5, expressionLines);
        Assert.IsTrue(expressionLines.All(line => line == "RELEASE_TAG: ${{ github.ref_name }}"));

        foreach (var (stepName, nextStepName) in new[]
                 {
                     ("Build portable package", "Compile per-user installer"),
                     ("Compile per-user installer", "Verify installer refuses a portable directory"),
                     ("Verify silent installer and retained local data", "Write package checksums"),
                     ("Write package checksums", "Create GitHub release")
                 })
        {
            var step = WorkflowStep(workflow, stepName, nextStepName);
            StringAssert.Contains(step, "RELEASE_TAG: ${{ github.ref_name }}");
            StringAssert.Contains(step, "$env:RELEASE_TAG");
        }

        var releaseStep = workflow[workflow.IndexOf("      - name: Create GitHub release", StringComparison.Ordinal)..];
        StringAssert.Contains(releaseStep, "RELEASE_TAG: ${{ github.ref_name }}");
        StringAssert.Contains(releaseStep, "$releaseTag = $env:RELEASE_TAG");
        Assert.AreEqual(-1, releaseStep.IndexOf("${{ github.ref_name }}", releaseStep.IndexOf("run: |", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReleaseWorkflow_ProvesInstallerRefusesPortableDirectoryWithoutChangingItsData()
    {
        var step = WorkflowStep(
            ReleaseWorkflow(),
            "Verify installer refuses a portable directory",
            "Verify silent installer and retained local data");

        string[] required =
        [
            "$runnerTemp = [System.IO.Path]::GetFullPath($env:RUNNER_TEMP)",
            "ScanioPortableConflictProbe-$([Guid]::NewGuid().ToString('N'))",
            "$portableConflictDir = Join-Path $runnerTemp $probeName",
            "if (Test-Path -LiteralPath $portableConflictDir)",
            "portable.flag",
            "Data/scanio.db",
            "Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256",
            "$manifestBefore = Get-ProbeManifest $portableConflictDir",
            "/VERYSILENT",
            "/DIR=$portableConflictDir",
            "if ($installerExitCode -eq 0)",
            "$manifestAfter = Get-ProbeManifest $portableConflictDir",
            "Compare-Object -ReferenceObject $manifestBefore -DifferenceObject $manifestAfter",
            "Remove-Item -LiteralPath $portableConflictDir -Recurse -Force"
        ];

        foreach (var token in required)
        {
            StringAssert.Contains(step, token);
        }

        AssertAppearsInOrder(
            step,
            "$manifestBefore = Get-ProbeManifest $portableConflictDir",
            "Start-Process -FilePath $setup",
            "if ($installerExitCode -eq 0)",
            "$manifestAfter = Get-ProbeManifest $portableConflictDir",
            "Compare-Object -ReferenceObject $manifestBefore -DifferenceObject $manifestAfter");
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
            "Start-Process -FilePath $uninstaller",
            "$leftovers = @(Get-ChildItem -LiteralPath $installDir -Force)",
            "if ($leftovers.Count -gt 0)",
            "Remove-Item -LiteralPath $installDir -Force");
    }

    [TestMethod]
    public void ReleaseWorkflow_WaitsForGuiInstallerProcessesAndReadsTheirExitCodes()
    {
        var installerStep = WorkflowStep(
            ReleaseWorkflow(),
            "Verify silent installer and retained local data",
            "Write package checksums");

        StringAssert.Contains(installerStep, "$setupProcess = Start-Process -FilePath $setup -ArgumentList @(");
        StringAssert.Contains(installerStep, ") -Wait -PassThru");
        StringAssert.Contains(installerStep, "$setupExitCode = $setupProcess.ExitCode");
        StringAssert.Contains(installerStep, "if ($setupExitCode -ne 0)");
        StringAssert.Contains(installerStep, "$uninstallerProcess = Start-Process -FilePath $uninstaller -ArgumentList @(");
        StringAssert.Contains(installerStep, "$uninstallerExitCode = $uninstallerProcess.ExitCode");
        StringAssert.Contains(installerStep, "if ($uninstallerExitCode -ne 0)");
        Assert.AreEqual(2, CountOccurrences(installerStep, ") -Wait -PassThru"));
        Assert.AreEqual(-1, installerStep.IndexOf("& $setup", StringComparison.Ordinal));
        Assert.AreEqual(-1, installerStep.IndexOf("& $uninstaller", StringComparison.Ordinal));
        Assert.AreEqual(-1, installerStep.IndexOf("$LASTEXITCODE", StringComparison.Ordinal));
        AssertAppearsInOrder(
            installerStep,
            "$setupProcess = Start-Process -FilePath $setup",
            "\"/VERYSILENT\"",
            "\"/SUPPRESSMSGBOXES\"",
            "\"/NORESTART\"",
            "\"/SP-\"",
            "\"/DIR=$installDir\"",
            ") -Wait -PassThru",
            "$setupExitCode = $setupProcess.ExitCode",
            "if ($setupExitCode -ne 0)",
            "if (-not (Test-Path $installedExe))",
            "Start-Process $installedExe -PassThru",
            "Start-Process -FilePath $uninstaller",
            "\"/VERYSILENT\"",
            "\"/SUPPRESSMSGBOXES\"",
            "\"/NORESTART\"",
            ") -Wait -PassThru",
            "$uninstallerExitCode = $uninstallerProcess.ExitCode",
            "if ($uninstallerExitCode -ne 0)",
            "if (Test-Path $installedExe)",
            "if (-not (Test-Path $marker))",
            "$leftovers = @(Get-ChildItem -LiteralPath $installDir -Force)");
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
