# Security policy

## Supported versions

The latest release is the supported one. Fixes go into a new release; there are no long-lived maintenance branches.

| Version | Supported |
|---|---|
| 1.0.x | yes |
| older | no |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Use GitHub's private reporting: **[Security → Report a vulnerability](https://github.com/qorion-net/duble/security/advisories/new)**.
You will get a reply within a few days. If the report is valid, you will be credited in the release notes unless
you prefer otherwise.

Useful in a report: what an attacker controls (a pack, an `.rpf` file, a project file?), what happens, and a
minimal file that reproduces it.

## What counts as a vulnerability here

Duble is an offline desktop app that reads untrusted files (clothing packs downloaded from the internet) and
writes only inside folders the user picked. Things worth reporting:

- a crafted `.rpf` / `.ydd` / `.ytd` / `.duble` file that makes Duble write outside the source folder or the bin
  (path traversal), execute code, or overwrite unrelated files,
- anything that makes Duble delete data instead of moving it to the bin,
- code execution through the interface layer (WebView2) — the interface is local and has no network access, so
  any way to load remote content into it is interesting.

Not vulnerabilities: SmartScreen warnings about the unsigned executable, a crash on a corrupted pack (report it
as a normal bug), or antivirus false positives on the self-extracting single-file build.

## What Duble does with your data

No telemetry, no accounts, no network calls. Settings live in `%AppData%\Bobadu\Duble\`, projects where you put
them, thumbnails and previews in the `.duble.cache` folder next to the project. `.rpf` archives are opened
read-only and never written to.
