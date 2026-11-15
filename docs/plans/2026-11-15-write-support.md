# Plan: raw CD model, write support, profiles

Date: 2026-11-15. Follows [the design](../specs/2026-11-15-wobble-design.md). Decided with the owner on 2026-11-15: serve both kinds of software, raw CD model first.

## Why the order changed

`D:\CarbonCD`, the software under test, is a raw CD copier. It does not use IMAPI2. A survey of its source shows what it sends:

- Reading: READ CD MSF (0xB9) and READ CD (0xBE) with user data (`0x10`) or the whole 2352-byte sector (`0xF8`), sector types "any" and CD-DA, subchannel selections Q-16 (`0x02`) and raw P to W 96 (`0x01`); READ TOC including format 2 (full TOC) and format 4 (ATIP); vendor reads 0xD8, 0xD4 (10 and 12 byte) and 0xD5; SET CD SPEED.
- Writing: MODE SELECT of the write parameters page (0x05) with write type RAW (RAW-96, packed RAW-96, RAW-16) and session-at-once; SEND CUE SHEET (0x5D); WRITE (10); SYNCHRONIZE CACHE; SEND OPC (0x54); BLANK (0xA1); READ DISC INFORMATION; READ TRACK INFORMATION; PREVENT ALLOW; START STOP; REZERO (0x01).

Step 1 models a disc as 2048-byte blocks from an ISO, which cannot answer any of the raw commands. So the disc model comes first.

## Slices

Each slice ends with unit tests and, where Windows is involved, an end-to-end test, all passing.

### A. Raw CD disc model and raw reads

- Disc model: sessions and tracks (number, session, audio or Mode 1 or Mode 2, control, pregap, start, length), a lead-out per session, and a sector source that yields 2352-byte raw sectors and 96 bytes of subchannel for any address.
- Sources: an ISO as one Mode 1 track, with sync, header, EDC and ECC generated; CUE/BIN (`MODE1/2352`, `MODE1/2048`, `MODE2/2352`, `AUDIO`, pregaps, several files). Subchannel is synthesised (P and Q) when the image has none. CloneCD CCD/IMG/SUB loading follows in slice B, where it is also the format written discs are saved in.
- Commands: READ TOC formats 0, 1, 2 for many tracks; READ CD and READ CD MSF with every header and subchannel selection CarbonCD uses, and sector type checks; READ SUBCHANNEL (0x42); reads of audio tracks through READ (10) refused with 05/64/00; the vendor reads exactly as CarbonCD sends them.
- Done when the step 1 tests still pass and a mixed-mode CUE/BIN reads back byte for byte through raw reads.

### B. CD-R and CD-RW writing

- Blank and appendable media states, ATIP, write parameters page with changeable fields, READ DISC INFORMATION and READ TRACK INFORMATION for blank and incomplete discs, next writable address, READ BUFFER CAPACITY, write speeds.
- Track-at-once WRITE (10), session-at-once with SEND CUE SHEET, RAW-16 and RAW-96 disc-at-once including lead-in and lead-out, SYNCHRONIZE CACHE, CLOSE TRACK SESSION, SEND OPC, BLANK.
- Written discs persist as CCD/IMG/SUB so a test can inspect or reload them.
- Done when CarbonCD-style sequences (raw and cue sheet) burn a disc in tests and the result reads back identical.

### C. IMAPI2 data burning

- CD-R and CD-RW track-at-once as IMAPI2 drives them, then DVD-R, DVD+R, DVD-RW and DVD+RW: incremental and restricted overwrite, FORMAT UNIT, RESERVE TRACK, close and finalise, GET PERFORMANCE and SET STREAMING write speeds.
- Done when `MsftDiscFormat2Data` burns a file system image to each media type end to end on Windows and the files read back.

### D. Fault injection

- The rule engine and `PUT /faults` from the design, covering reads and writes: sense at an LBA, delays, buffer underrun, media removed mid-burn, dropped connection.
- Done when each fault in the design's example is driven from a Windows test and repeats exactly.

### E. Profiles and capture (design step 3)

- `Wobble.Profiles`, `PUT /drive`, drive and media profile files, `wobble-capture`, trace recording and comparison.
- Needs a real drive attached to the Windows machine for the first captures; none is present today.
