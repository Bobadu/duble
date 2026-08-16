// ui.js — drobne narzedzia interfejsu: tworzenie elementow, dialogi, toasty, menu, formatowanie.
import { t } from './i18n.js';
import { icon } from './icons.js';

/** Element z HTML (jeden korzen). */
export function el(html) {
  const tpl = document.createElement('template');
  tpl.innerHTML = html.trim();
  return tpl.content.firstElementChild;
}
export function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

/** Dialog modalny. przyciski: [{tekst, rola:'primary'|'default'|'danger', akcja(zamknij) -> false = nie zamykaj}] ; zwraca Promise z wartoscia z zamknij(wartosc). */
export function dialog({ tytul, tresc, przyciski = [], szeroki = false, naStart }) {
  return new Promise(resolve => {
    const warstwa = document.getElementById('warstwa-dialog');
    const backdrop = el(`<div class="dialog-backdrop"><div class="dialog${szeroki ? ' wide' : ''}" role="dialog" aria-modal="true"><header><h2>${esc(tytul)}</h2></header><div class="body"></div><footer></footer></div></div>`);
    const body = backdrop.querySelector('.body'); const footer = backdrop.querySelector('footer');
    const zamknij = (wartosc) => { backdrop.remove(); document.removeEventListener('keydown', naKlawisz); resolve(wartosc); };
    if (typeof tresc === 'string') body.innerHTML = tresc; else if (tresc instanceof Node) body.append(tresc); else if (typeof tresc === 'function') tresc(body, zamknij);
    for (const p of przyciski) {
      const b = el(`<button class="btn ${p.rola === 'primary' ? 'primary' : p.rola === 'danger' ? 'danger' : ''}">${esc(p.tekst)}</button>`);
      b.onclick = async () => { const r = p.akcja ? await p.akcja(zamknij) : undefined; if (r !== false && !p.zostaw) zamknij(r ?? p.wartosc); };
      footer.append(b);
      if (p.rola === 'primary') setTimeout(() => b.focus(), 0);
    }
    const naKlawisz = (e) => { if (e.key === 'Escape') { e.preventDefault(); zamknij(undefined); } };
    document.addEventListener('keydown', naKlawisz);
    backdrop.addEventListener('mousedown', e => { if (e.target === backdrop) zamknij(undefined); });
    warstwa.append(backdrop);
    naStart?.(body, zamknij);
  });
}

export function confirm(tekst, { ok = t('common.ok'), anuluj = t('common.cancel'), niebezpieczne = false, tytul = '' } = {}) {
  return dialog({ tytul, tresc: `<p class="lead">${esc(tekst)}</p>`, przyciski: [{ tekst: anuluj, wartosc: false }, { tekst: ok, rola: niebezpieczne ? 'danger' : 'primary', wartosc: true }] }).then(v => v === true);
}

/** Toast. akcja: {tekst, fn} = przycisk w toascie (np. „Cofnij", „Pokaż"); toast z akcja zyje dluzej (czas domyslnie 9 s). */
export function toast(tekst, { typ = 'info', czas, akcja } = {}) {
  const warstwa = document.getElementById('warstwa-toast');
  const ik = typ === 'ok' ? 'ok' : typ === 'warn' ? 'warn' : typ === 'error' ? 'warn' : 'info';
  if (czas === undefined) czas = akcja ? 9000 : 4200;
  const node = el(`<div class="toast ${typ}" role="status">${icon(ik)}<span class="txt">${esc(tekst)}</span>${akcja ? `<button class="act">${esc(akcja.tekst)}</button>` : ''}<button class="close" aria-label="${esc(t('common.close'))}">${icon('x')}</button></div>`);
  node.querySelector('.close').onclick = () => node.remove();
  if (akcja) node.querySelector('.act').onclick = () => { node.remove(); try { akcja.fn?.(); } catch (e) { console.error(e); } };
  warstwa.append(node);
  if (czas > 0) setTimeout(() => node.remove(), czas);
  return node;
}

/** Proste menu przy elemencie kotwicy. pozycje: [{tekst, ikona, akcja, niebezpieczna, sep}] */
export function menu(kotwica, pozycje) {
  document.querySelectorAll('.menu').forEach(m => m.remove());
  const m = el('<div class="menu" role="menu"></div>');
  for (const p of pozycje) {
    if (p.sep) { m.append(el('<hr>')); continue; }
    const b = el(`<button role="menuitem" class="${p.niebezpieczna ? 'danger' : ''}">${p.ikona ? icon(p.ikona) : ''}<span>${esc(p.tekst)}</span></button>`);
    b.onclick = () => { m.remove(); p.akcja?.(); };
    m.append(b);
  }
  document.body.append(m);
  const r = kotwica.getBoundingClientRect(); const mr = m.getBoundingClientRect();
  let x = r.right - mr.width, y = r.bottom + 6;
  if (x < 8) x = 8; if (y + mr.height > window.innerHeight - 8) y = r.top - mr.height - 6;
  m.style.left = x + 'px'; m.style.top = y + 'px';
  const zamknij = (e) => { if (!m.contains(e.target)) { m.remove(); document.removeEventListener('mousedown', zamknij, true); } };
  setTimeout(() => document.addEventListener('mousedown', zamknij, true), 0);
  return m;
}

export const fmt = {
  liczba(n) { return new Intl.NumberFormat(document.documentElement.lang || 'pl').format(n ?? 0); },
  rozmiar(b) {
    if (b == null) return '';
    const j = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0; let x = Number(b);
    while (x >= 1024 && i < j.length - 1) { x /= 1024; i++; }
    return `${x < 10 && i > 0 ? x.toFixed(1) : Math.round(x)} ${j[i]}`;
  },
  data(iso) {
    if (!iso) return '';
    const d = new Date(String(iso).replace(' ', 'T'));
    if (isNaN(d)) return String(iso);
    return new Intl.DateTimeFormat(document.documentElement.lang || 'pl', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
  },
  sciezkaKrotka(p, maks = 60) { if (!p || p.length <= maks) return p || ''; return '…' + p.slice(-(maks - 1)); },
};
