---
schema_version: 1
id: packaging-and-docs
domain: 07-release
status: active
title: "TODO-01 -- Packaging, Releases and Documentation"
---

# TODO-01 -- Packaging, Releases and Documentation

> **Goal:** Wobble can be installed and used by someone who did not build it: versioned self-contained downloads for Windows and Linux, the test library as a NuGet package, a release made by pushing a tag, and a guide that takes a tester from download to a first injected fault.

> **Current state:** Measured 2026-11-15. Wobble runs only from source with `dotnet run`. No version is set, nothing is published, `Wobble.Testing` is not packable, and the only documentation is `README.md`, the design and the plan.

## Inputs

- `docs/specs/2026-11-15-wobble-design.md`, Components and Control API.
- `README.md`, `Wobble.slnx`, `.github/workflows/ci.yml`.
- The finished emulator, profiles, capture tool and control interface of the earlier domains.

## Outcome

- Pushing a `v*` tag publishes a GitHub release with `wobble` for win-x64 and linux-x64 and the `Wobble.Testing` package.
- `docs/guide/` explains attach, insert, burn, inject and capture, and every command in it was run.

**Adjacency:** list=not-applicable (packaging and documentation only); document=applicable @ §4; settings=not-applicable (packaging and documentation only); reporting=not-applicable (packaging and documentation only); notifications=not-applicable (packaging and documentation only); permissions=not-applicable (packaging and documentation only); audit=not-applicable (packaging and documentation only); exchange=applicable @ §2; reverse=not-applicable (packaging and documentation only)

**Adjacency rationale:** The guide is the document a user carries (§4). The published archives are the exchange with the outside (§2).

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Version numbers and the version command | -- | [ ] |
| 2 | §2 | Self-contained downloads | §1 | [ ] |
| 3 | §3 | The test library as a NuGet package | §2, D04 T01 §6 | [ ] |
| 4 | §4 | The user guide | §3, D05 T02 §4 | [ ] |
| 5 | §5 | Releases from a tag | §2, §4 | [ ] |

## 1. Version numbers and the version command

A release needs one version, set in one place, that the executable can state.

**Phase:** 7
**Needs:** none
**Surface:** no

**Build order:**

1. Set `Version` in `Directory.Build.props`. Done when: every assembly carries it.
2. Add `--version`. Done when: `wobble --version` prints it.

- [ ] Add `Version` `0.1.0` to `Directory.Build.props` with the commit hash as `SourceRevisionId`. Done when: `dotnet build` stamps `0.1.0+<hash>` as the informational version.
- [ ] Add `--version` to `src/Wobble.Host/Program.cs` and `src/Wobble.Capture/Program.cs`. Done when: both print the informational version and exit 0.
- [ ] Report the version in `GET /status`. Done when: the JSON has `version`.
- [ ] Create `CHANGELOG.md` with a `0.1.0` entry listing what each plan slice delivered. Done when: the file names read, CD write, DVD, Blu-ray, faults, profiles, capture and the interface.
- [ ] Commit: `build: set the version and add --version`

**Test checkpoint:** `dotnet run --project src/Wobble.Host -- --version` prints a line starting `0.1.0`.

## 2. Self-contained downloads

A tester should not need the .NET SDK. One script publishes single-file, self-contained builds with the profiles beside them.

**Phase:** 7
**Needs:** none
**Surface:** no

**Build order:**

1. Add `scripts/publish.ps1`. Done when: it writes a zip per runtime under `artifacts/`.
2. Run the published build. Done when: the win-x64 `wobble.exe` serves a disc on a machine path with no SDK lookup.

- [ ] Create `scripts/publish.ps1` running `dotnet publish src/Wobble.Host -c Release -r <rid> --self-contained -p:PublishSingleFile=true` for `win-x64` and `linux-x64`, and `src/Wobble.Capture` for `win-x64`. Done when: `artifacts/wobble-0.1.0-win-x64.zip` and the Linux archive exist.
- [ ] Include `profiles/`, `tools/attach.ps1`, `README.md` and the licence in each archive. Done when: listing the zip shows them.
- [ ] Start the published `wobble.exe` from the unpacked archive and insert a profile through the control API. Done when: `GET /profiles` lists the bundled set from the unpacked folder.
- [ ] Write a SHA-256 file beside each archive. Done when: `Get-FileHash` matches it.
- [ ] Commit: `build: publish self-contained downloads`

**Test checkpoint:** `pwsh scripts/publish.ps1` exits 0 and `artifacts/wobble-0.1.0-win-x64/wobble.exe --version` prints `0.1.0`.

## 3. The test library as a NuGet package

`Wobble.Testing` is how other projects' tests drive the emulator. A package lets CarbonCD's tests reference it without a path into this repository.

**Phase:** 7
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Make `Wobble.Testing` and `Wobble.Spti` packable. Done when: `dotnet pack` writes both packages.
2. Let the library find a published `wobble`. Done when: `EmulatorProcess.StartAsync` takes a path or finds `wobble` on `PATH`.

- [ ] Set `IsPackable`, `PackageId`, `Description`, `PackageLicenseExpression` and `RepositoryUrl` in `src/Wobble.Testing/Wobble.Testing.csproj` and `src/Wobble.Spti/Wobble.Spti.csproj`. Done when: `dotnet pack -c Release` writes `Wobble.Testing.0.1.0.nupkg`.
- [ ] Resolve the executable in `EmulatorProcess.StartAsync` from an explicit path, then the `WOBBLE_EXE` variable, then `PATH`, and fail with a message naming all three when none is found. Done when: the section's Test checkpoint proves it for each case.
- [ ] Add XML documentation to the public members of `Wobble.Testing` and enable `GenerateDocumentationFile`. Done when: the build has no missing-documentation warning.
- [ ] Add a sample test project under `samples/FirstTest` that references the package from a local feed and reads a disc. Done when: `dotnet test samples/FirstTest` passes on the attached host.
- [ ] Commit: `testing: package Wobble.Testing for other projects`

**Test checkpoint:** `dotnet pack src/Wobble.Testing -c Release -o artifacts` exits 0 and `dotnet test samples/FirstTest` prints `Passed!`.

## 4. The user guide

The README is a reference. A guide walks one path end to end so a new tester gets from a download to a failing burn they caused on purpose.

**Phase:** 7
**Needs:** windows-host, iscsi-initiator
**Surface:** no

**Build order:**

1. Write `docs/guide/` in five pages. Done when: each page's commands were run and their output pasted from the run.
2. Link it from `README.md`. Done when: the README's first screen points at the guide.

- [ ] Write `docs/guide/01-install-and-attach.md`: download, start, `tools/attach.ps1` elevated once per boot, the drive appearing. Done when: every command in it was run on this machine.
- [ ] Write `docs/guide/02-discs.md`: insert an ISO, a cue sheet and a blank disc, eject, save a written disc. Done when: each `curl` in it returns what the page shows.
- [ ] Write `docs/guide/03-burning.md`: a CD and a DVD burn with IMAPI2 from PowerShell, reading the trace. Done when: the script in it burns.
- [ ] Write `docs/guide/04-faults.md`: each trigger and action with one worked failure. Done when: the example script fires as the page says.
- [ ] Write `docs/guide/05-profiles-and-capture.md`: load a drive profile, capture one, compare traces. Done when: the commands run against Wobble.
- [ ] Rewrite `README.md` Status, Run and Limits for the finished product and link the guide. Done when: no line of the README describes step 1 as the current state.
- [ ] Commit: `docs: add the user guide`

**Test checkpoint:** `python scripts/check-guide.py` exits 0: it extracts every fenced `powershell` block marked `# run` from `docs/guide/` and each exits 0 against a running emulator; a broken command makes it exit 1.

## 5. Releases from a tag

A release should be one action with no hand steps: push a tag, get the archives and the package attached to a GitHub release.

**Phase:** 7
**Needs:** none
**Surface:** no

**Build order:**

1. Add `.github/workflows/release.yml`. Done when: a `v*` tag builds, tests, publishes and creates the release.
2. Prove it on a pre-release tag. Done when: `v0.1.0-rc.1` produces a draft release with every asset.

- [ ] Create `.github/workflows/release.yml` triggered by tags `v*`, running `scripts/check.ps1`, `scripts/publish.ps1` and `dotnet pack`, then `gh release create` with the archives, checksums and packages and the tag's `CHANGELOG.md` section as notes. Done when: the workflow file passes `gh workflow view release.yml`.
- [ ] Fail the workflow when the tag does not equal `v` plus the `Version` in `Directory.Build.props`. Done when: a mismatched tag run fails at that step.
- [ ] Push the tag `v0.1.0-rc.1` and inspect the release. Done when: `gh release view v0.1.0-rc.1` lists both archives, both checksum files and both packages, marked pre-release.
- [ ] Document the release steps in `CONTRIBUTING.md`. Done when: it says to update `CHANGELOG.md`, set `Version` and push the tag.
- [ ] Commit: `ci: publish a release from a tag`

**Test checkpoint:** `gh release view v0.1.0-rc.1 --json assets --jq '.assets | length'` prints `6`.

## Verification

- A machine without the .NET SDK runs the published `wobble.exe` and serves a disc.
- `gh release view v0.1.0-rc.1` shows the archives, checksums and packages.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
