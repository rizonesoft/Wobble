---
schema_version: 1
id: capture
domain: 05-profiles
status: active
title: "TODO-02 -- The Capture Tool"
---

# TODO-02 -- The Capture Tool

> **Goal:** `wobble-capture` queries a real drive on Windows over SPTI and writes a drive profile, a media profile and command traces that Wobble replays. The first real captures are committed once a drive is attached.

> **Current state:** Measured 2026-11-15. No `Wobble.Capture` project exists. The plan notes that the first captures need a real drive attached to the Windows machine and none is present today.

## Inputs

- `docs/specs/2026-11-15-wobble-design.md`, Components (`Wobble.Capture`) and Profiles.
- `docs/plans/2026-11-15-write-support.md`, slice E.
- `src/Wobble.Spti` (D01 T01 §6) and `src/Wobble.Profiles` (T01 §1).

## Outcome

- `wobble-capture drive E:` writes a profile that, loaded into Wobble and captured again, gives the same profile.
- Captured profiles of the owner's real drive and discs are in `profiles/`, once the manual section has run.

**Adjacency:** list=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); document=applicable @ §5; settings=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); reporting=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); notifications=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); permissions=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); audit=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); exchange=applicable @ §2; reverse=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set)

**Adjacency rationale:** The tool's output is the profile file (§2), which is the document a tester carries to another machine; the real captures are §5.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | The capture tool project and its command line | T01 §1, D01 T01 §6 | [ ] |
| 2 | §2 | Capturing a drive profile | §1, T01 §2 | [ ] |
| 3 | §3 | Capturing a media profile | §2, T01 §4 | [ ] |
| 4 | §4 | Recording command traces from a real drive | §1, T01 §6 | [ ] |
| 5 | §5 | First captures from a real drive | §3, §4 | [ ] |

## 1. The capture tool project and its command line

A small console tool with three verbs. The shape comes first so each capture section adds one verb.

**Phase:** 6
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Create `src/Wobble.Capture`. Done when: `wobble-capture --help` lists `drive`, `media` and `trace`.
2. List drives. Done when: `wobble-capture list` prints each optical drive's letter, vendor and product.

- [ ] Create `src/Wobble.Capture/Wobble.Capture.csproj` (assembly name `wobble-capture`, Windows only) referencing `Wobble.Spti` and `Wobble.Profiles`, and add it to `Wobble.slnx`. Done when: it builds.
- [ ] Parse `list`, `drive <letter> [--out <file>]`, `media <letter> [--out <file>]` and `trace <letter> --script <file> [--out <file>]` in `src/Wobble.Capture/Program.cs`, exiting 2 with usage for anything else. Done when: tested by running the built tool.
- [ ] Implement `list` over the drive letters whose type is CD-ROM, sending INQUIRY to each. Done when: against the Wobble drive it prints `WOBBLE`.
- [ ] Refuse an existing `--out` file unless `--overwrite` is given. Done when: tested. Cheaper substitute: overwriting a capture someone made by hand.
- [ ] Commit: `capture: add the wobble-capture tool`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~CaptureCliTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## 2. Capturing a drive profile

The drive profile is everything the drive says about itself with no disc: identity, features, mode pages and speeds, kept as the raw bytes it returned.

**Phase:** 6
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add `DriveCapture` in `src/Wobble.Capture/DriveCapture.cs`. Done when: it fills every field of `DriveProfile`.
2. Prove it against Wobble. Done when: capture, load and capture again give equal profiles.

- [ ] Capture standard INQUIRY and VPD pages `00h`, `80h` and `83h`. Done when: the profile's `inquiry` holds the raw bytes.
- [ ] Capture GET CONFIGURATION (`46h`) with RT 0 in full, following the data length across requests, and split it into features by code. Done when: the feature count equals what Wobble serves.
- [ ] Capture each mode page with page control 0 (current), 1 (changeable) and 2 (default) through MODE SENSE (10) page `3Fh`. Done when: page `05h` and `2Ah` are in the profile with both masks.
- [ ] Capture read and write speeds from GET PERFORMANCE and mode page `2Ah`, and probe the vendor read opcodes with a zero-length request, recording which are accepted. Done when: the profile's `speeds` and `vendorCommands` are filled.
- [ ] Write the profile in `DriveCapture.Save` through a temporary file and rename. Done when: a cancelled capture leaves no partial file.
- [ ] Add `tests/Wobble.EndToEnd/CaptureDriveTests.cs` capturing Wobble, loading the capture with `SetDriveAsync` and capturing again. Done when: the two files are equal.
- [ ] Commit: `capture: capture a drive profile`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~CaptureDriveTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## 3. Capturing a media profile

The media profile is what the drive reports about the disc in it: kind, state, manufacturer, capacity and speeds.

**Phase:** 6
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add `MediaCapture` in `src/Wobble.Capture/MediaCapture.cs`. Done when: it fills `MediaProfile` for CD, DVD and BD media.
2. Prove it against Wobble. Done when: a captured blank DVD+R equals the generic profile it was inserted from.

- [ ] Read the current profile from GET CONFIGURATION and the state from READ DISC INFORMATION. Done when: a blank CD-R captures as profile `0x09`, state `blank`.
- [ ] Read the manufacturer id from the ATIP (CD), READ DISC STRUCTURE `0Eh` or `11h` (DVD) or the BD disc information. Done when: tested for one disc of each family.
- [ ] Read the capacity from READ TRACK INFORMATION free blocks or READ FORMAT CAPACITIES, and write speeds from GET PERFORMANCE type `03h`. Done when: tested.
- [ ] Optionally dump a finalised disc to an image beside the profile with `--image`, as ISO for one data track and CCD/IMG/SUB for other CDs. Done when: the dumped image inserts into Wobble and reads identical.
- [ ] Add `tests/Wobble.EndToEnd/CaptureMediaTests.cs`. Done when: it passes and fails if the state is not read.
- [ ] Commit: `capture: capture a media profile`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~CaptureMediaTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## 4. Recording command traces from a real drive

Comparison needs the same commands sent to the real drive and to Wobble. A script of read-only commands, replayed and recorded, gives that.

**Phase:** 6
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add `TraceCapture` in `src/Wobble.Capture/TraceCapture.cs`. Done when: it runs a script of CDBs and writes a trace file.
2. Refuse commands that write. Done when: a script with WRITE (10) is rejected before anything is sent.

- [ ] Define the script format (one JSON object per line: CDB hex, data-in length) and ship `profiles/scripts/identify.jsonl` covering INQUIRY, GET CONFIGURATION, MODE SENSE, READ TOC formats 0 to 5, READ DISC INFORMATION and READ TRACK INFORMATION. Done when: the script runs against Wobble.
- [ ] Run the script through `ScsiDrive` and write a `TraceFile`. Done when: the output loads in `wobble trace diff`.
- [ ] Refuse any opcode not on a read-only allow-list in `src/Wobble.Capture/SafeOpcodes.cs` before sending anything. Done when: a script with `2Ah`, `04h`, `A1h` or `55h` exits 2 naming the line. Cheaper substitute: a deny-list, which lets an unlisted destructive command through to a real disc.
- [ ] Add `tests/Wobble.EndToEnd/CaptureTraceTests.cs` comparing a trace captured from Wobble with Wobble's own trace of the same script. Done when: `trace diff` reports no difference.
- [ ] Commit: `capture: record command traces from a drive`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~CaptureTraceTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## 5. First captures from a real drive

The tool is proven against Wobble itself; its purpose is real hardware. This needs the owner to attach a drive and insert discs, so it is manual.

**Phase:** 99
**Needs:** optical-drive, operator
**Surface:** no

**Build order:**

1. Capture the attached drive and each disc on hand. Done when: the profiles are in `profiles/drives/` and `profiles/media/`.
2. Compare Wobble against the capture. Done when: `wobble trace diff` on the identify script shows only differences recorded as findings.

- [ ] Run `wobble-capture drive <letter> --out profiles/drives/<vendor>-<product>.json` on the real drive. Done when: the file loads with `SetDriveAsync`.
- [ ] Run `wobble-capture media <letter>` for each blank and written disc on hand. Done when: each file is in `profiles/media/` and loads.
- [ ] Run `wobble-capture trace <letter> --script profiles/scripts/identify.jsonl` and the same script against Wobble with the captured profile, then `wobble trace diff`. Done when: every difference is either fixed or filed in `todo/findings.md` with the command.
- [ ] Record the drive's make, firmware and the date in `docs/profiles.md`. Done when: the document lists each real capture.
- [ ] Commit: `profiles: add the first captures from real hardware`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~MediaProfileTests"` prints `Passed!` with the captured files present in `profiles/`, since that test loads every file in the folder.

## Verification

- Capturing Wobble with `wobble-capture drive`, loading the result and capturing again gives equal files.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
