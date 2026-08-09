# Monitor, Notebook, Keyboard Capture, and Russian UX Design

**Date:** 2026-08-09

**Status:** Approved in conversation; awaiting written-spec review

**Platform:** Windows 10 and later

**UI stack:** .NET 10, C#, WPF, MVVM

## 1. Goal

Correct the field-test failures in Scanio:

1. returning to Monitor must resume selection of the latest scan instead of leaving the workspace stuck on an earlier row;
2. Notebook must present byte-identical repeats as one understandable row while preserving every physical scan for storage and export;
3. Russian user-facing copy must explain scanner and analyzer behavior in normal language instead of leaking literal technical English;
4. keyboard-emulating USB scanners must have an obvious, focused capture mode inside Connection;
5. Settings controls must change the workspace they describe instead of only persisting unused values;
6. primary actions and the Settings page must use the approved restrained workbench visual language.

The change also removes the poorly rendered number-sign prefix from Monitor ledger rows.

## 2. Confirmed user decisions

- Keyboard capture is a mode inside **Connection**, selected alongside **COM port**. It is not a new primary navigation destination and not a modal window.
- Notebook displays one row per byte-exact payload.
- When a repeat arrives, its grouped row updates to the latest occurrence, moves to the bottom, shows a localized occurrence label such as `2 раза`, and receives the duplicate arrival pulse.
- Documentation remains English. The default product UI remains Russian and can still be switched to English in Settings.
- Primary actions use graphite with light text. Cyan is a signal accent, not a button fill.
- The permanent follow-latest setting is removed. Entering Monitor always resumes the latest scan; selecting an older row is a temporary inspection action.

## 3. Monitor selection behavior

### 3.1 Entering Monitor

Every navigation into Monitor calls the existing return-to-latest behavior before the destination is displayed. If scans exist, the last retained event is selected and the center workspace shows it. If no scans exist, the selection remains empty.

This rule applies after returning from Connection, Notebook, History, or Settings.

### 3.2 Inspecting an older scan

Selecting an older Monitor row still pauses automatic selection while the user remains in Monitor. New events continue to arrive in the ledger and the **Latest scan** action remains available.

Leaving Monitor ends that temporary inspection state. Returning to Monitor resumes follow-latest automatically. This avoids a hidden selection mode surviving navigation.

### 3.3 Ledger numbering

Monitor ledger rows show the plain sequence number without `№`, `#`, an arrow, or another prefix. The selected-scan workspace may keep a clearly styled technical event identifier if it remains visually unambiguous.

## 4. Notebook grouping

### 4.1 Authoritative records

Every accepted scan remains a separate immutable `NotebookRecord` in persistence. Recording order, exact payload bytes, decoded text, analyses, timestamp, transport identity, and occurrence count remain available.

Grouping is presentation-only. It must not rewrite history, collapse database rows, or remove occurrences from the common **Copy all**, TXT, CSV, or JSON paths.

### 4.2 Byte identity

Rows group only when `PayloadBytes` are byte-for-byte equal. Equal decoded text with different bytes remains distinct. The grouping identity must use the existing `NotebookPayloadIdentity` behavior so summary, unique export, and visible rows agree.

### 4.3 Grouped row behavior

A grouped row represents the most recent occurrence of one payload and retains:

- the latest scan sequence;
- the latest recorded time;
- the latest analysis presentation;
- the exact escaped payload display;
- the total number of occurrences in the active notebook session.

On the first occurrence, Scanio appends a new row and shows the existing turquoise arrival pulse. No `×1` or `1 раз` label is displayed.

On a repeat, Scanio updates the existing row, removes it from its old position, appends it at the bottom, shows the amber duplicate pulse, and displays a localized label:

- Russian: `2 раза`, `3 раза`, `5 раз`, with correct plural selection;
- English: `2 scans`, `3 scans`.

The visible row count therefore equals the unique payload count, while the summary remains:

- **Total scans:** number of persisted occurrences;
- **Unique codes:** number of byte-exact payload groups;
- **Repeats:** `Total scans - Unique codes`.

### 4.4 Session scope

Grouping and occurrence labels reset on each new Notebook session. Pause and resume preserve the current groups. Scans received before recording starts or after it stops do not affect Notebook groups.

History uses the same grouped presentation for a selected saved session while exporting from the underlying occurrence records.

## 5. Keyboard scanner mode

### 5.1 Connection information architecture

Connection gains a transport selector with two choices:

- **COM port**;
- **Keyboard scanner**.

COM mode keeps the existing passive device list, serial parameters, manual connect action, and status inspector.

Keyboard mode replaces the serial controls with:

- a short explanation of keyboard-wedge limitations;
- a large, visually distinct capture surface;
- a manual **Start test** action;
- a manual **Stop test** action while active;
- an explicit focused/unfocused status;
- the most recently completed keyboard scan as immediate confirmation.

### 5.2 Focus and disclosure

Windows exposes a keyboard-wedge scanner as keyboard input and does not reliably identify which keyboard-class device produced each character. Scanio therefore captures only while its dedicated surface is active and focused.

The screen must state in plain language that normal typing is captured too. Scanio installs no global hook, suppresses no input in other applications, and does not claim device-level USB raw bytes.

The capture surface receives focus after **Start test** and reacquires focus after each completed scan. If focus leaves the surface, the UI shows that input is paused until the user activates it again.

### 5.3 Transport and framing

`KeyboardCaptureTransport` implements `IScannerTransport` with `TransportKind.KeyboardCapture`. WPF forwards accepted Unicode text input into the active transport. The transport emits reconstructed UTF-8 `RawChunk` values with wall-clock and monotonic timestamps.

A keyboard scan completes on:

- Enter;
- Tab;
- the configured 100 ms silence timeout after the last accepted character.

Enter and Tab are framing signals for keyboard capture. The reconstructed payload excludes them, matching the existing transport-terminator behavior. The UI and diagnostics must identify keyboard bytes as reconstructed Windows text rather than a physical USB report.

The transport uses the existing `ConnectionCoordinator`, `ScanProcessingPipeline`, analyzers, `LiveMonitor`, and `NotebookRecorder`. Keyboard scans therefore have the same analysis, copy, grouping, and persistence behavior as serial scans.

### 5.4 Lifecycle

Only one transport may be active. Starting keyboard capture while a COM transport is active is disabled and explains that the current scanner must be disconnected first. Starting COM while keyboard capture is active follows the same rule.

Starting and stopping keyboard capture are always manual. Navigation does not stop it. Disconnect, application shutdown, or an input-transport failure cancels reads, completes cleanup, and releases the active coordinator session without blocking the UI.

## 6. Russian UX copy

### 6.1 Principles

- Explain the outcome first and the protocol term second.
- Keep standards and byte notation culture-neutral: `GS1`, `AI`, `GS`, `RAW`, `HEX`, `COM`, `EAN-13`, `DataMatrix`, `UTF-8`.
- Do not expose English analyzer narratives in Russian UI.
- Avoid abstract headings where a task-oriented label is clearer.
- Preserve detailed diagnostic meaning; simplification must not turn a conditional warning into a definite error.

### 6.2 Required replacements

At minimum, the Russian resources and mapped analyzer messages use these meanings:

| Current or observed text | Replacement |
| --- | --- |
| `Терминатор` or `Termination` | `Завершён по символу окончания` |
| `Пауза чтения` | `Завершён после паузы` |
| `Точный формат данных` | `Распознано точно` |
| `Предположение по структуре` | `Похоже на этот формат` |
| `Строка элементов GS1` | `Данные GS1` |
| `ФАКТ` | `ПОЛУЧЕНО` |
| `ИНТЕРПРЕТАЦИЯ` | `РАСПОЗНАНО` |
| `ЖИВАЯ ЛЕНТА` | `ПОСЛЕДНИЕ СКАНИРОВАНИЯ` |
| `Сканы` | `Отсканированные коды` |
| `Чтения транспорта` | `Порции данных от сканера` |

The variable-length AI warning becomes:

> Поле {AI} (AI {AI}) прочитано до конца кода. Это нормально, если оно последнее. Если после него должны быть другие данные, сканер не передал разделитель GS.

For AI 92 this renders as:

> Поле 92 (AI 92) прочитано до конца кода. Это нормально, если оно последнее. Если после него должны быть другие данные, сканер не передал разделитель GS.

The Russian resource audit covers every value visible in Connection, Monitor, Notebook, History, Settings, dialogs, connection states, completion reasons, analyzer summaries, analyzer evidence, validation warnings, and validation errors. English is allowed only for the standard tokens listed above, proper product names, and an explicitly labeled technical-detail value supplied by Windows.

## 7. Error handling

- Keyboard capture cannot start without an available capture surface and active focus request; the UI remains inactive and presents a localized recovery message.
- Empty Enter or Tab input does not create a scan.
- Losing focus does not silently capture into another control. The keyboard panel changes to **Click here to continue scanning**.
- Disconnect and shutdown are bounded by the existing coordinator cleanup rules and must not freeze WPF.
- Presentation grouping failures must not mutate or discard authoritative notebook records.
- Unknown analyzer messages use a localized wrapper and preserve the original technical detail only after the localized explanation.

## 8. Functional Settings

### 8.1 Display settings

Settings must not expose controls that only write unused JSON values. The remaining display controls apply immediately and remain persisted across restarts:

- **Show control characters as `<GS>`** switches the RAW evidence between escaped `RawBytes` (for example, `<GS>` and `<CR>`) and decoded payload text without framing controls. It does not alter authoritative bytes, payload copy, export, or analysis.
- **Show HEX** shows or hides the Monitor HEX evidence region. RAW remains available.
- **Show scanner data chunks** shows or hides the Monitor chunk/read inspector.
- **Compact / Comfortable** changes row density in Monitor, Notebook, History, and the COM device list.

The existing **Follow the latest scan** setting is removed from `AppSettings`, Settings UI, persistence output, and localization. Loading an older settings file that still contains the property remains tolerant. Monitor navigation follows the behavior in section 3.

Display settings are consumed through a presentation-level settings dependency. Monitor and shell presentation models expose observable properties for relevant visibility and density values. XAML binds to those properties or to shared dynamic resources updated by the settings service; it must not duplicate independent density constants per view.

### 8.2 Density values

The established row heights remain the source of truth:

- Compact: 54 DIPs for primary ledger rows;
- Comfortable: 66 DIPs for primary ledger rows.

Adaptive record rows may use a smaller content minimum only when the selected density still produces a clearly different rendered height. Switching density updates already rendered lists without restarting Scanio.

### 8.3 Settings layout

Settings becomes a full-bleed, flat two-column workspace consistent with the other destinations:

- no isolated milk-colored page rectangle inside the shell;
- no white outlined card around local data;
- no decorative nested borders;
- a settings column for language, Monitor display, and density;
- a calm local-data rail separated by one neutral vertical divider;
- hierarchy is created with spacing, typography, and section dividers rather than card outlines.

The layout must remain fully visible without horizontal page scrolling at 1440×900 and 1024×700.

## 9. Action color system

Primary actions use `Brush.SurfaceInk` as the normal background and a light foreground with sufficient WCAG AA contrast. This applies consistently to actions such as **Connect**, **Start recording**, **Resume**, **Copy code**, **Open session**, and **Start keyboard test**.

Cyan is reserved for state and attention:

- active navigation underline;
- keyboard focus indicator;
- selected-row edge and restrained selection surface;
- new-arrival pulse;
- RAW/HEX technical accents.

No normal button combines a cyan background with dark text. Hover, pressed, disabled, and keyboard-focus states must be explicit, readable, and visually distinct. Disabled primary actions must not retain an active saturated fill.

## 10. Verification

### 10.1 Automated behavior tests

- navigating away from and back to Monitor returns selection to the latest event;
- manual old-event selection remains stable while Monitor stays active;
- Monitor list numbering contains no prefix glyph;
- byte-identical Notebook occurrences produce one row, update its latest metadata, move it last, and show the correct occurrence label;
- byte-distinct payloads with equal decoded text remain separate rows;
- summary and all export formats continue to use the intended authoritative occurrence set;
- new Notebook sessions reset grouping while pause/resume preserves it;
- keyboard transport open/read/close lifecycle, Enter framing, Tab framing, 100 ms silence framing, empty framing input, cancellation, and restart are deterministic;
- keyboard and COM transports remain mutually exclusive through `ConnectionCoordinator`;
- Russian localization tests assert the required copy and reject known leaked English phrases;
- each retained Settings display control changes an observable workspace property and round-trips through storage;
- older settings JSON containing the removed follow-latest property loads without failure;
- compact and comfortable modes produce the expected row-height resources and update all four list surfaces;
- primary action styles use graphite/light colors and define readable normal, hover, pressed, disabled, and focus states.

### 10.2 Windows UI verification

Windows CI renders Connection in COM and Keyboard modes plus Monitor, Notebook, History, and Settings in Russian and English at 1440×900 and 1024×700. It rejects clipping, overlap, unintended horizontal scrolling, undersized actions, inaccessible primary controls, cyan/dark primary buttons, and the previous boxed Settings layout.

The keyboard panel receives focus in a WPF integration test, accepts a representative text sequence plus Enter, and exposes the resulting scan in Monitor.

The rendered Settings evidence is captured in both density modes. The Monitor evidence is captured with HEX and chunk display enabled and disabled so the controls are proven to change actual content.

### 10.3 Manual hardware acceptance

After GitHub prerelease publication, the user verifies on a real Windows computer:

- keyboard-wedge scanner input with Enter suffix;
- keyboard-wedge scanner input without suffix, using silence completion;
- repeated scanning while Notebook records;
- navigation from Monitor to Notebook and back while scans continue;
- immediate Settings changes for RAW controls, HEX, chunks, and density;
- COM scanner regression with existing Datalogic or Zebra hardware.

Physical hardware acceptance remains distinct from automated CI evidence.

## 11. Release scope

The implementation ships as the next unsigned Windows prerelease with a self-contained portable ZIP and SHA-256 checksum. Release notes call out:

- keyboard scanner mode;
- Monitor follow-latest navigation fix;
- grouped Notebook repeats;
- Russian copy revision;
- plain Monitor row numbering;
- functional display settings and density;
- graphite primary actions and the flat Settings layout.

SmartScreen behavior remains expected for the unsigned build.
