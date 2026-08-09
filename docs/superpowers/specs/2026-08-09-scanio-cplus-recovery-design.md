# Scanio C+ design recovery specification

**Status:** Proposed recovery specification for user review

**Date:** 2026-08-09

**Target:** Windows 10/11 x64, WPF, .NET 10

**Reference sizes:** 1440×900 and 1024×700 logical DIPs

**Source of truth:** `docs/design/03-wpf-design-handoff.md` and the five images in `design/ui-system-cplus/`

## 1. Objective

Rebuild the Scanio presentation layer so every implemented screen follows the approved C+ Editorial Diagnostic Workbench design instead of the current coarse WPF scaffold. The recovery covers the application shell, Connection, Monitor, Notebook, History, and a functional Settings screen.

This is a coherent presentation recovery, not a sequence of isolated spacing patches. The result must preserve the existing COM capture, analysis, notebook, storage, export, and shutdown behavior while replacing layouts that currently overflow, overlap, scroll unnecessarily, mix languages, or expose non-functional actions.

## 2. Observed implementation failures

The current implementation differs from the approved design in structural ways:

- Monitor uses a fixed-width `GridView` inside a narrower ledger column. Its columns total 376 DIPs before padding, so horizontal scrolling is unavoidable.
- Notebook and History also use fixed table widths that cannot adapt to their available regions.
- Several `DockPanel` and `StackPanel` combinations do not reserve independent space for headings and metadata, which allows text to overlap.
- Monitor duplicates analysis information across loosely related cards instead of preserving the reference hierarchy of payload, raw evidence, fact, interpretation, and structured fields.
- The right Monitor column is an undifferentiated list instead of a connection, timing, and transport inspector.
- The header allocates 220 DIPs to a full device name plus Disconnect, so real Datalogic and Zebra names clip.
- Settings is visible but disabled, and no application-wide localization mechanism exists.
- English analyzer copy is rendered beside Russian shell copy.
- Monitor has no discoverable selected-code copy action.
- The layouts do not implement the handoff's 1320, 1180, and 1024 DIP behaviors.

## 3. Recovery principles

1. **Approved references are authoritative.** Screen hierarchy, column proportions, spacing, colors, typography, and action placement follow the C+ images and handoff.
2. **No fake controls.** A visible interactive control must work. Future USB, keyboard, theme, backup, and logging controls remain hidden until their underlying capability exists.
3. **No generic fixed-width data tables.** Repeated data uses stretchable grids or item templates whose preview column consumes remaining width.
4. **No page-level scrolling at acceptance sizes.** Only a bounded data region may scroll.
5. **Raw evidence remains authoritative.** Payload, RAW, HEX, terminator, byte count, chunks, and analysis stay accessible without visual ambiguity.
6. **One language at a time.** Runtime localization switches the complete user-facing surface together.
7. **Long real-world values are first-class fixtures.** Layout acceptance includes long device names, long payloads, analyzer evidence, paths, and localized text.

## 4. Application shell

The shell retains the 78-DIP header with 28-DIP horizontal padding, the production Scanio logo, centered navigation, and a 2-DIP bottom rule.

The right header area contains only compact persistent context:

- disconnected: `Нет подключения` / `Not connected`, with Disconnect disabled;
- connected: port plus localized state, for example `COM6 · Подключено`;
- a 38-DIP Disconnect button when a connection is active.

The full scanner identity is not placed in the header. It appears in the Connection summary and Monitor inspector. The connection presentation model exposes port, friendly name, and state separately so the UI never parses a preformatted label.

Navigation shows Connection, Monitor, Notebook, History, and Settings. Every destination is functional. The active destination uses the approved signal underline and semibold text.

At narrower widths the logo and actions keep their minimum bounds; navigation reduces horizontal gaps before any text is clipped. Essential connection actions never scroll.

## 5. Connection screen

At 1440 DIPs the screen uses the reference hierarchy:

```text
390 device ledger | 700 serial configuration | remaining status/action panel
```

- The device ledger presents port, friendly name, manufacturer or identity evidence, and selected state as stretchable rows.
- The refresh action remains explicit and never opens a port.
- Only implemented COM discovery is shown. USB and keyboard tabs remain absent until those transports are functional.
- Serial options use aligned 36–40 DIP controls and localized enum display values.
- The dark summary panel contains full selected-device identity, localized connection state, current serial parameters, localized errors, and the 48-DIP Connect action.
- Connect and Disconnect reflect actual command availability and never appear active without a valid operation.

## 6. Monitor screen

### 6.1 Reference layout

At 1440 DIPs:

```text
370 live ledger | 710 selected scan workspace | remaining inspector
```

The three regions keep their own boundaries and do not introduce a horizontal scrollbar.

### 6.2 Live ledger

The left region uses a virtualized `ListBox` or `ListView` with a custom row template, not `GridView`.

Each row shows:

- sequence and timestamp;
- format and duplicate count;
- one-line payload preview with ellipsis;
- warning state when decoding or validation reported a problem.

Selection uses the signal surface and 4-DIP signal rule. Selecting an older event suspends follow-latest selection, while a visible Return to latest action restores it.

### 6.3 Selected scan workspace

The center region follows this reading order:

1. selected sequence and diagnostic context;
2. title `Содержимое, байты и структура` / `Content, bytes, and structure`;
3. primary `Скопировать код` / `Copy code` action and an overflow menu;
4. featured decoded payload;
5. always-visible RAW and HEX evidence on the ink surface;
6. adjacent Fact and Interpretation regions with independent grid columns;
7. structured analyzer results and fields.

Headings and confidence labels use explicit grid columns; no last-child-fill panel is allowed where values can collide. Long evidence and field values wrap within their own columns.

The analysis region may scroll internally when a genuinely large structured payload exceeds the available height. The page title, primary copy action, payload, and RAW/HEX regions remain fixed at 1440×900.

### 6.4 Copy behavior

`Copy code` is always visible and is disabled when no scan is selected. It copies the decoded payload without the configured transport terminator, while preserving meaningful in-payload separators such as GS.

`Ctrl+C` performs the same action when the Monitor owns focus and a text editor is not handling the shortcut. Successful copy produces a short non-blocking localized confirmation.

The overflow menu provides separate Copy RAW, Copy HEX, and Copy diagnostic JSON actions. These commands use the existing platform clipboard interaction abstraction and have command-state tests.

### 6.5 Inspector

The right region displays structured sections rather than an empty bordered list:

- connection: port, full friendly name, localized state, and active parameters;
- measurement: start, end, duration, completion reason, and duplicate count;
- transport chunks: sequence, byte count, HEX preview, receive time, and inter-read interval when available.

The full scanner name wraps in the inspector. The header remains compact.

## 7. Notebook screen

The Notebook follows the approved layout:

```text
92 active recording bar
300 session summary | remaining recorded scans | 320 copy/export actions
```

- Start, Pause, Resume, and Stop appear according to recorder state rather than competing simultaneously.
- Session summary shows total, unique, and duplicate counts plus device identity.
- Recorded scans use a stretchable virtualized row grid. Payload consumes remaining width and previews with ellipsis; no horizontal scrollbar appears.
- Copy actions distinguish all values, unique values, and escaped-control text.
- TXT, CSV, and diagnostic JSON exports are explicit actions rather than a cramped format selector paired with a generic Export button.
- Persistence state and errors remain visible without covering the data list.

## 8. History screen

At 1440 DIPs:

```text
390 saved sessions | remaining session records | 320 copy/export actions
```

- Saved-session rows expose name, date, scan count, and device or port.
- The selected session header exposes rename and secondary actions without overlapping metadata.
- Records use the same adaptive row semantics as Notebook.
- Copy and export actions are explicit and grouped by purpose.
- Delete remains isolated, requires confirmation, and cannot target the active recording.

## 9. Settings screen and localization

Settings becomes a real destination based on the approved Settings reference. Only implemented capabilities are visible.

The initial functional sections are:

- **Language:** Russian and English, applied without restart and persisted locally;
- **Monitor display:** show escaped control characters, show HEX preview, show chunk boundaries, and follow latest by default;
- **List density:** compact and comfortable;
- **Local data:** installed or portable mode, database path, open data folder, application version, and GitHub Releases link.

Theme switching, backup browsing, and technical-log actions remain hidden until implemented. The Settings UI must never advertise them as active placeholders.

Localization uses resource keys, not already-rendered mixed-language strings. Static XAML labels, ViewModel state labels, errors, confirmations, copy feedback, enum labels, analyzer summaries, analyzer evidence, field names, and validation messages switch as one culture transaction.

Standards and byte notation remain culture-neutral: `EAN-13`, `DataMatrix`, `GS1`, `IATA BCBP`, `RAW`, `HEX`, `COM`, `CR`, `LF`, and `UTF-8`. Unknown operating-system error details may be shown in their original language only inside an explicitly labeled technical-details area; the primary error summary is localized.

Russian is the first-run default. The language choice is stored in the local application settings and applies on the next start as well as immediately.

## 10. Responsive and DPI behavior

The recovery implements the handoff breakpoints:

| Logical width | Required behavior |
| --- | --- |
| `>=1320` | Reference three-column layouts and proportions |
| `1180–1319` | Smaller side padding and reduced secondary metadata; all three columns remain |
| `1024–1179` | Monitor inspector becomes a tab or expander; Connection summary docks below configuration; Notebook and History actions become lower panels |
| `<1024` | Unsupported; the window enforces its 1024-DIP minimum |

At 700–779 DIP heights, vertical padding reduces and data regions show fewer items. The shell, screen heading, connection state, and primary actions remain fixed.

Layouts are validated at Windows scaling of 100%, 125%, 150%, 175%, and 200%. WPF DIPs, star sizing, shared size groups, and minimum/maximum widths are used instead of physical-pixel assumptions.

## 11. Design system implementation

The existing C+ tokens remain authoritative. The recovery adds reusable styles and templates for:

- shell navigation and compact connection state;
- ledger rows and selected rows;
- adaptive data rows with preview columns;
- inspector sections and key/value rows;
- primary, secondary, danger, and quiet actions;
- localized status badges and transient feedback;
- empty, loading, warning, and error states;
- compact and comfortable density variants.

Screen XAML consumes these components instead of restating margins, type sizes, borders, and selection behavior. Generic rounded dashboard cards are not introduced.

## 12. Accessibility and interaction

- Primary actions remain keyboard reachable with visible focus.
- Minimum command height is 38 DIPs and primary action height is 48 DIPs.
- Color is paired with text for connection, warning, error, recording, and persistence states.
- Tooltips supplement but never replace essential visible content.
- New scans do not steal focus.
- Runtime language changes preserve the current screen, selected scan or session, active connection, and active notebook recording.

## 13. Verification strategy

### 13.1 Automated behavior tests

- command availability for Connect, Disconnect, copy, recording, export, rename, and delete;
- clipboard output for decoded, RAW, HEX, unique, escaped, and JSON variants;
- runtime RU/EN switching and persistence;
- analyzer copy and validation messages in both languages;
- settings persistence and application without restart;
- existing transport, analysis, notebook, storage, and shutdown regressions.

### 13.2 Automated layout contracts

Windows UI automation measures actual rendered bounds at 1440×900 and 1024×700 and rejects:

- overlapping visible text or controls;
- clipped essential labels and actions;
- horizontal scrollbars in Monitor, Notebook, and History data regions;
- page-level vertical scrollbars at acceptance sizes;
- columns below their specified minimums;
- an enabled Disconnect command without an active connection.

The fixture matrix includes long Datalogic and Zebra names, long payloads, long analyzer evidence, multiple chunks, long session names, long local paths, and both languages.

### 13.3 Visual evidence

The Windows CI publishes screenshots for all five screens at 1440×900 and compact screenshots at 1024×700. Screenshots are reviewed against the five C+ references before a release is tagged. CI screenshots and local contract tests do not replace physical scanner acceptance.

## 14. Acceptance criteria

The recovery is complete only when:

1. all five implemented screens match the C+ hierarchy, column proportions, visual tokens, and action placement;
2. no visible action is a placeholder;
3. no essential text overlaps or clips in RU or EN fixture matrices;
4. Monitor, Notebook, and History have no horizontal data scrollbar at 1440×900 or 1024×700;
5. no page-level scrollbar appears at either acceptance size;
6. the complete interface uses one selected language at a time;
7. Settings switches RU/EN without restart and persists the choice;
8. selected Monitor payload can be copied in one obvious action and with `Ctrl+C`;
9. full scanner identity is visible in the Monitor inspector while the header remains compact;
10. all existing portable automated tests pass;
11. Windows CI passes build, startup smoke, UI automation, and screenshot capture;
12. a new portable prerelease is published for physical Datalogic and Zebra verification.

## 15. Explicit non-goals of this recovery

- implementing direct USB HID/POS transport;
- implementing keyboard-wedge capture;
- implementing dark theme before a dark C+ reference is approved;
- implementing backup management or technical-log browsing;
- changing scan framing, analyzer detection rules, database schema, or export schemas except where localization requires presentation keys;
- claiming physical-hardware acceptance from automated results.
