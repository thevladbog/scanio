# Scanio Windows scanner acceptance matrix

Status values are `confirmed`, `failed`, or `not run`. Automated and simulated results never replace a physical scanner result.

## Current status

| Scanner | Mode | Windows version | Status | Evidence |
| --- | --- | --- | --- | --- |
| Datalogic model available to tester | USB CDC / COM | Not recorded | not run | Awaiting user acceptance on Windows |
| Zebra model available to tester | USB CDC / COM | Not recorded | not run | Awaiting user acceptance on Windows |
| Keyboard-wedge model available to tester | Focused Windows text reconstruction | Not recorded | not run | Awaiting user acceptance on Windows |

The keyboard mode reconstructs normal Windows text delivered while Scanio's dedicated input has focus. It is not raw USB capture, does not identify the scanner automatically, and does not add direct USB HID/POS support.

## COM per-device acceptance procedure

Record the scanner model, firmware, Windows version, COM number, baud rate, data bits, parity, stop bits, handshake, DTR, and RTS.

1. Start Scanio and press **Refresh**. Confirm that discovery does not disturb another application currently using the port.
2. Select the device and press **Connect**. Passive discovery alone must never show the port as available or busy.
3. Scan one known payload containing the configured terminator. Confirm payload, RAW, HEX, completion reason, chunks, and order.
4. Run 100 consecutive known scans. Confirm exactly 100 events, stable order, and no merged or missing values.
5. Exercise available `CR`, `LF`, `GS`, AIM identifier, and non-ASCII fixtures. Record exact RAW and HEX evidence without including sensitive production payloads.
6. Start a named Notebook session, scan two identical codes, pause, scan once, resume, and scan once. Stop recording and confirm that three ordered occurrences are saved while the paused scan is absent.
7. Restart Scanio, open the saved session in History, and confirm the exact values remain. Export TXT, CSV, and JSON, verify that all three occurrences are present, and verify the JSON Base64 bytes against Monitor HEX.
8. Hold the port open in another application, manually connect in Scanio, and confirm **Port busy** without automatic retry.
9. Disconnect the cable while connected. Confirm **Device removed**, responsive UI, and no automatic reconnect.
10. Press **Disconnect**, then immediately open the port in another application. Confirm success.
11. Connect again, exit Scanio while a Notebook session is recording, then immediately open the port elsewhere. Confirm both port reuse and that queued scans were saved.
12. Repeat at least once after Windows assigns a different COM number. Stable identity may persist only when the device exposes a real serial number.

## Keyboard-wedge acceptance procedure

Record the scanner model, firmware, Windows version, keyboard layout, suffix configuration, and display scaling. Do not enter secrets or sensitive production payloads during acceptance.

1. Select **Keyboard scanner**, start the test, and confirm that the dedicated Scanio input has focus. Normal typing elsewhere in Windows must not be presented as raw USB evidence.
2. Scan a known payload with an Enter suffix. Confirm exactly one Monitor event with the expected reconstructed text, RAW labels, HEX, completion reason, and chunks.
3. Configure a Tab suffix and scan the same payload. Confirm exactly one Monitor event completed by Tab, without a Tab character in the payload.
4. Configure the scanner without a suffix and scan again. Confirm exactly one event after the silence deadline, with no missing or merged characters.
5. Repeat the scan while Notebook records. Navigate from Monitor to Notebook and back while capture remains active; confirm Monitor resumes the latest scan.
6. Scan byte-identical values repeatedly. Confirm Notebook and History show one grouped visual row with the correct occurrence count, while copy and TXT, CSV, and JSON exports retain every occurrence in order.
7. Change RAW control-label, HEX, chunk, and list-density settings. Confirm each change is visible immediately and persists after restart.
8. Stop keyboard capture, return to COM mode, and run the applicable COM acceptance procedure with the available Datalogic or Zebra device.

## Evidence record

| Field | Result |
| --- | --- |
| Scanner model / firmware / mode | not run |
| Windows version / DPI | not run |
| Keyboard layout | not run |
| Keyboard suffix configuration | not run |
| Connection profile | not run |
| 100 ordered scans | not run |
| CR / LF / GS / AIM evidence | not run |
| Keyboard Enter / Tab / silence completion | not run |
| Monitor resume-latest navigation | not run |
| RAW labels / HEX / chunks / density settings | not run |
| Notebook pause / resume / restart persistence | not run |
| Grouped rows / occurrence-preserving exports | not run |
| TXT / CSV / JSON byte evidence | not run |
| Busy state | not run |
| Physical removal / no reconnect | not run |
| Reuse after Disconnect | not run |
| Reuse after exit | not run |
| Final status | not run |
