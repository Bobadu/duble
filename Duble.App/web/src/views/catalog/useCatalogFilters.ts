// views/catalog/useCatalogFilters.ts — what the Catalog is narrowed to, kept for the session.
import { useCallback, useState } from 'react';
import type { CommandArgs } from '../../bridge/contract';

export type CatalogFilters = Required<NonNullable<CommandArgs<'catalog.list'>>>;

const STORAGE_KEY = 'catalog.filters';

const NONE: CatalogFilters = { zrodla: [], sloty: [], formaty: [], problemy: false, wGrupie: false, szukaj: '' };

function read(): CatalogFilters {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY);
    return stored ? { ...NONE, ...(JSON.parse(stored) as Partial<CatalogFilters>) } : NONE;
  } catch {
    return NONE;
  }
}

export interface CatalogFiltersState {
  filters: CatalogFilters;
  set: <K extends keyof CatalogFilters>(key: K, value: CatalogFilters[K]) => void;
  clear: () => void;
  any: boolean;
}

export function useCatalogFilters(): CatalogFiltersState {
  const [filters, setFilters] = useState<CatalogFilters>(read);

  const store = useCallback((next: CatalogFilters) => {
    setFilters(next);
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // a session without storage still filters, it just forgets between screens
    }
  }, []);

  const set = useCallback<CatalogFiltersState['set']>((key, value) => store({ ...read(), [key]: value }), [store]);

  const clear = useCallback(() => store(NONE), [store]);

  const any =
    filters.zrodla.length > 0 ||
    filters.sloty.length > 0 ||
    filters.formaty.length > 0 ||
    filters.problemy ||
    filters.wGrupie ||
    filters.szukaj !== '';

  return { filters, set, clear, any };
}
