## Round 1: astra (`gpt-6-astra` via `codex`)

- Writer: claude
- Run: 2026-11-15T20:21:08Z
- Candidate: 92f03a3fe6056370d094a8b2642db8542b6cf499
- Effort: medium
- Lenses: adversarial, integration, source-defect, consistency, quality, optimization, design
- Lines: 37
- Digest: 3ed0be0f7ed29161

### adversarial: approve

No concrete finding.

### integration: needs-attention

1. **P2** `todo/06-interface/TODO-01-control-app.md:475`: Publishing the app beside the host overwrites the host on normal Windows filesystems. Section 2 names the app assembly `Wobble`; `src/Wobble.Host/Wobble.Host.csproj:12` names the host `wobble`. Reproduce by following §17’s win-x64 publication instructions: `Wobble.exe` and `wobble.exe` resolve to the same file. The app subsequently launches itself instead of its emulator. Use distinct executable names or separate directories.

### source-defect: needs-attention

2. **P2** `todo/06-interface/TODO-01-control-app.md:308`: The editor specifies one chosen action, excluding combinations required by the governing design. Reproduce by entering its fourth example: the 200th WRITE must both delay 4,000 ms and report buffer underrun (`docs/specs/2026-11-15-wobble-design.md:94`). A single-choice action picker cannot represent that rule. Specify combined actions and verify that example through the editor.

### consistency: needs-attention

3. **P1** `todo/06-interface/TODO-01-control-app.md:92`: The new test project is added to the solution but never wired into the repository’s gate. `scripts/check.ps1:20` runs only `Wobble.Tests`; its `-EndToEnd` branch runs only `Wobble.EndToEnd`. CI invokes that same script. After implementing the app sections, an ordinary UI regression can fail `Wobble.App.Tests` while both gates remain green. Add explicit app-test execution, separating the initiator-dependent tests introduced at line 451 from portable headless tests.

### quality: needs-attention

4. **P2** `todo/06-interface/TODO-01-control-app.md:382`: The prescribed icon tool cannot render the supplied SVG files. `campaign-icon.py frames` accepts two PNG inputs and calls `read_png` directly. Reproduced with `python -B scripts/campaign-icon.py frames assets/icons/tray-idle.svg assets/icons/tray-idle.svg R:/scratch/wobble-review-unused.ico`: it reports `campaign-icon: not a PNG`, before writing anything. Specify an SVG rasterization step or extend the owning renderer.

### optimization: approve

No concrete finding.

### design: needs-attention

5. **P2** `design/SPEC.md:37`: The minimum window width is 1100, but the responsive Faults sheet requires a width below 1100. The plan repeats both requirements at `todo/06-interface/TODO-01-control-app.md:95` and explicitly requires resizing to 1000 at line 96. Following the minimum-width requirement prevents that user action and verification from succeeding. Resolve the minimum width or change the collapse threshold.

LENS adversarial: clean
LENS integration: findings
LENS source-defect: findings
LENS consistency: findings
LENS quality: findings
LENS optimization: clean
LENS design: findings
HIGHEST: P1
OVERALL: needs-attention

- Implemented: finding 1, the app's executable is `WobbleApp` (`todo/06-interface/TODO-01-control-app.md` §2 and §17, `design/SPEC.md`, the design's Components table).
- Implemented: finding 2, the rule editor's actions are switches that combine, and the design's fourth example is entered through the editor (§10, `design/SPEC.md` F3).
- Implemented: finding 3, §2 adds `tests/Wobble.App.Tests` to `scripts/check.ps1`, with the initiator-dependent tests of §16 under the trait `Category=EndToEnd` in the `-EndToEnd` step.
- Implemented: finding 4, §13's script draws each SVG to PNG in a headless browser before `campaign-icon.py frames`.
- Implemented: finding 5, the minimum window is 900 by 600 and the Faults column collapses below 1100 (`design/SPEC.md` Window, §2).
