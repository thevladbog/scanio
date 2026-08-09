# Scanio

Scanio is a local Windows diagnostic utility for barcode scanners. The current alpha focuses on manually connected COM scanners and preserves the exact bytes received from the device.

## Download and run

Download `Scanio-0.1.0-alpha.1-win-x64-portable.zip` and `SHA256SUMS.txt` from GitHub Releases.

1. Verify the ZIP SHA-256 checksum.
2. Extract the complete ZIP to a writable directory.
3. Run `Scanio.exe`.
4. If Windows SmartScreen warns, inspect the publisher and checksum before choosing whether to run it. The alpha is intentionally unsigned and never disables Windows protections.

The package is self-contained and does not require a preinstalled .NET runtime. Keep the adjacent `data` directory with the application; persistence is not active in this alpha yet.

## Current capabilities

- passive COM discovery through Windows SetupAPI;
- explicit Connect and Disconnect with one active scanner;
- baud rate, data bits, parity, stop bits, handshake, DTR, and RTS configuration;
- clear busy, access denied, device removed, and transport error states;
- terminator, silence-timeout, and overflow framing with original bytes retained;
- live scan ledger with duplicate counts, RAW, HEX, framing reason, and read chunks;
- UTF-8, ASCII, Windows-1251, and Latin-1 decoding;
- EAN-13 check-digit validation and plain-text fallback;
- deterministic port release on Disconnect and application exit.

## Not included in this alpha

Direct USB HID/POS, keyboard-wedge capture, GS1/Честный знак parsing, IATA BCBP parsing, Notebook, History, persistence, exports, installer, automatic updates, telemetry, accounts, cloud sync, Linux, and macOS support are not included.

Physical Datalogic and Zebra verification is intentionally marked **not run** until tested on real Windows hardware. Use [the acceptance matrix](docs/acceptance/com-capture-matrix.md) to record results.

## Development

Requirements: .NET SDK `10.0.102` or a compatible `10.0.x` patch.

```powershell
dotnet restore Scanio.slnx --locked-mode
dotnet test Scanio.slnx -c Release --no-restore
dotnet build src/Scanio.Presentation/Scanio.Presentation.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
```

Portable core and ViewModel tests also run on macOS. This does not imply macOS application support.
