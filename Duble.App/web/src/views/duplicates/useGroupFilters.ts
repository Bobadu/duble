// views/duplicates/useGroupFilters.ts — what the Duplicates screen is filtered by, kept for the session.
//
// The shape is the command's own arguments, so a rename of the wire follows automatically instead of drifting.
import { useCallback, useState } from 'react';
import type { CommandArgs } from '../../bridge/contract';

export type GroupFilters = Required<NonNullable<CommandArgs<'groups.list'>>>;

const STORAGE_KEY = 'duplicates.filters';

const NONE: GroupFilters = { werdykty: [], sloty: [], zrodla: [], szukaj: '', zignorowane: false };

function read(): GroupFilters {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY);
    return stored ? { ...NONE, ...(JSON.parse(stored) as Partial<GroupFilters>) } : NONE;
  } catch {
    return NONE;
  }
}

export interface GroupFiltersState {
  filters: GroupFilters;
  set: <K extends keyof GroupFilters>(key: K, value: GroupFilters[K]) => void;
  /** Clears everything except "show ignored", which is a way of looking rather than a filter. */
  clear: () => void;
  /** Whether anything is narrowing the list, which decides if the clear button is offered. */
  any: boolean;
}

export function useGroupFilters(): GroupFiltersState {
  const [filters, setFilters] = useState<GroupFilters>(read);

  const store = useCallback((next: GroupFilters) => {
    setFilters(next);
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // a session without storage still filters, it just forgets between screens
    }
  }, []);

  const set = useCallback<GroupFiltersState['set']>((key, value) => store({ ...read(), [key]: value }), [store]);

  const clear = useCallback(() => store({ ...NONE, zignorowane: read().zignorowane }), [store]);

  const any = filters.werdykty.length > 0 || filters.sloty.length > 0 || filters.zrodla.length > 0 || filters.szukaj !== '';

  return { filters, set, clear, any };
}
