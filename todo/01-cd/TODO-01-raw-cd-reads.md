---
schema_version: 1
id: raw-cd-reads
domain: 01-cd
status: active
title: "TODO-01 -- Raw CD Model and Reads"
---

# TODO-01 -- Raw CD Model and Reads

> **Goal:** Slice A of the plan is finished: a CD is modelled as sessions, tracks and raw 2352-byte sectors with subchannel, every read command CarbonCD and Windows send against a pressed CD answers correctly, and a mixed-mode CUE/BIN reads back byte for byte through the Windows initiator.

> **Current state:** Measured 2026-11-15. `src/Wobble.Core/CueSheet.cs` loads `AUDIO`, `MODE1/2352`, `MODE1/2048` and `MODE2/2352` tracks with `PREGAP` and `INDEX 00`/`01` from `BINARY` files. `Media.ReadRaw` returns stored raw sectors or generates Mode 1 sectors with EDC and ECC through `SectorCodec`. `OpticalDrive.ReadCdSectors` serves READ CD (0xBE) and READ CD MSF (0xB9) with header, user data, EDC/ECC, formatted Q and raw P to W; `ReadToc` serves formats 0, 1 and 2 for one session. `tests/Wobble.Tests/RawCdTests.cs` has 15 passing tests. Gaps: one session only (`OpticalDrive.Info.cs:42` hard-codes session 1), no READ SUB-CHANNEL (0x42), no CD-TEXT, MCN or ISRC, a generated Mode 2 gap sector has no subheader or EDC, cue sheet errors are not all refused, the trace list in `OpticalDrive.cs:19` grows without bound, and nothing reads raw sectors from Windows end to end.

## Inputs

- `docs/plans/2026-11-15-write-support.md`, slice A and the CarbonCD command survey under Why the order changed.
- `docs/specs/2026-11-15-wobble-design.md`, Transport (the `IScsiDevice` seam) and Control API.
- `src/Wobble.Core/Media.cs`, `CueSheet.cs`, `SectorCodec.cs`, `OpticalDrive.cs`, `OpticalDrive.Info.cs`; `tests/Wobble.Tests/RawCdTests.cs`; `src/Wobble.Testing/`.

## Outcome

- Every read command in the CarbonCD survey that belongs to the generic drive answers byte-exact for single and multi-session CDs.
- `tests/Wobble.EndToEnd` reads a mixed-mode CUE/BIN through SPTI on Windows and the bytes equal the source files.

**Adjacency:** list=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); document=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); settings=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); reporting=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); notifications=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); permissions=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); audit=applicable @ §7; exchange=applicable @ §1; reverse=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set)

**Adjacency rationale:** Audit is the command trace, which §7 bounds. Exchange is the image formats a disc is loaded from, which §1 hardens. Nothing here is listed, reported, permitted or reversed.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Cue sheet refusals and remaining commands | -- | [ ] |
| 2 | §2 | Sessions in the disc model and the multi-session TOC | §1 | [ ] |
| 3 | §3 | Complete generated sectors for Mode 2 and gaps | §2 | [ ] |
| 4 | §4 | READ SUB-CHANNEL: position, catalogue number, ISRC | §1, §3 | [ ] |
| 5 | §5 | CD-TEXT through READ TOC format 5 | §1 | [ ] |
| 6 | §6 | An SPTI library for tests and capture | -- | [ ] |
| 7 | §7 | A bounded command trace | -- | [ ] |
| 8 | §8 | Mixed-mode disc read back through Windows | §4, §6 | [ ] |

## 1. Cue sheet refusals and remaining commands

A cue sheet that Wobble half understands gives a disc that differs silently from the one the tester meant. Every line Wobble does not model must be refused by name, and the commands that carry disc text and codes must be kept for later sections.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Extend the parser in `src/Wobble.Core/CueSheet.cs`. Done when: `CATALOG`, `ISRC`, `TITLE`, `PERFORMER`, `FLAGS` and `POSTGAP` are stored on the model and unknown track types, `WAVE` files and out-of-order indexes are refused with the line quoted.
2. Cover each refusal in `tests/Wobble.Tests/CueSheetTests.cs`. Done when: each bad sheet throws `InvalidDataException` naming the line.

- [ ] Add `Catalog`, and per track `Isrc`, `Title`, `Performer`, `FourChannel`, `CopyPermitted` and `PreEmphasis`, to `Track` and `Media` in `src/Wobble.Core/Media.cs`. Done when: `dotnet build src/Wobble.Core` succeeds and `Track.Control` returns the nibble built from the flags (MMC-3 table 21: bit 0 pre-emphasis, bit 1 copy permitted, bit 2 data, bit 3 four channel).
- [ ] Parse `CATALOG`, `ISRC`, `TITLE`, `PERFORMER`, `FLAGS DCP 4CH PRE` and `POSTGAP` in `CueSheet.Load`. Done when: a test sheet with each line loads and the model carries the values.
- [ ] Refuse a `FILE` whose type is not `BINARY`, a `TRACK` number that does not rise by one, an `INDEX 01` before its `INDEX 00`, and a missing track file in `CueSheet.Load`. Done when: each throws `InvalidDataException` whose message holds the file name and the offending line. Cheaper substitute: skipping the line, as the current `if (!m.Success) continue` does for commands it knows nothing of.
- [ ] Add `tests/Wobble.Tests/CueSheetTests.cs` with one test per accepted command and one per refusal. Done when: the tests pass and each refusal test fails if its check is removed.
- [ ] State the supported cue sheet commands in a `## Cue sheets` section of `README.md`. Done when: the section lists every accepted command and says what is refused.
- [ ] Commit: `core: parse cue sheet text, codes and flags, refuse what is not modelled`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~CueSheetTests"` prints `Passed!` with no failed test.

## 2. Sessions in the disc model and the multi-session TOC

`ReadToc` writes session 1 into every answer. Written discs and CloneCD images have several sessions, each with its own lead-in and lead-out, and READ TOC formats 1 and 2 must describe them.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Session` to `src/Wobble.Core/Media.cs`. Done when: `Media.Sessions` lists each session's tracks, lead-out address and the gap before the next session.
2. Rewrite formats 0, 1 and 2 in `OpticalDrive.Info.cs` over sessions. Done when: a two-session disc answers as MMC-3 6.25 describes.

- [ ] Add `public sealed record Session(int Number, IReadOnlyList<Track> Tracks, long LeadOut)` and `Media.Sessions` in `src/Wobble.Core/Media.cs`, with single-session constructors building one session. Done when: existing tests pass unchanged.
- [ ] Make `Media.BlockCount` the last session's lead-out and keep the 11 400 frame gap between sessions (6 750 lead-out plus 4 500 lead-in plus 150 pregap, MMC-3 4.2.3.7) unreadable in `Media.ReadRaw`. Done when: a read inside the gap returns check condition `05/21/00`.
- [ ] Answer READ TOC format 1 in `OpticalDrive.ReadToc` with the first and last session numbers and the first track of the last session. Done when: a unit test on a two-session disc reads `01 02` and track 2's address.
- [ ] Answer READ TOC format 2 with points `A0`, `A1`, `A2` and each track for every session, and `B0` and `C0` for sessions that are followed by another. Done when: the test compares the answer byte for byte with a hand-built expected array.
- [ ] Report the session count and the last session's first track in `ReadDiscInformation` and the session number in `ReadTrackInformation`. Done when: bytes 4 to 6 of READ DISC INFORMATION and byte 3 of READ TRACK INFORMATION match the two-session disc.
- [ ] Add `tests/Wobble.Tests/MultiSessionTests.cs` building the two-session disc in memory. Done when: the tests pass and fail when `data[3] = 1` is restored in `ReadToc`.
- [ ] Commit: `core: model sessions and answer the multi-session TOC`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~MultiSessionTests"` prints `Passed!` with no failed test.

## 3. Complete generated sectors for Mode 2 and gaps

`Media.ReadRaw` writes only a header for a Mode 2 gap sector, so software that checks EDC sees a damaged sector where a real disc has a valid empty one.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add Mode 2 Form 1 and Form 2 encoders to `src/Wobble.Core/SectorCodec.cs`. Done when: both produce sectors whose EDC verifies.
2. Use them in `Media.ReadRaw`. Done when: a generated Mode 2 gap sector is a valid empty Form 2 sector.

- [ ] Add `SectorCodec.EncodeMode2Form1` (subheader, 2048 user bytes, EDC over bytes 16 to 2071, P and Q parity with the header zeroed, ECMA-130 annex A) in `src/Wobble.Core/SectorCodec.cs`. Done when: a test checks the parity against a sector taken from a known-good XA image fixture built in the test.
- [ ] Add `SectorCodec.EncodeMode2Form2` (subheader with the form 2 bit, 2324 user bytes, EDC over bytes 16 to 2347). Done when: `SectorCodec.Edc` over the sector's body equals its last four bytes.
- [ ] Generate an empty Form 2 sector for an unstored Mode 2 extent in `Media.ReadRaw`. Done when: READ CD of a Mode 2 pregap with sector type 5 expected succeeds. Cheaper substitute: the header-only sector the code writes today.
- [ ] Add the cases to `tests/Wobble.Tests/RawCdTests.cs`. Done when: the new tests pass and fail when the encoder's EDC call is removed.
- [ ] Commit: `core: generate valid Mode 2 sectors`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~RawCdTests"` prints `Passed!` with no failed test.

## 4. READ SUB-CHANNEL: position, catalogue number, ISRC

Windows CD audio code and many copiers ask the drive where it is and for the disc's codes through READ SUB-CHANNEL (0x42). Wobble answers `05/20/00` today.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `ReadSubChannel` to `src/Wobble.Core/OpticalDrive.Info.cs` and its opcode to `Dispatch`. Done when: formats 1, 2 and 3 answer as MMC-3 6.23 lays them out.
2. Track the last sector read as the current position. Done when: format 1 reports it in LBA and MSF.

- [ ] Record the address after the last sector of each READ, READ CD and READ CD MSF in a `position` field of `OpticalDrive`. Done when: a test reads 10 sectors at LBA 100 and format 1 reports absolute address 110.
- [ ] Answer format 1 (current position) with audio status `15h`, ADR and control, track, index, absolute and track-relative address in `OpticalDrive.ReadSubChannel`. Done when: the test compares all 16 bytes, with and without the MSF bit.
- [ ] Answer format 2 (media catalogue number) from `Media.Catalog` and format 3 (ISRC) from `Track.Isrc`, with the valid bit clear when the disc has none. Done when: tests cover a disc with and without each code.
- [ ] Honour the SubQ bit in `OpticalDrive.ReadSubChannel`: with it clear return only the 4 byte header. Done when: the test reads a 4 byte answer.
- [ ] Add 0x42 to the `needsMedia` list in `OpticalDrive.Dispatch`. Done when: the command returns `02/3A/01` with an empty tray.
- [ ] Commit: `core: answer READ SUB-CHANNEL`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~ReadSubChannel"` prints `Passed!` with no failed test.

## 5. CD-TEXT through READ TOC format 5

A cue sheet's `TITLE` and `PERFORMER` lines are CD-TEXT on a real disc. READ TOC format 5 returns it as 18-byte packs with a CRC, and software that shows track names reads it.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `src/Wobble.Core/CdText.cs` that builds packs from the model. Done when: each pack's CRC-16 verifies.
2. Serve format 5 in `OpticalDrive.ReadToc`. Done when: a disc with text answers and a disc without is refused.

- [ ] Add `CdText.Build(Media)` producing title (pack type `80h`) and performer (`81h`) packs for the disc and each track, 12 text bytes per pack with sequence numbers and character positions, plus the size information packs (`8Fh`) in `src/Wobble.Core/CdText.cs` (MMC-3 annex J). Done when: a test decodes the packs back to the strings.
- [ ] Append the CRC-16 (polynomial `x^16 + x^12 + x^5 + 1`, inverted) to each pack. Done when: the test recomputes it for every pack.
- [ ] Return the packs for READ TOC format 5 in `OpticalDrive.ReadToc`, and `05/24/00` when the disc carries no text. Done when: a test in `CdTextTests` proves it.
- [ ] Add `tests/Wobble.Tests/CdTextTests.cs`. Done when: it passes and fails if the pack sequence number is not incremented.
- [ ] Commit: `core: serve CD-TEXT from the cue sheet`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~CdTextTests"` prints `Passed!` with no failed test.

## 6. An SPTI library for tests and capture

End-to-end tests and the capture tool both send raw CDBs to a Windows drive. One small library owns `IOCTL_SCSI_PASS_THROUGH_DIRECT` so neither reimplements it.

**Phase:** 1
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Create `src/Wobble.Spti` with a `ScsiDrive` class. Done when: it opens a drive letter and executes a CDB with data in, data out and sense returned.
2. Reference it from `Wobble.Testing`. Done when: an end-to-end test sends INQUIRY through it.

- [ ] Create `src/Wobble.Spti/Wobble.Spti.csproj` and add it to `Wobble.slnx`. Done when: `dotnet build Wobble.slnx` builds it.
- [ ] Add `ScsiDrive.Open(string root)` and `Execute(ReadOnlySpan<byte> cdb, Span<byte> dataIn, ReadOnlySpan<byte> dataOut, TimeSpan timeout)` returning status and sense in `src/Wobble.Spti/ScsiDrive.cs`, over `DeviceIoControl` with `IOCTL_SCSI_PASS_THROUGH_DIRECT` and an aligned buffer. Done when: INQUIRY against the Wobble drive returns vendor `WOBBLE`.
- [ ] Return a failed command as a result carrying the sense bytes, and throw only when the IOCTL itself fails. Done when: READ (10) past the end returns key 5, ASC `21h`. Cheaper substitute: throwing on every check condition, which hides the sense a test needs to assert.
- [ ] Add `WindowsDrive.OpenScsi()` in `src/Wobble.Testing/WindowsDrive.cs` returning a `ScsiDrive` for the drive's root. Done when: `tests/Wobble.EndToEnd/SptiTests.cs` uses it.
- [ ] Add `tests/Wobble.EndToEnd/SptiTests.cs` with INQUIRY, a good read and a refused read. Done when: the three tests pass on the attached host.
- [ ] Commit: `spti: add a SCSI pass-through library for Windows`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~SptiTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## 7. A bounded command trace

`OpticalDrive` appends every command to a list that is never trimmed. A long burn test sends hundreds of thousands of commands, and the emulator's memory grows with them.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Bound the trace in `src/Wobble.Core/OpticalDrive.cs`. Done when: it keeps the newest entries up to a limit and counts what it dropped.
2. Expose the limit and the paging in the control API. Done when: `GET /trace?after=<seq>` returns only newer entries.

- [ ] Replace the `List<TraceEntry>` in `src/Wobble.Core/OpticalDrive.cs` with a ring of `TraceCapacity` entries (default 100 000, settable in the constructor) and a `TraceDropped` count. Done when: a test executes 150 000 commands on a drive with capacity 1 000 and reads 1 000 entries whose first `Seq` is 149 001.
- [ ] Add `after` and `limit` query parameters to `GET /trace` in `src/Wobble.Host/Program.cs`, and return `dropped` beside the entries. Done when: `curl 'http://127.0.0.1:3261/trace?after=5&limit=2'` returns entries 6 and 7.
- [ ] Add `--trace-capacity <n>` to `src/Wobble.Host/Program.cs` and refuse a value below 1. Done when: `wobble --trace-capacity 0` exits 2 with a message naming the option.
- [ ] Add `TraceAsync(long after)` to `src/Wobble.Testing/WobbleControl.cs`. Done when: an end-to-end test pages the trace with it.
- [ ] Document the two parameters and the option in `README.md` under Control API. Done when: the README names `after`, `limit`, `dropped` and `--trace-capacity`.
- [ ] Commit: `core: bound the command trace and page it`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~Trace"` prints `Passed!` with no failed test.

## 8. Mixed-mode disc read back through Windows

Slice A is done when a mixed-mode CUE/BIN reads back byte for byte through raw reads. Unit tests prove the core; this proves the whole path through the iSCSI target and the Windows CD-ROM class driver.

**Phase:** 1
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Build a mixed-mode fixture in `tests/Wobble.EndToEnd/Fixtures.cs`. Done when: it writes a data track and three audio tracks with pregaps to a temporary folder.
2. Read every sector back through `ScsiDrive`. Done when: the bytes equal the fixture's.

- [ ] Add `Fixtures.MixedModeCue(string folder)` in `tests/Wobble.EndToEnd/Fixtures.cs` writing one `MODE1/2352` track of 600 sectors and three `AUDIO` tracks of 300 sectors with deterministic content, and its `.cue`. Done when: `CueSheet.Load` opens the result.
- [ ] Add `RawReadTests.EverySectorReadsBackRaw` in `tests/Wobble.EndToEnd/RawReadTests.cs` reading the whole disc with READ CD (0xBE, fields `F8h`) in runs of 26 sectors. Done when: the bytes equal the `.bin` file.
- [ ] Add `RawReadTests.FormattedQCrossesATrackBoundary` reading formatted Q (subchannel selection 2) across a track boundary. Done when: the track number, index and relative address in Q match the layout, including index 0 in the pregap.
- [ ] Add `RawReadTests.TheFullTocListsEveryTrack` sending READ TOC format 2 from Windows. Done when: the points and addresses match the cue sheet.
- [ ] Add a test that READ (10) of an audio sector fails with `05/64/00` from Windows. Done when: the sense reaches the test through `ScsiDrive`.
- [ ] Tick slice A done in `README.md` Status and move its limits out of Limits of step 1. Done when: the README no longer says READ CD returns user data only.
- [ ] Commit: `e2e: read a mixed-mode CD back raw through Windows`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~RawReadTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## Verification

- `dotnet test tests/Wobble.Tests` and `dotnet test tests/Wobble.EndToEnd` both pass on the attached Windows host.
- The plan's slice A done condition holds: the step 1 tests still pass and the mixed-mode disc reads back byte for byte.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
