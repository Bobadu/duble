// app/AppState.tsx — the handful of things every screen needs: what the program is, what is configured, which
// project is open, and what the one background job is doing.
//
// All four are pushed by C#, so this is where the host's events are turned into state once, instead of every
// screen subscribing to the same ones. Anything a single screen needs it asks for itself, with useCommand.
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { bridge } from '../bridge/bridge';
import type { AppInfo, AppSettings, JobEvent, ProjectSummary } from '../bridge/contract';
import { useBridgeEvent } from '../bridge/hooks';
import { I18nProvider, LANGUAGES, type Language } from '../i18n';

export type Theme = 'system' | 'dark' | 'light';

interface AppState {
  info: AppInfo;
  settings: AppSettings;
  project: ProjectSummary | undefined;
  /** The last thing the running job said, or undefined when nothing has run yet. */
  job: JobEvent | undefined;
  /** Whether that job is still going. */
  busy: boolean;
  windowMaximized: boolean;
  /** "system" clears the choice and follows Windows, which is what C# stores as no language at all. */
  setLanguage: (language: Language | 'system') => Promise<void>;
  setTheme: (theme: Theme) => Promise<void>;
}

const AppContext = createContext<AppState | null>(null);

function applyTheme(theme: Theme): void {
  if (theme === 'dark' || theme === 'light') document.documentElement.dataset.theme = theme;
  else delete document.documentElement.dataset.theme;
}

function isLanguage(value: string | null): value is Language {
  return !!value && (LANGUAGES as readonly string[]).includes(value);
}

/**
 * Reads the settings and the program's own details, then renders the application. Until that answer arrives
 * there is nothing sensible to draw — every screen depends on the language.
 */
export function AppProvider({ children }: { children: ReactNode }) {
  const [loaded, setLoaded] = useState<{ info: AppInfo; settings: AppSettings } | null>(null);
  const [project, setProject] = useState<ProjectSummary | undefined>();
  const [job, setJob] = useState<JobEvent | undefined>();
  const [windowMaximized, setWindowMaximized] = useState(false);

  // --lang and --theme belong to the run, not to the settings: a screenshot must not change what the user chose
  const overrides = useMemo(() => new URLSearchParams(location.search), []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const [settings, info] = await Promise.all([
        bridge.call('settings.get').catch(() => fallbackSettings),
        bridge.call('app.info').catch(() => fallbackInfo),
      ]);
      const state = await bridge.call('window.state').catch(() => ({ maximized: false }));
      const opened = await bridge.call('project.get').catch(() => ({ project: undefined }));

      if (cancelled) return;
      applyTheme((overrides.get('theme') as Theme | null) ?? settings.theme);
      setWindowMaximized(state.maximized);
      setProject(opened.project);
      setLoaded({ info, settings });
    })();

    return () => {
      cancelled = true;
    };
  }, [overrides]);

  useBridgeEvent('project.opened', (data) => setProject(data.project));
  useBridgeEvent('project.changed', (data) => setProject(data.project));
  useBridgeEvent('project.closed', () => setProject(undefined));
  useBridgeEvent('job', setJob);
  useBridgeEvent('window.state', (data) => setWindowMaximized(data.maximized));

  const setLanguage = useCallback(async (language: Language | 'system') => {
    const settings = await bridge.call('settings.set', { language: language });
    setLoaded((previous) => (previous ? { ...previous, settings } : previous));
  }, []);

  const setTheme = useCallback(async (theme: Theme) => {
    applyTheme(theme);
    const settings = await bridge.call('settings.set', { theme: theme });
    setLoaded((previous) => (previous ? { ...previous, settings } : previous));
  }, []);

  const language: Language = isLanguage(overrides.get('lang'))
    ? (overrides.get('lang') as Language)
    : isLanguage(loaded?.settings.language ?? null)
      ? (loaded!.settings.language as Language)
      : 'pl';

  const value = useMemo<AppState | null>(
    () =>
      loaded && {
        info: loaded.info,
        settings: loaded.settings,
        project,
        job,
        busy: job?.state === 'start' || job?.state === 'progress',
        windowMaximized,
        setLanguage,
        setTheme,
      },
    [loaded, project, job, windowMaximized, setLanguage, setTheme],
  );

  useDeveloperHandle(loaded?.info.dev ?? false);

  if (!value) return null;

  return (
    <AppContext value={value}>
      <I18nProvider language={language}>{children}</I18nProvider>
    </AppContext>
  );
}

export function useApp(): AppState {
  const state = useContext(AppContext);
  if (!state) throw new Error('useApp outside AppProvider');
  return state;
}

/** Tells the host the interface is up. Sent once, after the first screen has been drawn. */
export function useAnnounceReady(): void {
  const announced = useRef(false);

  useEffect(() => {
    if (announced.current) return;
    announced.current = true;
    void bridge.call('ui.ready');
  }, []);
}

/**
 * In developer mode the bridge is reachable from the console and from `Duble.exe --exec`, which is how the
 * screenshots in the README are made. It is not exposed in a normal run.
 */
function useDeveloperHandle(dev: boolean): void {
  useEffect(() => {
    if (!dev) return;
    window.duble = { bridge };
    return () => {
      delete window.duble;
    };
  }, [dev]);
}

const fallbackInfo: AppInfo = {
  name: 'Duble',
  by: 'Bobadu',
  version: '?',
  dev: true,
  website: '',
  repository: '',
  licence: 'MIT',
  paths: { settings: '', webView2: '', projects: '' },
};

const fallbackSettings: AppSettings = { language: 'pl', theme: 'dark', recent: [] };
