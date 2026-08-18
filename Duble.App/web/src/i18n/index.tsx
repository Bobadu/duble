// i18n — the interface's own words, plus the engine's.
//
// Two dictionaries, each owned by the side that writes the sentences:
//
//  * this one, bundled with the interface, typed, and available before the first render;
//  * the engine's (verdicts, reasons, the quality breakdown), fetched once from https://duble.data/i18n/<lang>,
//    because those sentences are produced by Duble.Core and travel as keys with parameters.
//
// `t` looks in the interface's first and falls back to the engine's. An unknown key renders as [key] rather
// than as nothing, so a missing translation is visible instead of silently blank.
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import en from './en.json';
import pl from './pl.json';

export const LANGUAGES = ['pl', 'en'] as const;
export type Language = (typeof LANGUAGES)[number];

const dictionaries: Record<Language, Record<string, string>> = { pl, en };

/**
 * The keys of the interface's dictionary, for autocomplete and for catching a typo at build time. Keys built
 * at run time — `slot.${name}`, `verdict.${verdict}` — are plain strings, which the union below still admits.
 */
export type TranslationKey = keyof typeof pl | (string & {});

export type Translate = (key: TranslationKey, params?: Record<string, string | number>) => string;

interface I18n {
  language: Language;
  t: Translate;
  /** Numbers in the reader's language: thousands separators as they expect them. */
  formatNumber: (value: number | undefined) => string;
  /** A date the engine wrote as "yyyy-MM-dd HH:mm:ss" or ISO, shown the way the reader's locale writes dates. */
  formatDate: (value: string | undefined) => string;
}

const I18nContext = createContext<I18n | null>(null);

export function I18nProvider({ language, children }: { language: Language; children: ReactNode }) {
  const [engine, setEngine] = useState<Record<string, string>>({});

  useEffect(() => {
    let cancelled = false;
    fetch(`https://duble.data/i18n/${language}.json`, { cache: 'no-store' })
      .then((response) => response.json() as Promise<Record<string, string>>)
      .then((words) => {
        if (!cancelled) setEngine(words);
      })
      .catch((failure: unknown) => console.error('the engine dictionary could not be read', failure));
    return () => {
      cancelled = true;
    };
  }, [language]);

  useEffect(() => {
    document.documentElement.lang = language;
  }, [language]);

  const t = useCallback<Translate>(
    (key, params) => {
      const template = dictionaries[language][key] ?? engine[key];
      // an unknown slot (a prop from a head pack, say) is better shown by its own name than as [slot.xyz]
      if (template === undefined) return key.startsWith('slot.') ? key.slice(5) : `[${key}]`;
      if (!params) return template;

      let text = template;
      for (const [name, value] of Object.entries(params)) {
        // a value starting with '@' is itself a key, the way Duble.Core writes {geo: '@geo.identical'}
        const resolved = typeof value === 'string' && value.startsWith('@') ? t(value.slice(1)) : String(value);
        text = text.replaceAll(`{${name}}`, resolved);
      }
      return text;
    },
    [language, engine],
  );

  const value = useMemo<I18n>(
    () => ({
      language,
      t,
      formatNumber: (number) => new Intl.NumberFormat(language).format(number ?? 0),
      formatDate: (date) => {
        if (!date) return '';
        const parsed = new Date(date.replace(' ', 'T'));
        if (Number.isNaN(parsed.getTime())) return date;
        return new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(parsed);
      },
    }),
    [language, t],
  );

  return <I18nContext value={value}>{children}</I18nContext>;
}

export function useI18n(): I18n {
  const value = useContext(I18nContext);
  if (!value) throw new Error('useI18n outside I18nProvider');
  return value;
}

/** Just the translate function, which is what most components want. */
export function useTranslate(): Translate {
  return useI18n().t;
}

/** Bytes as the file manager would write them. Not localised beyond the decimal separator. */
export function formatSize(bytes: number | undefined, language: Language = 'pl'): string {
  if (bytes == null) return '';

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let size = bytes;
  let step = 0;
  while (size >= 1024 && step < units.length - 1) {
    size /= 1024;
    step++;
  }

  const rounded = size < 10 && step > 0 ? size.toFixed(1) : String(Math.round(size));
  return `${new Intl.NumberFormat(language).format(Number(rounded))} ${units[step] ?? 'B'}`;
}

/** A path too long for the space it has, shortened from the left where the interesting part is not. */
export function shortenPath(path: string | undefined, max = 60): string {
  if (!path || path.length <= max) return path ?? '';
  return `…${path.slice(-(max - 1))}`;
}
