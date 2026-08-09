# Notebook Exact Export and Duplicate Feedback Design

**Date:** 2026-08-09
**Status:** Approved in conversation; awaiting written-spec review

## Goal

Make the common Notebook and History copy/export path preserve machine-usable barcode text, including real control characters such as the GS character (`U+001D`), while retaining an explicit human-readable export mode. Duplicate counts must describe only the active notebook session, and each recorded occurrence must provide immediate visual feedback that distinguishes a new value from a repeat.

## Scope

This change covers Notebook recording, Notebook and History copy/export actions, persisted per-session occurrence counts, and transient row feedback in the WPF Notebook view. It does not change scanner framing, decoding, Monitor duplicate semantics, existing stored raw bytes, or barcode analysis.

## Copy and Export Semantics

The primary actions are machine-usable by default:

- **Copy all** joins each record's `Decoded.Text` with the platform newline and preserves control characters inside every value.
- **Copy unique** returns the first occurrence of each byte-exact payload within the selected session and preserves its `Decoded.Text`.
- **Export TXT** writes one `Decoded.Text` occurrence per line and preserves embedded control characters.
- **Export CSV** writes the real `Decoded.Text` in the `Value` column. CSV quoting continues to protect commas, quotes, CR, and LF. The exact raw bytes remain available in `RawBase64`.
- **Diagnostic JSON** retains the semantic `text` property and exact Base64 byte properties. JSON represents GS with a Unicode escape such as `\u001D`; a conforming JSON reader restores it as `U+001D`.

Human-readable output is separate and explicit:

- Rename the existing escaped-copy action to **Copy as readable text (`<GS>`)**.
- Add **Export TXT as readable text (`<GS>`)**.
- Readable output uses `Decoded.EscapedDisplay`, including labels such as `<GS>`, and must never be presented as the default or machine-usable representation.

The same semantics and labels apply to both Notebook and History so that an archived session behaves exactly like the active session.

## Per-Session Duplicate Counting

Monitor duplicate counts remain unchanged and continue to describe the Monitor's retained live stream. Notebook must not copy that global count into a session record.

`NotebookRecorder` owns a byte-exact occurrence counter for the active session:

- The key is `CompletedScan.PayloadBytes`, not the localized display text.
- Starting a session resets the counter.
- The first occurrence receives `DuplicateCount = 1`; subsequent byte-identical payloads receive 2, 3, and so on.
- Scans observed before recording starts do not affect the session.
- Pausing does not reset the counter; resuming continues the same session.
- Starting a later session creates a fresh counter.
- The computed count is persisted in `NotebookRecord`, so History reproduces the original session result.

Payload bytes are used instead of raw framed bytes so that a framing terminator is not accidentally treated as barcode content.

## Visual Feedback

Each newly persisted Notebook row receives one non-blocking arrival pulse:

- first occurrence of a payload: turquoise surface highlight;
- duplicate occurrence: amber surface highlight, including the updated `×N` count;
- duration: 600 milliseconds with a smooth fade to the normal row surface;
- no movement, repeated flashing, sound, or input blocking;
- the payload and `×N` text remain visible without relying on color alone.

The pulse is transient presentation state and is not persisted. Rows reconstructed during localization changes or History loading do not pulse. This change introduces no continuous animation.

## Architecture

`NotebookExportService` will expose separate exact-text and readable-text paths. Notebook and History ViewModels will route their primary commands to exact text and expose an additional readable TXT command. Byte-exact distinct selection will be centralized with the export logic so copy and file export cannot drift.

`NotebookRecorder` will compute session occurrence counts before enqueuing records. The application layer remains independent of WPF and stores the resulting count through the existing `NotebookRecord` contract.

`NotebookRecordItemViewModel` will expose duplicate and transient-pulse state. `NotebookViewModel` will mark only a newly persisted item as active and clear that state after the pulse window. XAML data triggers/storyboards will select turquoise or amber feedback based on whether the session occurrence count is one or greater than one.

## Failure and Compatibility Boundaries

- Exact-text actions operate on successfully decoded text; raw bytes remain available through Base64 fields in CSV/JSON when a decoder warning exists.
- Atomic file replacement behavior remains unchanged.
- Existing databases require no migration because `DuplicateCount` is already stored.
- Existing records keep their previously persisted counts; only newly recorded sessions receive corrected session-local counts.
- Empty sessions keep all copy/export actions disabled.

## Verification

Automated tests will prove:

1. primary clipboard, TXT, and CSV output contains real `U+001D` rather than the literal `<GS>`;
2. readable clipboard and readable TXT contain `<GS>` rather than `U+001D`;
3. JSON round-trips `text` to the real control character and retains exact raw/payload Base64;
4. a matching Monitor scan before `Start` does not make the first Notebook occurrence `×2`;
5. repeats within one session increment in order, pause/resume preserves the count, and a new session resets it;
6. distinct selection uses byte-exact payload identity and preserves first-occurrence order;
7. newly persisted rows expose turquoise-versus-amber pulse state, while reconstructed rows do not pulse;
8. Russian and English resources remain in parity and layout contracts keep all actions visible at supported window sizes.

The release gate remains the full portable test suite, Windows WPF build and rendered-layout checks in CI, successful self-contained startup smoke, and later physical Windows scanner acceptance by the user.
