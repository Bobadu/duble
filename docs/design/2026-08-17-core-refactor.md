# Duble.Core refactor — design

**Status:** approved 2026-08-17
**Scope:** stage 1 of a four-stage refactor. This document specifies stage 1 (`Duble.Core`) in full and sketches stages 2–4.

## Why

`Duble.Core` works and is well tested (116 tests, a golden master over real packs), but it reads like a private
notebook rather than a library other people can contribute to:

- Every identifier, comment and XML doc is in Polish, including the keys written to disk (`katalog.json`,
  `duble.json`, the `.duble` project file). A contributor who does not read Polish cannot follow it.
- 23 files sit flat in one namespace `Duble`, with no separation between domain model, IO, and algorithms.
- Behaviour lives in static classes with no seams: `Indeks`, `Porownanie`, `Odciski`, `Zastosowanie`, `Raport`,
  `Kalibracja`. Nothing can be substituted in a test.
- Hidden global state: `Zrodla.Otwarte` is a `static Dictionary<string, RpfFile>` of opened archives — not
  thread-safe, never released, and stale after `Apply` moves files. `RpfManager.IsGen9` is set from a
  `[ModuleInitializer]`, so merely loading the assembly mutates a global (and raises CA2255 on every build).
- Verdicts are magic strings (`"DUPLIKAT"`), file-move states are magic strings (`"przenies"`), the game format
  is a `bool Gen9`.
- Persistence is baked into the model (`Katalog.Wczytaj/Zapisz`, `Projekt.Zapisz`, `Cofka.Wczytaj/Zapisz`).
- Failures are swallowed: `catch { }` appears throughout indexing, decoding and preview building.
- Progress and logging are ad-hoc delegates (`Action<string> log`, `Action<Postep> postep`) threaded by hand.

## Goals

1. English throughout: identifiers, comments, XML docs, and the JSON written to disk.
2. A structure a newcomer can navigate: one folder per stage of the pipeline, one responsibility per file.
3. Seams: services behind interfaces, resolved from `Microsoft.Extensions.DependencyInjection`.
4. Expected failures as values (`Result<T>`), exceptions reserved for programmer error. No silent `catch { }`.
5. Identical behaviour. The golden master must stay green at every step.

## Non-goals

- No change to comparison behaviour, thresholds, or scoring. Any difference in the golden master is a bug.
- No new features.
- No interfaces on data. `Garment`, `Catalog`, `Thresholds`, `Result` are types, not services.
- No third-party dependency beyond the two first-party `Microsoft.Extensions.*.Abstractions` packages.

## Global constraints

- Target framework `net10.0`, SDK pinned by `global.json` (10.0.100, rollForward latestMinor).
- **The garment id format is frozen:** `pack|container|slot|number|suffix`. Group ids are a SHA-256 over the
  sorted member ids. Changing either would orphan every decision the user has made. Tests must assert this.
- The i18n dictionaries (`Duble.Core/i18n/{pl,en}.json`) and the reason codes (`SAME_MODEL_SAME_TEX`, …) do not
  change in stage 1 — the golden master compares formatted Polish text.
- CodeWalker types must not appear in Core's public API. Core takes and returns bytes and its own types.
- User-visible text goes through i18n, never a literal in code.
- Every pull request builds and passes the whole suite. `main` is protected; the maintainer merges.
- Commits in English, signed with the maintainer's SSH key.

## Target structure

Namespace `Duble.Core`, one folder per concern:

```
Model/          Garment, TextureInfo, GeometryFingerprint, Catalog, GameFormat
Naming/         ClothingFileNameParser, ModelFileName, TextureFileName
Sources/        ISourceReader, FolderSourceReader, ArchiveSourceReader, ISourceReaderFactory,
                IArchiveCache, RpfArchiveCache, IArchiveExtractor, RpfArchiveExtractor,
                SourceEntry, SourceKind
Fingerprints/   IGeometryFingerprinter, ITextureFingerprinter, ThumbnailRequest,
                PerceptualHash, ColorSignature, Distances
Indexing/       IGarmentIndexer, GarmentIndexer, IndexOptions, IndexReport
Comparison/     IDuplicateFinder, DuplicateFinder, Verdict, ComparisonResult, DuplicateGroup, GarmentPair,
                Thresholds, IQualityScorer, QualityScorer, QualityScore, Reason, IReasonFormatter
Decisions/      Decision, Resolution, IResolutionService, ResolutionService
Projects/       Project, ProjectSource, ProjectSettings, SourceKind, SourceFormat,
                IProjectStore, JsonProjectStore
Storage/        ICatalogStore, JsonCatalogStore, IComparisonStore, JsonComparisonStore, IUndoStore, JsonUndoStore
Apply/          IApplyPlanner, ApplyPlanner, IApplyExecutor, ApplyExecutor, ApplyPlan, PlannedGarment,
                FileMove, FileMoveState, BinTarget, UndoLog, FileRestore, UndoOutcome
Reporting/      IHtmlReportBuilder, HtmlReportBuilder, ICsvExporter, CsvExporter, ReportOptions
Calibration/    ICalibrator, Calibrator, CalibrationReport, Distribution
Formats/        CodeWalkerRuntime, Rsc7Header, ITextureDecoder, TextureDecoder, DecodedTexture,
                ITexturePreviewBuilder, TexturePreviewBuilder, PngWriter, GlbWriter,
                IMeshPreviewBuilder, MeshPreviewBuilder
Results/        Result, Result<T>, Error, ErrorCodes
Time/           IClock, SystemClock
CoreServiceCollectionExtensions.cs, ProgressReport.cs
```

### Composition

```csharp
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddDubleCore(this IServiceCollection services);
}
```

Every service is registered as a singleton — they are stateless apart from `RpfArchiveCache`, which owns the
open archive handles and is `IDisposable`. `AddDubleCore` also constructs `CodeWalkerRuntime`, whose constructor
sets `RpfManager.IsGen9 = true` once. That replaces the `[ModuleInitializer]`: the global is set when the
container is built, not when the assembly happens to load, and CA2255 disappears.

`Duble.App` and `Duble.Cli` build a container at startup and resolve what they need. Neither calls a static
engine method any more.

### Service interfaces

```csharp
public interface IGarmentIndexer
{
    Result<IndexReport> Index(string sourcePath, string packName, IndexOptions options,
                              IProgress<ProgressReport>? progress = null, CancellationToken ct = default);
}

public interface IDuplicateFinder
{
    ComparisonResult Find(Catalog catalog, Thresholds thresholds,
                          IProgress<ProgressReport>? progress = null, CancellationToken ct = default);
}

public interface IQualityScorer      { QualityScore Score(Garment garment); }

public interface IResolutionService
{
    Resolution Resolve(DuplicateGroup group, Decision? decision);
    int CarryOver(IDictionary<string, Decision> decisions,
                  IEnumerable<DuplicateGroup> previous, IEnumerable<DuplicateGroup> current);
}

public interface IProjectStore       { Result<Project> Load(string path); Result Save(Project project); }
public interface ICatalogStore       { Catalog Load(string path);  Result Save(Catalog catalog, string path); }
public interface IComparisonStore    { ComparisonResult Load(string path); Result Save(ComparisonResult result, string path); }
public interface IUndoStore          { Result<UndoLog> Load(string path); Result Save(UndoLog log, string path); }

public interface ISourceReaderFactory { Result<ISourceReader> Create(string path); }
public interface ISourceReader : IDisposable { IReadOnlyList<SourceEntry> Enumerate(CancellationToken ct = default); }
public interface IArchiveCache : IDisposable { Result<byte[]> Read(string logicalPath); void Clear(); }

public interface IGeometryFingerprinter { Result<GeometryFingerprint> Compute(byte[] modelBytes); }
public interface ITextureFingerprinter  { Result<TextureInfo> Compute(byte[] textureBytes, ThumbnailRequest? thumbnail = null); }
public interface ITextureDecoder        { Result<DecodedTexture> Decode(byte[] textureBytes, string? name = null, int maxSide = 1024); }
public interface ITexturePreviewBuilder { Result<byte[]> RenderPng(byte[] textureBytes, string? name = null, int maxSide = 1024); }
public interface IMeshPreviewBuilder    { Result<byte[]> BuildGlb(byte[] modelBytes, byte[]? textureBytes = null); }

public interface IApplyPlanner  { ApplyPlan Plan(Catalog catalog, IEnumerable<string> rejectedIds, Func<Garment, BinTarget> target); }
public interface IApplyExecutor
{
    Result<UndoLog> Execute(ApplyPlan plan, string description,
                            IProgress<ProgressReport>? progress = null, CancellationToken ct = default);
    Result<UndoOutcome> Undo(UndoLog log, IReadOnlyCollection<string>? garmentIds = null, CancellationToken ct = default);
}

public interface IHtmlReportBuilder { Result Build(Catalog catalog, ComparisonResult result, string outputPath, ReportOptions options); }
public interface ICsvExporter       { string Export(Catalog catalog, ComparisonResult result, ReportOptions options); }
public interface ICalibrator        { CalibrationReport Run(Catalog catalog, Thresholds thresholds, CancellationToken ct = default); }
public interface IReasonFormatter   { string Format(Reason reason, string language); }
public interface IClock             { DateTimeOffset Now { get; } }
```

Indexing and comparison stay **synchronous**. They are CPU-bound and already parallel internally; wrapping them
in `Task` would be async-over-sync. Callers offload (the app already does, through `JobRunner`).

`IArchiveCache.Clear()` is called when a project closes and after every apply — the current static cache keeps
handles to archives whose files have since moved, which is a latent bug the refactor removes.

## Naming

| Now | After | Note |
|---|---|---|
| `Pozycja` | `Garment` | one garment: model plus all its textures |
| `Tekstura` | `TextureInfo` | |
| `Geo` | `GeometryFingerprint` | |
| `Katalog` | `Catalog` (`Garments`) | |
| `Odciski` | `GeometryFingerprinter` + `TextureFingerprinter` | one class did two unrelated jobs |
| `Indeks` | `GarmentIndexer` | |
| `Porownanie` | `DuplicateFinder` | |
| `Progi` | `Thresholds` | |
| `Powod` | `Reason` | code plus parameters, unchanged |
| `Punktacja` | `QualityScore` | |
| `Grupa` / `Para` | `DuplicateGroup` / `GarmentPair` | |
| `WynikPorownania` | `ComparisonResult` | |
| `Decyzja` / `Rozstrzygniecie` | `Decision` / `Resolution` | |
| `Zastosowanie` | `ApplyPlanner` + `ApplyExecutor` | 378 lines doing planning, execution and undo |
| `PlanZastosowania` / `PozycjaPlanu` | `ApplyPlan` / `PlannedGarment` | |
| `Cofka` / `Przeniesienie` / `RuchPliku` | `UndoLog` / `FileRestore` / `FileMove` | |
| `Projekt` / `ZrodloProjektu` / `UstawieniaProjektu` | `Project` / `ProjectSource` / `ProjectSettings` | |
| `Nazwy` | `ClothingFileNameParser` | |
| `Raport` | `HtmlReportBuilder` + `CsvExporter` | |
| `Kalibracja` / `Rozklad` / `WynikKalibracji` | `Calibrator` / `Distribution` / `CalibrationReport` | |
| `Zrodla` | `RpfArchiveCache` | plus a real lifetime |
| `Rozpakowanie` | `RpfArchiveExtractor` | unpacking an `.rpf` into a folder of RSC7 files |
| `Format` | `CodeWalkerRuntime` | |
| `Rsc7` / `Png` / `Glb` / `Tekstury` / `Podglad3D` | `Rsc7Header` / `PngWriter` / `GlbWriter` / `TextureDecoder` / `MeshPreviewBuilder` | |
| `Teksty` | `IReasonFormatter` + internal dictionary loader | |
| `OpcjeIndeksu` / `Postep` | `IndexOptions` / `ProgressReport` | |

Field-level renames follow the same rule and are listed with each task in the implementation plan. Three that
matter because they appear everywhere:

- `Pozycja.Typ` → `Garment.Slot`. The four-letter R* code (`jbib`, `uppr`, `feet`) is called a *slot* in the
  interface and in the README; Core should use the same word.
- `bool Gen9` → `enum GameFormat { Legacy, Enhanced }`, matching the README's vocabulary.
- `Znacznik` → `ChangeStamp` (size and timestamp used for incremental indexing).

### Magic strings become enums

```csharp
public enum Verdict     { Duplicate, Superset, NeedsReview, Retexture }
public enum FileMoveState { Move, Shared, InArchive, Missing }
public enum SourceKind  { Folder, Archive, FiveMResource }
public enum GameFormat  { Legacy, Enhanced }
public enum SourceFormat { Unknown, Legacy, Enhanced, Mixed }
```

`Verdict` keeps its declaration order — the app sorts groups by it (duplicate first, retexture last).

## On-disk formats

JSON written by Core uses `JsonNamingPolicy.CamelCase` and `JsonStringEnumConverter` (camelCase), so a verdict
reads `"duplicate"` and a garment reads `{"id": …, "packName": …, "slot": …}`.

**Duble reads exactly the version it writes.** The `.duble` project file goes to version 2 with English keys,
and a file at any other version comes back as `project.unsupported_version` rather than being guessed at. There
is no migration code: version 1.0.0 has no users with projects to carry over, and a translation layer kept for
nobody is a translation layer nobody maintains. Should a future version need one, it will be written then,
against a format that is already in English.

**The catalog and `duble.json`** live in `<project>.duble.cache`, which the README already documents as
disposable, so the catalog is renamed `katalog.json` → `catalog.json` along with everything else.
`Catalog.Version` goes 2 → 3; a catalog with a lower version or unreadable content loads as empty, which makes
the app re-index once on first open. Nothing to migrate — indexing produces it.

**Undo logs** (`<cache>/history/*.json`) describe files that physically moved, so they are the one thing worth
reading back. They are rewritten with the rest of Apply, and the version written before that rewrite is not
supported either — for the same reason.

## Errors

```csharp
public readonly record struct Error(string Code, string Message);

public readonly struct Result
{
    public bool IsSuccess { get; }
    public Error Error { get; }
    public static Result Ok();
    public static Result Fail(string code, string message);
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }                    // InvalidOperationException when accessed on a failure
    public Error Error { get; }
    public static Result<T> Ok(T value);
    public static Result<T> Fail(string code, string message);
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onError);
}
```

Codes are constants on `ErrorCodes`, one per failure the caller can act on:
`project.unreadable`, `project.unsupported_version`, `source.missing`, `source.unreadable`,
`archive.unreadable`, `model.unreadable`, `texture.undecodable`, `catalog.unwritable`, `apply.io`,
`report.unwritable`. The app maps them to bridge error codes and i18n keys; the CLI prints them.

`Result` covers expected failures — a missing file, a texture format that cannot be decoded, a locked target.
Programmer errors keep throwing. Every existing `catch { }` becomes either a failed `Result` or a logged
warning with the file that caused it; nothing disappears silently. Files skipped during indexing move from a
log line into `IndexReport.SkippedFiles`, so the app can show them.

## Logging, progress, cancellation

- `ILogger<T>` from `Microsoft.Extensions.Logging.Abstractions` replaces every `Action<string> log` parameter.
- `IProgress<ProgressReport>` replaces `Action<Postep>`.
- `CancellationToken ct = default` is the last parameter of every long-running method.

## Compiler settings

`Duble.Core` and `Duble.Tests` get `TreatWarningsAsErrors=true` in PR 1. `Nullable=enable` and
`ImplicitUsings=enable` are project-wide switches that would light up every not-yet-rewritten file at once, so
they flip in PR 5, once the last file has been rewritten; until then every new or rewritten file opts in with
`#nullable enable` on its first line. Nullability is expressed in the model: `Garment.Geometry` is non-null (an empty fingerprint when a model cannot
be read), `TextureInfo.PerceptualHash` is `ulong[]?` (null when undecodable), `Decision?` is nullable in
`IResolutionService.Resolve`.

Two existing test warnings become errors and are fixed in stage 1 PR 1: CA2022 (inexact `Stream.Read`) in
`SesjaPorownanieTests`, and xUnit2009/xUnit2013 in `GrupyKomendyTests` and `ZastosowanieTests`.

## Testing

The golden master (`Duble.Tests/golden/`, currently `ZlotyWzorzecTests`) is the contract for the whole refactor:
the same sources must produce the same groups, verdicts, winners, scores and formatted Polish reasons. The
golden JSON files are **not** regenerated at any point. The test's projection maps the new types onto the old
golden shape.

Added along the way, where the new seams make it possible:

- `GarmentIndexer` against a fake `ISourceReader` — no disk, no packs.
- `JsonProjectStore`: a saved project reads back identically, and any other version is refused.
- `JsonUndoStore` reading a pre-refactor undo log, from a fixture in the same folder.
- `RpfArchiveCache` concurrency: parallel reads of one archive, and `Clear()` releasing handles.
- `IClock` injected, so `Catalog.Built` and `Project.Created` are deterministic in tests.

Test files are renamed with their subject, in the same pull request.

## Stage 1 — five pull requests

Each one compiles, passes all tests, keeps the golden master byte-identical, and leaves the app working.

**PR 1 — Foundations.** Namespace `Duble.Core` and the folder layout (files moved, not renamed), `Result`,
`Result<T>`, `Error`, `ErrorCodes`, `IClock`/`SystemClock`, `CodeWalkerRuntime` replacing the module
initializer, `AddDubleCore` with the services that exist so far, the two `Microsoft.Extensions.*.Abstractions`
references, compiler flags, and the three warning fixes in the test project. No domain renames — deliberately
small, so the mechanical parts land before anything interesting moves.

**PR 2 — Model, projects, storage.** `Garment`, `TextureInfo`, `GeometryFingerprint`, `Catalog`,
`Project`, `ProjectSource`, `ProjectSettings`, `Decision`, `Resolution`, `IResolutionService`; persistence
extracted into `IProjectStore` and `ICatalogStore`. The largest of them, because every consumer names
these types.

**PR 3 — Sources, indexing, fingerprints.** `ISourceReader` and its two implementations, `ISourceReaderFactory`,
`RpfArchiveCache` replacing the static dictionary, `GarmentIndexer`, `GeometryFingerprinter`,
`TextureFingerprinter`, `TextureDecoder`, `TexturePreviewBuilder`, `Rsc7Header`, `PngWriter`. `Duble.App` stops
referencing CodeWalker directly (`Sesja.cs` moves to `ITexturePreviewBuilder`).

**PR 4 — Comparison, scoring, decisions.** `DuplicateFinder`, `Verdict`, `Thresholds`, `QualityScorer`,
`Reason`/`IReasonFormatter`, `Calibrator`. The app's verdict ordering switches to the enum; the bridge gains one
explicit table mapping `Verdict` to the strings the interface still expects, to be deleted in stage 3.

**PR 5 — Apply, reporting, cleanup.** `ApplyPlanner`, `ApplyExecutor`, `UndoLog`, `HtmlReportBuilder`,
`CsvExporter`, `GlbWriter`, `MeshPreviewBuilder`. Dead code goes: the `Jakosc(Pozycja, out string)`
back-compatibility overload, `Zastosowanie.Zastosuj`/`Cofnij` (CLI-era wrappers around the planner), and the
stale reference to `docs/superpowers/plans/` in `Mostek.cs`. Documentation catches up: `docs/how-it-works.md`
and the README project table use the new vocabulary.

## Stages 2–4

**Stage 2 — app shell and bridge.** *Done.* `Sesja` → `Session`, `Mostek` → `Bridge`, `Komendy` → `Commands`,
`Ustawienia` → `Settings`, `Zasoby` → `WebResources` (a `Window` already has a `Resources`), `Gry` →
`GameDetector`, `Argumenty` → `StartupOptions`. Each group of commands became a class with its dependencies in
its constructor rather than a static method full of closures, the two steps every command ends with — index,
then compare — became `CatalogWorkflow`, and the tuple `(group, members, resolution)` that three screens passed
around became `LiveGroups`.

Two things this document expected did not happen, and deliberately:

- **The bridge vocabulary stays Polish for now.** The keys inside `result` and `data` are the *interface's*
  names, and the interface is stage 3. Renaming them here would mean editing every JS file in the same breath,
  which is precisely the change that made the project name vanish from the start screen once already. Instead
  every payload now names its fields explicitly — `nazwa = source.Name`, never the shorthand `new {
  source.Name }` — so stage 3 changes one side of a written-out mapping rather than hunting for implicit ones.
- **Settings written by 1.0.0 are migrated.** `settings.json` is small but not disposable: without it an update
  would silently reset the language, the theme and the recent projects. The file is read under both sets of
  names and rewritten under the new ones. (The project file needs no such thing: its keys were already English
  after stage 1.)

**Stage 3 — interface.** *Rewritten rather than renamed.* This document assumed the interface would be
translated in place: module names, `data-` attributes, session-storage keys, CSS class names. It was rebuilt
instead, as React and TypeScript in `Duble.App/web`, because the thing worth fixing was never the language of
the identifiers:

- Every screen built HTML by concatenating template literals, with `esc()` remembered by hand at each
  interpolation and three-way ternaries nested inside markup. It could not be read, and therefore could not be
  changed safely.
- Any change redrew the whole screen, which is why the Duplicates filter bar had a hand-written trick to put
  the caret back into the search box after every keystroke.
- View state lived in module-level `let`s cleared by an `unmount()` that had to be remembered.

What the rewrite buys beyond readable components is `web/src/bridge/contract.ts`: every command, its arguments,
its result and every event, written down once. A field renamed in C# now fails the interface's build at each
use site — the failure mode that removed the project name from the start screen, and that took a hand-written
test to catch. It found one on its first day: the calibration charts had been reading `od`, `do` and `kubelki`
while the engine sent `from`, `to` and `buckets`, so every chart in 1.0.0 drew nothing.

The bridge vocabulary is still the Polish one, now declared in that single file. Renaming it is a mechanical
pass over the contract and the matching payloads in `Duble.App/Commands`, with the compiler pointing at every
use on both sides; it is deliberately a step of its own rather than part of a rewrite.

i18n is now split the way the writing is: the interface bundles its own dictionary, typed from `pl.json`, and
reads the engine's over the bridge.

**Stage 4 — CLI and user-visible names.** *Done.* Verbs `indeks / porownaj / raport / zastosuj / cofnij /
kalibruj` became `index / compare / report / apply / undo / calibrate`, and the bin folder `_odrzucone` became
`_rejected`.

This document said the old bin name would still be recognised when scanning, so that existing bins kept being
skipped by the indexer. It is not, and deliberately: there was no `_odrzucone` folder anywhere on the only
machine that has run Duble, and 1.0.0 has no users with one either. Recognising a name nothing writes any more
is migration code kept for nobody — the same reason this refactor carries no project-file migration. Should a
bin from an older build ever turn up, the indexer would read it as a pack of its own; that is visible, and the
answer is to rename the folder.

## Risks

- **Behaviour drift.** Mitigated by the golden master and by keeping the reason codes and i18n dictionaries
  frozen in stage 1. If a run differs, the change is wrong; the golden file is never the thing to update.
- **Decisions orphaned by an id change.** Mitigated by freezing the garment id format and testing it directly.
- **Single-file publish and DI.** Reflection-based container resolution is fine without trimming, but PR 1's
  acceptance includes `dotnet publish Duble.App -p:PublishProfile=win-x64` followed by a launch of the produced
  exe, so the risk is caught at the first step rather than the fifth.
- **`Duble.Cli` keeps using CodeWalker types directly** until stage 4. It is a development tool, and pulling its
  inspection commands into Core services now would widen stage 1 for no user-visible gain.
