---
schema_version: 1
id: bd-media
domain: 03-bluray
status: active
title: "TODO-01 -- Blu-ray Read and Write"
---

# TODO-01 -- Blu-ray Read and Write

> **Goal:** Wobble keeps the README's promise of Blu-ray: BD-ROM from an image, BD-R in sequential recording mode and BD-RE, single and dual layer, read and written end to end on Windows. AACS and BD+ stay out of scope.

> **Current state:** Measured 2026-11-15. No Blu-ray support exists. An ISO above the CD size becomes a DVD-ROM whatever its size (`Media.FromImage`, `src/Wobble.Core/Media.cs:120`), `--media` takes `cd` or `dvd`, and no BD profile or feature is listed.

## Inputs

- `docs/specs/2026-11-15-wobble-design.md`, the drive profile example (profiles `43h`, `41h`) and Out of scope (AACS, BD+).
- `README.md`, first paragraph (CD, DVD and Blu-ray).
- The DVD write path of D02 T01: formats, sequential and random writing, the disc file.

## Outcome

- A BD-ROM image mounts and reads in Windows, and BD-R and BD-RE burn through IMAPI2 and read back identical.
- Copy protection commands stay refused, as the design says.

**Adjacency:** list=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); document=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); settings=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); reporting=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); notifications=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); permissions=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); audit=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); exchange=applicable @ D02 T01 §8; reverse=applicable @ §3

**Adjacency rationale:** Written Blu-ray media is saved in the disc file D02 T01 §8 owns. Reverse is the reformat of BD-RE (§3). BD-R cannot be undone.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | BD-ROM media from an image | D02 T01 §1 | [ ] |
| 2 | §2 | BD-R sequential recording | §1, D02 T01 §3 | [ ] |
| 3 | §3 | BD-RE formatting and random writing | §2, D02 T01 §6 | [ ] |
| 4 | §4 | Blu-ray through Windows and IMAPI2 | §3, D02 T01 §11 | [ ] |

## 1. BD-ROM media from an image

Reading comes first: a BD-ROM is a large 2048-byte block image with its own profile, feature and disc information.

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `BdRom` to `MediaKind` and the choice by size or option. Done when: an image above 8.5 GB, or `--media bd`, becomes a BD-ROM.
2. Answer the BD read feature and disc structure. Done when: Windows lists profile `0040h`.

- [ ] Add `MediaKind.BdRom` with profile `0040h` and choose it in `Media.FromImage` for images above 4 173 824 blocks, with `bd` accepted by `--media` and by `PUT /media` `kind`. Done when: a test in `BdReadTests` proves it with a sparse 9 GB file.
- [ ] Add feature `0040h` (BD read, with the class bitmaps for BD-RE, BD-R and BD-ROM) and the BD profiles to the profile list. Done when: a test in `BdReadTests` proves it against MMC-6 5.3.28.
- [ ] Answer READ DISC STRUCTURE with media type 1 (BD) format `00h` (disc information: `DI`, disc type identifier `BDO`, layers, channel bit length). Done when: a test decodes the identifier.
- [ ] Report 1x BD speed as 4 495 kB/s in `MaxReadSpeedKbps`, mode page `2Ah` and GET PERFORMANCE. Done when: a test in `BdReadTests` asserts it and fails when the behaviour is removed.
- [ ] Use READ (12) (`A8h`) and READ CAPACITY correctly beyond 4 GiB of blocks. Done when: a read at block 4 000 000 returns the image's bytes.
- [ ] Add `tests/Wobble.Tests/BdReadTests.cs`. Done when: it passes and fails if the media type field of READ DISC STRUCTURE is ignored.
- [ ] Commit: `core: read BD-ROM media`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~BdReadTests"` prints `Passed!` with no failed test.

## 2. BD-R sequential recording

BD-R in sequential recording mode behaves like DVD+R with larger numbers: tracks, sessions, a next writable address and a close. Pseudo overwrite is not offered.

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `BdR` media and its write path. Done when: a sequential burn closes and reads back.
2. Report BD write capability. Done when: feature `0041h` is current with BD-R loaded.

- [ ] Add `MediaKind.BdR` (profile `0041h`) with capacities 12 219 392 (25 GB) and 24 438 784 (50 GB) accepted by `PUT /media` `blank` as `bd-r` and `bd-r-dl`. Done when: `GET /status` reports them.
- [ ] Add feature `0041h` (BD write) and mark `0021h` current for BD-R. Done when: a test in `BdRTests` asserts it and fails when the behaviour is removed.
- [ ] Write sequentially at the NWA with WRITE (10) and WRITE (12), with RESERVE TRACK, READ TRACK INFORMATION and CLOSE TRACK SESSION functions 1, 2 and 6. Done when: a finalised BD-R refuses further writes with `05/30/05`.
- [ ] Answer READ FORMAT CAPACITIES for blank BD-R with types `00h` and `32h`, accept FORMAT UNIT type `00h` as sequential mode without spares, and refuse `32h` (pseudo overwrite) with `05/26/00`. Done when: a test in `BdRTests` proves it, and `README.md` Limits says pseudo overwrite is not offered.
- [ ] Add `tests/Wobble.Tests/BdRTests.cs`. Done when: it passes and fails if the NWA does not advance.
- [ ] Commit: `core: write BD-R in sequential recording mode`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~BdRTests"` prints `Passed!` with no failed test.

## 3. BD-RE formatting and random writing

BD-RE is formatted once and then written anywhere, like DVD+RW without the background format.

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `BdRe` media with FORMAT UNIT types `30h` and `31h`. Done when: a formatted disc reports its capacity.
2. Write at random. Done when: writes anywhere read back and a reformat empties the disc.

- [ ] Add `MediaKind.BdRe` (profile `0043h`), accepted as `bd-re` and `bd-re-dl`. Done when: a test in `BdReTests` asserts it and fails when the behaviour is removed.
- [ ] List format types `00h`, `30h` (with spare areas) and `31h` (without) in READ FORMAT CAPACITIES and accept them in FORMAT UNIT, reducing the capacity by the spare size for `30h`. Done when: the formatted capacity differs between the two types as MMC-6 says.
- [ ] Write at any address of a formatted BD-RE and refuse writes to an unformatted one with `05/30/10`. Done when: a test in `BdReTests` asserts it and fails when the behaviour is removed.
- [ ] Clear the disc on a new FORMAT UNIT in `OpticalDrive.Format.cs`. Done when: previously written blocks read as zeros. Cheaper substitute: keeping old data after a reformat.
- [ ] Answer READ DISC STRUCTURE BD formats `09h` (cartridge status: none) and `0Ah` (spare area information). Done when: free spare blocks are reported for a `30h` format.
- [ ] Add `tests/Wobble.Tests/BdReTests.cs`. Done when: it passes and fails if spares are not subtracted.
- [ ] Commit: `core: format and write BD-RE`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~BdReTests"` prints `Passed!` with no failed test.

## 4. Blu-ray through Windows and IMAPI2

The same two checks as DVD: the file system mounts and reads, and IMAPI2 burns.

**Phase:** 4
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add BD cases to `tests/Wobble.EndToEnd`. Done when: BD-ROM mounts and BD-R and BD-RE burn.
2. Keep copy protection refused. Done when: REPORT KEY for AACS answers `05/20/00` and the test expects it.

- [ ] Add `BdTests.ABdRomMountsAndReads` with a small UDF image inserted as `bd`. Done when: a file's hash equals the source.
- [ ] Add `BdTests.ImapiBurnsBdR` and `ImapiBurnsBdRe` through `Imapi.BurnAsync`. Done when: the burned files read back with equal hashes.
- [ ] Add a BD-RE case that burns, erases through `Imapi.EraseAsync` or a reformat, and burns again. Done when: it passes.
- [ ] Assert that the only refused commands in the trace are the copy protection queries the design puts out of scope. Done when: the assertion names `A3h` and `A4h` and holds.
- [ ] Update `README.md` Status and Limits for Blu-ray. Done when: the README lists BD-ROM, BD-R and BD-RE with their limits.
- [ ] Commit: `e2e: read and burn Blu-ray through Windows`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~BdTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## Verification

- `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~BdTests"` passes on the attached host.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
