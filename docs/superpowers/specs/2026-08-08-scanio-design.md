# Scanio desktop scanner diagnostic utility — design specification

**Date:** 2026-08-08  
**Status:** Approved design  
**Official first platform:** Windows 10/11 x64  
**Technology:** .NET 10 LTS, C#, WPF, MVVM

## 1. Product definition

Scanio is a small, completely local desktop utility for connecting to and testing barcode scanners. Its primary purpose is COM-port connection and diagnostics. Direct USB HID/POS support is secondary, and keyboard-emulating USB scanners have a basic in-app capture mode.

The product name is localized:

- Russian UI: **Сканио**;
- English UI: **Scanio**;
- repository, executable, package, and artifact identifiers use `scanio` or `Scanio` in Latin characters.

Scanio has no accounts, server, cloud synchronization, telemetry, automatic crash reporting, or background network activity. It processes and stores all scanner data locally.

## 2. Goals and success criteria

The first release must let a service engineer:

1. See connected COM and USB devices without opening their interfaces.
2. Select a device and configure a serial connection manually.
3. Open exactly one scanner connection by explicit user action.
4. See raw reads, complete byte sequences, HEX, control characters, timing, and framing details for every scan.
5. Distinguish known facts from inferred barcode symbology or payload format.
6. Decode supported structured payloads into named fields.
7. Turn on a notebook and persist subsequent scans into a named local session.
8. Copy all or unique values and export either a clean code list or a complete diagnostic report.
9. Disconnect cleanly and release the device handle when the user disconnects or exits Scanio.

The release is accepted only after automated checks and physical tests with available Datalogic and Zebra scanners. Browser, simulated transport, or UI automation results do not replace hardware acceptance.

## 3. First-release scope

### 3.1 Included

- Windows 10 and Windows 11, x64 only.
- One running Scanio instance.
- One active scanner transport at a time.
- Manually configured and manually opened COM ports.
- Direct USB HID/POS enumeration and input for supported interfaces.
- Raw report display for accessible vendor-specific HID interfaces.
- Focused in-app capture for keyboard-emulating scanners.
- Live diagnostics with a rolling in-memory history.
- Optional persistent notebook sessions.
- RU and EN interface languages, switchable without restart.
- Light, dark, and system themes.
- Installer and true portable distribution.
- GitHub Releases without paid code signing.

### 3.2 Explicitly excluded

- Linux, macOS, ARM64, and 32-bit Windows.
- Multiple simultaneous scanner connections.
- Automatic connection, reconnection, or serial-parameter probing.
- Probing ports merely to determine whether they are busy.
- Identifying the process that owns a busy port.
- Sending commands to a scanner or changing its configuration.
- Driver installation or updating.
- Global keyboard hooks.
- Camera/image barcode decoding.
- Barcode generation or printing.
- Online Честный знак lookup or online code verification.
- Accounts, cloud storage, synchronization, or telemetry.
- Third-party runtime plugins.
- Automatic application updates.
- Digital signing of first-release binaries.

## 4. Primary user flow

1. The user launches Scanio. A second launch activates the existing window instead of starting another instance.
2. Scanio passively lists detected COM and USB devices. Enumeration does not open a port or HID interface.
3. The user chooses a transport and device.
4. For COM, the user selects a saved profile or sets the serial parameters, text encoding, terminator, and silence timeout.
5. The user clicks **Connect**. Only this command attempts to open the device.
6. Scanio reports the connection state and begins receiving data.
7. Each completed scan appears in the live monitor with raw bytes, readable text, timing, transport chunks, and analysis results.
8. The user may start a named notebook session. Only scans completed after recording starts are persisted.
9. The user may stop recording, copy codes, or export the session.
10. Disconnecting and reconnecting are always manual. Closing Scanio stops reads, flushes queued notebook writes, closes the active transport, and releases its handle.

## 5. Architecture

Scanio uses .NET 10 LTS, C#, WPF, and MVVM. Hardware access and domain logic do not depend on WPF controls.

### 5.1 Modules

| Module | Responsibility |
| --- | --- |
| `Presentation` | WPF views, ViewModels, commands, localization, and UI state |
| `Application` | Connection orchestration, live monitor, notebook lifecycle, profiles, and exports |
| `Transports` | Common scanner transport contract and concrete COM/HID/keyboard adapters |
| `Capture` | Raw chunk collection and deterministic scan framing |
| `Analysis` | Payload classification, validation, and structured parsing |
| `Storage` | SQLite persistence, schema migrations, backup, and repositories |
| `Export` | TXT, CSV, JSON, and clipboard representations |
| `Platform.Windows` | Device enumeration, hardware identity, single-instance behavior, and Windows errors |

### 5.2 Transport boundary

All input implementations conform to a common `IScannerTransport`-style contract. The contract exposes identity and state, opens and closes explicitly, and emits immutable raw chunks. It does not perform payload parsing or persistence.

First-release implementations are:

- `SerialTransport` for COM devices;
- `HidPosTransport` for supported direct USB HID/POS interfaces;
- `KeyboardCaptureTransport` for focused in-app keyboard-emulation tests.

The application layer permits at most one open transport. Switching devices requires an explicit disconnect first.

### 5.3 Data flow

```text
COM or HID input
  -> immutable RawChunk
  -> ScanAssembler
  -> immutable Scan event
  -> selected text decoder
  -> analyzer chain
  -> live diagnostic monitor
  -> notebook persistence, only while recording is enabled
```

COM reading runs in a background asynchronous operation. Cancellation and deterministic disposal belong to the transport layer; views and ViewModels never own a device handle.

## 6. Device discovery and connection

### 6.1 Passive discovery

Scanio monitors Windows device changes and refreshes the device list automatically. A manual **Refresh** command is also available. Refreshing is passive: it must not open a COM port or HID collection.

For COM devices, Scanio displays available values such as:

- current COM name;
- description and friendly name;
- manufacturer;
- VID/PID;
- serial number;
- Windows hardware ID.

Windows does not provide a reliable passive busy-port status. Before a connection attempt, a listed port is shown as detected, not as confirmed available. **Busy**, **access denied**, and similar states are established only when the user clicks **Connect**.

### 6.2 Serial profiles

A serial profile contains:

- user-visible name;
- current port association;
- standard or custom baud rate;
- data bits from 5 through 8;
- parity: none, even, odd, mark, or space;
- stop bits: 1, 1.5, or 2;
- flow control;
- advanced DTR and RTS settings;
- text encoding;
- scan terminator;
- silence timeout.

The default connection preset is 9600 baud, 8 data bits, no parity, 1 stop bit, and no flow control. This is a starting value, not automatic detection.

When available, Scanio associates a profile with a stable serial number or hardware ID rather than the current COM number. A changed COM number therefore does not lose the profile. If the available identity is absent or ambiguous, Scanio asks the user to choose the profile rather than guessing.

### 6.3 Manual lifecycle

The user must explicitly connect, disconnect, and reconnect. Detecting a device never opens it. A physical disconnect:

1. stops the input operation;
2. closes and disposes the transport;
3. adds a connection event to the monitor;
4. leaves an active notebook session open;
5. waits for the user to choose and connect a device again.

There is no retry loop or automatic reopen.

### 6.4 Connection states

The application distinguishes:

- detected;
- connecting;
- connected;
- disconnecting;
- disconnected;
- busy;
- access denied;
- device removed;
- unsupported interface;
- transport error.

Each failure has a short localized explanation and expandable technical details containing the operation, Windows error, device identity, and timestamp.

## 7. USB modes

### 7.1 Direct HID/POS

The device list shows VID/PID, manufacturer, product name, serial number, usage page, usage, and whether the interface can be opened.

- Standard HID POS Scanner input reports are converted into scan input.
- Accessible vendor-specific HID input reports are shown as raw reports and HEX.
- Vendor-specific data is converted to text only when Scanio has an explicit compatible decoder.
- Unsupported or inaccessible collections remain visible with an explanation, but their **Connect** command is disabled.

Direct USB support is validated per real scanner mode. Merely enumerating a model is not sufficient to claim that its reports are supported.

### 7.2 Keyboard emulation

Keyboard-mode capture works only while the dedicated Scanio capture mode has focus. It does not install a global hook, suppress input in other programs, or claim that it can uniquely identify a particular keyboard-class scanner.

## 8. Raw capture and scan framing

### 8.1 Raw chunks

Every completed operating-system read creates an immutable `RawChunk` containing:

- exact bytes;
- wall-clock timestamp;
- monotonic timestamp for interval calculation;
- sequence number;
- connection and transport identity.

Chunk boundaries remain available for diagnostics even after chunks are assembled into scans.

### 8.2 Framing rules

The configured terminator has priority. Supported presets are `CR`, `LF`, `CRLF`, and `ETX`; the user may also enter an arbitrary byte sequence or select no terminator.

The assembler must handle:

- a terminator split across chunks;
- multiple scans in one chunk;
- a payload and terminator arriving separately;
- empty segments created by repeated terminators;
- no terminator, using silence timeout fallback.

The default silence timeout is 100 ms and is configurable from 10 through 5000 ms. The timeout is measured from the last received byte.

The maximum unfinished scan is 64 KiB. Exceeding it creates a diagnostic error and resets the assembly buffer so unbounded input cannot exhaust memory.

Transport terminators remain in the raw representation and HEX view but are excluded from the payload passed to text decoding and analysis.

### 8.3 Text decoding

The selected profile controls decoding. The first release supports:

- UTF-8 by default;
- ASCII;
- Windows-1251;
- Latin-1.

Raw bytes are always authoritative. Invalid text does not discard a scan. Scanio preserves its bytes, marks the decoding warning, and presents a safe replacement-character representation.

Control characters have visible labels such as `<CR>`, `<LF>`, `<GS>`, `<RS>`, `<EOT>`, and `<ESC>`.

### 8.4 Scan event

A completed immutable scan event contains:

- raw bytes and payload bytes;
- contributing chunks and inter-chunk intervals;
- start/end timestamps and total duration;
- completion reason: terminator or silence timeout;
- chosen encoding and decoded text;
- decoding warnings;
- analysis results;
- duplicate information;
- device and connection settings snapshot.

The live monitor retains the latest 1000 scans in memory. Older non-persisted events are evicted. Notebook records are unaffected.

Duplicates are determined by exact payload-byte equality. Visual normalization or parsed fields do not change duplicate identity.

## 9. Symbology and payload analysis

Scanio treats physical barcode symbology and payload format as separate facts.

### 9.1 Confidence

Physical symbology is exact only when an AIM identifier or transport metadata provides evidence. Without it, Scanio may show an inference, such as **probably DataMatrix**, but must label the inference. GS1-shaped data alone does not prove the physical barcode type.

Every analyzer returns:

- format identity;
- confidence: exact, inferred, or unknown;
- evidence used for the decision;
- structured fields;
- validation errors and warnings;
- localized human-readable summary.

Analyzer failure must not lose or mutate the scan. Analyzer execution order is deterministic.

### 9.2 Built-in analyzers

#### GS1

- Recognizes supported Application Identifiers.
- Handles fixed- and variable-length fields.
- Honors FNC1/GS separators.
- Extracts values such as GTIN, serial number, batch, expiry, price, and other known AIs.
- Validates field length, character rules, and check digits where applicable.

#### Честный знак

- Extracts GTIN, serial number, verification key, and crypto tail when present.
- Attempts product-group classification only from bundled local structural rules.
- Returns multiple candidates or **not determined** when the payload is ambiguous.
- Performs no network lookup and does not imply official online validity.

#### EAN/UPC

- Recognizes EAN-8, EAN-13, and UPC-A payloads.
- Separates data and check digit.
- Reports check-digit validity.

#### IATA BCBP

- Recognizes the Bar Coded Boarding Pass payload structure.
- Parses mandatory and available conditional fields.
- Reports incomplete, unsupported, or structurally invalid sections without discarding the raw payload.

#### URL

- Validates and displays the URI.
- Does not open it automatically.

#### Plain text

- Acts as the final fallback for any decoded payload not claimed by another analyzer.

### 9.3 Extensibility boundary

Analyzers use a stable internal interface and are independently testable. New formats are added as compiled modules with fixtures and tests. The first release does not load third-party assemblies or executable plugins.

## 10. Notebook and persistence

### 10.1 Recording semantics

The notebook is off by default. Starting it creates a named session; the UI proposes a timestamp-based name that the user can edit.

- Only scans completed after recording starts are persisted.
- Every occurrence is stored, including duplicates.
- Duplicate counters are derived without collapsing records.
- Disconnecting a scanner does not close the notebook.
- The user stops recording explicitly.
- A clean application exit flushes queued writes and closes the active session.

### 10.2 SQLite storage

SQLite stores:

- application settings;
- device profiles and hardware associations;
- notebook sessions;
- ordered scan records;
- connection-setting snapshots;
- raw payloads and chunks;
- decoded representations;
- analysis results and analyzer versions;
- warnings and errors.

Installed Scanio stores its database under the current user's local application-data directory. Portable Scanio stores it in a `data` directory next to the executable. Both use the same schema and migrations.

Before a schema migration, Scanio creates a database backup. If the portable directory is not writable, notebook recording cannot start and the UI explains why.

### 10.3 Storage failure

If a notebook write fails:

1. recording pauses immediately;
2. affected live events are visibly marked unsaved;
3. unsaved events remain in the rolling memory history;
4. the user may retry persistence or create an emergency JSON export.

Scanio must not silently claim that a failed record was saved.

### 10.4 Corrupt database

Scanio never overwrites a corrupt database automatically. Recovery options are:

- open the most recent backup;
- preserve the corrupt database for diagnosis;
- create a new empty database after explicit confirmation.

## 11. Copy and export

Users can:

- copy all decoded payloads in scan order;
- copy unique payloads in first-occurrence order;
- copy visible escaped text;
- copy HEX;
- export clean values as TXT;
- export a flattened scan table as CSV;
- export a lossless diagnostic report as JSON.

TXT contains one decoded payload per line and omits the configured transport terminator. CSV contains one row per scan and columns suitable for filtering. JSON includes a schema version, exact bytes, HEX, chunks, device settings, timestamps, warnings, and structured analysis results.

File export writes a temporary sibling file and atomically replaces the destination only after the write succeeds. A failed export must not truncate an existing destination.

## 12. Functional interface structure

Visual styling is intentionally deferred until the technical specification is accepted. The functional information architecture is fixed.

### 12.1 Screens

- **Connection:** transport type, passive device list, profile, settings, and manual connect action.
- **Monitor:** live event list and selected-event diagnostics.
- **Notebook:** recording state, active session, duplicates, copy, and export.
- **History:** saved sessions, reopen, rename, delete, copy, and export.
- **Settings:** language, theme, display defaults, and data location.

Active device state and manual disconnect remain visible from every screen.

### 12.2 Monitor behavior

The monitor provides:

- follow-latest mode;
- display pause without stopping transport reads;
- search and filters for format, error, and duplicate status;
- clear command for the non-persisted live history only;
- text, escaped-control, HEX, chunk, timing, and analysis views.

Inspecting an older scan prevents automatic selection changes. New events continue to arrive, and a separate command returns the user to the latest event.

### 12.3 Accessibility and localization

- All primary actions are keyboard-accessible.
- UI strings come from RU and EN resources and can switch without restart.
- System DPI scaling from 100% through 200% is supported.
- High-contrast mode keeps connection, disconnect, recording, and error states distinguishable.
- Minimum supported window size is 1024×700.
- At smaller available sizes, secondary panels may collapse, but connect/disconnect controls remain visible.

## 13. Error handling and local diagnostics

### 13.1 User-visible failures

Failures use a concise localized message plus expandable technical details. Core cases include:

- busy or inaccessible port;
- physical removal;
- input/read failure;
- unsupported HID collection;
- decoding warning;
- analyzer failure;
- unfinished-scan overflow;
- database write or migration failure;
- export failure.

No error triggers hidden reconnection.

### 13.2 Technical log

The local rolling log contains application lifecycle, versions, device-state transitions, operations, and system errors. It excludes scanned payloads and raw bytes by default.

Full scanner data exists only in an explicitly recorded notebook session or explicitly requested diagnostic export. Logs, device metadata, and scan data are never uploaded.

## 14. Distribution

Scanio is published from versioned Git tags through GitHub Releases.

Each release contains:

- `Scanio-<version>-win-x64-setup.exe`;
- `Scanio-<version>-win-x64-portable.zip`;
- SHA-256 checksums;
- release notes and known limitations.

Both application variants are self-contained and do not require a preinstalled .NET runtime.

### 14.1 Installer

- Per-user installation without administrator privileges by default.
- Start-menu/desktop integration and standard uninstall entry.
- Application data stored separately from program files.
- Uninstall preserves sessions and profiles unless the user explicitly chooses to remove them.

### 14.2 Portable

- Distributed as a ZIP directory, not an installer.
- Settings, profiles, and sessions remain in its adjacent `data` directory.
- The complete folder can be moved between writable locations.

### 14.3 Unsigned releases

First releases are unsigned. Documentation must explain that Windows SmartScreen may warn and must publish checksums for verification. Scanio must not disable or bypass Windows protections.

There is no automatic update mechanism. **About** displays the installed version and offers an explicit link to GitHub Releases, opened only by user action.

## 15. Verification strategy

### 15.1 Unit tests

Unit tests cover:

- every scan-framing boundary case;
- terminators split across chunks;
- multiple scans per read;
- silence-timeout behavior;
- buffer overflow;
- invalid encodings;
- duplicate and unique ordering;
- analyzer valid, boundary, and malformed fixtures;
- SQLite repositories and migrations;
- byte-preserving TXT/CSV/JSON export behavior.

Property-style tests split identical byte streams into randomized chunk boundaries. The assembled scan result must not depend on operating-system read partitioning.

### 15.2 Integration tests

Fake COM and HID transports reproduce:

- normal and slow input;
- disconnect in the middle of a scan;
- read failure;
- busy/open failure;
- cancellation and shutdown;
- persistence failure.

ViewModels and core workflows are tested without a real window. Windows UI smoke tests cover connect-state transitions, monitor navigation, notebook lifecycle, history, and export.

### 15.3 Physical acceptance

Available Datalogic and Zebra scanners are tested in their available COM, direct USB HID/POS, and keyboard-emulation modes.

Required scenarios are:

1. Device appearance and removal update the passive list.
2. A profile survives a COM-number change when stable device identity is available.
3. A busy port produces a clear error only after manual connect.
4. Closing Scanio makes the port immediately openable by another application.
5. Physical removal does not hang the UI or cause automatic reconnection.
6. One hundred consecutive known scans arrive without loss or reordering on each accepted transport/model combination.
7. `CR`, `LF`, `GS`, AIM identifiers, and real raw-read boundaries remain visible and correct.
8. Known GS1/Честный знак, EAN/UPC, and IATA BCBP fixtures produce expected structured fields.
9. Notebook sessions survive application restart.
10. TXT, CSV, and JSON exports match their source records.

Installer and portable artifacts are tested on clean Windows 10 and Windows 11 x64 systems. Support for a device mode is documented as confirmed only after physical acceptance with that mode.

## 16. Non-functional requirements

- Hardware reads, analysis, persistence, and export never block the UI thread.
- The virtualized monitor remains responsive at its 1000-event capacity.
- Raw bytes are never replaced by a decoded or normalized representation.
- Scan order remains stable from transport through persistence and export.
- Device and file handles are deterministically released.
- The application performs no implicit network requests.
- A failure in one analyzer cannot interrupt capture or other analyzers.
- Local storage failures are visible and never silently lose recorded status.
- The project structure keeps transport, framing, analysis, persistence, and UI independently testable.

## 17. Deferred visual design

WPF is the approved UI technology. The final visual direction, component styling, typography, colors, iconography, and detailed layouts will be designed after this technical specification is accepted. The design must fit the functional screens and accessibility requirements in this document; changing the visual treatment must not require changing hardware, capture, analysis, or persistence modules.
