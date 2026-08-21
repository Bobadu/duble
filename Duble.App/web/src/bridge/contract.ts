// bridge/contract.ts — everything that crosses between C# and the interface, in one file.
//
// This is the contract with Duble.App: every command, its arguments, its result, and every event. Nothing else
// in the interface may talk to the host without going through a name declared here, which is the point — the
// project name once vanished from the start screen because a C# property was renamed and no one on this side
// could notice. Now a rename here fails the build at every use site.
//
// C# leaves nulls out of the JSON entirely (DefaultIgnoreCondition.WhenWritingNull), so an optional value is
// `field?: T` — absent — and never `T | null`.

// ---------------------------------------------------------------- domain

/** The verdict keys the engine sends; the interface looks up `verdict.<key>` for the word. */
export type Verdict = 'duplicate' | 'superset' | 'needsReview' | 'retexture';

/** A reason for a verdict: a code and the numbers to put in the sentence, translated on this side. */
export interface Reason {
  code: string;
  parameters?: Record<string, string | number>;
}

export interface ProjectSummary {
  name: string;
  path: string;
  sources: number;
  garments: number;
  textures: number;
  duplicates?: number;
  compared?: string;
}

export interface RecentProject {
  path: string;
  name: string;
  lastOpened: string;
  exists: boolean;
}

export interface Source {
  id: string;
  name: string;
  path: string;
  kind: 'folder' | 'rpf' | 'fivem' | string;
  format?: 'legacy' | 'gen9' | 'mixed';
  enabled: boolean;
  indexedAt?: string;
  exists: boolean;
  garments: number;
  textures: number;
  perSlot: Record<string, number>;
  bc7: number;
  /** Garments whose model sits inside a .rpf and so cannot be moved. */
  inArchives: number;
  bin?: string;
}

/** The quality score of one garment, out of 100, with the parts that made it. */
export interface QualityScore {
  total: number;
  resolution: number;
  mipmaps: number;
  variants: number;
  format: number;
  lod: number;
  resolutionPx: number;
  mipmapShare: number;
  variantCount: number;
  wrongFormat: number;
  lodLevels: number;
  noTextures: boolean;
}

export interface Texture {
  sha?: string;
  file: string;
  name?: string;
  /** The colour variant: a, b, c… Worked out by the engine, not by a regular expression over the file name. */
  variant?: string;
  width: number;
  height: number;
  format?: string;
  mipmaps: number;
  alpha: number;
  decoded: boolean;
  bytes: number;
}

/** A garment, as every screen that lists one reads it. `textures` and `quality` only come with the details. */
export interface Garment {
  id: string;
  sourceId?: string;
  source: string;
  container?: string;
  /** The R* component code: jbib / hair / feet…, or p_head for a prop. */
  slot: string;
  number: number;
  suffix?: string;
  gen9: boolean;
  prop: boolean;
  score: number;
  thumbnail?: string;
  textureCount: number;
  vertices: number;
  triangles: number;
  lods: number;
  bytes: number;
  inArchive: boolean;
  quality?: QualityScore;
  modelPath?: string;
  modelBytes?: number;
  textures?: Texture[];
}

/** Who stays and who goes in a group: the engine's rule with the user's decision on top of it. */
export interface Resolution {
  winner?: string;
  rejected: string[];
  ignored: boolean;
  isDefault: boolean;
  note?: string;
}

export interface GarmentPair {
  a: string;
  b: string;
  verdict: Verdict;
  reason?: Reason;
  geometryDistance: number;
  coverageA: number;
  coverageB: number;
  sharedTextures: number;
}

/** Which texture of A matches which of B, for the comparison screen to draw a line between them. */
export interface TextureMatch {
  a: string;
  b: string;
  pairs: [string | null, string | null][];
}

export interface Group {
  id: string;
  verdict: Verdict;
  reason?: Reason;
  winner?: string;
  resolution: Resolution;
  members: Garment[];
  pairs?: GarmentPair[];
  matches?: TextureMatch[];
}

/** A bucket of a filter: the value, its label where it needs one, and how many fall into it. */
export interface SlotFilter {
  slot: string;
  n: number;
}

export interface SourceFilter {
  id: string;
  name: string;
  n: number;
}

/**
 * A garment as the catalog grid lists it — deliberately lighter than <see cref="Garment"/>, because the grid
 * sends every indexed garment at once and draws only the tiles on screen.
 */
export interface CatalogGarment {
  id: string;
  sourceId?: string;
  source: string;
  container?: string;
  slot: string;
  number: number;
  suffix?: string;
  gen9: boolean;
  prop: boolean;
  thumbnail?: string;
  textureCount: number;
  bytes: number;
  inArchive: boolean;
  noMipmaps: boolean;
  bc1WithAlpha: boolean;
  bc7: boolean;
  /** The sharpest verdict of the live groups this garment is in, or absent when it is in none. */
  verdict?: Verdict;
}

/** Where a garment stands in one of the groups it belongs to, as the badge on its card reads it. */
export type GarmentStanding = 'ignored' | 'stays' | 'rejected' | 'neutral';

export interface GarmentGroupRef {
  id: string;
  verdict: Verdict;
  ignored: boolean;
  reason?: Reason;
  others: { id: string; name: string; suffix?: string; source: string }[];
  standing: GarmentStanding;
}

export interface PlannedGarment {
  id: string;
  name: string;
  suffix?: string;
  source: string;
  sourceId: string;
  container?: string;
  bin?: string;
  thumbnail?: string;
  files: number;
  bytes: number;
  shared: number;
  inArchive: number;
  missing: number;
}

/** What applying the decisions would do: the totals, and with `list` every garment it would move. */
export interface ApplyPlan {
  garments: number;
  files: number;
  bytes: number;
  inArchive: number;
  shared: number;
  missing: number;
  missingSources: string[];
  bin?: string;
  bins: { bin: string; files: number; bytes: number }[];
  list?: PlannedGarment[];
}

export interface GroupsSummary {
  /** Absent until something has been compared — which is not the same as "compared, found nothing". */
  total?: number;
  duplicate: number;
  superset: number;
  needsReview: number;
  retexture: number;
  ignored: number;
  compared?: string;
  toReject: ApplyPlan;
}

export interface Thresholds {
  geometryIdentical: number;
  geometrySimilar: number;
  geometryTriangleTolerance: number;
  geometryBoundsTolerance: number;
  textureHashDistance: number;
  textureColorDistance: number;
  flatTextureVariance: number;
  flatTextureColorDistance: number;
  fullCoverage: number;
  partialCoverage: number;
}

export interface CacheSize {
  files: number;
  bytes: number;
}

export interface ProjectSettingsState {
  bin?: string;
  thresholds: Thresholds;
  defaultThresholds: Thresholds;
  thresholdsChanged: boolean;
  /** By part of the cache: thumbnails, textures, meshes, history, and total. */
  cache: Record<string, CacheSize>;
  cacheFolder: string;
  sources: number;
  garments: number;
  /** true = a fresh comparison started, false = the runner was busy, absent = it was not needed. */
  comparing?: boolean;
}

/**
 * How a set of measured values is spread: percentiles plus a histogram over `from`..`to`. The last bucket
 * collects everything above the range, so nothing falls off the chart silently.
 */
export interface Distribution {
  n: number;
  min: number;
  p01: number;
  p05: number;
  p50: number;
  p95: number;
  max: number;
  from: number;
  to: number;
  buckets: number[];
}

/** What calibration measured on this catalog, and what it suggests the thresholds should be. */
export interface CalibrationReport {
  when: string;
  garments: number;
  garmentsWithGeometry: number;
  textures: number;
  decodedTextures: number;

  geoSameFile?: Distribution;
  geoSameHash?: Distribution;
  geoNearestForeign?: Distribution;
  geoPairsAcrossPacks: number;
  geoSuspicious: number;

  hashIdentical?: Distribution;
  colorIdentical?: Distribution;
  hashVariants?: Distribution;
  colorVariants?: Distribution;
  variance?: Distribution;
  hashRandom?: Distribution;
  colorRandom?: Distribution;

  /** The thresholds in force while it ran, so the charts can mark them. */
  usedThresholds?: Thresholds;
  proposal?: Thresholds;

  /** Pairs worth a person's eye. The screen shows how many there are; the lists are for the report. */
  suspicious: SuspiciousPair[];
  closeRandom: CloseRandomPair[];
}

/** Two garments whose shape histograms nearly agree although their meshes do not. */
export interface SuspiciousPair {
  d: number;
  bbox: number;
  a: string;
  b: string;
  triA: number;
  triB: number;
}

/** Two textures from different garments that landed closer together than random pairs should. */
export interface CloseRandomPair {
  pHash: number;
  color: number;
  a: string;
  b: string;
}

export interface MovedFile {
  from: string;
  to: string;
  bytes: number;
  undone: boolean;
  /** Whether the file is where the log says it was moved to, checked as the entry is read. */
  exists: boolean;
}

export interface HistoryEntry {
  file: string;
  name: string;
  when?: string;
  description?: string;
  garments: number;
  files: number;
  bytes: number;
  bins: string[];
  shared: number;
  inArchive: number;
  missing: number;
  undoneAt?: string;
  partlyUndone: boolean;
  canUndo: boolean;
  aborted: boolean;
  error?: string;
  list?: {
    id: string;
    name: string;
    source: string;
    sourceId: string;
    bin?: string;
    files: MovedFile[];
    canUndo: boolean;
  }[];
}

/** A log that will not parse. It is still listed: the files it describes are sitting in a bin folder. */
export interface DamagedHistoryEntry {
  file: string;
  name: string;
  error: string;
  damaged: true;
}

export interface DetectedGame {
  edition: 'enhanced' | 'legacy';
  path: string;
  folders: { name: string; path: string; kind: string }[];
}

export interface AppInfo {
  name: string;
  by: string;
  version: string;
  dev: boolean;
  website: string;
  repository: string;
  licence: string;
  paths: { settings: string; webView2: string; projects: string; executable?: string };
}

export interface AppSettings {
  language: string;
  /** The language actually chosen, absent when following Windows. */
  chosenLanguage?: string;
  theme: 'system' | 'dark' | 'light';
  recent: RecentProject[];
  /** Whether the program asks GitHub for the newest release when it starts. */
  checkUpdates: boolean;
}

/** What the update check learned: the newest release there is, and whether it is ahead of this build. */
export interface UpdateCheck {
  version: string;
  newer: boolean;
  url: string;
  /** The release notes, as Markdown — the same section CHANGELOG.md carries. */
  notes?: string;
  published?: string;
}

export interface AddedSources {
  added: Source[];
  skipped: string[];
}

/** Started, or not started because the one background job at a time was taken. */
export interface Started {
  started: boolean;
}

// ---------------------------------------------------------------- commands

/**
 * Every command the interface may send. `bridge.call` accepts nothing else, and gets the result typed back.
 */
export interface Commands {
  'app.info': { args: null; result: AppInfo };
  'app.changelog': { args: null; result: { markdown: string } };
  'ui.ready': { args: null; result: unknown };
  'settings.get': { args: null; result: AppSettings };
  'settings.set': { args: { language?: string; theme?: string; checkUpdates?: boolean }; result: AppSettings };
  'update.check': { args: null; result: UpdateCheck };

  'window.minimize': { args: null; result: unknown };
  'window.maximize': { args: null; result: { maximized: boolean } };
  'window.close': { args: null; result: unknown };
  'window.state': { args: null; result: { maximized: boolean } };
  'window.dragStart': { args: null; result: unknown };

  'shell.openFolder': { args: { path: string }; result: unknown };
  'shell.showInExplorer': { args: { path: string }; result: unknown };
  'shell.openUrl': { args: { url: string }; result: unknown };

  'dialogs.pickFolder': { args: { title?: string; start?: string }; result: { path?: string } };
  'dialogs.pickFiles': { args: { title?: string; filter?: string; multiple?: boolean; start?: string }; result: { paths: string[] } };
  'dialogs.saveFile': { args: { title?: string; filter?: string; name?: string; start?: string }; result: { path?: string } };

  'project.recent': { args: null; result: { recent: RecentProject[]; defaultFolder: string } };
  'project.get': { args: null; result: { project?: ProjectSummary } };
  'project.new': { args: { name: string; folder?: string }; result: { project?: ProjectSummary } };
  'project.open': { args: { path: string }; result: { project?: ProjectSummary } };
  'project.pickOpen': { args: null; result: { project?: ProjectSummary } };
  'project.pickFolder': { args: null; result: { path?: string } };
  'project.save': { args: null; result: unknown };
  'project.close': { args: null; result: unknown };
  'project.forget': { args: { path: string }; result: unknown };

  'sources.list': { args: null; result: { sources: Source[] } };
  'sources.add': { args: { paths: string[] }; result: AddedSources };
  'sources.pickFolder': { args: null; result: AddedSources };
  'sources.pickRpf': { args: null; result: AddedSources };
  'sources.remove': { args: { id: string }; result: unknown };
  'sources.toggle': { args: { id: string; enabled?: boolean }; result: { enabled: boolean } };
  'sources.cancel': { args: null; result: unknown };
  'sources.detectGames': { args: null; result: { games: DetectedGame[] } };
  'sources.index': { args: { ids?: string[]; force?: boolean }; result: Started & { sources?: string[] } };
  'sources.unpack': { args: { id: string; folder: string; addAsSource?: boolean }; result: Started & { folder: string } };

  'compare.run': { args: null; result: Started };
  'groups.list': {
    args: { verdicts?: Verdict[]; slots?: string[]; sources?: string[]; search?: string; ignored?: boolean };
    result: { summary: GroupsSummary; filters: { slots: SlotFilter[]; sources: SourceFilter[] }; groups: Group[] };
  };
  'groups.get': { args: { id: string }; result: { group: Group } };
  'groups.decide': {
    args: { id: string; winner?: string; rejected?: string[]; ignored?: boolean; note?: string };
    result: { resolution: Resolution };
  };
  'groups.reset': { args: { id: string }; result: { resolution: Resolution } };

  'catalog.list': {
    args: { sources?: string[]; slots?: string[]; formats?: string[]; problems?: boolean; inGroup?: boolean; search?: string };
    result: {
      total: number;
      textures: number;
      shown: number;
      filters: { slots: SlotFilter[]; sources: SourceFilter[]; formats: { legacy: number; gen9: number } };
      garments: CatalogGarment[];
    };
  };
  'catalog.item': { args: { id: string }; result: { garment: Garment & { sourcePath?: string }; groups: GarmentGroupRef[] } };

  'apply.preview': { args: { bin?: string | null; setBin?: boolean } | null; result: ApplyPlan };
  'apply.run': { args: { bin?: string | null; setBin?: boolean } | null; result: Started & { plan: ApplyPlan } };

  'history.list': { args: null; result: { entries: (HistoryEntry | DamagedHistoryEntry)[] } };
  'history.get': { args: { file: string }; result: { entry: HistoryEntry } };
  'history.undo': { args: { file: string; garments?: string[] }; result: Started & { restored?: number; skipped?: number } };

  'report.exportHtml': { args: { path?: string }; result: (Started & { file: string }) | { cancelled: true } };
  'report.exportCsv': { args: { path?: string }; result: { file: string } | { cancelled: true } };

  'project.settings.get': { args: null; result: ProjectSettingsState };
  'project.settings.set': { args: { bin?: string | null; thresholds?: Partial<Thresholds> }; result: ProjectSettingsState };
  'project.settings.resetThresholds': { args: null; result: ProjectSettingsState };
  'cache.clear': { args: { textures?: boolean; meshes?: boolean }; result: { deleted: number; bytes: number; cache: Record<string, CacheSize> } };
  'calibrate.run': { args: null; result: Started };
}

export type CommandName = keyof Commands;
export type CommandArgs<K extends CommandName> = Commands[K]['args'];
export type CommandResult<K extends CommandName> = Commands[K]['result'];

// ---------------------------------------------------------------- events

/** The kinds of long job; a screen shows progress only for the one it is waiting for. */
export type JobKind = 'index' | 'compare' | 'apply' | 'undo' | 'unpack' | 'report' | 'calibration';

export type JobState = 'start' | 'progress' | 'done' | 'cancelled' | 'failed';

/** A long job, reported while it runs. */
export interface JobEvent {
  kind: JobKind;
  description: string;
  state: JobState;
  /** Which part of the work: models, textures, compare… The interface looks up `stage.<key>`. */
  stage?: string;
  done?: number;
  total?: number;
  percent?: number;
  /** What is being worked on right now — the name of a source, usually. */
  text?: string;
  error?: string;
}

/** Everything the host pushes without being asked. */
export interface Events {
  job: JobEvent;
  'project.opened': { project?: ProjectSummary };
  'project.closed': Record<string, never>;
  'project.changed': { project?: ProjectSummary };
  'groups.changed': { id: string };
  'sources.changed': { id?: string };
  'compare.done': { summary?: ProjectSummary };
  'apply.done': {
    file: string;
    moved: number;
    garments: number;
    bytes: number;
    shared: number;
    inArchive: number;
    missing: number;
    bins: string[];
    aborted: boolean;
    error?: string;
  };
  'undo.done': { file: string; restored: number; skipped: number; undoneAt?: string };
  'history.changed': { file: string };
  'unpack.done': { id: string; folder: string; files: number; archives: number; bytes: number; errors: string[]; added?: string };
  'report.done': { file: string; kind: 'html' | 'csv' };
  'calibrate.done': { report: CalibrationReport };
  'settings.changed': { source: 'project' | 'cache' };
  'update.available': { version: string; url: string; notes?: string };
  'window.state': { maximized: boolean };
  'files.dropped': { paths: string[] };
}

export type EventName = keyof Events;
