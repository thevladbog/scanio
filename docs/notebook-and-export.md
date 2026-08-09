# Notebook and export

Scanio stores Notebook data locally. It does not synchronize, upload, or validate saved payloads with an online service.

## Recording lifecycle

- **Start** creates a named session and records only scans completed after the command succeeds.
- **Pause** keeps the session open but does not persist scans completed while paused.
- **Resume** continues the same session.
- **Stop** drains all queued records before marking the session complete.
- Application shutdown drains the Notebook queue before closing and disposing the scanner transport.

Every occurrence is stored independently, including identical consecutive payloads. The database preserves raw and payload bytes, read chunks, timestamps, framing settings and completion reason, transport identity, decoded text, and every ordered analysis result.

## Storage paths

- GitHub portable package: `Data/scanio.db` beside `Scanio.exe`. The package contains `portable.flag`, which selects this mode.
- Installed/development mode without `portable.flag`: `%LOCALAPPDATA%\Scanio\scanio.db`.

Copy the database only while Scanio is closed. Deleting a History session permanently deletes its child scans after an explicit confirmation.

## Copy and export

**Copy** puts one escaped display value per line on the Windows clipboard. Scanner control characters remain visible as labels such as `<CR>`, `<LF>`, and `<GS>`.

- TXT: one escaped display value per line, preserving duplicates and order.
- CSV: UTF-8, RFC 4180 quoting, with sequence, timestamp, transport, primary format, value, and raw bytes in Base64.
- JSON: an indented diagnostic document containing exact raw and payload bytes in Base64, transport and framing metadata, decoding information, and structured analysis fields and validation messages.

Exports are written to a temporary sibling file, flushed, and atomically moved over the target. If generation or writing fails, the previous target is retained.

## Recovery boundary

The schema is versioned and initialized idempotently. This alpha does not yet include an in-app backup, restore, or corruption-repair workflow. Keep independent copies of important databases and exports. Physical Windows shutdown and power-loss behavior remains part of user acceptance.
