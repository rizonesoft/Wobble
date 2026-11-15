# Wobble design

Date: 2026-11-15. Status: agreed starting point. Builds on [the iSCSI proof](../proof/2026-11-15-iscsi-optical-drive.md).

## Decisions

| Topic | Decision |
| --- | --- |
| Transport | A self-contained userspace iSCSI target inside the Wobble process. Not a TCMU handler. |
| Language | C# on .NET 11, for every component. |
| Repository | One repository, `D:\Wobble`, developed on Windows. The capture tool lives here. |
| Where the emulator runs | Natively on Windows by default. The same build runs in WSL or Linux. |
| Profile format | JSON files, with captured drive answers stored as hex strings. |
| Fault injection | Declarative rule lists (JSON), loaded per test through the control API. |
| Test control | A local HTTP JSON control API, wrapped by a `Wobble.Testing` library for xUnit. |
| Interface | `Wobble.App`, an Avalonia desktop app for Windows and Linux, a client of the control API. Specified in [design/SPEC.md](../../design/SPEC.md); decided 2026-11-15. |
| Licence | GPL-3.0, decided 2026-11-15. |

## Transport: why a built-in iSCSI target

- The proof needed no root and no kernel modules. TCMU needs root, configfs and `target_core_user`, which nobody has confirmed ships with the WSL kernel.
- A built-in target lets faults reach the transport: stall a response, drop the connection mid-burn, answer late.
- It runs on Windows itself, so a test can start the emulator as a child process with no WSL hop.
- The proof showed Windows is strict about response lengths. Owning every byte from the PDU to the MMC payload makes that testable in one place.

iSCSI scope (RFC 7143 subset): one connection per session, error recovery level 0, no digests, no authentication, login and text negotiation, SCSI command and response, Data-In, Data-Out with R2T and immediate data, NOP, task management (abort, LUN reset), logout, and unit attention reporting. It listens on loopback only by default.

The core never sees iSCSI. The seam is one interface:

```csharp
public interface IScsiDevice
{
    ScsiResult Execute(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> dataOut);
}
// ScsiResult: status byte, sense data, data-in bytes. Allocation lengths are read from the CDB.
```

A Storport miniport bridge or a USB gadget front end would call the same interface.

## Components

| Project | Role |
| --- | --- |
| `Wobble.Core` | MMC command set and disc state model: drive, media, sessions and tracks, write state, mode pages, features, sense. No sockets, no file-format knowledge beyond image access, time only through an injected clock. |
| `Wobble.Profiles` | Drive and media profile types, JSON loading and validation. Shared by the emulator and the capture tool. |
| `Wobble.Iscsi` | The iSCSI target. |
| `Wobble.Host` (`wobble`) | The executable: loads profiles, hosts the target and the control API. |
| `Wobble.Capture` (`wobble-capture`) | Windows tool: queries a real drive over SPTI, writes a drive or media profile, and records command traces. |
| `Wobble.App` (`WobbleApp`) | The control app: disc, live command trace and fault rules, over the control API. |
| `Wobble.Testing` | Test library: starts the emulator, attaches the Windows initiator, finds the drive, calls the control API. |
| `tests/*` | Unit tests of byte-exact responses; end-to-end tests on Windows through SPTI and IMAPI2. |

## Profiles

A drive profile describes hardware; a media profile describes a disc. Both are JSON so they diff well and need no extra library. Anything captured from hardware is kept as the raw bytes the drive returned (hex), so a capture is replayed exactly and nothing is lost in translation.

```json
{
  "kind": "drive",
  "identity": { "vendor": "HL-DT-ST", "product": "BD-RE  WH16NS40", "revision": "1.05", "serial": "WOBBLE0001" },
  "inquiry": "0580003247000000...",
  "profiles": ["0x43", "0x41", "0x1B", "0x14", "0x10", "0x0A", "0x09", "0x08"],
  "features": { "0x0000": "...", "0x0001": "...", "0x0021": "..." },
  "modePages": { "0x05": { "default": "...", "changeable": "..." }, "0x2A": { "default": "..." } },
  "speeds": { "0x1B": { "readKbps": [22160], "writeKbps": [5540, 11080, 22160] } },
  "quirks": { "readTocFormat2": true },
  "vendorCommands": []
}
```

```json
{
  "kind": "media",
  "profile": "0x1B",
  "state": "blank",
  "manufacturerId": "MCC-004-00",
  "capacityBlocks": 2295104,
  "writeSpeedsKbps": [5540, 11080, 22160],
  "image": null
}
```

A written disc points `image` at an ISO or a Wobble disc file (sessions, tracks and data). Which features and profiles are current is computed from the loaded media, not stored.

## Fault injection

A fault script is an ordered list of rules. Each rule has a trigger and an action, and fires a set number of times. Rules are deterministic: no randomness, and time-based triggers use the emulator clock, so a test repeats exactly.

```json
[
  { "when": { "opcode": "WRITE_10", "lbaFrom": 120000, "lbaTo": 120015 }, "do": { "sense": "03/0C/00" }, "times": 1 },
  { "when": { "opcode": "WRITE_10", "afterBlocksWritten": 50000 }, "do": { "ejectMedia": true } },
  { "when": { "opcode": "READ_10", "lbaFrom": 4096, "lbaTo": 4099 }, "do": { "sense": "03/11/00" } },
  { "when": { "opcode": "WRITE_10", "nth": 200 }, "do": { "delayMs": 4000, "bufferUnderrun": true } },
  { "when": { "opcode": "SYNCHRONIZE_CACHE" }, "do": { "dropConnection": true }, "times": 1 }
]
```

Triggers: opcode, LBA range, nth matching command, blocks written so far, emulator time. Actions: return a sense code, delay, report a buffer underrun, eject or swap media, become not ready, drop the connection. Odd media is a media profile, not a fault.

## Control API and how Windows tests drive the emulator

`wobble` serves HTTP JSON on loopback:

- `PUT /drive` and `PUT /media`: load a drive profile, insert or eject media.
- `PUT /faults`: replace the fault script.
- `GET /trace`: every command received since the last reset (CDB, data lengths, status, sense, timing), for assertions and for comparison with traces captured from real hardware.
- `POST /reset`: back to a known state.

A Windows end-to-end test uses `Wobble.Testing`: start `wobble` as a child process (in a job object, so it dies with the test), attach the Windows initiator to it, find the drive by its serial number, run the software under test or SPTI and IMAPI2 calls, then assert on results and on the trace. The same library can instead start the emulator in WSL through `wsl.exe`.

## Build order

1. **Read support.** iSCSI target, read-only MMC for CD and DVD data discs from an ISO, control API basics, trace. Done when Windows mounts and reads a disc served by Wobble and IMAPI2 lists the drive, repeating the proof without tgt.
2. **Raw CD model and write support.** CarbonCD, the first software under test, is a raw CD copier, so the disc model becomes raw 2352-byte sectors with subchannel before writing is added: raw reads, CD-R and CD-RW writing (track-at-once, cue sheet, RAW), then IMAPI2 data burning on CD and DVD, then fault injection. Slices are in [the plan](../plans/2026-11-15-write-support.md).
3. **Profiles and capture.** `wobble-capture`, real drive and media profiles, trace recording and comparison, quirks.

## Verified during step 1 (2026-11-15)

- The Windows initiator attaches over loopback to a target running on the same Windows host. Wobble runs natively on Windows; WSL is not involved.
- Managing the initiator needs elevation: disconnecting a target and removing a portal fail with "Access is denied" from an ordinary PowerShell, and the connection was made elevated. So `tools/attach.ps1` is run elevated once per boot. After that the initiator reconnects by itself whenever the same target name is back on the same port (about 15 seconds), so tests start and stop the emulator freely with no elevation.
- The core, the target and the unit tests also build and pass on Linux. WSL here has the .NET 10 SDK, so that check used a copy retargeted to `net10.0`; building the repository as is in WSL needs the .NET 11 SDK there.

## Out of scope

AACS, CSS and BD+; physical-layer fidelity beyond a simple timing and error model; multiple connections per session and iSCSI error recovery above level 0.
