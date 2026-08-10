# Scanio beta installer specification

**Status:** Approved for implementation

**Date:** 2026-08-10

**Release:** `v0.5.0-beta.1`

**Target:** Windows 10/11 x64, WPF, .NET 10

## 1. Objective

Promote Scanio from alpha to `v0.5.0-beta.1` and add a conventional per-user Windows installer while retaining the existing portable ZIP. The installer must make first use straightforward without requiring administrator rights, preserve local scanner data across upgrades and uninstallation, and remain compatible with the current offline-only privacy model.

The release remains unsigned. Windows SmartScreen may warn before running either the installer or the application; Scanio must not suppress or bypass that warning.

## 2. Release artifacts

The GitHub prerelease publishes exactly these downloadable artifacts:

- `Scanio-0.5.0-beta.1-win-x64-setup.exe`;
- `Scanio-0.5.0-beta.1-win-x64-portable.zip`;
- `SHA256SUMS.txt`, containing SHA-256 entries for both binaries.

The portable ZIP retains its current behavior, including `portable.flag`, the adjacent `Data` directory, and the database path `Data/scanio.db`.

The installer package must not contain `portable.flag`. Installed mode therefore continues to store the database and settings beneath `%LOCALAPPDATA%\Scanio` through the existing application storage selection.

## 3. Installer technology

Use Inno Setup 6 and a repository-owned `.iss` definition. The fixed application identifier is `{B786AC90-6A74-4E80-AE30-8D3C15A8C9C2}` so later beta and stable installers recognize the same installation and upgrade it in place.

MSIX is not used because an unsigned MSIX produces additional trust and sideloading friction. WiX/MSI is not used because machine-wide deployment and enterprise policy integration are outside the beta scope.

## 4. Installation behavior

The installer uses per-user mode:

- `PrivilegesRequired=lowest` and no UAC prompt;
- default installation path `%LOCALAPPDATA%\Programs\Scanio`;
- Start menu shortcut named `Scanio`;
- optional desktop shortcut, disabled by default;
- standard Programs and Features uninstall entry;
- application launch option on the final installer screen;
- English through Inno Setup's built-in messages and Russian through the bundled `compiler:Languages\Russian.isl`; compilation fails if that language file is unavailable.

The installer uses the existing multi-size `scanio.ico` for the setup executable, uninstall entry, and shortcuts. The installed application keeps its embedded executable and WPF window icons.

Re-running a newer installer with the same application identifier replaces program files in place. The installer uses `CloseApplications=yes` and `RestartApplications=no`. A separate final-page launch action starts Scanio only when selected by the user and is skipped during silent installation.

## 5. Data retention and uninstall

Program files and user data have separate lifecycles:

- upgrade replaces only files under `%LOCALAPPDATA%\Programs\Scanio`;
- uninstall removes installed binaries and shortcuts;
- uninstall does not remove `%LOCALAPPDATA%\Scanio`, including `scanio.db` and persisted settings;
- the installer never migrates, imports, or deletes a portable package's adjacent `Data` directory;
- reinstalling Scanio reconnects to the preserved installed-mode data automatically.

Removing local scan data remains a deliberate manual user action outside the installer.

## 6. Build and release pipeline

The tag-driven GitHub Actions release remains the sole publication path. For `v0.5.0-beta.1`, it must:

1. restore locked dependencies;
2. run the complete Windows test solution, including rendered WPF checks;
3. publish the self-contained `win-x64` application once into a neutral staging directory without `portable.flag`;
4. smoke-launch the staged application;
5. copy the staged files into the portable directory, add `portable.flag`, add `Data`, and create the portable ZIP;
6. compile the Inno Setup definition against the neutral staging directory;
7. perform an unattended per-user install into an isolated test path;
8. verify the installed `Scanio.exe`, absence of `portable.flag`, expected uninstaller, and installed-mode launch;
9. perform unattended uninstall and verify program files are removed;
10. generate SHA-256 entries for both the ZIP and setup executable;
11. publish the prerelease using `docs/releases/v0.5.0-beta.1.md`.

The workflow must locate `ISCC.exe` explicitly and fail with a clear message if Inno Setup is unavailable. It must not download or execute an unpinned installer compiler during the release.

## 7. Automated contracts

Portable tests must validate the repository-owned installer and release definitions without requiring Windows execution. Contracts cover:

- beta version and expected artifact names;
- stable Inno Setup application identifier;
- per-user privilege level and `%LOCALAPPDATA%` installation root;
- no `portable.flag` in installed mode;
- Start menu shortcut and optional desktop shortcut;
- uninstall data retention: no deletion of `%LOCALAPPDATA%\Scanio`;
- both artifact names in `SHA256SUMS.txt` generation and GitHub release upload;
- README download links and beta release notes.

Windows release validation remains authoritative for compilation, rendered WPF behavior, silent install, installed-mode smoke launch, and uninstall.

## 8. Documentation and user communication

All repository documentation and release notes remain English. The README presents the installer as the default download and the portable ZIP as the no-install alternative. It explains:

- per-user installation without administrator rights;
- installed data location `%LOCALAPPDATA%\Scanio`;
- data retention after uninstall;
- the optional desktop shortcut;
- the unsigned SmartScreen warning;
- offline-only behavior and absence of automatic updates.

The beta release notes list the installer as the primary beta addition and retain the physical Windows scanner acceptance boundary.

## 9. Out of scope

This beta does not add:

- digital code signing;
- automatic or in-app updates;
- machine-wide installation;
- MSI or MSIX packages;
- file associations, URL protocols, services, scheduled tasks, or startup launch;
- automatic deletion or migration of user databases;
- Linux or macOS application packages.

## 10. Acceptance criteria

The release is complete only when:

1. the full portable test suite passes;
2. the Windows solution, rendered WPF suite, and smoke launch pass in GitHub Actions;
3. a silent per-user install succeeds without elevation;
4. installed Scanio starts without `portable.flag` and uses installed-mode storage;
5. silent uninstall removes program files but leaves a seeded `%LOCALAPPDATA%\Scanio` marker intact;
6. both artifacts are present in the GitHub prerelease and match `SHA256SUMS.txt`;
7. a downloaded setup executable and portable ZIP can be inspected from the published release;
8. physical Datalogic, Zebra, and keyboard-wedge acceptance remains explicitly user-run and is not represented as automated.
