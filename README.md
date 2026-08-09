# Scanio

Scanio is a local Windows diagnostic utility for barcode scanners. The current alpha supports manually connected COM scanners with exact device bytes and a focused keyboard-scanner test mode that reconstructs ordinary Windows text input.

## Download and run

Download `Scanio-0.5.0-alpha.2-win-x64-portable.zip` and `SHA256SUMS.txt` from [GitHub Releases](https://github.com/thevladbog/scanio/releases/tag/v0.5.0-alpha.2).

1. Verify the ZIP SHA-256 checksum.
2. Extract the complete ZIP to a writable directory.
3. Run `Scanio.exe`.
4. If Windows SmartScreen warns, inspect the publisher and checksum before choosing whether to run it. The alpha is intentionally unsigned and never disables Windows protections.

The package is self-contained and does not require a preinstalled .NET runtime. Keep `portable.flag` and the adjacent `Data` directory with the application. Notebook sessions are stored locally in `Data/scanio.db` for this portable package.

## Current capabilities

- passive COM discovery through Windows SetupAPI;
- explicit Connect and Disconnect with one active scanner;
- an explicit keyboard-scanner mode that reconstructs focused Windows text and completes scans on Enter, Tab, or a short silence;
- a rebuilt C+ workspace with a persistent active navigation state, a larger Scanio identity, responsive three-column diagnostics, and no horizontal data grids;
- baud rate, data bits, parity, stop bits, handshake, DTR, and RTS configuration;
- clear busy, access denied, device removed, and transport error states;
- terminator, silence-timeout, and overflow framing with original bytes retained;
- live scan ledger with duplicate counts, RAW, HEX, framing reason, and read chunks;
- UTF-8, ASCII, Windows-1251, and Latin-1 decoding;
- GS1 application-identifier parsing with FNC1/GS separator handling and validation;
- offline Честный знак field extraction with conservative local product-group candidates;
- EAN-8, EAN-13, and UPC-A check-digit validation;
- IATA BCBP mandatory-field parsing with partial-data diagnostics;
- safe HTTP/HTTPS URL recognition without automatic navigation;
- structured fields, confidence, evidence, errors, and warnings in the Monitor;
- automatic selection of the latest scan whenever Monitor is reopened, with plain row numbering;
- always-visible one-click copy for decoded code, RAW, HEX, and diagnostic JSON;
- plain-text fallback for unstructured decoded data;
- named local Notebook sessions with Record, Pause, Resume, and Stop controls;
- byte-identical repeat grouping in Notebook and History, while copy and exports retain every recorded occurrence in order;
- SQLite persistence of exact raw bytes, chunks, framing, transport identity, decoded values, and structured analyses;
- local History with session rename and confirmed cascade deletion;
- explicit copy-all, copy-unique, and escaped-control actions plus atomic UTF-8 TXT, RFC 4180 CSV, and structured JSON export;
- revised Russian/English workflow copy and persisted controls for RAW labels, HEX, chunks, and comfortable/compact list density;
- an embedded Scanio executable icon for Explorer, the taskbar, and window chrome;
- deterministic port release on Disconnect and application exit.

## Not included in this alpha

Direct raw USB HID/POS capture, global keyboard hooks, automatic scanner identification, installer, automatic updates, telemetry, accounts, cloud sync, Linux, and macOS application support are not included. Keyboard scanner mode observes reconstructed text only while Scanio's dedicated input has focus; it is not raw USB capture.

Payload structure does not prove the physical barcode symbology. Scanio only reports exact physical symbology when future transport/AIM evidence provides it. Честный знак analysis is fully offline, does not contact official services, does not verify cryptographic validity, and returns multiple candidates or `Not determined` when local structural rules are ambiguous.

Physical keyboard-wedge, Datalogic, and Zebra verification is intentionally marked **not run** until tested on real Windows hardware. Use [the acceptance matrix](docs/acceptance/com-capture-matrix.md) to record results. See [Notebook and export](docs/notebook-and-export.md) for persistence paths, formats, and recovery boundaries.

Before publication, Windows CI must render both Connection modes and every destination at 1440×900 and 1024×700 in both languages, cover both density modes and Monitor evidence variants, exercise focused WPF text-plus-Enter capture, check action visibility and horizontal overflow, capture PNG evidence, verify the icon embedded in the published PE, and launch the self-contained package. These automated checks do not replace physical scanner acceptance.

## Development

Requirements: .NET SDK `10.0.102` or a compatible `10.0.x` patch.

```powershell
dotnet restore Scanio.slnx --locked-mode
dotnet test Scanio.slnx -c Release --no-restore
dotnet build src/Scanio.Presentation/Scanio.Presentation.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
```

Portable core and ViewModel tests also run on macOS. This does not imply macOS application support.
