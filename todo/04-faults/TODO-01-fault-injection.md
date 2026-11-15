---
schema_version: 1
id: fault-injection
domain: 04-faults
status: active
title: "TODO-01 -- Fault Injection"
---

# TODO-01 -- Fault Injection

> **Goal:** Slice D of the plan is finished: a test loads an ordered list of fault rules through the control API, each rule fires on its trigger a set number of times, and each fault in the design's example is driven from a Windows test and repeats exactly.

> **Current state:** Measured 2026-11-15. No fault engine exists. `OpticalDrive.Execute` goes straight to `Dispatch`, `ScsiResult` carries only status, sense and data, the iSCSI connection has no way to delay or drop on request, and the control API has no `/faults` route.

## Inputs

- `docs/specs/2026-11-15-wobble-design.md`, Fault injection (the rule list, triggers and actions) and Control API (`PUT /faults`).
- `docs/plans/2026-11-15-write-support.md`, slice D.
- `src/Wobble.Core/OpticalDrive.cs` (`Execute`, the injected `TimeProvider`), `src/Wobble.Iscsi/IscsiConnection.cs` (`ScsiCommandAsync`), `src/Wobble.Host/Program.cs`.

## Outcome

- Each trigger and action in the design works alone and combined, on reads and on writes.
- A fault script run twice gives the same trace both times.

**Adjacency:** list=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); document=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); settings=applicable @ §4; reporting=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); notifications=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); permissions=not-applicable (the emulator engine has no records or users of its own; its surface is the SCSI command set); audit=applicable @ §5; exchange=applicable @ §1; reverse=applicable @ §4

**Adjacency rationale:** The fault script is the setting (§4 loads and replaces it) and its JSON is the exchange format (§1). The trace records which rule fired (§5). Reverse is clearing the script, also §4.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Fault rule model and JSON parsing | D01 T02 §6 | [ ] |
| 2 | §2 | Triggers and the actions the drive performs | §1 | [ ] |
| 3 | §3 | Delays and dropped connections in the transport | §2 | [ ] |
| 4 | §4 | Fault scripts through the control API | §3 | [ ] |
| 5 | §5 | Fired rules in the trace | §4, D01 T01 §7 | [ ] |
| 6 | §6 | Every design fault driven from Windows, repeatably | §5, D02 T01 §10 | [ ] |

## 1. Fault rule model and JSON parsing

A rule is data: a trigger, an action and a count. Parsing it strictly means a typo in a test's script is an error at load, not a fault that silently never fires.

**Phase:** 5
**Needs:** none
**Surface:** no

**Build order:**

1. Add `src/Wobble.Core/Faults/FaultRule.cs` and `FaultScript.cs`. Done when: the design's example script parses into five rules.
2. Refuse anything unknown. Done when: an unknown key, opcode or sense string fails with the rule's index.

- [ ] Add `FaultRule` (`When`: opcode, LBA range, nth, after blocks written, at time; `Do`: sense, delay, buffer underrun, eject, swap media, not ready, drop connection; `Times`) in `src/Wobble.Core/Faults/FaultRule.cs`. Done when: `dotnet build` succeeds.
- [ ] Parse the JSON list in `FaultScript.Parse`, taking opcodes by name (`WRITE_10`, `READ_10`, `SYNCHRONIZE_CACHE` and the rest of a table in `src/Wobble.Core/Opcodes.cs`) or as hex. Done when: the example in the design parses.
- [ ] Refuse an unknown property, an unknown opcode name, a sense that is not `KK/AA/QQ`, `lbaFrom` above `lbaTo`, `times` below 1, and a rule with no action, each with a message holding the rule's index and the property. Done when: a test in `FaultScriptTests` proves it. Cheaper substitute: ignoring unknown properties.
- [ ] Add `tests/Wobble.Tests/FaultScriptTests.cs`. Done when: it passes and fails if unknown properties are accepted.
- [ ] Commit: `core: parse fault scripts`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~FaultScriptTests"` prints `Passed!` with no failed test.

## 2. Triggers and the actions the drive performs

Most faults are answers the drive gives: a sense code, not ready, a disc that goes away. They belong in the core, before `Dispatch`, under the same lock.

**Phase:** 5
**Needs:** none
**Surface:** no

**Build order:**

1. Add `FaultEngine` in `src/Wobble.Core/Faults/FaultEngine.cs`, consulted by `OpticalDrive.Execute`. Done when: the first matching rule with fires left decides the result.
2. Count blocks written and commands per opcode. Done when: `nth` and `afterBlocksWritten` fire on the right command.

- [ ] Match opcode, LBA range overlap, the nth command that matches the rule's other conditions, total blocks written so far, and emulator time from the injected `TimeProvider`, in `FaultEngine.Match`. Done when: one test per trigger passes.
- [ ] Return the rule's sense as a check condition without running the command. Done when: a WRITE (10) over LBA 120 000 fails with `03/0C/00` once and succeeds the second time with `times` 1.
- [ ] Implement `ejectMedia`, `swapMedia` (an image path) and `notReady` (sense `02/04/00` until the script is replaced) in `OpticalDrive`. Done when: after `ejectMedia` the next command gets `02/3A/00` and the media event is set.
- [ ] Implement `bufferUnderrun`: fail the write with `03/0C/09` and leave the track damaged in READ TRACK INFORMATION. Done when: a test in `FaultEngineTests` proves it on a track-at-once burn.
- [ ] Apply rules in list order and decrement `times` only when a rule fires. Done when: a test with two rules on the same opcode sees the first exhaust before the second fires.
- [ ] Add `tests/Wobble.Tests/FaultEngineTests.cs`. Done when: it passes and fails if `times` is not decremented.
- [ ] Commit: `core: fire fault rules in the drive`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~FaultEngineTests"` prints `Passed!` with no failed test.

## 3. Delays and dropped connections in the transport

The design chose a built-in iSCSI target so faults can reach the transport: a response that stalls, a connection that drops mid-burn. The core decides, the target acts.

**Phase:** 5
**Needs:** none
**Surface:** no

**Build order:**

1. Add a `Directive` to `ScsiResult` in `src/Wobble.Core/Scsi.cs`. Done when: a result can carry a delay or a drop without the core knowing about sockets.
2. Honour it in `IscsiConnection.ScsiCommandAsync`. Done when: a delayed response arrives late and a drop closes the socket with no response.

- [ ] Add `ScsiResult.Delay` (a `TimeSpan`) and `ScsiResult.DropConnection` and set them from `delayMs` and `dropConnection` in `FaultEngine`. Done when: a unit test reads them off the result.
- [ ] Wait the delay on the connection's `TimeProvider` before sending the response in `src/Wobble.Iscsi/IscsiConnection.cs`. Done when: a test with a fake clock sees no response until the clock advances.
- [ ] Close the connection without a response for `DropConnection`, after any Data-Out has been received. Done when: `MiniInitiator` in `tests/Wobble.Tests` sees the socket close and can log in again.
- [ ] Keep the drive's state across the drop, with a unit attention pending for the new session. Done when: the first command after the new login gets `06/29/00`.
- [ ] Add `tests/Wobble.Tests/TransportFaultTests.cs`. Done when: it passes and fails if the delay is skipped.
- [ ] Commit: `iscsi: delay responses and drop connections on request`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~TransportFaultTests"` prints `Passed!` with no failed test.

## 4. Fault scripts through the control API

Tests load faults per test through the control API, as the design says, and need to read back what fired.

**Phase:** 5
**Needs:** none
**Surface:** no

**Build order:**

1. Add `/faults` routes to `src/Wobble.Host/Program.cs`. Done when: `PUT` replaces the script, `GET` returns it with fire counts and `DELETE` clears it.
2. Add them to `WobbleControl`. Done when: a test sets and reads faults in two calls.

- [ ] Add `PUT /faults` taking the JSON list and answering 400 with the parser's message for a bad script, leaving the old script in place. Done when: a test in `ControlApiTests` proves it for a good and a bad script.
- [ ] Add `GET /faults` returning each rule with `fired` and `remaining`, and `DELETE /faults`. Done when: a test in `ControlApiTests` asserts it and fails when the behaviour is removed.
- [ ] Clear the script in `POST /reset`. Done when: `GET /faults` returns an empty list after a reset.
- [ ] Add `SetFaultsAsync`, `FaultsAsync` and `ClearFaultsAsync` to `src/Wobble.Testing/WobbleControl.cs`, with a `FaultRuleStatus` record. Done when: an end-to-end test uses all three.
- [ ] Add `--faults <file>` to the command line, refusing an unreadable or invalid file with exit 2. Done when: a test in `ControlApiTests` asserts it and fails when the behaviour is removed.
- [ ] Document the routes and the rule format in `README.md`. Done when: the README shows the design's example and every trigger and action.
- [ ] Commit: `host: load, read and clear fault scripts`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~ControlApiTests"` prints `Passed!` with no failed test.

## 5. Fired rules in the trace

A failing test needs to tell a fault it asked for from a defect. The trace must say which rule answered a command.

**Phase:** 5
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Fault` to `TraceEntry` in `src/Wobble.Core/Scsi.cs`. Done when: an entry answered by a rule carries the rule's index.
2. Carry it through the control API. Done when: `TraceLine.Fault` is set in `Wobble.Testing`.

- [ ] Add `int? Fault` to `TraceEntry` and set it in `OpticalDrive.Execute` when a rule fired. Done when: a unit test reads index 0 on the faulted command and null on the others.
- [ ] Add the field to `TraceLine` in `src/Wobble.Testing/WobbleControl.cs`. Done when: an end-to-end test asserts it.
- [ ] Record the applied delay in the time of the `TraceEntry`. Done when: the entry after a 4 000 ms delay is at least 4 000 ms later on the emulator clock.
- [ ] Commit: `core: record the fault rule that answered a command`

**Test checkpoint:** `dotnet test tests/Wobble.Tests --filter "FullyQualifiedName~FaultEngineTests"` prints `Passed!` with no failed test.

## 6. Every design fault driven from Windows, repeatably

Slice D is done when each fault in the design's example is driven from a Windows test and repeats exactly.

**Phase:** 5
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Add `tests/Wobble.EndToEnd/FaultTests.cs` with one test per rule of the example. Done when: each fault reaches the test as the sense, removal or disconnect it describes.
2. Run each twice. Done when: the two traces are equal apart from time.

- [ ] Add `FaultTests.AWriteErrorAtAnLba` (sense `03/0C/00` at LBA 120 000, once). Done when: the burn fails there with that sense through `ScsiDrive` and a retry succeeds.
- [ ] Add `FaultTests.TheDiscIsEjectedMidBurn` (after 50 000 blocks). Done when: the next write fails with `02/3A/00` and Windows reports no media.
- [ ] Add `FaultTests.AnUnreadableSector` (READ (10) LBA 4 096 to 4 099, sense `03/11/00`). Done when: a file read through the file system fails with a CRC or I/O error.
- [ ] Add `FaultTests.ASlowWriteThenAnUnderrun` (200th write, 4 000 ms, underrun). Done when: the 200th write takes at least 4 seconds and fails with `03/0C/09`.
- [ ] Add `FaultTests.TheConnectionDropsOnSynchronize`. Done when: the initiator reconnects by itself and the drive answers again within 60 seconds.
- [ ] Run each scenario twice in one test of `FaultTests` and compare the traces' opcode, status, sense and fault columns. Done when: they are equal.
- [ ] Mark slice D done in `README.md` Status. Done when: fault injection is listed as supported.
- [ ] Commit: `e2e: drive every design fault from Windows`

**Test checkpoint:** `dotnet test tests/Wobble.EndToEnd --filter "FullyQualifiedName~FaultTests"` prints `Passed!` with no failed test, on a host where `tools/attach.ps1` has run.

## Verification

- The plan's slice D done condition holds: each fault in the design's example is driven from a Windows test and repeats exactly.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
