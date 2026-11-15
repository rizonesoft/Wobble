---
schema_version: 1
id: cd-writing
domain: 01-cd
status: active
title: "TODO-02 -- CD-R and CD-RW Writing"
---

# TODO-02 -- CD-R and CD-RW Writing

> **Goal:** Slice B of the plan is finished: Wobble presents blank and appendable CD-R and CD-RW media, accepts track-at-once, session-at-once and raw disc-at-once writing as CarbonCD sends it, saves the written disc as CCD/IMG/SUB, and the result reads back identical.

> **Current state:** Measured 2026-11-15. Nothing can be written. `MediaKind` has `CdRom` and `DvdRom` only, `Media` is immutable, `OpticalDrive.ModeSelect` accepts any list and changes nothing (`OpticalDrive.Info.cs:356`), mode page `05h` does not exist, and WRITE (10), SYNCHRONIZE CACHE as a write, SEND CUE SHEET, SEND OPC, BLANK, CLOSE TRACK SESSION, RESERVE TRACK and READ BUFFER CAPACITY are not dispatched. The iSCSI target already carries Data-Out with R2T and immediate data (`src/Wobble.Iscsi/IscsiConnection.cs:363`).

## Inputs

- `docs/plans/2026-11-15-write-support.md`, slice B and the CarbonCD write survey.
- `docs/specs/2026-11-15-wobble-design.md`, Profiles (media state) and Control API.
- `D:\CarbonCD\MMCWriter.cpp` and `CDWriter.cpp` on this machine, read only, for the exact command sequences.
- `src/Wobble.Core/`, `src/Wobble.Host/Program.cs`, `src/Wobble.Testing/WobbleControl.cs`.

## Outcome

- A blank CD-R or CD-RW inserted through the control API can be burned in each write type and reads back identical.
- A written disc persists as CCD/IMG/SUB and reloads to the same TOC and sectors.
- BLANK returns a CD-RW to blank, and is refused on a CD-R.

**Adjacency:** list=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); document=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); settings=applicable @ §5; reporting=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); notifications=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); permissions=applicable @ §6; audit=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); exchange=applicable @ §12; reverse=applicable @ §11

**Adjacency rationale:** Settings are the write parameters mode page (§5). Exchange is the CloneCD image a written disc is saved as and loaded from (§12). Reverse is BLANK, the only undo a disc has (§11). Permissions are the write refusals of media that cannot take the write (§6).

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Writable CD media and its sector store | T01 §2 | [ ] |
| 2 | §2 | Blank discs through the control API and command line | §1 | [ ] |
| 3 | §3 | Writer profiles and features for CD | §1 | [ ] |
| 4 | §4 | ATIP and disc and track information for writable CDs | §1 | [ ] |
| 5 | §5 | Write parameters mode page and MODE SELECT | §3 | [ ] |
| 6 | §6 | Track-at-once writing | §4, §5 | [ ] |
| 7 | §7 | Session-at-once writing with SEND CUE SHEET | §6 | [ ] |
| 8 | §8 | Raw disc-at-once writing | §6 | [ ] |
| 9 | §9 | Buffer capacity, speeds and the small write commands | §6 | [ ] |
| 10 | §10 | Reads of written and partly written discs | §7, §8 | [ ] |
| 11 | §11 | BLANK for CD-RW | §9 | [ ] |
| 12 | §12 | CloneCD images: save written discs and load them | §10, §11 | [ ] |
| 13 | §13 | CarbonCD-style burns through Windows | §12, T01 §8 | [ ] |

## 1. Writable CD media and its sector store

Writing needs a disc that changes: a state (blank, appendable, complete), a place to keep raw sectors and subchannel as they arrive, and the facts a blank disc is sold with.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Add `CdR` and `CdRw` to `MediaKind` and a `DiscState` to `Media` in `src/Wobble.Core/Media.cs`. Done when: a blank disc has no tracks, state `Blank` and a capacity.
2. Add `src/Wobble.Core/SectorStore.cs`. Done when: raw sectors and 96 subchannel bytes written at any address, negative lead-in addresses included, read back.

- [ ] Add `MediaKind.CdR`, `MediaKind.CdRw` and `enum DiscState { Blank, Appendable, Complete }` in `src/Wobble.Core/Media.cs`, with `Media.Profile` returning `0009h` and `000Ah`. Done when: `dotnet build` succeeds and existing tests pass.
- [ ] Create `SectorStore` in `src/Wobble.Core/SectorStore.cs`: a sparse temporary file of 2448-byte records (2352 sector, 96 subchannel) addressed from the start of the lead-in, with a written-range map. Done when: a test writes LBA -150 and LBA 1000 and reads both back, and an unwritten address reports unwritten.
- [ ] Delete the store's temporary file in `SectorStore.Dispose`. Done when: the test asserts the file is gone after disposal. Cheaper substitute: an in-memory array, which a 700 MB burn would hold in RAM.
- [ ] Add `Media.Blank(MediaKind kind, long capacityBlocks, AtipInfo atip)` with `AtipInfo` (lead-in start, last possible lead-out, speeds, manufacturer) in `src/Wobble.Core/Media.cs`. Done when: `Media.Blank(MediaKind.CdR, 359849, AtipInfo.Generic80)` has `State == DiscState.Blank`.
- [ ] Make `Media.Tracks`, `Sessions`, `State` and `BlockCount` reflect what has been written to the store. Done when: a test that writes a track through `Media` sees one track.
- [ ] Add `tests/Wobble.Tests/WritableMediaTests.cs`. Done when: the tests pass and fail if `SectorStore` ignores the subchannel half of a record.
- [ ] Commit: `core: add writable CD media with a sparse sector store`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~WritableMediaTests"` prints `Passed!` with no failed test.

## 2. Blank discs through the control API and command line

A test must be able to put a blank disc in the drive. `PUT /media` takes only an image path today.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Extend `MediaRequest` in `src/Wobble.Host/Program.cs`. Done when: `{"blank": "cd-r"}` inserts a blank disc.
2. Add the same to `WobbleControl` and the command line. Done when: a test inserts a blank CD-RW in one call.

- [ ] Accept `{"blank": "cd-r"|"cd-rw", "capacityBlocks": <n>}` in `PUT /media` in `src/Wobble.Host/Program.cs`, defaulting to 359 849 blocks. Done when: `GET /status` then shows `state: "Blank"` and the kind.
- [ ] Refuse a request with both `image` and `blank`, an unknown `blank` value, and a capacity outside 1 to 449 849 with 400 and a message naming the field. Done when: each case is asserted in a host test. Cheaper substitute: ignoring the extra field.
- [ ] Add `--blank <kind>` to `src/Wobble.Host/Program.cs`. Done when: `wobble --blank cd-r` starts with a blank disc and `--blank` with `--image` exits 2.
- [ ] Add `InsertBlankAsync(string kind, long? capacityBlocks = null)` to `src/Wobble.Testing/WobbleControl.cs`. Done when: an end-to-end fixture uses it.
- [ ] Add state, kind, session and track counts to `GET /status`. Done when: the JSON carries `state`, `kind`, `sessions` and `tracks`.
- [ ] Add `tests/Wobble.Tests/ControlApiTests.cs` hosting the app in process with `WebApplicationFactory` or an in-test `WebApplication`. Done when: the accept and refuse cases pass.
- [ ] Commit: `host: insert blank discs through the control API`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~ControlApiTests"` prints `Passed!` with no failed test.

## 3. Writer profiles and features for CD

Burning software decides what it may do from GET CONFIGURATION. The drive must list the CD-R and CD-RW profiles and mark the write features current only when writable media is loaded.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Extend `Features()` in `src/Wobble.Core/OpticalDrive.Info.cs`. Done when: the profile list has `0009h` and `000Ah`, and features `0021h`, `002Dh`, `002Eh` and `0037h` exist.
2. Compute `current` from the loaded disc. Done when: a blank CD-R makes `002Dh` and `002Eh` current and a pressed CD does not.

- [ ] Add profiles `000Ah` and `0009h` to the profile list feature, each current only for its media, in `Features()`. Done when: a test reads the list with a blank CD-R and sees `0009h` current.
- [ ] Add feature `0021h` (incremental streaming writable), `002Dh` (CD track at once, with test write, CD-RW, raw subchannel and data type support bits) and `002Eh` (CD mastering: SAO, raw, raw multi-session, test write, maximum cue sheet length) per MMC-5 5.3. Done when: the payload bytes match a hand-built expectation.
- [ ] Add feature `0037h` (CD-RW media write support) current with CD-RW media. Done when: a test in `WriterFeatures` asserts it and fails when the behaviour is removed.
- [ ] Set the current profile in the GET CONFIGURATION header from the loaded media. Done when: bytes 6 and 7 read `0009h` with a blank CD-R.
- [ ] Update mode page `2Ah` in `ModePages()` to set the CD-R and CD-RW write bits and the write speed fields. Done when: byte 3 reads `03h`.
- [ ] Commit: `core: report CD writer profiles and features`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~WriterFeatures"` prints `Passed!` with no failed test.

## 4. ATIP and disc and track information for writable CDs

Before writing, software reads the ATIP, the disc status and the next writable address. The answers for a blank or appendable disc differ in almost every byte from a finished one.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Serve READ TOC format 4 in `OpticalDrive.ReadToc`. Done when: a CD-R answers its ATIP and a pressed CD is refused.
2. Rewrite `ReadDiscInformation` and `ReadTrackInformation` over `DiscState`. Done when: blank, appendable and complete discs each answer per MMC-5 6.22 and 6.27.

- [ ] Answer READ TOC format 4 with the ATIP: reference power, disc type bit for CD-RW, lead-in start and last possible lead-out start from `AtipInfo`. Done when: a test decodes `97:34:23` and `79:59:74`.
- [ ] Set disc status, last session state and the erasable bit in byte 2 of READ DISC INFORMATION from `Media.State` and kind. Done when: blank CD-RW reads `10h`, appendable CD-R `01h` plus session bits, complete `0Eh`.
- [ ] Report the last session lead-in start and last possible lead-out start in bytes 16 to 23. Done when: a blank disc reports the ATIP values instead of `FFFFFFFFh`.
- [ ] Answer READ TRACK INFORMATION for the invisible track (track number `FFh`, or the first unwritten track) with the blank bit, NWA valid, the next writable address and free blocks. Done when: a blank disc reports NWA 0 and its capacity free.
- [ ] Report damage, reserved and packet bits and the track's data mode for a partly written track in `OpticalDrive.ReadTrackInformation`. Done when: a test that writes 100 sectors track at once reads NWA 100.
- [ ] Make READ CAPACITY on a blank disc return last LBA 0 with block size 2048, and READ TOC format 0 on a blank disc fail with `05/24/00`. Done when: a test in `WritableInfoTests` proves it against the MMC text.
- [ ] Add `tests/Wobble.Tests/WritableInfoTests.cs`. Done when: it passes and fails when the status byte is forced to `0Eh`.
- [ ] Commit: `core: answer ATIP, disc and track information for writable CDs`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~WritableInfoTests"` prints `Passed!` with no failed test.

## 5. Write parameters mode page and MODE SELECT

Every write type is chosen through mode page `05h`. Wobble has no such page and its MODE SELECT changes nothing, which the README lists as a limit.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Add page `05h` with current and changeable masks to `src/Wobble.Core/OpticalDrive.Info.cs`. Done when: MODE SENSE returns it with page control 0, 1 and 2.
2. Make `ModeSelect` parse and validate pages. Done when: a valid page `05h` is stored and an invalid one is refused with `05/26/00`.

- [ ] Add `WriteParameters` (write type, test write, track mode, data block type, multi-session field, session format, packet size, pause length, MCN, ISRC) in `src/Wobble.Core/WriteParameters.cs` with `Parse` and `ToPage`. Done when: a round trip of the 52-byte page is byte-identical.
- [ ] Return page `05h` from `ModePages()` with its changeable mask. Done when: MODE SENSE (10) page control 1 shows the changeable bits set.
- [ ] Parse the mode parameter header and each page in `OpticalDrive.ModeSelect`, for both the 6 and 10 byte commands, requiring the PF bit. Done when: a list shorter than its header is refused with `05/1A/00`.
- [ ] Refuse write type above 3, a data block type the write type does not allow (MMC-5 table 669), and a change to a field that is not changeable, with `05/26/00`. Done when: a test in `WriteParameters` proves it. Cheaper substitute: storing whatever arrives, which lets a later WRITE run under settings no drive would accept.
- [ ] Reset the page to its default on `OpticalDrive.Reset` and on media change. Done when: a test changes the page, swaps the disc and reads the default back.
- [ ] Remove the MODE SELECT limit from `README.md`. Done when: the Limits list no longer says it changes nothing.
- [ ] Commit: `core: add the write parameters page and a real MODE SELECT`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~WriteParameters"` prints `Passed!` with no failed test.

## 6. Track-at-once writing

Track at once is the simplest write type and the one IMAPI2 uses on CD. It defines the shared write path: address checks, the sector store, the next writable address and the links between tracks.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Add `src/Wobble.Core/OpticalDrive.Write.cs` with WRITE (10), WRITE (12) and SYNCHRONIZE CACHE. Done when: a track written at the NWA reads back.
2. Add CLOSE TRACK SESSION and RESERVE TRACK. Done when: closing the session yields a TOC and the state the multi-session field asks for.

- [ ] Dispatch `2Ah` and `AAh` to `OpticalDrive.Write` in `src/Wobble.Core/OpticalDrive.cs`, taking the block size from the data block type (2048, 2336, 2352, 2368 or 2448). Done when: a data length that is not blocks times that size is refused with `05/24/00`.
- [ ] Refuse a write to media that is not writable with `05/30/05`, to an address that is not the NWA with `05/21/02`, and beyond capacity with `05/21/00`. Done when: a test in `TrackAtOnceTests` proves it.
- [ ] Encode cooked blocks into raw sectors with `SectorCodec` and synthesise P and Q subchannel as they are stored. Done when: READ CD of a written Mode 1 sector returns valid EDC and ECC.
- [ ] Add, in `OpticalDrive.Write.cs`, the run-out, link and run-in blocks after a closed track and the 150 frame pregap before the next (7 blocks of link area, MMC-3 4.2.3.8). Done when: the second track's NWA is the first track's end plus 152.
- [ ] Implement CLOSE TRACK SESSION (`5Bh`) functions 1 (track) and 2 (session) in `OpticalDrive.Write.cs`, writing the lead-in TOC and lead-out, and leaving the disc appendable or complete by the multi-session field. Done when: after function 2 READ TOC lists the tracks.
- [ ] Refuse closing a session with an open track that is too short (under 300 blocks) with `05/72/03`, and implement RESERVE TRACK (`53h`). Done when: a test in `TrackAtOnceTests` proves it.
- [ ] Treat SYNCHRONIZE CACHE (`35h`) as a flush of the store and report `02/04/08` (long write in progress) to TEST UNIT READY only while a delayed close is pending under an injected clock. Done when: a test in `TrackAtOnceTests` proves it with a fake `TimeProvider`.
- [ ] Add `tests/Wobble.Tests/TrackAtOnceTests.cs` burning two tracks and one session and reading them back. Done when: it passes and fails if the link blocks are dropped.
- [ ] Commit: `core: write CDs track at once`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~TrackAtOnceTests"` prints `Passed!` with no failed test.

## 7. Session-at-once writing with SEND CUE SHEET

CarbonCD writes cue sheet discs session at once: it sends the layout first with SEND CUE SHEET (`5Dh`), then every sector from the first pregap in one stream.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Parse and check the cue sheet in `src/Wobble.Core/CueSheetCommand.cs`. Done when: an 8-byte-per-entry sheet becomes tracks, indexes and the lead-out.
2. Write under it in `OpticalDrive.Write.cs`. Done when: a stream starting at LBA -150 produces a complete disc whose TOC matches the sheet.

- [ ] Add `CueSheetCommand.Parse(ReadOnlySpan<byte>)` reading each entry's control and ADR, track, index, data form, SCMS and MSF (MMC-5 6.33). Done when: a test parses a five-entry sheet into one data and one audio track.
- [ ] Refuse a sheet whose length is not a multiple of 8, whose first entry is not the lead-in, whose times do not rise, or that arrives while write type is not session at once, with `05/26/00` or `05/2C/00`. Done when: a test in `SessionAtOnceTests` proves it.
- [ ] Accept WRITE (10) from the first address of the sheet and require each write to follow the last. Done when: a write out of sequence is refused with `05/21/02`.
- [ ] Take the block size per region from the entry's data form (`00h` audio 2352, `10h` Mode 1 2048, `11h` Mode 1 2352 and the Mode 2 forms) and store subchannel synthesised from the sheet. Done when: Q read back in a pregap counts down at index 0.
- [ ] Complete the disc in `OpticalDrive.Write.cs` when the address before the lead-out has been written and the cache is synchronised: generate lead-in and lead-out and set the state from the multi-session field. Done when: READ DISC INFORMATION reads complete.
- [ ] Add `tests/Wobble.Tests/SessionAtOnceTests.cs`. Done when: it passes and fails if the sheet's pregap entry is ignored.
- [ ] Commit: `core: write CDs session at once under a cue sheet`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~SessionAtOnceTests"` prints `Passed!` with no failed test.

## 8. Raw disc-at-once writing

CarbonCD's main mode is RAW: it sends every sector of the disc as 2352 raw bytes with 16 or 96 bytes of subchannel, lead-in and lead-out included, and the drive writes exactly what it is given.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Accept write type 3 with data block types 1, 2 and 3 in `OpticalDrive.Write.cs`. Done when: sectors and their subchannel are stored as sent.
2. Derive the TOC from the Q subchannel of the lead-in. Done when: the tracks the software wrote appear in READ TOC.

- [ ] Accept blocks of 2368 bytes (raw with P and Q, type 1) and 2448 bytes (raw with P to W, types 2 packed and 3) starting at the ATIP lead-in start address. Done when: a write at the lead-in start is accepted and one elsewhere on a blank disc is refused with `05/21/02`.
- [ ] Expand a 16-byte P and Q block to the 96-byte channel layout, and de-interleave packed P to W (type 2), in `src/Wobble.Core/Subchannel.cs`. Done when: a round trip test restores the original 96 bytes for each type.
- [ ] Store the sector bytes in `SectorStore` without regenerating EDC, ECC or sync. Done when: a sector written with a deliberately wrong EDC reads back with the same wrong EDC. Cheaper substitute: re-encoding from user data, which would repair sectors a copy-protection test depends on.
- [ ] Decode mode 1 Q frames in the lead-in to build tracks, the lead-out and the disc type (points `A0`, `A1`, `A2`), checking each frame's CRC. Done when: the TOC from a raw burn equals the TOC of the source disc.
- [ ] Finish the disc when the lead-out has been written and the cache synchronised. Done when: the state is `Complete` and READ CD returns the stored subchannel in place of synthesised Q.
- [ ] Add `tests/Wobble.Tests/RawWriteTests.cs` burning a small disc in RAW-16, RAW-96 and packed RAW-96. Done when: all three read back identical and the test fails if the lead-in TOC decode is skipped.
- [ ] Commit: `core: write CDs raw, disc at once`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~RawWriteTests"` prints `Passed!` with no failed test.

## 9. Buffer capacity, speeds and the small write commands

Around the writes, burners poll the buffer, set the speed, calibrate power and rewind. Each is small, and a missing one stops a burn before it starts.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Add READ BUFFER CAPACITY, SEND OPC and REZERO UNIT to `OpticalDrive.Write.cs`. Done when: each answers as MMC-5 lays out.
2. Carry write speed through SET CD SPEED, GET PERFORMANCE and page `2Ah`. Done when: a set speed is read back from all three.

- [ ] Answer READ BUFFER CAPACITY (`5Ch`) with a 12-byte block: buffer length and blank length from a modelled 2 MiB buffer that drains at the write speed under the injected clock. Done when: a test with a fake clock sees the blank length fall after a write and recover later.
- [ ] Accept SEND OPC INFORMATION (`54h`) with DoOpc set on writable media, and refuse it on pressed media with `05/30/05`. Done when: a test in `WriteSupportTests` proves it.
- [ ] Accept REZERO UNIT (`01h`) and reset the current position. Done when: READ SUB-CHANNEL then reports address 0.
- [ ] Store the write speed from SET CD SPEED bytes 4 and 5, clamped to the media's speeds, and report it in mode page `2Ah` and in GET PERFORMANCE type 3 write speed descriptors. Done when: a set speed of 4 234 kB/s reads back from both.
- [ ] Implement VERIFY (10) (`2Fh`) over written data, failing with `03/11/00` only where a sector was never written. Done when: a test in `WriteSupportTests` proves it on a partly written track.
- [ ] Add `tests/Wobble.Tests/WriteSupportTests.cs`. Done when: it passes and fails if the buffer never drains.
- [ ] Commit: `core: add buffer capacity, OPC, rezero and write speeds`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~WriteSupportTests"` prints `Passed!` with no failed test.

## 10. Reads of written and partly written discs

After a burn the software verifies. Reads must come from the sector store, and must fail the way a drive does where nothing was written.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Route `Media.ReadRaw` and `TryReadUserData` to the store for writable media. Done when: written sectors read back in every READ CD selection.
2. Fail unwritten addresses. Done when: a read of an unwritten sector fails with `03/11/00` and a read beyond the last track with `05/21/00`.

- [ ] Read sectors and stored subchannel from `SectorStore` in `Media.ReadRaw` when the media is writable. Done when: READ CD with subchannel selection 1 returns the 96 bytes that were written.
- [ ] Return `03/11/00` for a sector inside a track that was reserved and never written, and `05/21/00` for an address beyond the last track of an appendable disc. Done when: a test in `ReadBack` proves it.
- [ ] Allow reads of closed tracks through `Media.ReadRaw` while the disc is still appendable. Done when: a test reads track 1 before the session is closed.
- [ ] Add the cases to `tests/Wobble.Tests/TrackAtOnceTests.cs` and `RawWriteTests.cs`. Done when: they pass and fail if reads still go to the empty extent list.
- [ ] Commit: `core: read back written discs from the sector store`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~ReadBack"` prints `Passed!` with no failed test.

## 11. BLANK for CD-RW

A rewritable disc is reused. BLANK (`A1h`) is the undo of a burn and the first thing a test of erase support sends.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Blank` to `OpticalDrive.Write.cs`. Done when: blanking types 0 (disc), 1 (minimal) and 5 (unclose last session) work on CD-RW.
2. Refuse it elsewhere. Done when: CD-R and pressed media answer `05/30/05`.

- [ ] Implement blanking type 0 and 1: clear the store and return the disc to `DiscState.Blank` with its ATIP kept. Done when: READ DISC INFORMATION reads blank and READ TOC is refused after it.
- [ ] Implement type 5 (unclose the last session) in `OpticalDrive.Blank`: remove its lead-out and lead-in and make the disc appendable. Done when: the NWA is the end of the last track.
- [ ] Implement type 6 (erase the last session) and refuse types 2, 3 and 4 with `05/24/00`. Done when: a test in `BlankTests` asserts it and fails when the behaviour is removed.
- [ ] Refuse BLANK on CD-R, pressed CD and DVD media with `05/30/05`, and with no disc with `02/3A/01`. Done when: a test in `BlankTests` asserts it and fails when the behaviour is removed.
- [ ] Honour the Immed bit: with it set return at once and report `02/04/07` to TEST UNIT READY until the modelled erase time has passed on the injected clock. Done when: a test in `BlankTests` proves it with a fake clock.
- [ ] Add `tests/Wobble.Tests/BlankTests.cs`. Done when: it passes and fails if the store is not cleared.
- [ ] Commit: `core: blank CD-RW media`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~BlankTests"` prints `Passed!` with no failed test.

## 12. CloneCD images: save written discs and load them

The plan keeps written discs as CCD/IMG/SUB so a test can inspect or reload them, and CloneCD images are also a common source of raw test discs.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Add `src/Wobble.Core/CloneCdImage.cs` with `Save` and `Load`. Done when: a saved disc loads to the same sessions, tracks, sectors and subchannel.
2. Wire both into the host. Done when: `POST /media/save` writes the three files and `PUT /media` opens a `.ccd`.

- [ ] Write the `.ccd` text (`[CloneCD]`, `[Disc]`, `[Session n]`, `[Entry n]` with the full TOC points, `[TRACK n]` with mode and indexes), the `.img` of 2352-byte sectors and the `.sub` of 96-byte de-interleaved subchannel in `CloneCdImage.Save`. Done when: the files open in a second `CloneCdImage.Load`.
- [ ] Write the three files to temporary names and rename them into place. Done when: a test that fails the write half way leaves no `.ccd` behind. Cheaper substitute: writing in place, which can leave a `.ccd` pointing at a short `.img`.
- [ ] Parse the `.ccd` and build a multi-session `Media` over the `.img` and `.sub` in `CloneCdImage.Load`, refusing a missing `.img`, an entry count that does not match `TocEntries`, and a `.sub` of the wrong length. Done when: each refusal names the file.
- [ ] Open `.ccd` in `Media.Open`. Done when: `wobble --image disc.ccd` serves it.
- [ ] Add `POST /media/save {"path": "<folder>/<name>"}` in `src/Wobble.Host/Program.cs` and `SaveMediaAsync` in `WobbleControl`, refusing when there is no disc or the disc is blank with 409. Done when: a test in `CloneCdImageTests` proves it through the control API.
- [ ] Add `tests/Wobble.Tests/CloneCdImageTests.cs` with a save and load round trip of a raw burn. Done when: it passes and fails if the `.sub` is written interleaved.
- [ ] Commit: `core: save and load CloneCD images`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~CloneCdImageTests"` prints `Passed!` with no failed test.

## 13. CarbonCD-style burns through Windows

Slice B is done when CarbonCD-style sequences, raw and cue sheet, burn a disc in tests and the result reads back identical. This runs those sequences through the real initiator.

**Phase:** 2
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add burn helpers to `tests/Wobble.EndToEnd`. Done when: a helper sends the CarbonCD command order for a cue sheet burn and a RAW-96 burn.
2. Assert the read back and the trace. Done when: every sector matches and the trace shows no refused command.

- [ ] Add `tests/Wobble.EndToEnd/CdBurn.cs` sending, through `ScsiDrive`, the order CarbonCD's `MMCWriter` uses: PREVENT ALLOW, MODE SELECT page `05h`, SEND OPC, SEND CUE SHEET or the raw lead-in, WRITE (10) runs, SYNCHRONIZE CACHE, PREVENT ALLOW. Done when: the helper burns the mixed-mode fixture session at once.
- [ ] Add `CdWriteTests.CueSheetBurnReadsBackIdentical`. Done when: READ CD of the whole disc equals the fixture and READ TOC equals its layout.
- [ ] Add `CdWriteTests.Raw96BurnReadsBackIdentical` and a RAW-16 twin. Done when: sectors and subchannel equal what was sent.
- [ ] Add `CdWriteTests.BlankThenBurnAgain` on CD-RW. Done when: the second burn reads back and the first is gone.
- [ ] Add `CdWriteTests.TheSavedImageReloads` saving through `SaveMediaAsync` and reinserting the `.ccd`. Done when: the reloaded disc reads identical.
- [ ] Assert through `WobbleControl.TraceAsync` that Windows and the test sent no command Wobble answered with `05/20/00`. Done when: the assertion holds for each burn.
- [ ] Mark slice B done in `README.md` Status. Done when: the README names track-at-once, session-at-once and raw writing as supported.
- [ ] Commit: `e2e: burn CDs the way CarbonCD does and read them back`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~CdWriteTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## Verification

- The plan's slice B done condition holds: raw and cue sheet burns pass in `tests/Wobble.EndToEnd` and read back identical.
- `dotnet test tests/Wobble.Tests` passes with every write test.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
