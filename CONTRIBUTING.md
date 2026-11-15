# Contributing to Wobble

Thank you for looking. Wobble is pre-alpha and built to a written plan, so the most useful contributions right now are bug reports with a command trace, captures from real drives once the capture tool exists, and fixes to answers that differ from the MMC specification.

## Build and test

You need the .NET 11 SDK, PowerShell 7 and Python 3.11 or later.

```powershell
pwsh scripts/check.ps1              # build with warnings as errors, unit tests, plan validation
pwsh scripts/check.ps1 -EndToEnd    # also the Windows end-to-end tests
```

The end-to-end tests need Windows and the iSCSI initiator attached to Wobble. Start `wobble`, run `tools\attach.ps1` from an elevated PowerShell once per boot, then run the tests; they start and stop the emulator themselves on ports 3260 and 3261.

## What a change needs

- **A test that fails without it.** Every answer the drive gives is checked byte for byte in `tests/Wobble.Tests`, against the MMC or SPC text it comes from. Name the clause in the test or a comment.
- **The fix in the layer that owns it.** `Wobble.Core` knows nothing about sockets, and the iSCSI target knows nothing about discs. A response Windows rejects is fixed where it is built, not patched on its way out.
- **The trace, when Windows is involved.** `GET /trace` shows what was sent. Attach it to a bug report or a pull request that changes behaviour Windows sees.
- **No new dependency without a reason.** The emulator uses the .NET base libraries and ASP.NET Core only.

## How the work is planned

The plan lives in `todo/` as small sections with checkable steps, and `todo/implementation-plan.md` is generated from it. A section lists what to build, what proves it and what it depends on. If your change belongs to a planned section, say which (`D01 T02 §6`) in the pull request. The format is described in `todo/README.md`.

## Pull requests

- Branch from `main`, keep the change to one subject, and write the commit message as a short imperative sentence.
- Run `pwsh scripts/check.ps1` before you push. CI runs the same file.
- Prose in Markdown is one line per paragraph, with no em dashes or en dashes.
- By contributing you agree that your contribution is licensed under the GPL-3.0, the licence of the project.

## Reporting a bug

Use the bug report form. The most useful things to include are the software that was talking to the drive, the image or media that was loaded, and the command trace around the failure.

## Conduct

This project follows the [Code of Conduct](CODE_OF_CONDUCT.md).
