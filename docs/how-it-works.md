# How Duble decides that two garments are the same

This is the reasoning behind the engine (`Duble.Core`). It is written for anyone who wants to trust the verdicts,
argue with them, or change the thresholds.

The rule the whole tool is built on:

> **A duplicate is the same model _and_ the same textures.**
> The same model with different textures is a **retexture** — a separate garment. Duble never proposes removing it.

## 1. Indexing: what is read from a pack

A source can be a folder of packs, a single `.rpf` archive or a FiveM resource. Duble walks it and picks up
clothing pairs: a model (`.ydd`) plus its textures (`.ytd`), including files inside `.rpf` archives and FiveM's
`name^file.ydd` convention. Every item is identified by
`pack | container | slot | number | suffix` (for example `dlcpacks | studio02_female.rpf | feet | 9 | u`), which is
what makes decisions survive re-indexing.

Reading is done with [CodeWalker.Core](https://github.com/dexyfex/CodeWalker) in gen9 mode — in that mode both
Enhanced (v159 / v5) and Legacy (v165 / v13) resources parse correctly, because the version comes from the RSC7
header. Textures compressed with BC1–BC5 are decoded by CodeWalker, BC7 (roughly 5 % of textures in packs found
online) by [BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET).

Everything expensive is cached next to the project (`.duble.cache`): thumbnails, mesh previews and the
fingerprints below. Re-indexing only re-reads files whose size or timestamp changed.

## 2. Fingerprints

**Model** — three things, in growing strictness:

- counts: vertices, triangles, LOD levels, bounding box,
- a shape histogram: vertex positions normalised into the bounding box and binned, so the same mesh exported
  twice lands in the same place,
- a hash of the vertex positions.

**Texture** — two independent signals per texture:

- a 256-bit perceptual hash (structure of the image), compared with Hamming distance,
- an 8×8 grid of average colour, compared as the mean per-channel difference (0–255).

Two signals are needed because each one alone is wrong in a predictable way: two different black garments have
almost the same colour, and colour variants of one garment have almost the same structure.

## 3. Thresholds and where they come from

The defaults were measured with `duble kalibruj` on a catalog of **1132 garments / 9437 textures** (15 Aug 2026).
Settings → Calibration re-runs that measurement on *your* catalog and draws the distributions, so you can check
whether the defaults fit your packs.

| Signal | Identical files | Colour variants of one garment | Random pairs | Default threshold |
|---|---|---|---|---|
| Texture, perceptual hash (256 bits) | 0 | median 26, p05 = 2 | p01 = 92, median 128 | **20** |
| Texture, colour (0–255 per channel) | 0 | median 13.7, p01 = 0.08 | p01 = 3.05 | **3.0** |
| Model, shape histogram (L1) | 0 for the same mesh | — | p05 = 0.112, median 0.254 | **0.02** for "identical" |

Two consequences of those numbers are baked into the engine:

- **Textures must match on both signals at once.** The hash alone would merge colour variants (p05 = 2 is below
  any useful threshold); colour alone would merge everything dark. A flat texture (variance below 3.0) is judged
  by colour only, with a tighter limit (1.0), because a perceptual hash of a flat image is noise.
- **"Identical geometry" needs equal triangle and vertex counts, not just a close histogram.** Measured
  counter-example: `hand_000` (3560 triangles) and `hand_025` (2480 triangles) sit 0.007 apart, because every
  glove has a similar silhouette. Without the counts, deduplication would happily delete different gloves.

## 4. From fingerprints to a verdict

Textures of the two items are matched into pairs first; from that Duble computes **coverage** — how much of A's
texture set is present in B, and vice versa (full coverage = 95 %).

| Model | Textures | Verdict |
|---|---|---|
| identical | both sides fully covered | **Duplicate** — the classic case |
| identical | one side fully covered, the other has extra | **Duplicate (superset)** — the richer one wins |
| identical | partial overlap (≥ 50 %) | **Needs review** — look yourself |
| identical | little or no overlap | **Retexture** — a separate garment, never rejected |
| similar (not identical) | both sides fully covered | **Needs review** |
| similar | partial overlap | **Needs review** |
| similar | different textures | not reported at all — just a different garment |

Pairs marked *duplicate* or *duplicate (superset)* are then merged into **groups** (transitively, so a set of
three copies is one group, not three pairs), and each group gets a proposed winner.

## 5. The quality score (0–100)

The winner is proposed by a score with a visible breakdown, so you can see *why* something won:

| Part | Points | What it measures |
|---|---|---|
| Resolution | 40 | median texture pixel count, on a log scale where 1024×1024 is a full score |
| Mipmaps | 20 | share of textures that have more than one mip level (28 % of textures in the measured catalog had only one — those shimmer in game) |
| Colour variants | 20 | number of textures, capped at 20 — a richer choice in the character menu |
| Format | 10 | penalty for BC1 with alpha (1-bit transparency) |
| LODs | 10 | number of LOD levels, full score at 3 |

The score only *proposes*. The decision is yours, it is stored in the project, and it can be overridden per item
("keep this", "reject", "not a duplicate", plus a note).

## 6. Applying decisions

Apply never deletes. It **moves** rejected files to a bin — `_rejected` next to the source or a folder you pick —
keeping the relative path, and writes an undo journal. Two rules are enforced while planning:

- a file shared with a garment that stays is left alone,
- files inside `.rpf` archives are skipped, because Duble opens archives read-only. Use **Unpack to folder** to
  get a writable copy of such a pack.

History lists every apply and can undo it as a whole or item by item.

## 7. What Duble does not do

- It does not rebuild `.ymt` / `.meta`. Removing a garment leaves a gap in the slot numbering; that is harmless in
  game, but the pack's own metadata still lists the old numbering — rebuild it with the tool the pack was made
  with. The Apply dialog explains this in "What does that mean?".
- It does not write into `.rpf` archives.
- It does not phone home: no telemetry, no accounts, no network access.
