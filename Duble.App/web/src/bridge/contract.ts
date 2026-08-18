// bridge/contract.ts — everything that crosses between C# and the interface, in one file.
//
// This is the contract with Duble.App: every command, its arguments, its result, and every event. Nothing else
// in the interface may talk to the host without going through a name declared here, which is the point — the
// project name once vanished from the start screen because a C# property was renamed and no one on this side
// could notice. Now a rename here fails the build at every use site.
//
// Two things to keep in mind while reading:
//
//  * The field names are the ones C# sends, and they are still Polish. Renaming them is one mechanical pass
//    over this file and the matching payloads in Duble.App/Commands, with the compiler pointing at every use.
//  * C# leaves nulls out of the JSON entirely (DefaultIgnoreCondition.WhenWritingNull), so an optional value
//    is `field?: T` — absent — and never `T | null`.

// ---------------------------------------------------------------- domain

/** The verdict keys the engine sends; the interface looks up `verdict.<key>` for the word. */
export type Verdict = 'duplicate' | 'superset' | 'needsReview' | 'retexture';

/** A reason for a verdict: a code and the numbers to put in the sentence, translated on this side. */
export interface Reason {
  kod: string;
  p?: Record<string, string | number>;
}

export interface ProjectSummary {
  nazwa: string;
  sciezka: string;
  zrodla: number;
  pozycje: number;
  tekstury: number;
  duplikaty?: number;
  porownano?: string;
}

export interface RecentProject {
  sciezka: string;
  nazwa: string;
  ostatnio: string;
  istnieje: boolean;
}

export interface Source {
  id: string;
  nazwa: string;
  sciezka: string;
  typ: 'folder' | 'rpf' | string;
  format?: 'legacy' | 'gen9' | 'mixed';
  wlaczone: boolean;
  zaindeksowano?: string;
  istnieje: boolean;
  pozycje: number;
  tekstury: number;
  perSlot: Record<string, number>;
  bc7: number;
  archiwa: number;
  kosz?: string;
}

/** The quality breakdown of one garment, out of 100 with the parts that made it. */
export interface QualityScore {
  razem: number;
  rozdz: number;
  mipy: number;
  warianty: number;
  format: number;
  lod: number;
  rozdzPx: number;
  udzialMipow: number;
  liczbaWariantow: number;
  zlyFormat: number;
  lody: number;
  brakTekstur: boolean;
}

export interface Texture {
  sha?: string;
  plik: string;
  nazwa?: string;
  /** The colour variant: a, b, c… Worked out by the engine, not by a regular expression over the file name. */
  litera?: string;
  w: number;
  h: number;
  format?: string;
  mipy: number;
  alfa: number;
  zdekodowana: boolean;
  bajty: number;
}

/** A garment, as every screen that lists one reads it. `tekstury` and `rozpiska` only come with the details. */
export interface Garment {
  id: string;
  zrodloId?: string;
  zrodlo: string;
  kontener?: string;
  typ: string;
  numer: number;
  sufiks?: string;
  gen9: boolean;
  props: boolean;
  punkty: number;
  thumb?: string;
  tekstur: number;
  wierzcholki: number;
  trojkaty: number;
  lody: number;
  bajty: number;
  wArchiwum: boolean;
  rozpiska?: QualityScore;
  sciezkaYdd?: string;
  bajtyYdd?: number;
  tekstury?: Texture[];
}

/** Who stays and who goes in a group: the engine's rule with the user's decision on top of it. */
export interface Resolution {
  zwyciezca?: string;
  odrzucone: string[];
  ignoruj: boolean;
  domyslna: boolean;
  notatka?: string;
}

export interface GarmentPair {
  a: string;
  b: string;
  werdykt: Verdict;
  powod?: Reason;
  distGeo: number;
  pokrycieA: number;
  pokrycieB: number;
  wspolnychTekstur: number;
}

/** Which texture of A matches which of B, for the comparison screen to draw a line between them. */
export interface TextureMatch {
  a: string;
  b: string;
  pary: [string | null, string | null][];
}

export interface Group {
  id: string;
  werdykt: Verdict;
  powod?: Reason;
  zwyciezca?: string;
  rozstrzygniecie: Resolution;
  czlonkowie: Garment[];
  pary?: GarmentPair[];
  dopasowania?: TextureMatch[];
}

/** A bucket of a filter: the value, its label where it needs one, and how many fall into it. */
export interface SlotFilter {
  typ: string;
  n: number;
}

export interface SourceFilter {
  id: string;
  nazwa: string;
  n: number;
}

export interface PlannedGarment {
  id: string;
  nazwa: string;
  sufiks?: string;
  zrodlo: string;
  zrodloId: string;
  kontener?: string;
  kosz?: string;
  thumb?: string;
  pliki: number;
  bajty: number;
  wspoldzielone: number;
  wArchiwum: number;
  brakujace: number;
}

/** What applying the decisions would do: the totals, and with `lista` every garment it would move. */
export interface ApplyPlan {
  pozycje: number;
  pliki: number;
  bajty: number;
  wArchiwum: number;
  wspoldzielone: number;
  brakujace: number;
  brakujaceZrodla: string[];
  kosz?: string;
  kosze: { kosz: string; pliki: number; bajty: number }[];
  lista?: PlannedGarment[];
}

export interface GroupsSummary {
  /** Absent until something has been compared — which is not the same as "compared, found nothing". */
  grup?: number;
  duplikat: number;
  nadzbior: number;
  wglad: number;
  przemalowanie: number;
  zignorowane: number;
  porownano?: string;
  doOdrzucenia: ApplyPlan;
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
  pliki: number;
  bajty: number;
}

export interface ProjectSettingsState {
  kosz?: string;
  progi: Thresholds;
  progiDomyslne: Thresholds;
  progiZmienione: boolean;
  cache: Record<string, CacheSize>;
  folderCache: string;
  zrodla: number;
  pozycje: number;
  /** true = a fresh comparison started, false = the runner was busy, absent = it was not needed. */
  porownanie?: boolean;
}

export interface MovedFile {
  z: string;
  do: string;
  bajty: number;
  cofniety: boolean;
  jest: boolean;
}

export interface HistoryEntry {
  plik: string;
  nazwa: string;
  kiedy?: string;
  opis?: string;
  pozycje: number;
  pliki: number;
  bajty: number;
  kosze: string[];
  wspoldzielone: number;
  wArchiwum: number;
  brakujace: number;
  cofnieto?: string;
  czesciowo: boolean;
  moznaCofnac: boolean;
  przerwano: boolean;
  blad?: string;
  lista?: {
    id: string;
    nazwa: string;
    zrodlo: string;
    zrodloId: string;
    kosz?: string;
    pliki: MovedFile[];
    moznaCofnac: boolean;
  }[];
}

/** A log that will not parse. It is still listed: the files it describes are sitting in a bin folder. */
export interface DamagedHistoryEntry {
  plik: string;
  nazwa: string;
  blad: string;
  uszkodzony: true;
}

export interface DetectedGame {
  gra: 'enhanced' | 'legacy';
  sciezka: string;
  propozycje: { nazwa: string; sciezka: string; typ: string }[];
}

export interface AppInfo {
  nazwa: string;
  by: string;
  wersja: string;
  dev: boolean;
  strona: string;
  repo: string;
  licencja: string;
  sciezki: { ustawienia: string; webview2: string; projekty: string; exe?: string };
}

export interface AppSettings {
  jezyk: string;
  jezykUstawiony?: string;
  motyw: 'system' | 'dark' | 'light';
  /**
   * Careful: these are the C# RecentProject objects serialised as they are, so their fields are the English
   * camelCase ones — unlike project.recent, which maps them to the interface's names by hand. Writing both
   * down is how the difference stops being a surprise.
   */
  ostatnie: { path: string; name: string; lastOpened: string }[];
}

export interface AddedSources {
  dodane: Source[];
  pominiete: string[];
}

/** Started, or not started because the one background job at a time was taken. */
export interface Started {
  uruchomiono: boolean;
}

// ---------------------------------------------------------------- commands

/**
 * Every command the interface may send. `bridge.call` accepts nothing else, and gets the result typed back.
 */
export interface Commands {
  'app.info': { args: null; result: AppInfo };
  'ui.ready': { args: null; result: unknown };
  'settings.get': { args: null; result: AppSettings };
  'settings.set': { args: { jezyk?: string; motyw?: string }; result: AppSettings };

  'window.minimize': { args: null; result: unknown };
  'window.maximize': { args: null; result: { maks: boolean } };
  'window.close': { args: null; result: unknown };
  'window.state': { args: null; result: { maks: boolean } };
  'window.dragStart': { args: null; result: unknown };

  'shell.openFolder': { args: { sciezka: string }; result: unknown };
  'shell.showInExplorer': { args: { sciezka: string }; result: unknown };
  'shell.openUrl': { args: { url: string }; result: unknown };

  'dialogs.pickFolder': { args: { tytul?: string; start?: string }; result: { sciezka?: string } };
  'dialogs.pickFiles': { args: { tytul?: string; filtr?: string; wiele?: boolean; start?: string }; result: { sciezki: string[] } };
  'dialogs.saveFile': { args: { tytul?: string; filtr?: string; nazwa?: string; start?: string }; result: { sciezka?: string } };

  'project.recent': { args: null; result: { ostatnie: RecentProject[]; folderDomyslny: string } };
  'project.get': { args: null; result: { projekt?: ProjectSummary } };
  'project.new': { args: { nazwa: string; folder?: string }; result: { projekt?: ProjectSummary } };
  'project.open': { args: { sciezka: string }; result: { projekt?: ProjectSummary } };
  'project.pickOpen': { args: null; result: { projekt?: ProjectSummary } };
  'project.pickFolder': { args: null; result: { sciezka?: string } };
  'project.save': { args: null; result: unknown };
  'project.close': { args: null; result: unknown };
  'project.forget': { args: { sciezka: string }; result: unknown };

  'sources.list': { args: null; result: { zrodla: Source[] } };
  'sources.add': { args: { sciezki: string[] }; result: AddedSources };
  'sources.pickFolder': { args: null; result: AddedSources };
  'sources.pickRpf': { args: null; result: AddedSources };
  'sources.remove': { args: { id: string }; result: unknown };
  'sources.toggle': { args: { id: string; wlaczone?: boolean }; result: { wlaczone: boolean } };
  'sources.cancel': { args: null; result: unknown };
  'sources.detectGames': { args: null; result: { gry: DetectedGame[] } };
  'sources.index': { args: { ids?: string[]; wymus?: boolean }; result: Started & { zrodla?: string[] } };
  'sources.unpack': { args: { id: string; folder: string; dodajZrodlo?: boolean }; result: Started & { folder: string } };

  'compare.run': { args: null; result: Started };
  'groups.list': {
    args: { werdykty?: Verdict[]; sloty?: string[]; zrodla?: string[]; szukaj?: string; zignorowane?: boolean };
    result: { podsumowanie: GroupsSummary; filtry: { sloty: SlotFilter[]; zrodla: SourceFilter[] }; grupy: Group[] };
  };
  'groups.get': { args: { id: string }; result: { grupa: Group } };
  'groups.decide': {
    args: { id: string; zwyciezca?: string; odrzucone?: string[]; ignoruj?: boolean; notatka?: string };
    result: { rozstrzygniecie: Resolution };
  };
  'groups.reset': { args: { id: string }; result: { rozstrzygniecie: Resolution } };

  'apply.preview': { args: { kosz?: string | null; ustawKosz?: boolean } | null; result: ApplyPlan };
  'apply.run': { args: { kosz?: string | null; ustawKosz?: boolean } | null; result: Started & { plan: ApplyPlan } };

  'history.list': { args: null; result: { wpisy: (HistoryEntry | DamagedHistoryEntry)[] } };
  'history.get': { args: { plik: string }; result: { wpis: HistoryEntry } };
  'history.undo': { args: { plik: string; pozycje?: string[] }; result: Started & { wrocilo?: number; pominieto?: number } };

  'report.exportHtml': { args: { sciezka?: string }; result: (Started & { plik: string }) | { anulowano: true } };
  'report.exportCsv': { args: { sciezka?: string }; result: { plik: string } | { anulowano: true } };

  'project.settings.get': { args: null; result: ProjectSettingsState };
  'project.settings.set': { args: { kosz?: string | null; progi?: Partial<Thresholds> }; result: ProjectSettingsState };
  'project.settings.resetProgi': { args: null; result: ProjectSettingsState };
  'cache.clear': { args: { tex?: boolean; mesh?: boolean }; result: { usunieto: number; bajty: number; cache: Record<string, CacheSize> } };
  'calibrate.run': { args: null; result: Started };
}

export type CommandName = keyof Commands;
export type CommandArgs<K extends CommandName> = Commands[K]['args'];
export type CommandResult<K extends CommandName> = Commands[K]['result'];

// ---------------------------------------------------------------- events

/** A long job, reported while it runs. The interface only shows the one it is waiting for, hence `typ`. */
export interface JobEvent {
  typ: 'indeks' | 'porownaj' | 'zastosuj' | 'cofnij' | 'rozpakuj' | 'raport' | 'kalibracja';
  opis: string;
  stan: 'start' | 'postep' | 'koniec' | 'anulowano' | 'blad';
  etap?: string;
  zrobione?: number;
  wszystkie?: number;
  procent?: number;
  tekst?: string;
  blad?: string;
}

/** Everything the host pushes without being asked. */
export interface Events {
  job: JobEvent;
  'project.opened': { projekt?: ProjectSummary };
  'project.closed': Record<string, never>;
  'project.changed': { projekt?: ProjectSummary };
  'groups.changed': { id: string };
  'sources.changed': { id?: string };
  'compare.done': { podsumowanie?: ProjectSummary };
  'apply.done': {
    plik: string;
    przeniesione: number;
    pozycje: number;
    bajty: number;
    wspoldzielone: number;
    wArchiwum: number;
    brakujace: number;
    kosze: string[];
    przerwano: boolean;
    blad?: string;
  };
  'undo.done': { plik: string; wrocilo: number; pominieto: number; cofnieto?: string };
  'history.changed': { plik: string };
  'unpack.done': { id: string; folder: string; pliki: number; archiwa: number; bajty: number; bledy: string[]; dodano?: string };
  'report.done': { plik: string; typ: 'html' | 'csv' };
  'calibrate.done': { wynik: unknown };
  'settings.changed': { zrodlo: 'project' | 'cache' };
  'window.state': { maks: boolean };
  'files.dropped': { sciezki: string[] };
  nav: { widok: string };
}

export type EventName = keyof Events;
