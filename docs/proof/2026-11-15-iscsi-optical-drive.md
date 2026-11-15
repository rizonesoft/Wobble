# Proof: Windows accepts an iSCSI optical drive

Date: 2026-11-15. Result: **pass**. Wobble stays on the iSCSI route; the Storport miniport remains the fallback only.

## Setup

- Host: Windows 11 Business 10.0.26300, WSL 3.0.5.0, kernel 6.18.58.1-microsoft-standard-WSL2, Ubuntu 26.04.1.
- Target: tgt 1.0.85 (`tgtd`), run as an ordinary user from an extracted package and later from a source build. No root, no kernel modules, no installed packages.
- Device: one LUN of tgt device type `cd` (SCSI peripheral type 5, tgt's `mmc` module), backed by a 3.4 MB ISO 9660 + Joliet + Rock Ridge image made with xorriso, volume label `WOBBLE_PROOF`, holding `HELLO.TXT` (44 bytes) and `sub/RANDOM.BIN` (3,000,000 random bytes).
- Initiator: the built-in Microsoft iSCSI initiator (`MSiSCSI`), portal `127.0.0.1:3260`, target `iqn.2026-11.test.wobble:proof`, non-persistent session.

## How Windows reaches the target

WSL runs in the default NAT networking mode (`.wslconfig` has no `networkingMode`, so not mirrored). Windows reached the listening port both ways: through localhost forwarding at `127.0.0.1:3260` and directly at the WSL address `172.26.251.0:3260`. The proof used `127.0.0.1`, which survives the WSL address changing between boots. `Get-IscsiConnection` reports target address `127.0.0.1`, port 3260.

## Results

| Check | Result | Evidence |
| --- | --- | --- |
| Drive letter | Pass | `E:`, `IET VIRTUAL-CDROM SCSI CdRom Device`, PnP ID `SCSI\CDROM&VEN_IET&PROD_VIRTUAL-CDROM\...`, class CDROM, status OK, media loaded |
| Files can be read | Pass, after a one-line tgt fix (below) | Mounted as CDFS, label `WOBBLE_PROOF`; SHA-256 of both files read from `E:` equals the source files (`6fc5012f...3cd2f` and `2e4121c6...bbca5`) |
| IMAPI2 lists the drive | Pass | `MsftDiscMaster2`: `IsSupportedEnvironment` true, count 1; `MsftDiscRecorder2` reports vendor `IET`, product `VIRTUAL-CDROM`, revision `0001`, mount `E:\`, current profile 0x10 (DVD-ROM), supported profiles 0x10 and 0x1B; `MsftDiscFormat2Data.IsRecorderSupported` true |

## The two assumptions

1. **tgt can present a type 5 device suitable for the proof: true only with a patch.** Stock tgt attaches, gets a drive letter and is listed by IMAPI2, and raw sector reads through `\\.\E:` return the right bytes. But CDFS refused to mount it (error 1393, "the disk structure is corrupted and unreadable"). Cause: `mmc_read_toc` in `usr/mmc.c` reports the response length as the TOC data length field without the two bytes of the field itself, so every READ TOC answer is two bytes short. The lead-out address arrived as zero and the session answer was 10 bytes instead of 12. `tools/proof/tgt-read-toc-length.patch` fixes it, and with it the disc mounts and reads. The same ISO mounts natively in Windows (`Mount-DiskImage`), which ruled the image out.
2. **Windows mounts it as a normal optical drive: true.** `cdrom.sys`, CDFS and IMAPI2 treat the iSCSI device as an ordinary optical drive.

## What this means for the design

- Windows is strict about MMC answers even for plain reading: a two-byte length error in one command was enough to lose the file system. Wobble's value is in exact responses, so it needs byte-level tests of every response against the MMC length rules.
- tgt's MMC emulation is not a base to build on. Besides the TOC length it showed: the last sector of the backing file unreadable (sector 1648 of a 1649-sector image), READ TOC formats 2 to 5 unimplemented (Windows asks for format 2), a hardcoded MSF lead-out, and it claims DVD+R (0x1B) support for a read-only image.
- An unprivileged userspace process is enough. The kernel target modules (`target_core_user`, `iscsi_target_mod`) were not needed and remain unverified.

## Windows requirements found

- `MSiSCSI` was stopped with manual start type. Starting it and connecting needed one elevated PowerShell (a UAC prompt). The service has been left running with its start type unchanged, so it stops at the next reboot.
- Reading files, raw sector reads, `IOCTL_CDROM_READ_TOC_EX` and IMAPI2 all worked without elevation.
- When the target restarted under the same name, the initiator reconnected by itself within about 15 seconds and remounted the disc.

## Reproduce

1. WSL: `tools/proof/run-target.sh <image.iso>` (clones tgt 1.0.85, applies the patch, builds, serves the image).
2. Windows, elevated: `tools/proof/attach.ps1`.
3. Windows: check `Get-Volume`, read the files, and enumerate `IMAPI2.MsftDiscMaster2`. `tools/proof/read-toc.ps1` prints the TOC and session answers Windows receives.
