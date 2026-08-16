# Duble — by Bobadu

**Duplicate clothing finder for GTA V (Legacy and Enhanced) — fast, safe, with previews.**

[Wersja polska → README.pl.md](README.pl.md) · Website: <https://qorion.net/duble> · Source: <https://github.com/qorion-net/duble>

Duble scans clothing packs (folders, `.rpf` archives, FiveM resources), finds garments that are **the same model
with the same textures**, shows them side by side in 2D and 3D and lets you decide which version to keep. Nothing
disappears without your decision: rejected files are **moved** to a bin next to the source, and one click
(**Undo**) brings them back.

> Rule #1: **duplicate = same model + same textures**. The same model with a different texture is a **retexture** —
> a separate garment that Duble never proposes for removal.

## Requirements

- Windows 10/11 (64-bit).
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — always present on
  Windows 11, usually on Windows 10 (Edge/Office install it). If missing, Duble shows a message with a link.
- Game files are not required — Duble reads clothing packs (`.ydd` / `.ytd`, also inside `.rpf`).

## Download and run

1. Download `Duble.exe` (a single file, ~90 MB — .NET included) from the repository **Releases**.
2. Run it. Windows SmartScreen may warn that the app is unsigned: "More info → Run anyway".
3. Program settings go to `%AppData%\Bobadu\Duble\`, projects by default to `Documents\Duble\`.

## How to use (step by step)

1. **Start → New project.** A project is your set of packs + comparison results + decisions in one `.duble` file
   (a `.duble.cache` folder next to it holds thumbnails and previews — safe to delete, it rebuilds).
2. **Sources.** Add a folder with packs, an `.rpf` file or a FiveM resource folder (drag & drop from Explorer works;
   "Find games" proposes mod folders of an installed GTA V). Click **Index all** — Duble reads models and textures,
   computes fingerprints (model shape, texture look and colour) and immediately compares everything with everything.
   Re-indexing is incremental (changed files only).
3. **Duplicates.** A list of **groups** — garments considered the same. Verdicts:
   - **Duplicate** — same model and same textures (Duble proposes keeping the better version: higher resolution,
     mipmaps, more colour variants, correct format, more LODs — the 0–100 "quality score"),
   - **Duplicate (superset)** — same model, one texture set contains the other,
   - **Needs review** — similar but not identical model (look yourself),
   - **Retexture** — same model, different textures (nothing proposed for rejection).
   Click a group → **comparison card**: textures side by side (matching "same image" pairs linked; click = large
   preview with an A↔B wipe slider), **Model (3D)** tab (synchronised orbit, "overlay A on B", variants, wireframe).
   Decisions: **Keep this**, **Reject / Keep** per item, **Not a duplicate**, a note. Decisions are saved in the
   project — nothing happens on disk yet.
4. **Apply** (bar at the bottom of Duplicates). The dialog shows exactly which files will be **moved** and where
   (the `_odrzucone` bin next to the source or a chosen folder). Files shared with a garment that stays and files inside
   `.rpf` archives are skipped. After applying, Duble re-indexes and compares again on its own.
5. **History.** Every Apply is an entry: **Undo all** or a single item. Export of an **HTML report** (self-contained,
   with thumbnails) and **CSV** lives here too.
6. **Catalog.** All indexed garments as a thumbnail grid with filters (source, slot, Legacy/Enhanced, "with problems":
   no mipmaps, BC1 with alpha; "in duplicate groups"). Click → item card with textures and 3D.
7. **Settings.** Language (PL/EN), theme, bin, **comparison thresholds** (with an explanation of where they come from;
   a change re-runs the comparison), **Calibration** (distance distributions on your catalog as charts — do the
   thresholds fit your packs?), cache.

### Legacy, Enhanced, FiveM, archives

- Duble reads both formats (Legacy `.ydd` v165 / `.ytd` v13, Enhanced v159 / v5) and badges every item.
- FiveM resources: files from `stream\` (also `name^file.ydd`) and `.rpf` archives inside.
- **`.rpf` archives are read-only** — Duble never writes into them. To tidy a pack that lives in an archive use
  **Sources → "…" → Unpack to folder**: a copy is created with the archives laid out as folders (`name.rpf\`, RSC7 files
  like an OpenIV/CodeWalker export), which you can add as a source, tidy (Apply/Undo) and repack with your own tool.

### What happens after removing garments? (numbering)

The game numbers garments in a slot consecutively (`jbib_000`, `jbib_001`…). Removing one leaves a "gap" — nothing
shows under that number, the rest keeps working. FiveM/DLC packs also list garments in `.ymt`/`.meta` — the safest
route is to rebuild it with the tool the pack was made with. Details: Apply dialog → "What does that mean?".

## Building from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), git, Windows.

```powershell
git clone https://github.com/qorion-net/duble
cd duble
.\build.ps1            # clones CodeWalker (dexyfex, pinned commit) into ..\CodeWalker, builds, runs tests
.\build.ps1 -Publish   # single-file publish\Duble.exe (self-contained, win-x64)
.\build.ps1 -Uruchom   # build and run the app in developer mode (UI from the ui\ folder, DevTools)
```

Layout:

| Folder | What |
|---|---|
| `Duble.Core` | engine: indexing, fingerprints, comparison, decisions, apply/undo, report, unpacking |
| `Duble.App` | WPF + WebView2 application; interface in `ui\` (HTML/CSS/JS, three.js), i18n `ui\i18n\pl.json` / `en.json` |
| `Duble.Cli` | command-line tool (`duble indeks / porownaj / raport / zastosuj / cofnij / kalibruj …`) |
| `Duble.Tests` | xunit tests (engine, bridge, commands, i18n; tests on real packs are skipped when the data is absent) |

The engine uses [CodeWalker.Core](https://github.com/dexyfex/CodeWalker) (reading `.rpf`/`.ydd`/`.ytd`),
[BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) (BC7) and [three.js](https://threejs.org) (3D).

## License

MIT — see [LICENSE](LICENSE). Designed and published by **Bobadu**.
