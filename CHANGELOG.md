# Changelog

All notable changes to Duble are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **The desktop application is in English throughout its C# code**, as the engine and the command line already
  were: `Sesja` → `Session`, `Mostek` → `Bridge`, `Komendy` → `Commands`, and one class per group of commands
  instead of one long registration method. The interface (`Duble.App/ui`) is unchanged and so is the vocabulary
  it speaks over the bridge; both move together in the next step.
- **Settings written by 1.0.0 are read and carried over.** `settings.json` now uses English names; the file
  from an earlier version is still read, so the language, the theme, the window position and the recent
  projects survive the update. The file is rewritten in the new shape the next time Duble exits.
- **The file dialogs and the WebView2 error follow the language.** They were Polish whatever the interface was
  set to.
- **The bin folder is `_rejected`**, not `_odrzucone`. A bin written by 1.0.0 is not recognised: the indexer
  would read it as a pack of its own, so rename it by hand if you have one.
- **Command line in English.** `indeks / porownaj / raport / zastosuj / cofnij / kalibruj` are now
  `index / compare / report / apply / undo / calibrate`, alongside `refresh / list / preview / textures /
  ytd / glb / hollow / obj`. `duble help` lists them and `duble help <command>` explains one. An unknown
  option is now an error rather than something silently ignored.
- **Working files** default to a `duble` folder in the current directory, or `DUBLE_HOME`, instead of paths
  guessed relative to the executable.
- The catalog cache file is `catalog.json` and the apply history lives in `history\`, both inside
  `<project>.duble.cache`. An existing project re-indexes itself once on first open.

### Fixed

- **An apply whose undo log cannot be written says so.** The files had already moved and the failure was
  swallowed, leaving an operation with no way back and nothing on screen to say why.
- **A project that cannot be saved reports it** instead of leaving the change in memory only.
- **Two commands arriving at the same moment can no longer both believe they started a job.** The check for
  "is something already running" and the taking of the slot are now one step.
- **The same catalog compares the same way twice.** Garments sharing a slot and number but differing in suffix
  were ordered by a dictionary's enumeration, which .NET randomises per process, so `comparison.json` differed
  between runs over identical input — pairs came out with their two sides swapped, and their coverages with
  them. The winner of a single-pair group had no tie-break and followed the same order.
- **A texture caption no longer bursts out of its tile.** The variant letter of a prop texture
  (`p_ears_diff_017_a.ytd`, with no race after the letter) failed to parse, so the whole file name was printed
  into a 96 px tile; the 3D variant buttons came out empty for the same reason.
- The start screen shows a project's name again.
- Indexing reports files that are clothing and would not read separately from files that are not clothing: the
  first kind means the catalog is quietly incomplete.
- Report thumbnails no longer accumulate for the life of the process, and the "no preview" counters no longer
  carry from one report into the next.
- `duble` and the desktop application no longer claim the same assembly file name.

## [1.0.0] — 2026-08-17

First public release.

### Added

- **Projects** — a `.duble` file holds your sources, comparison results and decisions; the `.duble.cache` folder
  next to it keeps thumbnails and previews and can be deleted at any time.
- **Sources** — folders with packs, single `.rpf` archives and FiveM resources (`stream/`), added by picker or
  drag & drop; "Find games" locates an installed GTA V. Indexing is incremental — only changed files are re-read.
- **Both game formats** — GTA V Legacy (`.ydd` v165 / `.ytd` v13) and Enhanced / gen9 (v159 / v5), each item
  badged with the format it came from. BC1–BC5 textures are decoded by CodeWalker, BC7 by BCnEncoder.Net.
- **Comparison** — model fingerprint (counts, shape histogram, vertex-position hash) plus texture fingerprint
  (256-bit perceptual hash and an 8×8 colour grid) produce four verdicts: **duplicate**, **duplicate (superset)**,
  **needs review** and **retexture**. Retextures are never proposed for removal.
- **Quality score (0–100)** with a visible breakdown — resolution, mipmaps, colour variants, texture format and
  LODs — used to propose which copy to keep.
- **Comparison card** — textures side by side with matching pairs linked, a full-size A/B comparison with a wipe
  slider, and a **Model (3D)** tab: models next to each other with synchronised orbit, or overlaid with an A→B
  blend slider, per-side variant choice, wireframe and a light background.
- **Catalog** — every indexed garment as a virtualised thumbnail grid with filters (source, slot, format,
  "with problems", "in duplicate groups") and an item card with textures, quality and 3D.
- **Apply** — a preview of exactly which files move where, then a move to the bin (`_odrzucone` next to the
  source or a folder you choose). Files shared with a garment that stays and files inside `.rpf` archives are
  skipped. Re-index and re-compare run automatically afterwards.
- **History and Undo** — every apply is an entry that can be undone as a whole or item by item.
- **Reports** — a self-contained HTML report with thumbnails, and a CSV export.
- **Unpack to folder** — a copy of a source with `.rpf` archives laid out as folders (RSC7 files, like an
  OpenIV/CodeWalker export) so archived packs can be cleaned up too. Archives themselves are never written to.
- **Settings** — Polish and English interface, light/dark/system theme, bin location, comparison thresholds with
  the reasoning behind their defaults, cache management, and **Calibration**: distance distributions measured on
  your own catalog, drawn as charts, with a suggested threshold.
- **Command line** (`duble`) — `indeks`, `porownaj`, `raport`, `zastosuj`, `cofnij`, `kalibruj` for scripted use.

[Unreleased]: https://github.com/Bobadu/duble/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Bobadu/duble/releases/tag/v1.0.0
