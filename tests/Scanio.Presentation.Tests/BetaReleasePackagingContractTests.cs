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

        Assert.AreEqual("0.5.0-beta.5", props.Descendants("Version").Single().Value);

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        StringAssert.Contains(readme, "v0.5.0-beta.5");
        StringAssert.Contains(readme, "Scanio-0.5.0-beta.5-win-x64-setup.exe");
        StringAssert.Contains(readme, "Scanio-0.5.0-beta.5-win-x64-portable.zip");
        Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "releases", "v0.5.0-beta.5.md")));

        var fixture = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Scanio.Presentation.Windows.Tests",
            "Fixtures",
            "CPlusFixtureFactory.cs"));
        StringAssert.Contains(fixture, "0.5.0-beta.5");
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
        var uninstallerIndex = workflow.IndexOf("Invoke-BoundedProcess -FilePath $uninstaller", StringComparison.Ordinal);
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
        AssertAppearsInOrder(installerStep, "Stop-Process -Id $process.Id -Force", "$process.WaitForExit(10000)", "Invoke-BoundedProcess -FilePath $uninstaller");
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
        StringAssert.Contains(installerStep, "$leftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)");
        StringAssert.Contains(installerStep, "if ($leftovers.Count -gt 0)");
        StringAssert.Contains(installerStep, "$leftoverNames = $leftovers.Name | Sort-Object");
        StringAssert.Contains(installerStep, "Installer probe retained files after uninstall: $($leftoverNames -join ', ')");
        StringAssert.Contains(installerStep, "Remove-EmptyExactDirectory -LiteralPath $installDir");
        AssertAppearsInOrder(
            installerStep,
            "Invoke-BoundedProcess -FilePath $uninstaller",
            "$leftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)",
            "if ($leftovers.Count -gt 0)",
            "Remove-EmptyExactDirectory -LiteralPath $installDir");
    }

    [TestMethod]
    public void ReleaseWorkflow_WaitsBoundedlyForUninstallerSelfDeletionBeforeCheckingLeftovers()
    {
        var installerStep = WorkflowStep(
            ReleaseWorkflow(),
            "Verify silent installer and retained local data",
            "Write package checksums");

        StringAssert.Contains(installerStep, "$postUninstallCleanupTimeoutMilliseconds = 15000");
        StringAssert.Contains(installerStep, "$postUninstallPollMilliseconds = 100");
        StringAssert.Contains(installerStep, "$postUninstallStopwatch = [System.Diagnostics.Stopwatch]::StartNew()");
        StringAssert.Contains(installerStep, "$postUninstallStopwatch.ElapsedMilliseconds -ge $postUninstallCleanupTimeoutMilliseconds");
        StringAssert.Contains(installerStep, "Start-Sleep -Milliseconds $postUninstallPollMilliseconds");
        Assert.AreEqual(-1, installerStep.IndexOf("Get-Date", StringComparison.Ordinal));
        Assert.AreEqual(
            1,
            CountOccurrences(installerStep, "$leftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)"),
            "The strict leftover assertion must run only once, after the bounded cleanup wait.");
        AssertAppearsInOrder(
            installerStep,
            "if ($uninstallerExitCode -ne 0)",
            "$postUninstallStopwatch = [System.Diagnostics.Stopwatch]::StartNew()",
            "$pendingUninstallLeftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)",
            "if ($pendingUninstallLeftovers.Count -eq 0)",
            "$postUninstallStopwatch.ElapsedMilliseconds -ge $postUninstallCleanupTimeoutMilliseconds",
            "Start-Sleep -Milliseconds $postUninstallPollMilliseconds",
            "$postUninstallStopwatch.Stop()",
            "$leftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)",
            "if ($leftovers.Count -gt 0)");
    }

    [TestMethod]
    public void ReleaseWorkflow_ToleratesOnlyTheExactProbeDisappearingDuringFinalInspection()
    {
        var installerStep = WorkflowStep(
            ReleaseWorkflow(),
            "Verify silent installer and retained local data",
            "Write package checksums");

        StringAssert.Contains(installerStep, "function Get-ExactDirectoryChildren");
        StringAssert.Contains(installerStep, "Get-ChildItem -LiteralPath $LiteralPath -Force -ErrorAction Stop");
        StringAssert.Contains(installerStep, "function Remove-EmptyExactDirectory");
        StringAssert.Contains(installerStep, "Remove-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop");
        Assert.AreEqual(4, CountOccurrences(installerStep, "if (Test-Path -LiteralPath $LiteralPath -ErrorAction Stop) { throw }"));
        Assert.AreEqual(2, CountOccurrences(installerStep, "catch [System.Management.Automation.ItemNotFoundException]"));
        Assert.AreEqual(2, CountOccurrences(installerStep, "catch [System.IO.DirectoryNotFoundException]"));

        var postUninstall = installerStep[installerStep.IndexOf("if ($uninstallerExitCode -ne 0)", StringComparison.Ordinal)..];
        Assert.AreEqual(-1, postUninstall.IndexOf("if (-not (Test-Path -LiteralPath $installDir))", StringComparison.Ordinal));
        Assert.AreEqual(-1, postUninstall.IndexOf("if (Test-Path $installDir)", StringComparison.Ordinal));
        Assert.AreEqual(-1, postUninstall.IndexOf("Get-ChildItem -LiteralPath $installDir", StringComparison.Ordinal));
        Assert.AreEqual(-1, postUninstall.IndexOf("Remove-Item -LiteralPath $installDir", StringComparison.Ordinal));
        AssertAppearsInOrder(
            postUninstall,
            "$pendingUninstallLeftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)",
            "$postUninstallStopwatch.Stop()",
            "if (Test-Path $installedExe)",
            "if (-not (Test-Path $marker))",
            "$leftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)",
            "if ($leftovers.Count -gt 0)",
            "Installer probe retained files after uninstall: $($leftoverNames -join ', ')",
            "Remove-EmptyExactDirectory -LiteralPath $installDir");
    }

    [TestMethod]
    public void ReleaseWorkflow_BoundsGuiInstallerProcessesAndReadsTheirExitCodes()
    {
        var installerStep = WorkflowStep(
            ReleaseWorkflow(),
            "Verify silent installer and retained local data",
            "Write package checksums");

        StringAssert.Contains(installerStep, "function Invoke-BoundedProcess");
        StringAssert.Contains(installerStep, "$processTimeoutMilliseconds = 120000");
        StringAssert.Contains(installerStep, "$cleanupTimeoutMilliseconds = 10000");
        StringAssert.Contains(installerStep, "$externalProcess = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -PassThru");
        StringAssert.Contains(installerStep, "if (-not $externalProcess.WaitForExit($processTimeoutMilliseconds))");
        StringAssert.Contains(installerStep, "$externalProcess.Kill($true)");
        StringAssert.Contains(installerStep, "if (-not $externalProcess.WaitForExit($cleanupTimeoutMilliseconds))");
        StringAssert.Contains(installerStep, "timed out after 120 seconds, and its process tree did not exit within 10 seconds after termination");
        StringAssert.Contains(installerStep, "timed out after 120 seconds; its process tree was terminated");
        StringAssert.Contains(installerStep, "$exitCode = $externalProcess.ExitCode");
        StringAssert.Contains(installerStep, "$externalProcess.Dispose()");
        StringAssert.Contains(installerStep, "$setupExitCode = Invoke-BoundedProcess -FilePath $setup -ArgumentList @(");
        StringAssert.Contains(installerStep, "if ($setupExitCode -ne 0)");
        StringAssert.Contains(installerStep, "$uninstallerExitCode = Invoke-BoundedProcess -FilePath $uninstaller -ArgumentList @(");
        StringAssert.Contains(installerStep, "if ($uninstallerExitCode -ne 0)");
        Assert.AreEqual(2, CountOccurrences(installerStep, "Invoke-BoundedProcess -FilePath"));
        Assert.AreEqual(-1, installerStep.IndexOf(" -Wait", StringComparison.Ordinal));
        Assert.AreEqual(-1, installerStep.IndexOf("& $setup", StringComparison.Ordinal));
        Assert.AreEqual(-1, installerStep.IndexOf("& $uninstaller", StringComparison.Ordinal));
        Assert.AreEqual(-1, installerStep.IndexOf("$LASTEXITCODE", StringComparison.Ordinal));
        Assert.AreEqual(-1, installerStep.IndexOf("\"/DIR=$installDir\"", StringComparison.Ordinal));
        AssertAppearsInOrder(
            installerStep,
            "$setupExitCode = Invoke-BoundedProcess -FilePath $setup",
            "\"/VERYSILENT\"",
            "\"/SUPPRESSMSGBOXES\"",
            "\"/NORESTART\"",
            "\"/SP-\"",
            "('/DIR=\"{0}\"' -f $installDir)",
            "-Description \"Silent installer\"",
            "if ($setupExitCode -ne 0)",
            "if (-not (Test-Path $installedExe))",
            "Start-Process $installedExe -PassThru",
            "$uninstallerExitCode = Invoke-BoundedProcess -FilePath $uninstaller",
            "\"/VERYSILENT\"",
            "\"/SUPPRESSMSGBOXES\"",
            "\"/NORESTART\"",
            "-Description \"Silent uninstaller\"",
            "if ($uninstallerExitCode -ne 0)",
            "if (Test-Path $installedExe)",
            "if (-not (Test-Path $marker))",
            "$leftovers = @(Get-ExactDirectoryChildren -LiteralPath $installDir)");
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
