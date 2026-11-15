---
schema_version: 1
id: control-app
domain: 06-interface
status: active
title: "TODO-01 -- The Wobble Control App"
---

# TODO-01 -- The Wobble Control App

> **Goal:** `Wobble.App`, an Avalonia desktop app for Windows and Linux, is the interface `design/SPEC.md` specifies: a tester watches the command trace live, changes the disc and the drive, and builds fault rules, with every control in the specification working against a real `wobble` through its control API.

> **Current state:** Measured 2026-11-15. No interface exists. `wobble` has only the HTTP control API (`src/Wobble.Host/Program.cs`): `GET /status`, `PUT /media`, `GET /trace`, `POST /reset`. `design/SPEC.md` (approved 2026-11-15, direction Groove) specifies the window; `assets/brand` and `assets/icons` hold the icon, tray and media glyph sources. `GET /status` does not report the tray, the drive identity, write progress or the initiator connection, and nothing can open or close the tray from outside.

## Inputs

- `design/SPEC.md`, every section; `design/reference/groove-board.html` for the rendering.
- `docs/specs/2026-11-15-wobble-design.md`, Control API.
- `assets/brand/wobble.ico`, `assets/icons/tray-*.svg`, `assets/icons/media-*.svg`.
- The control API as extended by D01 T01 §7 (trace paging), D01 T02 §2 and §12 (blank discs, save), D04 T01 §4 (faults) and D05 T01 §3 and §4 (drive and media profiles).

## Outcome

- Every control with an id in `design/SPEC.md` (W1 to W6, D1 to D10, T1 to T9, F1 to F6, S1 to S5, P1 to P3, G1 to G6, the tray) works against a running emulator and is covered by a headless test.
- A tester can run a burn from another program and watch it, with an injected fault, in the app.

**Adjacency:** list=applicable @ §7; document=applicable @ §8; settings=applicable @ §4; reporting=not-applicable (the app has one local user and keeps no records of its own); notifications=applicable @ §13; permissions=not-applicable (the app has one local user and keeps no records of its own); audit=applicable @ §7; exchange=applicable @ §9; reverse=applicable @ §6

**Adjacency rationale:** The trace is the list and the audit trail (§7), and its export is the document a tester attaches to a report (§8). Settings are the settings dialog (§4). Notifications are the tray states (§13). Exchange is loading and saving fault scripts (§9). Reverse is eject for insert, and the undo of a refused rule change (§6 and §9).

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Control API additions the interface needs | D05 T01 §4, D04 T01 §4 | [ ] |
| 2 | §2 | App project, Groove theme and headless tests | §1 | [ ] |
| 3 | §3 | Emulator session, title bar and status bar | §2 | [ ] |
| 4 | §4 | Settings dialog and stored settings | §3 | [ ] |
| 5 | §5 | Disc column: figure, state, facts and tracks | §3 | [ ] |
| 6 | §6 | Disc actions: insert, blank, eject, save and drop | §5 | [ ] |
| 7 | §7 | Trace table: live, virtualised, follow and pause | §3 | [ ] |
| 8 | §8 | Trace filter, detail panel and export | §7, D05 T01 §6 | [ ] |
| 9 | §9 | Fault rule list: toggle, order, load, save and clear | §3 | [ ] |
| 10 | §10 | Fault rule editor | §9 | [ ] |
| 11 | §11 | Drive menu and drive profiles | §3 | [ ] |
| 12 | §12 | Attach to Windows from the app | §3 | [ ] |
| 13 | §13 | Tray icon, its states and its menu | §6 | [ ] |
| 14 | §14 | Keyboard, focus and automation names | §10, §11, §12, §13 | [ ] |
| 15 | §15 | The window compared with the approved rendering | §14 | [ ] |
| 16 | §16 | A burn with a fault, watched in the app | §14, D04 T01 §6 | [ ] |
| 17 | §17 | The app in the downloads | §16, D07 T01 §2 | [ ] |

## 1. Control API additions the interface needs

The app shows only what the API reports. Tray state, drive identity, write progress, the buffer and whether Windows is attached are not reported today, and the tray cannot be moved from outside.

**Phase:** 7
**Needs:** none
**Surface:** no

**Build order:**

1. Extend `GET /status` in `src/Wobble.Host/Program.cs`. Done when: it carries everything the title bar, disc column and status bar show.
2. Add `POST /tray`. Done when: the tray opens and closes through the API with the drive's own refusals.

- [ ] Add `drive` (vendor, product, revision, serial), `tray` (`open` or `closed`), `preventRemoval`, `activity` (`idle`, `reading`, `writing`, `formatting`, `erasing`), `commands`, `lastSense`, `clockMs` and `version` to `GET /status`. Done when: a control API test reads each field.
- [ ] Add `media` detail to `GET /status`: kind, profile, state, manufacturer id, capacity, written blocks, sessions with their tracks (number, mode, start, length, pregap), speed and buffer fill. Done when: a test with a partly written CD-R reads the written count and the buffer.
- [ ] Expose `IscsiTarget.Sessions` (initiator name, connected since) in `src/Wobble.Iscsi/IscsiTarget.cs` and report it as `initiators` in `GET /status`. Done when: a test with `MiniInitiator` logged in reads one entry and none after logout.
- [ ] Add `POST /tray {"open": true|false}` calling new `OpticalDrive.OpenTray` and `CloseTray`, answering 409 with `removal is prevented` while PREVENT ALLOW holds the tray. Done when: a test in `ControlApiTests` proves it both ways. Cheaper substitute: removing the media, which loses the disc instead of opening the tray.
- [ ] Add the matching methods and records to `src/Wobble.Testing/WobbleControl.cs` and document the fields in `README.md`. Done when: `StatusAsync` returns a typed status.
- [ ] Commit: `host: report drive, media, tray and initiator state; move the tray`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~ControlApiTests"` prints `Passed!` with no failed test.

## 2. App project, Groove theme and headless tests

The shell every later section fills: the project, the theme tokens from the specification, the embedded fonts, the three-column window and a test project that renders it without a display.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Look and Window, and the control window in `design/reference/groove-board.html`.
**Job:** Give the tester one window whose three columns never move.
**Treatment:** The shell: title bar, three columns, status bar, all from tokens.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Create `src/Wobble.App` (Avalonia, MVVM with CommunityToolkit.Mvvm) and `tests/Wobble.App.Tests` (Avalonia.Headless.XUnit). Done when: the window opens and a headless test finds its three columns.
2. Put every colour, font and radius of the specification's Look section in `src/Wobble.App/Theme/Groove.axaml`. Done when: no view holds a literal colour.

- [ ] Create `src/Wobble.App/Wobble.App.csproj` (assembly `WobbleApp`, so it never collides with the host's `wobble.exe` on a case-insensitive file system, `WinExe`, `ApplicationIcon` `assets/brand/wobble.ico`) on the current stable Avalonia, and add it and `tests/Wobble.App.Tests` to `Wobble.slnx`. Done when: `dotnet build Wobble.slnx` builds both.
- [ ] Define the tokens of `design/SPEC.md` Look as resources in `src/Wobble.App/Theme/Groove.axaml`, with the spectrum gradient as one brush. Done when: a test reads each resource and compares its value with the specification's table.
- [ ] Embed Sora, IBM Plex Sans and IBM Plex Mono under `src/Wobble.App/Assets/Fonts/` with their OFL texts, and register them as `FontFamily` resources. Done when: a headless test measures a text block and its typeface is Sora.
- [ ] Build `MainWindow.axaml` with the title bar, the Disc, Trace and Faults columns (300, fill, 250) and the status bar, each an empty named region, minimum size 900 by 600. Done when: the headless test finds the five regions by automation id.
- [ ] Collapse the Faults column into a title bar button and side sheet below 1100 wide in `src/Wobble.App/Views/MainWindow.axaml`. Done when: a headless test resizes the window to 1000 and finds the button.
- [ ] Run `tests/Wobble.App.Tests` in the `unit tests` step of `scripts/check.ps1`, leaving out tests with the trait `Category=EndToEnd`, and run those in its `-EndToEnd` step. Done when: a failing headless test makes `pwsh scripts/check.ps1` exit 1. Cheaper substitute: a test project the gate and CI never run.
- [ ] Add a rule to `.editorconfig` or an analyzer test in `tests/Wobble.App.Tests/ThemeTests.cs` that fails when a `.axaml` view under `src/Wobble.App/Views` holds a `#` colour literal. Done when: adding `Background="#fff"` to a view fails the test. Cheaper substitute: literal colours in views, which drift from the tokens.
- [ ] Commit: `app: add the Avalonia shell with the Groove theme`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~ShellTests"` prints `Passed!` with no failed test.

## 3. Emulator session, title bar and status bar

The app is a client. One service owns the connection: it starts `wobble` or connects to one, polls it, and tells every view when it is unreachable.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Title bar, Status bar and States every surface handles, and the control window in `design/reference/groove-board.html`.
**Job:** Tell the tester at a glance whether the emulator runs and Windows sees the drive.
**Treatment:** W1 to W4, S1 to S5 and the unreachable banner.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add `EmulatorSession` in `src/Wobble.App/Services/`. Done when: it starts a child `wobble`, polls `GET /status` and `GET /trace?after=`, and reports unreachable.
2. Bind the title bar (W1 to W4) and status bar (S1 to S5). Done when: both show live values from a real emulator.

- [ ] Add `EmulatorSession` that starts `wobble` as a child in a job object on Windows and a process group on Linux, so the whole tree stops with the app, and stops it on exit. Done when: a test starts the app's session, kills the test host process abruptly in a child harness and finds no `wobble` left. Cheaper substitute: `Process.Kill` on the direct child only.
- [ ] Poll status every 250 ms and the trace after the last seen `seq`, off the UI thread, backing off to 2 seconds while unreachable. Done when: a headless test sees the command count rise after commands are sent to the emulator.
- [ ] Bind W2 (identity), W3 (the three connection states, with the drive letter found through the drive's serial on Windows) and W4 (target and endpoint, click copies) in `src/Wobble.App/Views/TitleBar.axaml`. Done when: tests cover stopped, running and attached.
- [ ] Bind S1 to S5, with S3 rose while the last failed command is among the newest 50 and S5 amber when versions differ. Done when: a test in `SessionTests` asserts it and fails when the behaviour is removed.
- [ ] Show the unreachable banner with the address, the error, `Retry` and `Settings`, and dim the columns. Done when: a headless test stops the emulator and finds the banner, then restarts it and the banner is gone.
- [ ] Commit: `app: connect to the emulator and show its state`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~SessionTests"` prints `Passed!` with no failed test.

## 4. Settings dialog and stored settings

Where the emulator runs, on which ports, and how much it records are the tester's choices, kept between runs.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Settings, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester choose where the emulator runs and what it keeps.
**Treatment:** The settings dialog, G1 to G6, and W6.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add `SettingsStore` in `src/Wobble.App/Services/`. Done when: settings round trip through the user's application data folder.
2. Build the dialog (G1 to G6). Done when: each setting changes the emulator session as the specification says.

- [ ] Add `AppSettings` and `SettingsStore` writing `settings.json` under the application data folder through a temporary file and a rename, and falling back to defaults with a visible notice when the file is unreadable. Done when: tests cover a round trip, a corrupt file and a missing folder.
- [ ] Build `SettingsDialog.axaml` with G1 (start or connect, with an address field), G2 (ports and bind address), G3 (trace capacity), G4 (record data), G5 (profile folder picker) and G6 (close to tray). Done when: a headless test sets each and reads it back from the store.
- [ ] Validate ports (1 to 65535, the two different), the address (an `http` URL) and the capacity (at least 1), showing the reason at the field and disabling `Save`. Done when: a test in `SettingsTests` proves it.
- [ ] Offer `Restart emulator` when a changed setting needs it, and report a port in use with its number. Done when: a test occupies the port and reads the message.
- [ ] Open the dialog from W6 and Ctrl+comma. Done when: a test in `SettingsTests` asserts it and fails when the behaviour is removed.
- [ ] Commit: `app: add settings`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~SettingsTests"` prints `Passed!` with no failed test.

## 5. Disc column: figure, state, facts and tracks

The left column answers what is in the drive right now, as a picture and as numbers.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Disc column, D1 to D4 and D9, and the control window in `design/reference/groove-board.html`.
**Job:** Show what is in the drive and how far a burn has got.
**Treatment:** The disc figure, state pill, facts and tracks table.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add `DiscView` and its view model. Done when: D2, D3, D4 and D9 show the status's media.
2. Draw the disc figure (D1) as a custom control. Done when: tracks, write progress, the empty tray and the open tray each draw differently.

- [ ] Add `DiscFigure` in `src/Wobble.App/Controls/DiscFigure.cs` drawing the written area as spectrum rings, one segment per track proportional to its blocks, and a progress arc while writing. Done when: a headless render of a four-track disc has four segments and differs from a blank disc's render.
- [ ] Draw a dashed outline for no disc and offset the figure when the tray is open in `src/Wobble.App/Views/DiscView.axaml`. Done when: both states render differently in a test.
- [ ] Bind D2 to the nine states of the specification and D3 to the media name built from kind and capacity in `src/Wobble.App/Views/DiscView.axaml`. Done when: a test per state passes.
- [ ] Bind D4, hiding a fact the disc does not have, with figures grouped in threes in `src/Wobble.App/Views/DiscView.axaml`. Done when: a pressed CD shows no manufacturer row and a CD-R shows its ATIP.
- [ ] Bind D9 with a header per session and MSF for CD, LBA otherwise in `src/Wobble.App/Views/DiscView.axaml`. Done when: a two-session disc shows two headers.
- [ ] Show the empty state text and only D5 and D6 when there is no disc. Done when: a test in `DiscViewTests` asserts it and fails when the behaviour is removed.
- [ ] Commit: `app: show the disc`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~DiscViewTests"` prints `Passed!` with no failed test.

## 6. Disc actions: insert, blank, eject, save and drop

Changing the disc is the tester's most common action. Each action goes through the control API and shows the API's own refusal where the tester clicked.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Disc column, D5 to D8 and D10, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester change the disc in one action.
**Treatment:** The three disc buttons, save, and the drop target.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add commands for D5 to D8 and D10 on the disc view model. Done when: each calls the API and updates from the next status.
2. Show refusals inline. Done when: a refused eject says why under the button.

- [ ] Implement D5 with the platform file picker filtered to `.iso`, `.cue`, `.ccd`, `.wobble` and `.json`, calling `PUT /media`. Done when: a headless test with a fake picker inserts the fixture ISO into a real emulator.
- [ ] Implement D6 as a flyout of the media profiles from `GET /profiles` grouped CD, DVD and Blu-ray with their glyphs from `assets/icons`. Done when: choosing `CD-R` inserts a blank CD-R.
- [ ] Implement D7 through `POST /tray`, labelled `Eject` or `Load` by the tray state, showing `removal is prevented` under the button on a 409. Done when: a test in `DiscActionTests` proves it with removal prevented. Cheaper substitute: a modal error box.
- [ ] Implement D8 with a folder and name picker calling `POST /media/save`, enabled only for a written disc, with progress in D2. Done when: a test in `DiscActionTests` proves it against a burned disc.
- [ ] Implement D10: dropping one file inserts it; dropping several or an unsupported type shows why nothing happened in `src/Wobble.App/ViewModels/DiscViewModel.cs`. Done when: a test in `DiscActionTests` proves it with the drag-drop events raised headless.
- [ ] Add Ctrl+O and Ctrl+E. Done when: a test in `DiscActionTests` asserts it and fails when the behaviour is removed.
- [ ] Commit: `app: insert, eject and save discs`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~DiscActionTests"` prints `Passed!` with no failed test.

## 7. Trace table: live, virtualised, follow and pause

The trace is where the tester's eye stays. It must keep up with a burn, hold 100 000 rows without slowing, and not move under the tester's cursor.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Trace column, T3, T4, T6, T8 and T9, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester watch every command as it arrives.
**Treatment:** The trace table and its pause, clear and follow controls.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add `TraceView` over a virtualised list. Done when: 100 000 rows scroll and new rows append without rebuilding.
2. Add follow, pause, clear and the dropped notice. Done when: T3, T4, T6, T8 and T9 behave as specified.

- [ ] Add `TraceViewModel` holding rows in an append-only buffer that mirrors the emulator's capacity, fed from `EmulatorSession`. Done when: a test appends 100 000 rows and the view model's memory stays within one copy of them.
- [ ] Build T6 with the eight columns, mono figures, the rose well and left bar on failed rows and the rule number on faulted rows, in a virtualising panel in `src/Wobble.App/Views/TraceView.axaml`. Done when: a headless test with 100 000 rows realises fewer than 100 row controls.
- [ ] Implement T8: follow the newest row until the user scrolls up, then show `Jump to newest`. Done when: a test in `TraceViewTests` proves it by scrolling.
- [ ] Implement T3: pause stops the view, the button counts commands received since, resume catches up; Space toggles it when the trace has focus. Done when: a test in `TraceViewTests` asserts it and fails when the behaviour is removed.
- [ ] Implement T4 with the in-button confirmation (`Clear`, then `Confirm clear` for 3 seconds) calling `POST /reset`'s trace half through a new `DELETE /trace` route in `src/Wobble.Host/Program.cs`. Done when: one click clears nothing and two do. Cheaper substitute: `POST /reset`, which would also remove the disc.
- [ ] Show T9 with the dropped count from the API, and the empty state text. Done when: a test in `TraceViewTests` asserts it and fails when the behaviour is removed.
- [ ] Commit: `app: show the live command trace`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~TraceViewTests"` prints `Passed!` with no failed test.

## 8. Trace filter, detail panel and export

Watching is the first job; finding the one command that matters is the second.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Trace column, T1, T2, T5 and T7, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester find and read one command among thousands.
**Treatment:** The filter, chips, detail panel and export.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add the filter and chips. Done when: T1 and T2 narrow the table as typed.
2. Add the detail panel and export. Done when: T7 decodes the selected command and T5 writes a trace file.

- [ ] Implement T1 matching opcode name, CDB hex, LBA and sense text, case-insensitively, within 100 ms on 100 000 rows in `src/Wobble.App/ViewModels/TraceViewModel.cs`. Done when: a timed test passes.
- [ ] Implement T2 (`All`, `Errors <n>`, `Writes`) with the error count live. Done when: a test in `TraceToolsTests` asserts it and fails when the behaviour is removed.
- [ ] Add `CdbDecoder` in `src/Wobble.Core/CdbDecoder.cs` returning named fields for every opcode the drive dispatches (LBA, length, format, page and the rest). Done when: a unit test decodes one CDB per opcode.
- [ ] Build T7 showing the decoded fields, the sense in words from a table in `src/Wobble.Core/SenseText.cs`, and up to 256 data bytes as hex when present. Done when: selecting the failed write shows `Medium error: write error`.
- [ ] Implement T5 saving the trace file format of D05 T01 §6 through the save picker. Done when: the saved file compares equal with `wobble trace diff` against the emulator's own export.
- [ ] Add Ctrl+F. Done when: a test in `TraceToolsTests` asserts it and fails when the behaviour is removed.
- [ ] Commit: `app: filter, inspect and export the trace`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~TraceToolsTests"` prints `Passed!` with no failed test.

## 9. Fault rule list: toggle, order, load, save and clear

Rules are an ordered list the tester edits while a test runs. The app owns the list, including disabled rules, and sends the enabled ones.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Faults column, F2 and F4 to F6, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester switch faults on and off while a test runs.
**Treatment:** The rule cards and the load, save and clear actions.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add `FaultsView` and its view model over `PUT /faults` and `GET /faults`. Done when: the cards show each rule with its fired count.
2. Add toggle, move, delete, load, save and clear. Done when: every change reaches the emulator or is undone with the reason shown.

- [ ] Build F2 cards (opcode, trigger, action, `fired x of n`, toggle, and edit, move up, move down, delete on hover or focus), with a rose border on a fired rule. Done when: a headless test with four rules finds four cards and the fired one styled.
- [ ] Send the enabled rules in card order with `PUT /faults` on every change, keeping fired counts by matching rules to the emulator's list. Done when: disabling the second rule sends three.
- [ ] Undo a change the emulator refuses and show its message on the card in `src/Wobble.App/ViewModels/FaultsViewModel.cs`. Done when: a test in `FaultListTests` proves it with a rule the emulator rejects. Cheaper substitute: leaving the list showing a rule the emulator does not have.
- [ ] Implement F4 (load a script, replacing the list only once the emulator accepted it), F5 (save all rules with an `enabled` field the loader of the app understands and the emulator's parser is not sent) and F6 (clear with the in-button confirmation). Done when: a save and load round trip keeps a disabled rule disabled.
- [ ] Keep the rule list across app restarts in the settings folder. Done when: a test in `FaultListTests` asserts it and fails when the behaviour is removed.
- [ ] Show the empty state text. Done when: a test in `FaultListTests` asserts it and fails when the behaviour is removed.
- [ ] Commit: `app: manage fault rules`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~FaultListTests"` prints `Passed!` with no failed test.

## 10. Fault rule editor

A rule has a dozen possible fields and most combinations are wrong. The editor shows only the fields that apply and says why a rule is not valid yet.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Faults column, F1 and F3, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester write a correct rule without reading the JSON format.
**Treatment:** The rule editor dialog.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Build `FaultRuleDialog.axaml` (F3) opened by F1 and by edit. Done when: every trigger and action of the design can be entered.
2. Validate as the emulator does. Done when: `Save` is enabled only for a rule `FaultScript.Parse` accepts.

- [ ] Build the opcode picker as a searchable list from `src/Wobble.Core/Opcodes.cs`. Done when: typing `wri` lists the WRITE opcodes.
- [ ] Build the trigger fields (LBA from and to, nth, after blocks written, at emulator time), each optional in `src/Wobble.App/Views/FaultRuleDialog.axaml`. Done when: a rule with only an opcode saves.
- [ ] Build the actions as a set of switches (sense, delay, buffer underrun, eject, swap media, not ready, drop connection), any of which may be combined as the design's `do` object allows, each showing its parameters when on, with sense entered as `KK/AA/QQ` or picked from the named list in `SenseText`. Done when: a test in `FaultEditorTests` proves it alone, and the design's fourth example (the 200th WRITE delayed 4 000 ms with a buffer underrun) is entered through the editor and reaches the emulator as one rule.
- [ ] Validate through `FaultScript.Parse` on the rule as edited, showing each message at its field. Done when: `lbaFrom` above `lbaTo` disables `Save` and names both fields. Cheaper substitute: validation rules written a second time in the app.
- [ ] Implement F1 and Ctrl+N in `src/Wobble.App/Views/FaultRuleDialog.axaml`. Done when: a headless test adds a rule and the emulator reports it.
- [ ] Commit: `app: add the fault rule editor`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~FaultEditorTests"` prints `Passed!` with no failed test.

## 11. Drive menu and drive profiles

Changing the emulated drive is rarer than changing the disc, so it lives behind the drive's name.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Drive menu, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester switch the emulated drive.
**Treatment:** The menu behind the drive identity, P1 to P3.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Build the drive menu opened by W2. Done when: P1 to P3 work against the API.
2. Handle the change. Done when: the title bar shows the new identity after the drive announces itself.

- [ ] Build P1 listing drive profiles from `GET /profiles` with the current one ticked, loading the chosen one with `PUT /drive`. Done when: a test in `DriveMenuTests` proves it with two bundled profiles.
- [ ] Build P2 with a file picker for a profile `.json`, showing the loader's message in the menu when it is refused. Done when: a test in `DriveMenuTests` proves it with a bad file.
- [ ] Build P3 with vendor, product, revision, serial and the supported media profiles as glyphs. Done when: a test in `DriveMenuTests` asserts it and fails when the behaviour is removed.
- [ ] Refresh W2 from the next status in `src/Wobble.App/Views/DriveMenu.axaml`. Done when: the new product name appears.
- [ ] Commit: `app: choose the emulated drive`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~DriveMenuTests"` prints `Passed!` with no failed test.

## 12. Attach to Windows from the app

Attaching the Windows initiator is the one step that needs elevation, once per boot. A button that runs it and reports the outcome removes a trip to an elevated console.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Title bar, W5, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester attach Windows without leaving the app.
**Treatment:** The Attach to Windows button and its messages.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add `WindowsAttach` in `src/Wobble.App/Services/`. Done when: it runs `tools/attach.ps1` elevated and reads its log.
2. Bind W5. Done when: the button shows only when it can help and reports refusal or failure.

- [ ] Add `WindowsAttach.RunAsync` starting `pwsh` with the `runas` verb on `tools/attach.ps1` with the session's port and target, waiting for exit and reading the log the script writes. Done when: a test with a fake process launcher sees the right arguments.
- [ ] Show W5 only on Windows while the emulator runs and `initiators` is empty. Done when: a test in `AttachTests` proves it for each condition.
- [ ] Report a declined UAC prompt as `Attach was cancelled` and a failed script with its log's last line, under the connection pill. Done when: a test in `AttachTests` proves it with the fake launcher.
- [ ] Ship `tools/attach.ps1` beside the app and find it relative to the executable. Done when: the published app finds it.
- [ ] Commit: `app: attach the Windows initiator from the title bar`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~AttachTests"` prints `Passed!` with no failed test.

## 13. Tray icon, its states and its menu

The app sits beside the software under test for hours. The tray icon says what the drive is doing when the window is covered.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Tray, and the control window in `design/reference/groove-board.html`.
**Job:** Tell the tester what the drive is doing while the window is hidden.
**Treatment:** The tray icon's four states and its menu.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Render the tray icons from `assets/icons/tray-*.svg`. Done when: four `.ico` files are embedded.
2. Add the tray icon and menu. Done when: the state follows the drive and the menu's four actions work.

- [ ] Add `scripts/build-tray-icons.ps1` that draws each `assets/icons/tray-*.svg` to a 1024 pixel PNG in headless Chrome or Edge with a transparent background (the method `assets/brand/README.md` records), then makes `src/Wobble.App/Assets/tray-*.ico` from the PNG with `scripts/campaign-icon.py frames`, and commit the renders. Done when: the four files exist with 16 to 48 pixel frames.
- [ ] Add a `TrayIcon` whose icon follows the activity: idle, reading, writing, and fault for 10 seconds after a rule fires. Done when: a test with a fake clock sees each state.
- [ ] Add the menu: Open Wobble, Insert image, Eject, Quit in `src/Wobble.App/Services/TrayService.cs`. Done when: a test in `TrayTests` proves it through its command.
- [ ] Implement G6: closing the window hides it to the tray when on, and quits the app and the emulator when off in `src/Wobble.App/Services/TrayService.cs`. Done when: a test in `TrayTests` proves it.
- [ ] Set the window and taskbar icon from `assets/brand/wobble.ico`. Done when: the built executable carries the icon.
- [ ] Commit: `app: add the tray icon`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~TrayTests"` prints `Passed!` with no failed test.

## 14. Keyboard, focus and automation names

The specification asks for every control to be reachable and named. One pass over the finished window checks it as a whole.

**Phase:** 7
**Needs:** none
**Surface:** yes
**Fidelity:** `design/SPEC.md`, Keyboard and accessibility, and the control window in `design/reference/groove-board.html`.
**Job:** Let the tester run the app from the keyboard and with a screen reader.
**Treatment:** Tab order, focus ring, names and shortcuts across the window.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add automation names and a tab order to every control. Done when: a test walks the tree and finds none unnamed.
2. Check contrast and focus. Done when: every text token meets 4.5 to 1 on its surface.

- [ ] Add `AutomationProperties.Name` to every interactive control in `src/Wobble.App/Views`. Done when: `AccessibilityTests.EveryControlIsNamed` walks the visual tree of each view and dialog and finds none without a name.
- [ ] Set the tab order to reading order and the 2 pixel `Violet` focus ring in `Groove.axaml`. Done when: a test tabs through the main window and visits the controls in the specification's order.
- [ ] Add `AccessibilityTests.TextContrast` computing the contrast of each text token on each surface token it is used on. Done when: every pair is at least 4.5 to 1, and the test fails if `Muted` is darkened.
- [ ] Check every shortcut of the specification in one test in `tests/Wobble.App.Tests/AccessibilityTests.cs`. Done when: Ctrl+O, Ctrl+E, Ctrl+F, Space, Ctrl+N and Ctrl+comma each run their command.
- [ ] Commit: `app: keyboard, focus and automation names`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~AccessibilityTests"` prints `Passed!` with no failed test.

## 15. The window compared with the approved rendering

Tokens and structure can all be right and the window still look unlike what the owner approved. A capture of the real window beside the reference settles it.

**Phase:** 7
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md`, every section, and the control window in `design/reference/groove-board.html`.
**Job:** Make the built window the one the owner approved.
**Treatment:** The whole window, captured and compared.
**Chrome:** Wobble's own dark window content under the system title bar; Groove tokens from `src/Wobble.App/Theme/Groove.axaml`, no platform accent colours.

**Build order:**

1. Add `scripts/capture-ui.ps1`. Done when: it starts the app against a scripted emulator state and saves a screenshot.
2. Compare with the reference. Done when: every difference is fixed or recorded with its reason.

- [ ] Add a `--demo-state <file>` option to `wobble` loading a disc, a trace and fault rules from a file for captures, labelled `demo` in `GET /status` and in the app's title bar. Done when: the app shows the reference's burn state. Cheaper substitute: hard-coding sample rows in the app.
- [ ] Add `scripts/capture-ui.ps1` starting the app with that state and saving `docs/design/control-window.png`. Done when: the file is written at 1340 by 760.
- [ ] Compare the capture with the control window in `design/reference/groove-board.html` region by region and fix layout, spacing, colour and type differences in the views. Done when: `docs/design/fidelity.md` lists each region as matching or gives the reason for a difference.
- [ ] Add the capture to `README.md` under the header. Done when: the README shows the real window.
- [ ] Commit: `app: match the approved rendering`

**Test checkpoint:** `pwsh scripts/capture-ui.ps1` exits 0 and writes `docs/design/control-window.png`; it exits 1 when the app fails to start.

## 16. A burn with a fault, watched in the app

The whole product in one test: Windows burns a disc through the emulator the app started, a rule the app sent fires, and the app shows it.

**Phase:** 7
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add `tests/Wobble.App.Tests/EndToEndTests.cs` running the app headless against its own emulator with the initiator attached. Done when: the test drives a burn from outside and reads the app's view models.
2. Assert what the tester would see. Done when: the failed write, the fired rule and the tray state are all present.

- [ ] Mark the class with the trait `Category=EndToEnd` and start the app's `EmulatorSession` on ports 3260 and 3261, insert a blank CD-R through the disc view model and add the write error rule through the fault editor's view model. Done when: `GET /faults` lists the rule.
- [ ] Burn the mixed-mode fixture from the test through `ScsiDrive`. Done when: the write over LBA 120 000 fails.
- [ ] Assert the trace view model has the failed row with rule 1, the rule card reads `fired 1 of 1`, the state pill passed through `Writing`, and the tray state was `fault`. Done when: all four hold.
- [ ] Export the trace through T5 and compare it with the emulator's own. Done when: `wobble trace diff` reports no difference.
- [ ] Commit: `app: prove a faulted burn end to end`

**Test checkpoint:** `dotnet test tests/Wobble.App.Tests --filter "FullyQualifiedName~EndToEndTests"` prints `Passed!` with no failed test.

## 17. The app in the downloads

The published archive should start with the app, not a console. The app joins the self-contained downloads beside `wobble`.

**Phase:** 7
**Needs:** none
**Surface:** no

**Build order:**

1. Extend `scripts/publish.ps1`. Done when: the archives hold `WobbleApp.exe` (or `WobbleApp` on Linux) beside `wobble`.
2. Start it from the archive. Done when: the unpacked app starts its emulator.

- [ ] Publish `src/Wobble.App` self-contained for `win-x64` and `linux-x64` into the same folder as `wobble` in `scripts/publish.ps1`. Done when: the archive lists both executables.
- [ ] Find `wobble`, `profiles/` and `tools/attach.ps1` relative to the app's own folder in `EmulatorSession`. Done when: the unpacked app starts the emulator with no setting changed.
- [ ] Start the unpacked app with `--smoke`, an option that starts the session, waits for a status and exits 0, or exits 1 with the error. Done when: it exits 0 from the archive folder.
- [ ] Describe the app in `README.md` and `docs/guide/01-install-and-attach.md`. Done when: both start from the app.
- [ ] Commit: `build: ship the app in the downloads`

**Test checkpoint:** `pwsh scripts/publish.ps1` exits 0 and `artifacts/wobble-0.1.0-win-x64/WobbleApp.exe --smoke` exits 0.

## Verification

- Every control id in `design/SPEC.md` is named in a passing test of `tests/Wobble.App.Tests`.
- `docs/design/fidelity.md` lists every region of the window as matching the approved rendering or explains the difference.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
