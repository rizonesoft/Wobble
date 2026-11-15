# Review: read support (commit 8c556ba)

Reviewer: Codex (`codex exec`, read-only sandbox), one round, 2026-11-15. Static review; Codex could not run the tests because its environment has the .NET 10 SDK. Each finding was checked against the code before acting.

| # | Finding | Verdict | Outcome |
| --- | --- | --- | --- |
| 1 | A READ CDB could demand a buffer of any size regardless of the iSCSI transfer length | Real | One read is limited to 32768 blocks (64 MiB), otherwise INVALID FIELD IN CDB. Test added. |
| 2 | Data-In ignored MaxBurstLength | Real | Data-In is split into sequences that end with the F bit. Test added. |
| 3 | A task management request during a write waiting for data was queued behind it; every function was answered as complete | Real | Task management and NOP are handled during the wait, an abort ends the write, resets reach the device, unsupported functions say so. Test added. |
| 4 | Data-Out was not validated | Real | Task tag, DataSN, offset, contiguity and total length are checked; a violation ends the connection. Test added. |
| 5 | CmdSN was overwritten, not validated | Real | A numbered request must carry the expected CmdSN or it is ignored. Test added. |
| 6 | GET EVENT STATUS NOTIFICATION length fields were wrong (4 and 0 instead of 6 and 2) | Real, and visible: Windows had stopped using the command after two tries and polled with TEST UNIT READY | Fixed. Unit tests corrected, and an end-to-end test checks that Windows keeps polling. |
| 7 | A header-only event probe consumed the event | Real | The event stays pending unless more than the header was fetched. Test added. |
| 8 | One unit attention is shared by all initiators | Design limit | Wobble serves one initiator at a time. Stated in the README. |
| 9 | READ CD ignored the subchannel selection | Real | Subchannel requests are refused. Test added. |
| 10 | Discovery login did not return TargetPortalGroupTag | Real | Returned for every session type. Test extended. |
| 11 | If job assignment failed, the emulator process outlived the test | Real | The process tree is killed, and the job handle is closed if the process cannot start. |
| 12 | Swapping media in the end-to-end fixture could return before Windows saw the change | Real | The fixture removes the disc, waits for Windows to notice, then inserts the new one. |
