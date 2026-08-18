// app/router.ts — which screen is showing.
//
// The address bar is invisible inside the application and there are seven fixed sections, two of which can
// also show one item: #/duplicates/<group id> and #/catalog/<garment id>. That is the whole routing problem,
// so it is solved here rather than by a router library — the hash is read as external state, which is exactly
// what useSyncExternalStore is for.
import { useSyncExternalStore } from 'react';

export const VIEWS = ['start', 'sources', 'duplicates', 'catalog', 'history', 'settings', 'about'] as const;

export type ViewName = (typeof VIEWS)[number];

export interface Route {
  view: ViewName;
  /** The group or garment being looked at, when the section shows one. */
  param?: string;
}

const DEFAULT_VIEW: ViewName = 'start';

function isView(name: string): name is ViewName {
  return (VIEWS as readonly string[]).includes(name);
}

export function parseRoute(hash: string): Route {
  const path = hash.replace(/^#\/?/, '').split('?')[0] ?? '';
  const [name = '', ...rest] = path.split('/');
  const view = isView(name) ? name : DEFAULT_VIEW;
  const param = rest.length ? decodeURIComponent(rest.join('/')) : undefined;
  return param === undefined ? { view } : { view, param };
}

export function routeToHash(view: ViewName, param?: string): string {
  return param ? `#/${view}/${encodeURIComponent(param)}` : `#/${view}`;
}

export function navigate(view: ViewName, param?: string): void {
  location.hash = routeToHash(view, param);
}

/**
 * `Duble.exe --view <name>` asks for a screen from the command line, which is how the screenshots in the
 * README are taken. It only applies when nothing else has been navigated to yet.
 */
export function applyStartupView(): void {
  if (location.hash) return;
  const asked = new URLSearchParams(location.search).get('view');
  if (asked && isView(asked)) location.hash = routeToHash(asked);
}

function subscribe(onChange: () => void): () => void {
  window.addEventListener('hashchange', onChange);
  return () => window.removeEventListener('hashchange', onChange);
}

// the snapshot has to be the same object for the same hash, or React would re-render without end
let lastHash: string | null = null;
let lastRoute: Route = { view: DEFAULT_VIEW };

function snapshot(): Route {
  if (location.hash !== lastHash) {
    lastHash = location.hash;
    lastRoute = parseRoute(location.hash);
  }
  return lastRoute;
}

export function useRoute(): Route {
  return useSyncExternalStore(subscribe, snapshot);
}
