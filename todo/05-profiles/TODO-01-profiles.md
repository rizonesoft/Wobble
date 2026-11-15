---
schema_version: 1
id: profiles
domain: 05-profiles
status: active
title: "TODO-01 -- Drive and Media Profiles"
---

# TODO-01 -- Drive and Media Profiles

> **Goal:** A drive and a disc are described by JSON profile files, as the design lays out. The emulator answers from the loaded drive profile, media comes from media profiles, vendor commands and quirks live in the profile, and traces can be saved and compared.

> **Current state:** Measured 2026-11-15. The drive's identity is `DriveIdentity.Default` and every answer is built in code (`src/Wobble.Core/OpticalDrive.Info.cs`). There is no `Wobble.Profiles` project, no `PUT /drive`, no profile file, and the vendor reads CarbonCD sends (`D8h`, `D4h`, `D5h`) are refused.

## Inputs

- `docs/specs/2026-11-15-wobble-design.md`, Profiles (both JSON examples), Components (`Wobble.Profiles`) and Control API (`PUT /drive`).
- `docs/plans/2026-11-15-write-support.md`, slice E and the note that vendor reads arrive with drive profiles.
- `src/Wobble.Core/OpticalDrive.cs`, `OpticalDrive.Info.cs`, `src/Wobble.Host/Program.cs`.

## Outcome

- `PUT /drive` with a profile makes INQUIRY, GET CONFIGURATION, MODE SENSE and speeds answer from the file, with current bits computed from the loaded media.
- Generic profiles ship in the repository, and a saved trace can be compared with another.

**Adjacency:** list=applicable @ §4; document=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); settings=applicable @ §3; reporting=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); notifications=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); permissions=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); audit=applicable @ §6; exchange=applicable @ §1; reverse=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set)

**Adjacency rationale:** Profiles are the records: §4 lists the bundled ones. Loading a drive profile is the setting (§3). The JSON files are the exchange format (§1). Trace files are the audit trail (§6).

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Profile types, JSON loading and validation | D03 T01 §3, D04 T01 §2 | [ ] |
| 2 | §2 | The drive answers from a drive profile | §1 | [ ] |
| 3 | §3 | Loading drive profiles through the control API | §2 | [ ] |
| 4 | §4 | Media profiles and the bundled generic set | §1 | [ ] |
| 5 | §5 | Quirks and vendor commands from the profile | §2 | [ ] |
| 6 | §6 | Trace files and trace comparison | §3 | [ ] |

## 1. Profile types, JSON loading and validation

The profile format is shared by the emulator and the capture tool, so it gets its own project and a strict loader.

**Phase:** 6
**Needs:** none
**Surface:** no

**Build order:**

1. Create `src/Wobble.Profiles` with `DriveProfile` and `MediaProfile`. Done when: both examples in the design load.
2. Validate strictly. Done when: a bad hex string or unknown key fails with the file and the JSON path.

- [ ] Create `src/Wobble.Profiles/Wobble.Profiles.csproj`, add it to `Wobble.slnx` and reference it from `Wobble.Host`. Done when: the solution builds.
- [ ] Add `DriveProfile` (`identity`, `inquiry`, `profiles`, `features`, `modePages` with `default` and `changeable`, `speeds`, `quirks`, `vendorCommands`) and `MediaProfile` (`profile`, `state`, `manufacturerId`, `capacityBlocks`, `writeSpeedsKbps`, `image`) in `src/Wobble.Profiles/`. Done when: the design's two JSON examples deserialize.
- [ ] Keep captured bytes as hex strings in the file and `byte[]` in memory, through a converter in `src/Wobble.Profiles/HexConverter.cs`. Done when: a load and save round trip is byte-identical.
- [ ] Refuse a wrong `kind`, an unknown property, odd-length or non-hex strings, a feature whose length byte disagrees with its data, and a mode page whose length byte disagrees, naming the file and JSON path. Done when: each refusal is tested.
- [ ] Document the format in `docs/profiles.md`. Done when: every property is described with its type and an example.
- [ ] Add `tests/Wobble.Tests/ProfileLoadTests.cs`. Done when: it passes and fails if unknown properties are accepted.
- [ ] Commit: `profiles: add profile types with a strict JSON loader`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~ProfileLoadTests"` prints `Passed!` with no failed test.

## 2. The drive answers from a drive profile

A profile is replayed exactly: the bytes a real drive returned are the bytes Wobble returns, with only the current bits and the current profile computed from the loaded media.

**Phase:** 6
**Needs:** none
**Surface:** no

**Build order:**

1. Give `OpticalDrive` a profile source. Done when: with a profile loaded, INQUIRY, features and mode pages come from it.
2. Keep the built-in generic drive as a profile. Done when: the code-built answers are gone and the generic profile reproduces them.

- [ ] Add `IDriveProfile` in `src/Wobble.Core/DriveProfile.cs` (inquiry bytes, VPD pages, feature payloads, mode pages, speeds per media profile, quirks) implemented by `Wobble.Profiles`. Done when: `Wobble.Core` still has no reference to JSON.
- [ ] Answer INQUIRY and its VPD pages from the profile's bytes in `OpticalDrive.Inquiry`. Done when: a profile's vendor and revision reach an INQUIRY test.
- [ ] Answer GET CONFIGURATION in `OpticalDrive.GetConfiguration` from the profile's features, setting each feature's current bit and the current profile from the loaded media by the rules in MMC-5 5.3. Done when: a captured feature list with a blank CD-R loaded marks the CD write features current and the DVD ones not.
- [ ] Answer MODE SENSE default and changeable values in `OpticalDrive.ModeSense` from the profile and keep current values per drive, with MODE SELECT limited by the changeable mask. Done when: tested.
- [ ] Refuse media the profile's `profiles` list does not include with `05/30/00` when it is inserted and read. Done when: a CD-only drive profile refuses a DVD.
- [ ] Express the generic drive as `profiles/drives/generic-bd-re.json` and load it by default, deleting the hand-built `Features()` and `ModePages()` bodies. Done when: every existing unit test passes against the profile. Cheaper substitute: keeping both paths, which lets them drift.
- [ ] Add `tests/Wobble.Tests/DriveProfileTests.cs`. Done when: it passes and fails if current bits are replayed from the capture.
- [ ] Commit: `core: answer from a drive profile`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~DriveProfileTests"` prints `Passed!` with no failed test.

## 3. Loading drive profiles through the control API

A test picks its drive. `PUT /drive` swaps the profile and the drive announces the change as a real drive that was replaced would.

**Phase:** 6
**Needs:** none
**Surface:** no

**Build order:**

1. Add `PUT /drive` and `GET /drive` to `src/Wobble.Host/Program.cs`. Done when: a profile path or inline JSON loads.
2. Add `--drive` and the `WobbleControl` call. Done when: a test changes drive in one call.

- [ ] Add `PUT /drive` taking `{"profile": "<path>"}` or the profile itself, answering 400 with the loader's message and keeping the old drive on failure. Done when: tested both ways.
- [ ] Raise unit attention `06/29/00` and keep the loaded disc when the profile changes. Done when: the first command after the change is a check condition and the disc is still there.
- [ ] Add `GET /drive` returning the identity and the supported profiles. Done when: tested.
- [ ] Add `--drive <file>` to the command line and `SetDriveAsync` to `src/Wobble.Testing/WobbleControl.cs`. Done when: an end-to-end test loads a profile and Windows shows the new product name.
- [ ] Document the route in `README.md`. Done when: the Control API list names `PUT /drive` and `GET /drive`.
- [ ] Commit: `host: load drive profiles through the control API`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~ControlApiTests"` prints `Passed!` with no failed test.

## 4. Media profiles and the bundled generic set

Odd media is a media profile, not a fault. Media profiles also give tests named discs in place of capacity numbers.

**Phase:** 6
**Needs:** none
**Surface:** no

**Build order:**

1. Insert media from a profile in `PUT /media`. Done when: a media profile yields a disc with its state, manufacturer, capacity and speeds.
2. Ship generic profiles under `profiles/`. Done when: one exists for every supported media kind.

- [ ] Accept `{"profile": "<path>"}` in `PUT /media` and build the `Media` from the profile, opening `image` relative to the profile file. Done when: tested for a blank and a written profile.
- [ ] Add `profiles/media/` with generic CD-R, CD-RW, DVD-R, DVD-RW, DVD+R, DVD+RW, DVD-R DL, DVD+R DL, BD-R and BD-RE profiles. Done when: a test loads every file in the folder.
- [ ] Add `GET /profiles` listing the drive and media profiles found under the profile folder (`--profiles <dir>`, default beside the executable). Done when: the list names each bundled file with its kind.
- [ ] Copy `profiles/` to the build output of `Wobble.Host`. Done when: `dotnet run --project src/Wobble.Host` serves the list.
- [ ] Add `InsertMediaProfileAsync` and `ProfilesAsync` to `src/Wobble.Testing/WobbleControl.cs`. Done when: used by a test.
- [ ] Add `tests/Wobble.Tests/MediaProfileTests.cs`. Done when: it passes and fails if `manufacturerId` does not reach READ DISC STRUCTURE or the ATIP.
- [ ] Commit: `profiles: add media profiles and a generic set`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~MediaProfileTests"` prints `Passed!` with no failed test.

## 5. Quirks and vendor commands from the profile

CarbonCD sends vendor reads (`D8h`, `D4h` in 10 and 12 byte forms, `D5h`) that belong to particular drive makes. They are part of a drive profile, not of the generic drive.

**Phase:** 6
**Needs:** none
**Surface:** no

**Build order:**

1. Add a vendor command table to `IDriveProfile`. Done when: a profile maps an opcode to a built-in behaviour.
2. Implement the CD-DA vendor reads. Done when: `D8h` returns the audio sectors READ CD would.

- [ ] Add `vendorCommands` entries of the form `{"opcode": "0xD8", "behaviour": "read-cdda-d8"}` to `DriveProfile` and dispatch them in `OpticalDrive.Dispatch` before the invalid opcode answer. Done when: a profile without the entry still refuses `D8h`.
- [ ] Implement `read-cdda-d8` (12 byte, LBA and length, subcode selector in byte 10), `read-cdda-d4` and `read-cdda-d5` over `ReadCdSectors`, from the layouts in `D:\CarbonCD\MMCReader.cpp`. Done when: each returns the same bytes as the matching READ CD.
- [ ] Add `quirks.readTocFormat2` (the drive refuses the full TOC when false) and apply it in `ReadToc`. Done when: tested both ways.
- [ ] Refuse an unknown `behaviour` name at profile load. Done when: tested.
- [ ] Add `profiles/drives/generic-plextor-style.json` enabling the vendor reads. Done when: it loads in a test.
- [ ] Add `tests/Wobble.Tests/VendorCommandTests.cs`. Done when: it passes and fails if the vendor table is consulted without a profile entry.
- [ ] Commit: `core: add profile quirks and vendor read commands`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~VendorCommandTests"` prints `Passed!` with no failed test.

## 6. Trace files and trace comparison

A trace captured from real hardware is the reference a profile is judged by. Saving traces and comparing them turns that into a test.

**Phase:** 6
**Needs:** none
**Surface:** no

**Build order:**

1. Define a trace file and write it from the host. Done when: `GET /trace?format=file` returns JSON lines with data digests.
2. Add `wobble trace diff`. Done when: two traces that differ in one answer produce one difference.

- [ ] Add `src/Wobble.Profiles/TraceFile.cs`: one JSON object per line with CDB, lengths, status, sense, time and the data-in bytes as hex up to 4 KiB plus a SHA-256 of the whole. Done when: a save and load round trip is equal.
- [ ] Record data-in digests in `TraceEntry` when the drive is started with `--trace-data`. Done when: the trace of an INQUIRY carries its 36 bytes.
- [ ] Add the `trace diff <a> <b>` verb to `src/Wobble.Host/Program.cs`, comparing by command order and reporting the first differing field of each differing command; exit 0 when equal, 1 when different, 2 on a bad file. Done when: tested for all three.
- [ ] Add `--ignore-time` (default on) and `--ignore <opcode>` options. Done when: polling commands can be excluded.
- [ ] Document the trace file in `docs/profiles.md`. Done when: the document shows a line and each field.
- [ ] Commit: `host: save traces and compare them`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~TraceFileTests"` prints `Passed!` with no failed test.

## Verification

- `dotnet test tests/Wobble.Tests` passes with the generic drive expressed as a profile.
- `wobble trace diff` reports no difference between two runs of the same end-to-end read test.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
