# Scanio Beta Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish `v0.5.0-beta.1` with a no-admin per-user Inno Setup executable, retain the portable ZIP, and verify both artifacts through Windows CI and GitHub Release inspection.

**Architecture:** Keep the current self-contained WPF publish as the single binary source. Copy that neutral publish into a portable package with `portable.flag`, and compile the same files into an installer without that flag. Portable contract tests inspect packaging definitions; Windows CI performs authoritative silent install, installed-mode launch, data-retention, uninstall, WPF, and artifact checks.

**Tech Stack:** .NET 10, WPF, MSTest 4, Inno Setup 6, PowerShell 7, GitHub Actions `windows-2025`, GitHub Releases.

## Global Constraints

- Release version is `0.5.0-beta.1`; tag is `v0.5.0-beta.1`.
- Artifacts are `Scanio-0.5.0-beta.1-win-x64-setup.exe`, `Scanio-0.5.0-beta.1-win-x64-portable.zip`, and `SHA256SUMS.txt`.
- Installation is per-user at `%LOCALAPPDATA%\Programs\Scanio` with `PrivilegesRequired=lowest`.
- Installed data remains at `%LOCALAPPDATA%\Scanio`; uninstall preserves it.
- Portable behavior remains selected only by adjacent `portable.flag` and uses `Data/scanio.db`.
- Application and installer remain unsigned; SmartScreen must not be bypassed.
- Repository documentation and release notes remain English.
- Preserve untracked `design/`, `docs/design/`, and unrelated files under `docs/superpowers/plans/`.
- Do not tag, merge, or publish until pull-request Windows CI is green.
- Physical keyboard-wedge, Datalogic, and Zebra acceptance remains user-run.

---

### Task 1: Repository-owned per-user Inno Setup definition

**Files:**
- Create: `installer/Scanio.iss`
- Create: `tests/Scanio.Presentation.Tests/InstallerDefinitionContractTests.cs`

**Interfaces:**
- Consumes: publish directory define `SourceDir`, release define `AppVersion`, artifact define `OutputDir`.
- Produces: `Scanio-{AppVersion}-win-x64-setup.exe` with upgrade identity `{B786AC90-6A74-4E80-AE30-8D3C15A8C9C2}`.

- [ ] **Step 1: Write failing installer definition contracts**

Create a test helper that walks parents from `AppContext.BaseDirectory` until it finds `Directory.Build.props`, throwing `DirectoryNotFoundException` at the filesystem root. Add tests that read `installer/Scanio.iss` and assert:

```csharp
var scriptPath = Path.Combine(RepositoryRoot(), "installer", "Scanio.iss");
Assert.IsTrue(File.Exists(scriptPath), $"Missing installer definition: {scriptPath}");
var script = File.ReadAllText(scriptPath);
StringAssert.Contains(script, "AppId={{B786AC90-6A74-4E80-AE30-8D3C15A8C9C2}");
StringAssert.Contains(script, "PrivilegesRequired=lowest");
StringAssert.Contains(script, @"DefaultDirName={localappdata}\Programs\Scanio");
StringAssert.Contains(script, "CloseApplications=yes");
StringAssert.Contains(script, "RestartApplications=no");
Assert.IsFalse(script.Contains("[UninstallDelete]", StringComparison.OrdinalIgnoreCase));
Assert.IsFalse(script.Contains(@"{localappdata}\Scanio", StringComparison.OrdinalIgnoreCase));
```

Add a second test for English and Russian languages, Start menu shortcut, optional unchecked desktop shortcut, `scanio.ico`, and `Excludes: "portable.flag,Data\*"`.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test tests/Scanio.Presentation.Tests/Scanio.Presentation.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~InstallerDefinitionContractTests -m:1 -nr:false
```

Expected: FAIL because `installer/Scanio.iss` does not exist.

- [ ] **Step 3: Create the complete Inno Setup definition**

Create `installer/Scanio.iss` with this behavior:

```iss
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{B786AC90-6A74-4E80-AE30-8D3C15A8C9C2}
AppName=Scanio
AppVersion={#AppVersion}
AppPublisher=Scanio
DefaultDirName={localappdata}\Programs\Scanio
DefaultGroupName=Scanio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=Scanio-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\Scanio.Presentation\Assets\scanio.ico
UninstallDisplayIcon={app}\Scanio.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.flag,Data\*"

[Icons]
Name: "{userprograms}\Scanio"; Filename: "{app}\Scanio.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Scanio"; Filename: "{app}\Scanio.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Scanio.exe"; Description: "{cm:LaunchProgram,Scanio}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
```

Do not add data deletion, services, startup entries, protocols, or updates.

- [ ] **Step 4: Run focused and full Presentation tests and verify GREEN**

Run the focused command from Step 2, then:

```bash
dotnet test tests/Scanio.Presentation.Tests/Scanio.Presentation.Tests.csproj -c Release --no-restore -m:1 -nr:false
```

Expected: all tests pass with zero failures.

- [ ] **Step 5: Review and commit Task 1**

Run `git diff --check`, inspect the exact installer script, and commit only:

```bash
git add installer/Scanio.iss tests/Scanio.Presentation.Tests/InstallerDefinitionContractTests.cs
git commit -m "feat: add per-user Windows installer"
```

---

### Task 2: Beta packaging contracts, workflow, version, and documentation

**Files:**
- Create: `tests/Scanio.Presentation.Tests/BetaReleasePackagingContractTests.cs`
- Create: `docs/releases/v0.5.0-beta.1.md`
- Modify: `Directory.Build.props`
- Modify: `.github/workflows/release.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: `installer/Scanio.iss`, WPF project, icon, Windows test suite, tag version.
- Produces: setup EXE, portable ZIP, two-entry checksum, and GitHub prerelease.

- [ ] **Step 1: Write failing beta release contracts**

Create `BetaReleasePackagingContractTests.cs` with the same repository-root helper. Add a version and documentation test:

```csharp
var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
Assert.AreEqual("0.5.0-beta.1", props.Descendants("Version").Single().Value);
var readme = File.ReadAllText(Path.Combine(root, "README.md"));
StringAssert.Contains(readme, "v0.5.0-beta.1");
StringAssert.Contains(readme, "Scanio-0.5.0-beta.1-win-x64-setup.exe");
StringAssert.Contains(readme, "Scanio-0.5.0-beta.1-win-x64-portable.zip");
Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "releases", "v0.5.0-beta.1.md")));
```

Add a workflow test requiring these tokens:

```csharp
string[] required =
[
    "installer/Scanio.iss", "ISCC.exe", "Scanio-$version-win-x64-setup.exe",
    "Scanio-$version-win-x64-portable.zip", "SHA256SUMS.txt", "/VERYSILENT",
    "unins000.exe", "portable.flag", "installer-retention.marker"
];
```

Also assert the workflow checks installed `Scanio.exe`, rejects installed `portable.flag`, verifies the retained marker after uninstall, and uploads both packages.

- [ ] **Step 2: Run focused tests and verify RED**

```bash
dotnet test tests/Scanio.Presentation.Tests/Scanio.Presentation.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~BetaReleasePackagingContractTests -m:1 -nr:false
```

Expected: FAIL on alpha version, missing beta notes, alpha README, and missing installer workflow.

- [ ] **Step 3: Update beta version and English documentation**

Set `<Version>0.5.0-beta.1</Version>` in `Directory.Build.props`. Make the setup EXE the primary README download and the portable ZIP the no-install alternative. Document no-admin installation, `%LOCALAPPDATA%\Scanio`, uninstall retention, optional desktop shortcut, unsigned SmartScreen warning, and no automatic updates. Change “current alpha” to “current beta”.

Create `docs/releases/v0.5.0-beta.1.md` with installer behavior, portable alternative, checksum instructions, unsigned boundary, retained local data, and user-run physical hardware acceptance.

- [ ] **Step 4: Publish once into a neutral staging directory**

Refactor `.github/workflows/release.yml` to publish into `artifacts/publish`, smoke-launch that executable, then copy those files to `artifacts/Scanio-$version-win-x64-portable`, add `Data`, `portable.flag`, and README, and compress the portable directory. The neutral publish must never receive `portable.flag`.

- [ ] **Step 5: Compile the setup executable with pinned runner Inno Setup**

Locate only these paths and fail clearly otherwise:

```powershell
$isccCandidates = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 compiler ISCC.exe was not found on the Windows runner." }
& $iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$(Join-Path $PWD 'artifacts')" "installer/Scanio.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
```

- [ ] **Step 6: Add silent install, installed launch, retention, and uninstall checks**

Use `$env:RUNNER_TEMP\ScanioInstallerProbe` as `/DIR`. Seed only `%LOCALAPPDATA%\Scanio\installer-retention.marker`. Run setup using `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`, then assert `Scanio.exe` and `unins000.exe` exist and `portable.flag` does not. Smoke-launch the installed EXE for five seconds. Run `unins000.exe` with silent flags, assert installed `Scanio.exe` is gone and the marker remains, then delete only that marker. Never recursively delete `%LOCALAPPDATA%\Scanio`.

- [ ] **Step 7: Hash and upload both artifacts**

Sort the two package paths, compute SHA-256 with `Get-FileHash`, and write exactly two newline-terminated entries to `artifacts/SHA256SUMS.txt`. Pass the portable ZIP, setup EXE, and checksum explicitly to `gh release create`. Keep `--prerelease` and tag-derived `docs/releases/${{ github.ref_name }}.md`.

- [ ] **Step 8: Run focused and all portable tests**

Run the focused beta contract, then all seven portable projects:

```bash
dotnet test tests/Scanio.Capture.Tests/Scanio.Capture.Tests.csproj -c Release --no-restore -m:1 -nr:false
dotnet test tests/Scanio.Analysis.Tests/Scanio.Analysis.Tests.csproj -c Release --no-restore -m:1 -nr:false
dotnet test tests/Scanio.Transports.Tests/Scanio.Transports.Tests.csproj -c Release --no-restore -m:1 -nr:false
dotnet test tests/Scanio.Application.Tests/Scanio.Application.Tests.csproj -c Release --no-restore -m:1 -nr:false
dotnet test tests/Scanio.Storage.Tests/Scanio.Storage.Tests.csproj -c Release --no-restore -m:1 -nr:false
dotnet test tests/Scanio.Platform.Windows.Tests/Scanio.Platform.Windows.Tests.csproj -c Release --no-restore -m:1 -nr:false
dotnet test tests/Scanio.Presentation.Tests/Scanio.Presentation.Tests.csproj -c Release --no-restore -m:1 -nr:false
```

Expected: every command exits 0 with no failed or skipped portable tests.

- [ ] **Step 9: Build Windows test target and full solution**

```bash
dotnet build tests/Scanio.Presentation.Windows.Tests/Scanio.Presentation.Windows.Tests.csproj -c Release --no-restore --framework net10.0-windows10.0.19041.0 -m:1 -nr:false
dotnet build Scanio.slnx -c Release --no-restore -m:1 -nr:false
```

Expected: zero warnings and zero errors. Do not claim WPF runtime execution on macOS.

- [ ] **Step 10: Review and commit Task 2**

Run `git diff --check`; review PowerShell quoting, explicit paths, and deletion scope. Commit only:

```bash
git add Directory.Build.props .github/workflows/release.yml README.md docs/releases/v0.5.0-beta.1.md tests/Scanio.Presentation.Tests/BetaReleasePackagingContractTests.cs
git commit -m "build: package Scanio beta installer"
```

---

### Task 3: PR validation, merge, tag, and published artifact audit

**Files:**
- Modify only when a verified review or CI finding requires a fix.

**Interfaces:**
- Consumes: Tasks 1–2, GitHub Actions CI, release workflow, GitHub Releases.
- Produces: merged `main`, tag `v0.5.0-beta.1`, published prerelease, verified checksums, download links.

- [ ] **Step 1: Perform a fresh local scope and verification audit**

Re-run Task 2’s complete portable tests and full build from the final tree. Confirm only preserved untracked user directories remain and installer/release contracts are included in the Presentation count.

- [ ] **Step 2: Push an isolated `codex/` branch and open a ready PR**

Describe the no-admin installer, retained ZIP, data preservation, exact test counts, unsigned boundary, and Windows installer/runtime gates pending CI.

- [ ] **Step 3: Require both PR jobs to pass**

Wait for `Portable core / macOS` and `Full solution / Windows`. If a check fails, inspect the exact logs, add or tighten a deterministic RED test/contract, implement the minimal fix, and rerun all affected gates. Do not merge queued, canceled, or partial runs.

- [ ] **Step 4: Review and merge the exact green head**

Review installer deletion boundaries, PowerShell quoting, artifact names, version parity, release notes, and absence of secrets. Merge the verified head SHA and update local `main` with fast-forward only while preserving untracked files.

- [ ] **Step 5: Create and push the annotated beta tag**

```bash
git tag -a v0.5.0-beta.1 -m "Scanio v0.5.0-beta.1"
git push origin v0.5.0-beta.1
```

Never move a public tag. If a published release fails, fix forward with a new beta version.

- [ ] **Step 6: Require the tag release workflow to pass**

Verify locked restore, complete Windows tests, rendered screenshots, neutral smoke launch, Inno compilation, silent install, installed-mode smoke launch, silent uninstall, retained marker, checksum generation, and GitHub prerelease creation.

- [ ] **Step 7: Download and audit the published assets**

Download all three files to a new temporary directory and verify both hashes from `SHA256SUMS.txt`. Inspect the ZIP for `Scanio.exe`, `portable.flag`, `Data/scanio.db`, and README. Inspect setup filename, nonzero size, GitHub digest, and PE signature. The successful Windows release job is the installation evidence.

- [ ] **Step 8: Deliver beta handoff**

Provide direct setup, portable, checksum, and release links. State exact automated evidence, the unsigned warning, and that real keyboard-wedge, Datalogic, and Zebra testing remains user-run.
