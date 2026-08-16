// app.js — start interfejsu: ustawienia, i18n, motyw, powloka (pasek tytulu, rail, statusbar), router widokow, zdarzenia mostka.
import { bridge } from './bridge.js';
import { i18n, t } from './i18n.js';
import { icon } from './icons.js';
import { el, toast, fmt } from './ui.js';
import * as start from './views/start.js';
import * as sources from './views/sources.js';
import * as settings from './views/settings.js';
import * as about from './views/about.js';
import * as wip from './views/wip.js';

const WIDOKI = { start, sources, duplicates: wip, catalog: wip, history: wip, settings, about };
const RAIL = [
  { id: 'start', ikona: 'home' }, { id: 'sources', ikona: 'sources' }, { id: 'duplicates', ikona: 'duplicates' },
  { id: 'catalog', ikona: 'catalog' }, { id: 'history', ikona: 'history' }, { grow: true },
  { id: 'settings', ikona: 'settings' }, { id: 'about', ikona: 'info' },
];

export const store = {
  ustawienia: null,      // {jezyk, jezykUstawiony, motyw, ostatnie}
  info: null,            // {nazwa, by, wersja, dev}
  projekt: null,         // podsumowanie projektu albo null
  zadanie: null,         // ostatnie zdarzenie "job"
  widok: null,
  nasluch: new Set(),
  emit() { for (const fn of this.nasluch) { try { fn(); } catch (e) { console.error(e); } } },
  on(fn) { this.nasluch.add(fn); return () => this.nasluch.delete(fn); },
};

let biezacy = null;   // { nazwa, modul }

export const ctx = { store, bridge, t, icon, toast, fmt, navigate, ustawMotyw, zmienJezyk };

// ---------- motyw / jezyk ----------
export function ustawMotyw(m) {
  const root = document.documentElement;
  if (m === 'dark' || m === 'light') root.dataset.theme = m; else root.removeAttribute('data-theme');
}
export async function zmienJezyk(j) {
  await i18n.load(j);
  i18n.applyDom(document);
  renderujRail(); renderujStatus(); renderujPasek();
  await montuj(true);
}

// ---------- powloka ----------
function renderujPasek() {
  const b = document.getElementById('brand');
  b.innerHTML = `${icon('logo')}<span>${t('app.name')}</span><span class="by">${t('app.by')}</span>`;
  const p = document.getElementById('titlebar-project');
  if (store.projekt) { p.hidden = false; p.textContent = store.projekt.nazwa; } else { p.hidden = true; p.textContent = ''; }
  document.getElementById('win-min').innerHTML = icon('minus');
  document.getElementById('win-close').innerHTML = icon('x');
  const maks = document.getElementById('win-max');
  maks.innerHTML = icon(store.oknoMaks ? 'restore' : 'square');
  maks.title = t(store.oknoMaks ? 'win.restore' : 'win.maximize');
}
function renderujRail() {
  const rail = document.getElementById('rail');
  rail.innerHTML = '';
  for (const r of RAIL) {
    if (r.grow) { rail.append(el('<div class="grow"></div>')); continue; }
    const a = el(`<a href="#/${r.id}" class="${store.widok === r.id ? 'active' : ''}" data-view="${r.id}">${icon(r.ikona)}<span>${t('nav.' + r.id)}</span></a>`);
    rail.append(a);
  }
}
function renderujStatus() {
  const s = document.getElementById('status');
  const p = store.projekt;
  let lewa = '';
  if (!p) lewa = `<span>${t('status.noProject')}</span>`;
  else lewa = `<span><b>${p.nazwa}</b></span><span class="sep"></span><span>${t('status.sources', { n: fmt.liczba(p.zrodla) })}</span><span class="sep"></span><span>${t('status.items', { n: fmt.liczba(p.pozycje) })}</span><span class="sep"></span><span>${t('status.textures', { n: fmt.liczba(p.tekstury) })}</span>`;
  const z = store.zadanie;
  let prawa = `<span class="stan-ok">${t('status.idle')}</span>`;
  if (z && (z.stan === 'start' || z.stan === 'postep')) {
    const proc = z.procent ?? 0; const tekst = z.stan === 'postep' && z.wszystkie ? t('sources.indexingOf', { etap: z.etap || '', zrobione: fmt.liczba(z.zrobione), wszystkie: fmt.liczba(z.wszystkie) }) : t('status.working');
    prawa = `<span>${tekst}</span><div class="progress ${z.stan === 'start' ? 'indeterminate' : ''}"><i style="width:${proc}%"></i></div><button class="btn ghost sm" id="status-cancel">${t('sources.cancel')}</button>`;
  }
  s.innerHTML = `${lewa}<div class="right">${prawa}</div>`;
  s.querySelector('#status-cancel')?.addEventListener('click', () => bridge.call('sources.cancel').catch(() => {}));
}

// ---------- router ----------
export function navigate(nazwa) { location.hash = '#/' + nazwa; }
async function montuj(wymus = false) {
  const nazwa = (location.hash || '#/start').replace(/^#\/?/, '').split('?')[0] || 'start';
  const modul = WIDOKI[nazwa] || wip;
  if (!wymus && biezacy?.nazwa === nazwa) return;
  try { biezacy?.modul?.unmount?.(); } catch (e) { console.error(e); }
  store.widok = nazwa;
  const wrap = document.getElementById('wrap');
  wrap.innerHTML = '';
  wrap.dataset.view = nazwa;
  biezacy = { nazwa, modul };
  renderujRail();
  try { await modul.render(wrap, { ...ctx, nazwa }); } catch (e) { console.error(e); wrap.append(el(`<div class="empty"><h3>${t('common.error')}</h3><p class="mono select-text">${String(e?.message || e)}</p></div>`)); }
  i18n.applyDom(wrap);
}

// ---------- start ----------
async function boot() {
  try {
    store.ustawienia = await bridge.call('settings.get');
    store.info = await bridge.call('app.info');
  } catch (e) { store.ustawienia = { jezyk: 'pl', motyw: 'dark' }; store.info = { nazwa: 'Duble', by: 'Bobadu', wersja: '?', dev: true }; }
  const params = new URLSearchParams(location.search);
  ustawMotyw(params.get('theme') || store.ustawienia.motyw);
  await i18n.load(params.get('lang') || store.ustawienia.jezyk || 'pl');
  i18n.applyDom(document);
  try { const st = await bridge.call('window.state'); store.oknoMaks = !!st.maks; } catch {}
  try { store.projekt = (await bridge.call('project.get'))?.projekt || null; } catch { store.projekt = null; }
  renderujPasek(); renderujRail(); renderujStatus();

  document.getElementById('win-min').onclick = () => bridge.call('window.minimize');
  document.getElementById('win-max').onclick = () => bridge.call('window.maximize');
  document.getElementById('win-close').onclick = () => bridge.call('window.close');
  document.getElementById('titlebar').addEventListener('dblclick', e => { if (!e.target.closest('.win')) bridge.call('window.maximize'); });

  bridge.on('window.state', d => { store.oknoMaks = !!d.maks; renderujPasek(); });
  bridge.on('project.opened', d => { store.projekt = d.projekt; renderujPasek(); renderujStatus(); store.emit(); });
  bridge.on('project.closed', () => { store.projekt = null; renderujPasek(); renderujStatus(); store.emit(); });
  bridge.on('project.changed', d => { store.projekt = d.projekt; renderujPasek(); renderujStatus(); store.emit(); });
  bridge.on('job', d => { store.zadanie = d; renderujStatus(); store.emit(); });
  bridge.on('nav', d => navigate(d.widok));
  bridge.on('files.dropped', d => { if (store.widok !== 'sources') { sessionStorage.setItem('drop', JSON.stringify(d.sciezki)); navigate('sources'); } else sources.dodajSciezki?.(d.sciezki, ctx); });

  // przeciaganie nad oknem: tylko efekt wizualny (dane ida przez hosta -> files.dropped)
  let licznikDrag = 0;
  document.addEventListener('dragenter', e => { licznikDrag++; document.body.classList.add('dragging'); e.preventDefault(); });
  document.addEventListener('dragleave', () => { if (--licznikDrag <= 0) { licznikDrag = 0; document.body.classList.remove('dragging'); } });
  document.addEventListener('dragover', e => e.preventDefault());
  document.addEventListener('drop', e => { e.preventDefault(); licznikDrag = 0; document.body.classList.remove('dragging'); });

  // skroty
  document.addEventListener('keydown', e => {
    if (e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 'o') { e.preventDefault(); bridge.call('project.pickOpen').catch(() => {}); }
    if (e.key === 'F5') { e.preventDefault(); if (store.projekt) bridge.call('sources.index', {}).catch(err => toast(err.message, { typ: 'warn' })); }
  });

  window.addEventListener('hashchange', () => montuj());
  await montuj();
  const widokStart = params.get('view'); if (widokStart) navigate(widokStart);
  bridge.emit('ui.ready');
}
boot();
