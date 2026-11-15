---
schema_version: 1
id: build-and-checks
domain: 00-workspace
status: active
title: "TODO-01 -- Build Settings and Checks"
---

# TODO-01 -- Build Settings and Checks

> **Goal:** Every project builds from one set of shared settings with warnings as errors, one command runs every check the repository has, and CI runs that command on Windows and Linux.

> **Current state:** Measured 2026-11-15. Six projects each repeat `TargetFramework`, `Nullable` and `ImplicitUsings` in their own `.csproj`; there is no `Directory.Build.props`, no `global.json` and no `.editorconfig`. `scripts/check.ps1` builds, runs `tests/Wobble.Tests` and validates the TODO tree, and `.github/workflows/ci.yml` runs it on `windows-latest` only. WSL has the .NET 10 SDK, so a Linux build is unproven on .NET 11 (design, Verified during step 1).

## Inputs

- `docs/specs/2026-11-15-wobble-design.md`, Decisions (C# on .NET 11 for every component) and Verified during step 1 (the Linux build).
- `Wobble.slnx`, the six `.csproj` files, `scripts/check.ps1`, `.github/workflows/ci.yml`.

## Outcome

- `dotnet build Wobble.slnx` gives zero warnings, and a new warning fails the build.
- `pwsh scripts/check.ps1` is the one gate a section runs, and CI runs the same file on Windows and Linux.

**Adjacency:** all=not-applicable (build configuration only: no records, users or data)

**Adjacency rationale:** This file changes how the code is built and checked, not what the emulator does, so no adjacent surface exists.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Shared build settings for every project | -- | [ ] |
| 2 | §2 | Formatting rules and a formatting gate | §1 | [ ] |
| 3 | §3 | Linux build and unit tests in CI | §1 | [ ] |

## 1. Shared build settings for every project

Settings repeated in six project files drift. One `Directory.Build.props` states them once and turns warnings into errors, so a section cannot land code the compiler already doubts.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Create `Directory.Build.props` and `global.json` at the repository root. Done when: `dotnet build Wobble.slnx` succeeds with the per-project copies removed.
2. Fix whatever `TreatWarningsAsErrors` surfaces in `src/` and `tests/`. Done when: the build prints `0 Warning(s)`.

- [ ] Create `Directory.Build.props` with `TargetFramework` `net11.0`, `Nullable` `enable`, `ImplicitUsings` `enable`, `LangVersion` `latest` and `TreatWarningsAsErrors` `true`. Done when: `dotnet msbuild src/Wobble.Core -getProperty:TreatWarningsAsErrors` prints `true`.
- [ ] Remove the `TargetFramework`, `Nullable` and `ImplicitUsings` lines from each of the six `.csproj` files under `src/` and `tests/`. Done when: `git grep -n TargetFramework -- '*.csproj'` prints nothing. Cheaper substitute: leaving both copies, which hides which one wins.
- [ ] Create `global.json` pinning the SDK to `11.0.100` with `rollForward` `latestFeature`. Done when: `dotnet --version` run in the repository prints an 11.0 SDK.
- [ ] Fix each warning the build now reports at its cause in `src/` or `tests/`. Done when: `dotnet build Wobble.slnx` prints `0 Warning(s)` and `0 Error(s)`. Cheaper substitute: a `NoWarn` entry or a `#pragma warning disable`, which must not be added.
- [ ] Commit: `build: share settings in Directory.Build.props and treat warnings as errors`

**Test checkpoint:** `dotnet build Wobble.slnx` prints `0 Warning(s)` and `0 Error(s)`; adding an unused local variable to `src/Wobble.Core/Bytes.cs` makes it fail.

## 2. Formatting rules and a formatting gate

Three agents write this code. An `.editorconfig` and a formatting check keep their diffs about behaviour, not whitespace.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Write `.editorconfig` from the style the code already has. Done when: `dotnet format Wobble.slnx --verify-no-changes` exits 0 after one formatting commit.
2. Add the check to `scripts/check.ps1`. Done when: a misindented line makes the script exit 1.

- [ ] Create `.editorconfig` stating four-space indentation, file-scoped namespaces, LF-agnostic line endings and `dotnet_sort_system_directives_first`. Done when: the file exists and `dotnet format Wobble.slnx --verify-no-changes --verbosity diagnostic` reads it.
- [ ] Run `dotnet format Wobble.slnx` once and review the diff for behaviour changes. Done when: `dotnet test tests/Wobble.Tests` still prints `Passed!` and the diff touches whitespace and using order only.
- [ ] Add a `dotnet format Wobble.slnx --verify-no-changes` step to `scripts/check.ps1` after the build. Done when: `pwsh scripts/check.ps1` exits 1 when a tab is added to `src/Wobble.Core/Bytes.cs` and 0 once it is removed.
- [ ] Commit: `build: add .editorconfig and a formatting gate`

**Test checkpoint:** `pwsh scripts/check.ps1` exits 0 on a clean tree and exits 1 after a line in `src/Wobble.Core/Bytes.cs` is indented with a tab.

## 3. Linux build and unit tests in CI

The design says the same build runs in WSL or Linux, and so far that was checked only on a copy retargeted to .NET 10. A CI job on Linux proves it on every push.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Add an `ubuntu-latest` job to `.github/workflows/ci.yml`. Done when: the job is green on `main`.
2. Keep Windows-only code out of the Linux build. Done when: `Wobble.Testing` and `Wobble.EndToEnd` are skipped on Linux by condition, not by failure.

- [ ] Add a matrix of `windows-latest` and `ubuntu-latest` to the `check` job in `.github/workflows/ci.yml`. Done when: `gh run list --workflow ci.yml --limit 1` shows both jobs.
- [ ] Make `scripts/check.ps1` build `src/Wobble.Core`, `src/Wobble.Iscsi`, `src/Wobble.Host` and `tests/Wobble.Tests` on Linux instead of `Wobble.slnx`. Done when: the Linux job builds without touching `Wobble.Testing`. Cheaper substitute: `continue-on-error` on the Linux job, which must not be used.
- [ ] Update the Linux paragraph of `docs/specs/2026-11-15-wobble-design.md` (Verified during step 1) to say CI proves the .NET 11 Linux build. Done when: the paragraph names the workflow.
- [ ] Commit: `ci: build and run the unit tests on Linux`

**Test checkpoint:** `python scripts/ci_status.py` exits 0 with the `ci.yml` run on `main` showing both the Windows and the Linux job green.

## Verification

- `pwsh scripts/check.ps1` exits 0 locally and both CI jobs are green on `main`.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
