# Security Policy

## Supported versions

Wobble is pre-alpha and has no published release yet. Once releases begin, security fixes go into the latest release; older releases and unreleased builds from `main` are best effort.

## What to keep in mind

Wobble is a test tool. Its iSCSI target and its control API have no authentication and listen on loopback only by default. Binding either to another address (`--bind`) exposes an unauthenticated drive and an unauthenticated API that can read image files the process can read, so do that only on a network you trust. A report that this is possible is not a vulnerability; a way to reach either from another machine in the default configuration is.

## Reporting a vulnerability

**Please do not report security vulnerabilities through public issues, discussions or pull requests.**

Report them privately through GitHub's private vulnerability reporting:

1. Open the [Security tab](https://github.com/rizonesoft/Wobble/security) of this repository.
2. Choose **Report a vulnerability** ([direct link](https://github.com/rizonesoft/Wobble/security/advisories/new)).
3. Fill in the advisory form.

Helpful details to include:

- The version or commit, and the operating system
- The type of issue, for example memory exhaustion or a crash from a crafted iSCSI PDU, CDB, image file or control API request
- Steps to reproduce, and a sample file or packet capture if one triggers it
- The impact as you understand it

You can expect an acknowledgement within a week. Fixes are released as soon as they are ready, with credit to the reporter unless you ask otherwise.
