# Wobble interface specification

Date: 2026-11-15. Status: approved by the owner on 2026-11-15 (direction "Groove", built as an Avalonia desktop app). The reference rendering is [reference/groove-board.html](reference/groove-board.html); the brand files are in [`assets/brand`](../assets/brand/README.md) and [`assets/icons`](../assets/icons).

The interface is `Wobble.App`, a desktop app for Windows and Linux written in C# with Avalonia. Its executable is `WobbleApp`, so it sits beside the emulator's `wobble` without a name clash. It is a client of the `wobble` control API ([design](../docs/specs/2026-11-15-wobble-design.md), Control API): everything it shows is read from that API and everything it changes goes through it. It never touches the drive model directly.

## Who uses it and for what

A tester of optical disc software, with the software under test on one monitor and Wobble on the other. Three jobs, in order of time spent: watch what the software sends (the trace), change what is in the drive (the disc), and make the drive misbehave on purpose (fault rules).

## Look

A dark instrument console. Colour appears only where something is live: the disc's spectrum on the mark and the disc figure, mint for good, rose for failed, amber for in progress.

| Token | Value | Use |
| ----- | ----- | --- |
| `Night` | `#0c0b13` | App background behind the window content |
| `Window` | `#100e19` | Window surface |
| `Tile` | `#14121f` | Title bar, cards, panels |
| `Well` | `#0b0a12` | Inputs, status bar |
| `Edge` | `#2a2540` | Hairlines and borders |
| `Text` | `#e9e6f7` | Primary text |
| `Muted` | `#9a94b8` | Labels, secondary text |
| `Violet` | `#8f7cf8` | Accent: primary button, toggles on, focus ring, Blu-ray |
| `Cyan` | `#38d6ee` | Spectrum middle |
| `Pink` | `#f472b6` | Spectrum end |
| `Mint` / `MintWell` | `#5fe3b1` / `#12302a` | Good status text and its pill |
| `Amber` / `AmberWell` | `#f7c56b` / `#3a2c12` | In progress, CD media |
| `Rose` / `RoseWell` | `#ff7a90` / `#3d1722` | Failed, fault fired, DVD media |

The spectrum gradient runs violet `#a78bfa`, cyan `#38d6ee`, pink `#f472b6`, top left to bottom right. It is used on the mark, the disc figure and nowhere else.

Type: Sora (600, 700) for the window title and the media name; IBM Plex Sans (400, 500, 600) for labels and text at 13 pixels; IBM Plex Mono (400, 500) at 12 pixels for bytes, addresses, counts and column headers, with tabular figures. The three families ship inside the app under the SIL Open Font License. Corner radius is 7 pixels on controls, 9 on cards and 12 on the window content. Column headers and section labels are mono, 11 pixels, upper case, tracked 0.1 em, in `Muted`.

## Window

One window, minimum 900 by 600 and 1340 by 760 by default, three columns that never reorder: Disc (300 wide), Trace (fills), Faults (250 wide), under a title bar and over a status bar. Below 1100 wide the Faults column collapses to a button in the title bar that opens it as a side sheet.

### Title bar

| Id | Control | Behaviour |
| -- | ------- | --------- |
| W1 | Mark and the name Wobble | Static. |
| W2 | Drive identity (vendor, product, revision) | A button: opens the drive menu (P1 to P3). |
| W3 | Connection pill | `Emulator stopped` (rose), `Running, Windows not attached` (amber), `Windows attached, drive E:` (mint). The drive letter is shown on Windows when the drive has one. |
| W4 | Target name and endpoint | Mono text; a click copies it. |
| W5 | Attach to Windows | Shown on Windows while the emulator runs and the initiator is not attached. Runs `tools/attach.ps1` elevated (a UAC prompt). On refusal or failure, shows the reason under the pill. |
| W6 | Settings | Opens the settings dialog (G1 to G6). |

### Disc column

| Id | Control | Behaviour |
| -- | ------- | --------- |
| D1 | Disc figure | Rings for the written area in the spectrum, one ring segment per track, an arc for write progress, a dashed outline when the tray is empty, the figure offset when the tray is open. |
| D2 | State pill | `No disc`, `Tray open`, `Blank`, `Appendable`, `Complete`, `Reading`, `Writing`, `Formatting`, `Erasing`. |
| D3 | Media name | For example `CD-R, 80 min`; from the media kind and capacity. |
| D4 | Facts | Profile, manufacturer id, written of capacity, speed, buffer. A row is hidden when the disc has no such fact. |
| D5 | Insert image | File picker for `.iso`, `.cue`, `.ccd`, `.wobble` and media profile `.json`. Primary button. |
| D6 | New blank disc | A flyout listing the media profiles from `GET /profiles`, grouped CD, DVD, Blu-ray. |
| D7 | Eject, or Load | Opens or closes the tray; refused while removal is prevented, with the reason shown. |
| D8 | Save disc | Enabled for a written disc. Folder and name picker; saves CCD/IMG/SUB for CD and a disc file otherwise. |
| D9 | Tracks | Session headers, then number, mode, start (MSF for CD, LBA otherwise), blocks, pregap. |
| D10 | Drop target | Dropping one image file on the column inserts it, as D5. |

### Trace column

| Id | Control | Behaviour |
| -- | ------- | --------- |
| T1 | Filter | Matches opcode name, CDB hex, LBA and sense text as typed. |
| T2 | Chips | `All`, `Errors <n>`, `Writes`. One is selected. |
| T3 | Pause, or Resume | Stops the view from following; commands are still recorded and counted on the button while paused. |
| T4 | Clear | Clears the emulator's trace after a confirmation inside the button (`Clear`, then `Confirm clear` for 3 seconds). |
| T5 | Export | Saves the trace as a trace file (`.jsonl`). |
| T6 | Table | Seq, ms, command, CDB, out, in, status, sense. A failed row has a rose well and a rose bar on its left edge; a row a fault rule answered also shows the rule number. Virtualised: 100 000 rows scroll smoothly. |
| T7 | Detail | Selecting a row opens a panel under the table: the CDB decoded field by field, the sense in words, and the first 256 bytes of data when the emulator records data. |
| T8 | Follow | The table stays on the newest row until the user scrolls up; a `Jump to newest` button then appears. |
| T9 | Dropped notice | When the emulator has dropped old entries, a line above the table says how many. |

### Faults column

| Id | Control | Behaviour |
| -- | ------- | --------- |
| F1 | Add rule | Opens the rule editor (F3) empty. |
| F2 | Rule card | Opcode, trigger, action, `fired x of n`. A toggle enables or disables it; a fired rule has a rose border. Buttons on hover or focus: edit, move up, move down, delete. |
| F3 | Rule editor | A dialog: opcode (searchable list), trigger fields (LBA from and to, nth, after blocks written, at emulator time), actions as switches that may be combined (sense, delay, buffer underrun, eject, swap media, not ready, drop connection), each with its parameters, and times. `Save` is disabled until the rule is valid, and each invalid field says why. |
| F4 | Load script | Replaces the rules with a fault script `.json`, after the emulator has accepted it. |
| F5 | Save script | Writes the rules, enabled or not, as a fault script. |
| F6 | Clear all | Removes every rule after the same in-button confirmation as T4. |

Rule order matters and is the order of the cards. A disabled rule stays in the app and is left out of the script sent to the emulator. Every change sends the whole enabled list with `PUT /faults`; if the emulator refuses it the change is undone in the list and the emulator's message is shown on the card.

### Status bar

| Id | Control | Behaviour |
| -- | ------- | --------- |
| S1 | Control API address | A click copies it. |
| S2 | Command count | Commands received since the last reset. |
| S3 | Last sense | Rose when the last failed command is among the newest 50. |
| S4 | Emulator clock | Milliseconds since the emulator started. |
| S5 | Version | The app's and the emulator's; amber when they differ. |

### Drive menu

| Id | Control | Behaviour |
| -- | ------- | --------- |
| P1 | Drive profiles | The drive profiles from `GET /profiles`; choosing one loads it with `PUT /drive`. The current one is ticked. |
| P2 | Load profile file | File picker for a drive profile `.json`. |
| P3 | Identity | Vendor, product, revision, serial and the supported media profiles of the current drive. |

### Settings

| Id | Control | Behaviour |
| -- | ------- | --------- |
| G1 | Emulator | `Start wobble with this app` (default; the app starts `wobble` as a child that stops with it) or `Connect to a running wobble` with an address. |
| G2 | Ports and bind address | iSCSI port, control port, bind address, for a started emulator. A port in use is reported with the port number. |
| G3 | Trace capacity | Entries the emulator keeps. |
| G4 | Record data | Whether the emulator keeps data bytes for T7. |
| G5 | Profile folder | Where drive and media profiles are read from. |
| G6 | Close to tray | When on, closing the window keeps the app in the tray. |

Settings are stored in the user's application data folder (`%APPDATA%\Wobble\settings.json` on Windows, `~/.config/wobble/settings.json` on Linux), written through a temporary file. A setting that needs the emulator restarted says so and offers `Restart emulator`.

### Tray

The tray icon is the small mark with a badge: none when idle, mint while reading, amber while writing, rose for 10 seconds after a fault rule fires (`assets/icons/tray-*.svg`). Its menu: Open Wobble, Insert image, Eject, Quit.

## States every surface handles

- **Emulator unreachable.** A banner under the title bar names the address and the error, with `Retry` and `Settings`. The three columns keep their last contents, dimmed.
- **Refused by the emulator.** The API's message is shown at the control that caused it (under the button, on the card, in the dialog), never in a modal box.
- **Empty.** No disc: the dashed disc figure and `No disc`, with D5 and D6 as the only actions. Empty trace: `No commands yet. Attach Windows, or start the software under test.` No rules: `No fault rules. A rule makes the drive misbehave on purpose, the same way every time.`
- **Busy.** A long operation (format, erase, save) shows its progress in D2 and disables only the controls it conflicts with.

## Keyboard and accessibility

Every control is reachable by Tab in reading order and has an automation name. Focus shows a 2 pixel `Violet` ring. Shortcuts: Ctrl+O insert image, Ctrl+E eject or load, Ctrl+F focus the filter, Space pause or resume when the trace has focus, Ctrl+N add rule, Ctrl+, settings. Text contrast is at least 4.5 to 1 on its surface. State is never carried by colour alone: every pill has words and failed rows say `CHECK`.

## Media glyphs

`assets/icons/media-*.svg`: one colour per family (CD amber, DVD rose, Blu-ray violet), a dashed outline for an empty tray, an arc for rewritable. They are used in D6, P3 and the tray menu.
