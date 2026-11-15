---
schema_version: 1
id: dvd-media
domain: 02-dvd
status: active
title: "TODO-01 -- DVD Read and Write"
---

# TODO-01 -- DVD Read and Write

> **Goal:** Slice C of the plan is finished: Wobble presents DVD-R, DVD-RW, DVD+R and DVD+RW media, single and dual layer where the format has it, in blank, appendable and finalised states; the SPTI sequences of a DVD burner and IMAPI2 both burn each type end to end on Windows, and the result reads back identical.

> **Current state:** Measured 2026-11-15. A DVD exists only as a finalised DVD-ROM made from an ISO (`MediaKind.DvdRom`, profile `0010h`). `OpticalDrive.ReadDiscStructure` answers formats `00h`, `01h` and `04h` for it. There is no recordable DVD profile or feature, no FORMAT UNIT, READ FORMAT CAPACITIES, SET STREAMING or write speed performance data, and IMAPI2 has only been used to list the drive (`tests/Wobble.EndToEnd/ReadTests.cs:38`).

## Inputs

- `docs/plans/2026-11-15-write-support.md`, slice C.
- `docs/specs/2026-11-15-wobble-design.md`, Profiles (media profiles and states).
- The CD write path of D01 T02: `SectorStore`, `OpticalDrive.Write.cs`, `WriteParameters`, the control API blank insert.
- `src/Wobble.Testing/WindowsDrive.cs` (IMAPI2 through COM).

## Outcome

- Each DVD media type can be inserted blank, written, closed and read back, through SPTI and through IMAPI2.
- DVD-RW and DVD+RW can be formatted and erased, and written discs save and reload.

**Adjacency:** list=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); document=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); settings=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); reporting=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); notifications=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); permissions=applicable @ §3; audit=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); exchange=applicable @ §8; reverse=applicable @ §5

**Adjacency rationale:** Exchange is the disc file a written DVD is saved as (§8). Reverse is the blank and format of rewritable media (§5 and §6). Permissions are the refusals of writes the loaded media cannot take (§3).

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Recordable DVD media kinds, profiles and features | D01 T02 §3 | [ ] |
| 2 | §2 | READ DISC STRUCTURE for recordable DVD | §1 | [ ] |
| 3 | §3 | DVD-R and DVD+R sequential writing | §1, D01 T02 §6 | [ ] |
| 4 | §4 | READ FORMAT CAPACITIES and FORMAT UNIT | §1 | [ ] |
| 5 | §5 | DVD-RW writing and blanking | §3, §4 | [ ] |
| 6 | §6 | DVD+RW random writing | §4, §5 | [ ] |
| 7 | §7 | Write speed performance data and SET STREAMING | §6 | [ ] |
| 8 | §8 | A disc file for written DVD and Blu-ray media | §6 | [ ] |
| 9 | §9 | Dual-layer DVD media | §3 | [ ] |
| 10 | §10 | DVD burns through SPTI on Windows | §7, D01 T01 §6 | [ ] |
| 11 | §11 | CD and DVD burns through IMAPI2 | §10, D01 T02 §13 | [ ] |

## 1. Recordable DVD media kinds, profiles and features

Everything a burner does with a DVD starts from the profile and feature list. The media kinds and their current profiles come first, so later sections only add behaviour.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Add the DVD kinds to `MediaKind` and `Media.Profile` in `src/Wobble.Core/Media.cs`. Done when: each kind maps to its MMC profile.
2. Add the DVD write features to `Features()`. Done when: each is current only for the media it applies to.

- [ ] Add `DvdR`, `DvdRwRestricted`, `DvdRwSequential`, `DvdPlusR`, `DvdPlusRw`, `DvdRDualLayer` and `DvdPlusRDualLayer` to `MediaKind`, with profiles `0011h`, `0013h`, `0014h`, `001Bh`, `001Ah`, `0015h` and `002Bh`. Done when: a unit test checks each mapping.
- [ ] Store 2048-byte blocks for DVD in `SectorStore` through a block-size parameter. Done when: a 4.7 GB disc's store is sparse and a test writes block 2 295 103.
- [ ] Add the profiles to the profile list feature and features `0020h` (random writable), `0023h` (formattable), `0026h` (restricted overwrite), `002Ah` (DVD+RW), `002Bh` (DVD+R), `002Ch` (rigid restricted overwrite), `002Fh` (DVD-R/-RW write) and `003Bh` (DVD+R dual layer) in `OpticalDrive.Info.cs` per MMC-5 5.3. Done when: a test per media kind compares the current feature set.
- [ ] Accept the DVD kinds in `PUT /media` `blank` (`dvd-r`, `dvd-rw`, `dvd+r`, `dvd+rw`, `dvd-r-dl`, `dvd+r-dl`) with default capacities 2 298 496, 2 295 104 and the dual layer sizes, in `src/Wobble.Host/Program.cs`. Done when: `GET /status` reports the kind and capacity.
- [ ] Set the DVD write bits in mode page `2Ah`. Done when: bytes 2 and 3 show DVD-R and DVD-RAM-free DVD write support.
- [ ] Add `tests/Wobble.Tests/DvdMediaTests.cs`. Done when: it passes and fails when a profile is left out of the list.
- [ ] Commit: `core: add recordable DVD media kinds, profiles and features`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DvdMediaTests"` prints `Passed!` with no failed test.

## 2. READ DISC STRUCTURE for recordable DVD

Burners read the physical format, the manufacturer's pre-recorded information and the ADIP to pick a write strategy. The current answers describe a pressed disc only.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Extend `ReadDiscStructure` in `OpticalDrive.Info.cs`. Done when: formats `00h`, `04h`, `0Eh`, `0Fh`, `11h` and `FFh` answer for the media they belong to.
2. Refuse formats the media does not have. Done when: ADIP on DVD-R answers `05/24/00`.

- [ ] Answer format `00h` with the book type and part version of each kind (DVD-R `2h`, DVD-RW `3h`, DVD+RW `9h`, DVD+R `Ah`, DVD+R DL `Eh`), layer count and track path, and the data area end from the written or possible extent. Done when: a test decodes the book type for every kind.
- [ ] Answer format `0Eh` (pre-recorded information in the lead-in) for DVD-R and DVD-RW with the manufacturer id from the media model. Done when: the id `MCC 03RG20` round trips.
- [ ] Answer format `11h` (ADIP) for DVD+R and DVD+RW with the disc manufacturer and media type id. Done when: `MCC` and `004` round trip.
- [ ] Answer format `0Fh` (unique disc identifier) and `FFh` (the list of supported formats with their readable flags). Done when: the list names exactly the formats that answer for the loaded disc.
- [ ] Refuse a format the loaded media does not have with `05/24/00` and a layer number above the disc's with `05/24/00`. Done when: a test in `DvdMediaTests` asserts it and fails when the behaviour is removed.
- [ ] Add the cases to `tests/Wobble.Tests/DvdMediaTests.cs`. Done when: they pass and fail if the book type is hard-coded to DVD-ROM.
- [ ] Commit: `core: answer READ DISC STRUCTURE for recordable DVD`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DvdMediaTests"` prints `Passed!` with no failed test.

## 3. DVD-R and DVD+R sequential writing

Write-once DVD is written front to back: disc at once or incrementally on DVD-R, sequentially with sessions on DVD+R. Both reuse the CD write path's address checks and closing logic.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Extend `OpticalDrive.Write.cs` for 2048-byte DVD writes. Done when: a write at the NWA is stored and a write elsewhere is refused.
2. Add the DVD closing functions. Done when: DVD-R and DVD+R discs can be left appendable or finalised.

- [ ] Accept WRITE (10) and WRITE (12) of 2048-byte blocks on `DvdR` and `DvdPlusR` at the next writable address, refusing others with `05/21/02`. Done when: a test in `DvdWriteOnceTests` asserts it and fails when the behaviour is removed.
- [ ] Honour write type on DVD-R from mode page `05h`: disc at once (type 2) needs the track reserved or the whole image length, incremental (type 0) writes packets with no link loss at the model's level. Done when: both burn in a test.
- [ ] Ignore mode page `05h` on DVD+R, as the format defines no write parameters. Done when: a burn succeeds with the page left at its default.
- [ ] Implement RESERVE TRACK (`53h`) for DVD-R and READ TRACK INFORMATION with NWA, free blocks and last recorded address. Done when: a test in `DvdWriteOnceTests` asserts it and fails when the behaviour is removed.
- [ ] Implement CLOSE TRACK SESSION for DVD-R (functions 1 and 2) and DVD+R (1 close track, 2 close session, 5 and 6 finalise). Done when: a finalised disc reads disc status complete and refuses a further write with `05/30/05`.
- [ ] Report sessions and borders in `OpticalDrive.ReadToc` formats 0 and 1 and `ReadDiscInformation` for multi-session DVD+R. Done when: a two-session disc lists both.
- [ ] Add `tests/Wobble.Tests/DvdWriteOnceTests.cs`. Done when: it passes and fails if finalise leaves the disc appendable.
- [ ] Commit: `core: write DVD-R and DVD+R`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DvdWriteOnceTests"` prints `Passed!` with no failed test.

## 4. READ FORMAT CAPACITIES and FORMAT UNIT

Rewritable DVD must be formatted before random writing, and software asks what formats the disc takes first.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Add `ReadFormatCapacities` (`23h`) and `FormatUnit` (`04h`) in `src/Wobble.Core/OpticalDrive.Format.cs`. Done when: each rewritable kind lists and accepts its format types.
2. Model format progress on the injected clock. Done when: a background format reports progress in REQUEST SENSE.

- [ ] Answer READ FORMAT CAPACITIES with the current capacity descriptor (unformatted, formatted or no media) and the formattable descriptors: type `00h` and `10h`/`15h` for DVD-RW, `26h` for DVD+RW. Done when: a test decodes each list.
- [ ] Parse the FORMAT UNIT parameter list (header, one format descriptor) and refuse a missing FmtData bit, a format code other than 1, or a type the media does not list, with `05/26/00`. Done when: a test in `FormatTests` proves it.
- [ ] Format DVD-RW full (`10h`) and quick (`15h`) into restricted overwrite, and DVD+RW (`26h`) with background formatting. Done when: READ FORMAT CAPACITIES then reports formatted media and the profile is the formatted one.
- [ ] Report `02/04/04` (format in progress) with a progress indication in the sense-key-specific bytes while the modelled format runs with the Immed bit. Done when: a fake-clock test sees progress rise to complete.
- [ ] Refuse FORMAT UNIT on write-once and pressed media with `05/30/06`. Done when: a test in `FormatTests` asserts it and fails when the behaviour is removed.
- [ ] Add `tests/Wobble.Tests/FormatTests.cs`. Done when: it passes and fails if the capacity descriptor stays unformatted.
- [ ] Commit: `core: add READ FORMAT CAPACITIES and FORMAT UNIT`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~FormatTests"` prints `Passed!` with no failed test.

## 5. DVD-RW writing and blanking

DVD-RW has two personalities: sequential, like DVD-R, and restricted overwrite after a format. BLANK returns it to empty.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Write both DVD-RW profiles in `OpticalDrive.Write.cs`. Done when: sequential and restricted overwrite burns read back.
2. Extend `OpticalDrive.Blank` to DVD-RW. Done when: a blanked disc is sequential and empty.

- [ ] Write `DvdRwSequential` as DVD-R, including close and finalise. Done when: the DVD-R tests run against it.
- [ ] Write `DvdRwRestricted` at any address that is a multiple of 16 blocks inside the formatted area, and refuse others with `05/21/02`. Done when: a test in `DvdRwTests` proves it with an aligned and an unaligned write.
- [ ] Implement CLOSE TRACK SESSION (`5Bh`) function 2 on restricted overwrite to close the border. Done when: READ DISC INFORMATION reports a complete session.
- [ ] Blank DVD-RW with BLANK types 0 and 1, returning it to `DvdRwSequential` and state blank. Done when: a test in `DvdRwTests` proves it, and BLANK on DVD+RW is refused with `05/30/05` because that format is erased by formatting.
- [ ] Add `tests/Wobble.Tests/DvdRwTests.cs`. Done when: it passes and fails if the alignment check is removed.
- [ ] Commit: `core: write and blank DVD-RW`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DvdRwTests"` prints `Passed!` with no failed test.

## 6. DVD+RW random writing

DVD+RW is written anywhere once formatting has started, and is never finalised. Its close command only stops the background format.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Write `DvdPlusRw` in `OpticalDrive.Write.cs`. Done when: writes at arbitrary addresses read back.
2. Implement its close functions. Done when: function 0 stops the background format and the disc stays writable.

- [ ] Accept WRITE (10) and WRITE (12) at any address inside the formatted capacity of `DvdPlusRw`. Done when: a test writes block 1 000 000 first and block 0 second and reads both.
- [ ] Refuse a write to unformatted DVD+RW with `05/30/10` (medium not formatted). Done when: a test in `DvdPlusRwTests` asserts it and fails when the behaviour is removed.
- [ ] Implement CLOSE TRACK SESSION (`5Bh`) functions 0 (stop background format), 2 and 3 (compatibility close) for DVD+RW. Done when: after function 0 the format progress stops and writes still succeed.
- [ ] Report one track covering the formatted capacity in `ReadTrackInformation` and `ReadToc`. Done when: a test reads track 1 with the formatted capacity as its length.
- [ ] Add `tests/Wobble.Tests/DvdPlusRwTests.cs`. Done when: it passes and fails if a second write to the same block does not replace the first.
- [ ] Commit: `core: write DVD+RW at random`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DvdPlusRwTests"` prints `Passed!` with no failed test.

## 7. Write speed performance data and SET STREAMING

IMAPI2 reads the write speed descriptors and sets the speed with SET STREAMING before it burns. Without them it reports no supported speeds and refuses to start.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Extend `GetPerformance` in `OpticalDrive.Info.cs`. Done when: type `03h` returns one descriptor per media write speed.
2. Add `SetStreaming` (`B6h`). Done when: the chosen speed is reported back as current.

- [ ] Return write speed descriptors (GET PERFORMANCE type `03h`) with the media's speeds, the end LBA and the current-speed flag. Done when: a blank DVD+R lists 5 540, 11 080 and 22 160 kB/s.
- [ ] Return nominal performance (type `00h`) for write as well as read when the Write bit is set. Done when: a test in `StreamingTests` asserts it and fails when the behaviour is removed.
- [ ] Parse the SET STREAMING performance descriptor (start and end LBA, read and write size and time) and store the write speed, refusing a descriptor shorter than 28 bytes with `05/1A/00`. Done when: the speed reads back from GET PERFORMANCE and mode page `2Ah`.
- [ ] Accept the streaming bit of WRITE (12) (`AAh`) and READ (12) (`A8h`). Done when: a write with it set succeeds.
- [ ] Add `tests/Wobble.Tests/StreamingTests.cs`. Done when: it passes and fails if SET STREAMING is accepted but not stored.
- [ ] Commit: `core: report write speeds and accept SET STREAMING`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~StreamingTests"` prints `Passed!` with no failed test.

## 8. A disc file for written DVD and Blu-ray media

CloneCD images describe CDs only. The design names a Wobble disc file (sessions, tracks and data) for everything else, so a written DVD can be saved, inspected and inserted again.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Add `src/Wobble.Core/DiscFile.cs` with `Save` and `Load`. Done when: a written DVD round trips with its kind, state, sessions, tracks and blocks.
2. Wire it into `POST /media/save` and `Media.Open`. Done when: a `.wobble` file inserts through the control API.

- [ ] Define the format in `docs/disc-file.md`: a JSON header (`kind`, `state`, `capacityBlocks`, `manufacturerId`, `sessions`, `tracks`, `format`) beside a `.bin` of 2048-byte blocks with a written-range list. Done when: the document states every field and its type.
- [ ] Write the header and data through temporary names and a rename in `DiscFile.Save`. Done when: a failed save leaves no header. Cheaper substitute: writing in place.
- [ ] Load it in `DiscFile.Load`, refusing an unknown `kind`, a range beyond capacity and a missing `.bin`, each naming the file and field. Done when: a test in `DiscFileTests` asserts it and fails when the behaviour is removed.
- [ ] Open `.wobble` in `Media.Open` and save non-CD media through `POST /media/save`. Done when: a control API test saves a DVD+R and reinserts it appendable.
- [ ] Export a finalised single-track disc as a plain ISO with `POST /media/save {"format": "iso"}`, refusing other discs with 409. Done when: the ISO's bytes equal what was written.
- [ ] Add `tests/Wobble.Tests/DiscFileTests.cs`. Done when: it passes and fails if the written-range list is dropped.
- [ ] Commit: `core: save and load written DVD media as a disc file`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DiscFileTests"` prints `Passed!` with no failed test.

## 9. Dual-layer DVD media

DVD-R DL and DVD+R DL change the capacity, the physical format answer and the layer break, which burners of large images set and read.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Model two layers in `Media`. Done when: capacity, layer 0 size and the layer break are reported.
2. Accept the layer break commands. Done when: SEND DISC STRUCTURE sets the layer 0 size on a blank disc.

- [ ] Add `LayerBreak` to writable DVD media and report two layers and opposite track path in READ DISC STRUCTURE format `00h`. Done when: a test in `DualLayerTests` proves it for both dual layer kinds.
- [ ] Answer READ DISC STRUCTURE format `20h` (layer capacity) and accept SEND DISC STRUCTURE (`BFh`) format `20h` on blank DVD+R DL and `23h` on DVD-R DL. Done when: a set layer 0 size reads back, and a set on a written disc is refused with `05/2C/00`.
- [ ] Report feature `003Bh` and `0033h` (layer jump recording) current as the media requires. Done when: a test in `DualLayerTests` asserts it and fails when the behaviour is removed.
- [ ] Write across the layer break as one address space in `OpticalDrive.Write.cs`. Done when: a burn that crosses it reads back identical.
- [ ] Add `tests/Wobble.Tests/DualLayerTests.cs`. Done when: it passes and fails if the layer count is reported as one.
- [ ] Commit: `core: add dual-layer DVD media`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DualLayerTests"` prints `Passed!` with no failed test.

## 10. DVD burns through SPTI on Windows

The first of the plan's two checks of slice C: the command sequences a DVD-capable CarbonCD would send, through the real initiator.

**Phase:** 3
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add `tests/Wobble.EndToEnd/DvdBurn.cs` with the sequence per media type. Done when: each helper burns an image.
2. Read each back. Done when: the bytes equal the image.

- [ ] Add `DvdBurn.WriteImage(ScsiDrive, Stream)` sending GET CONFIGURATION, READ DISC INFORMATION, MODE SELECT where the media needs it, RESERVE TRACK for DVD-R, WRITE (10) runs of 32 blocks, SYNCHRONIZE CACHE and CLOSE TRACK SESSION. Done when: it burns a 20 MB ISO to DVD-R.
- [ ] Add `DvdWriteTests` cases for DVD-R, DVD+R, DVD-RW sequential, DVD-RW restricted overwrite after FORMAT UNIT, and DVD+RW. Done when: each reads back identical through READ (10).
- [ ] Add `DvdWriteTests.AFinalisedDvdRMounts`: the disc mounts in Windows and its files read through the file system. Done when: a file's hash equals the source.
- [ ] Add `DvdWriteTests.BlankThenBurnAgain` on DVD-RW. Done when: the second burn reads back identical and the first image is gone.
- [ ] Assert no command in the trace was answered with `05/20/00`. Done when: the assertion holds for every case.
- [ ] Commit: `e2e: burn each DVD media type through SPTI`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~DvdWriteTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## 11. CD and DVD burns through IMAPI2

The second check: IMAPI2 is Windows' own burner and an implementation Wobble's authors did not write, so it finds what the SPTI tests assumed.

**Phase:** 3
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add IMAPI2 burn helpers to `src/Wobble.Testing`. Done when: `MsftDiscFormat2Data` writes a folder to the Wobble drive.
2. Burn every CD and DVD type. Done when: each reads back identical and erasable media erases.

- [ ] Add `Imapi.BurnAsync(WindowsDrive, string folder)` in `src/Wobble.Testing/Imapi.cs` building a file system image with `MsftFileSystemImage` and writing it with `MsftDiscFormat2Data`, on an STA thread. Done when: it burns to a blank CD-R.
- [ ] Add `Imapi.EraseAsync` over `MsftDiscFormat2Erase`. Done when: it blanks a written CD-RW.
- [ ] Add `ImapiTests` cases for CD-R, CD-RW, DVD-R, DVD+R, DVD-RW and DVD+RW. Done when: `IsCurrentMediaSupported` is true for each and the burned files read back with equal hashes.
- [ ] Fix at its cause each command IMAPI2 sends that Wobble refuses, adding a unit test for it in `tests/Wobble.Tests`. Done when: the trace of each burn holds no `05/20/00` or `05/24/00` that IMAPI2 did not expect. Cheaper substitute: accepting the command with an empty answer.
- [ ] Add `ImapiTests.ASecondSessionKeepsTheFirst`: burn, then burn again with the first session imported. Done when: both sets of files are present.
- [ ] Mark slice C done in `README.md` Status and remove the no-blank-media limit. Done when: the README lists every supported CD and DVD media type.
- [ ] Commit: `e2e: burn CDs and DVDs through IMAPI2`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~ImapiTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## Verification

- The plan's slice C done condition holds: a DVD image burns to each media type end to end on Windows and reads back identical, through SPTI and through IMAPI2.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
