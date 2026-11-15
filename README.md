# Wobble

Wobble is a CD, DVD and Blu-ray reader and writer drive emulator, used to test Windows optical disc software.

It runs in WSL as a userspace process and presents an emulated optical drive as an iSCSI target. Windows attaches with its built-in iSCSI initiator, so the software under test sees an ordinary optical drive and talks to it through SPTI and IMAPI2. Wobble implements the SCSI MMC command set and a disc state model, loads drives and media from profile files captured from real hardware, and injects faults on request.

Out of scope: AACS, CSS and BD+, and physical-layer fidelity beyond a simple model.

## Status

- 2026-11-15: the transport is proven. Windows 11 accepts an iSCSI optical drive served from WSL: drive letter, file reads and IMAPI2 all work. See [docs/proof/2026-11-15-iscsi-optical-drive.md](docs/proof/2026-11-15-iscsi-optical-drive.md).
