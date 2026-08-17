# Changelog

All notable changes to Duble are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/qorion-net/duble/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/qorion-net/duble/releases/tag/v1.0.0
