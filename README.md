# Wobble

Wobble is a CD, DVD and Blu-ray reader and writer drive emulator, used to test Windows optical disc software.

It is a userspace process that presents an emulated optical drive as an iSCSI target. Windows attaches with its built-in iSCSI initiator, so the software under test sees an ordinary optical drive and talks to it through SPTI and IMAPI2. Wobble implements the SCSI MMC command set and a disc state model, will load drives and media from profile files captured from real hardware, and will inject faults on request.

Out of scope: AACS, CSS and BD+, and physical-layer fidelity beyond a simple model.

## Status

- Transport proven (2026-11-15): Windows 11 accepts an iSCSI optical drive. See [the proof](docs/proof/2026-11-15-iscsi-optical-drive.md).
- Design: [docs/specs/2026-11-15-wobble-design.md](docs/specs/2026-11-15-wobble-design.md).
- Step 1, read support: done. Wobble serves an ISO as a finalised CD-ROM or DVD-ROM data disc; Windows gives it a drive letter, mounts and reads it, and IMAPI2 lists the drive.
- Step 2 (write support) and step 3 (profiles and the capture tool) are not started.

## Layout

| Path | What |
| --- | --- |
| `src/Wobble.Core` | MMC command set and disc state model. No transport, no sockets. |
| `src/Wobble.Iscsi` | Userspace iSCSI target. |
| `src/Wobble.Host` | `wobble`, the executable: target plus an HTTP control API. |
| `src/Wobble.Testing` | Library for Windows tests: runs the emulator, finds the drive, calls the control API. |
| `tests/Wobble.Tests` | Unit tests: byte-exact MMC responses and the iSCSI target over loopback. |
| `tests/Wobble.EndToEnd` | Windows tests through the real initiator, CDFS and IMAPI2. |
| `tools/attach.ps1` | Connects the Windows iSCSI initiator to Wobble. |

## Run

Requires the .NET 11 SDK.

```powershell
dotnet run --project src/Wobble.Host -- --iso C:\path\disc.iso
```

Options: `--iso <path>`, `--media cd|dvd` (default: by image size), `--port 3260`, `--control-port 3261`, `--bind 127.0.0.1`, `--iqn iqn.2026-11.test.wobble:drive0`.

Then, once per boot, from an elevated PowerShell while Wobble is running:

```powershell
tools\attach.ps1
```

It starts the Microsoft iSCSI initiator service and connects to the target. The drive appears as `WOBBLE VIRTUAL DVD-ROM`. The initiator reconnects by itself when Wobble restarts, within about 15 seconds.

## Control API

HTTP JSON on `http://127.0.0.1:3261`:

- `GET /status`: target name, port and loaded media.
- `PUT /media`: `{"image": "<path>", "kind": "cd"|"dvd"|null}` inserts a disc; `{"image": null}` removes it.
- `GET /trace`: every SCSI command received, with CDB, data lengths, status and sense.
- `POST /reset`: remove the disc and clear the trace.

## Test

```powershell
dotnet test tests/Wobble.Tests        # anywhere
dotnet test tests/Wobble.EndToEnd     # Windows, after tools\attach.ps1
```

The end-to-end tests start `wobble` themselves on ports 3260 and 3261, so stop any other instance first.

## Limits of step 1

- One finalised single-session, single-track data disc; no audio tracks, no multisession, no blank or rewritable media.
- READ CD returns user data only (no headers, subchannel or C2).
- Mode pages are fixed: MODE SELECT is accepted and changes nothing.
- REPORT KEY and other copy protection commands are refused (out of scope).
- One iSCSI connection per session, no digests, no authentication; it listens on loopback only by default.
