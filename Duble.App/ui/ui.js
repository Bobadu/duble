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

/** Toast. akcja: {tekst, fn} = przycisk w toascie (np. „Cofnij", „Pokaż"); opis = druga, przygaszona linia (np. sciezka);
 *  toast z akcja zyje dluzej (czas domyslnie 9 s). */
export function toast(tekst, { typ = 'info', czas, akcja, opis } = {}) {
  const warstwa = document.getElementById('warstwa-toast');
  const ik = typ === 'ok' ? 'ok' : typ === 'warn' ? 'warn' : typ === 'error' ? 'warn' : 'info';
  if (czas === undefined) czas = akcja ? 9000 : 4200;
  const node = el(`<div class="toast ${typ}" role="status">${icon(ik)}<div class="txt"><span>${esc(tekst)}</span>${opis ? `<span class="opis">${esc(opis)}</span>` : ''}</div>${akcja ? `<button class="act">${esc(akcja.tekst)}</button>` : ''}<button class="close" aria-label="${esc(t('common.close'))}">${icon('x')}</button></div>`);
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

/**
 * Wlasne listy rozwijane zamiast systemowego popupu <select> (nie da sie go ostylowac): kazdy `select.input` w root dostaje
 * przycisk .dd z aktualna opcja i panel .dd-menu z opcjami; wybor ustawia select.value i wysyla 'change' — kod widokow zostaje bez zmian.
 * MutationObserver ulepsza takze selecty dodawane pozniej (widoki przerysowuja sie same).
 */
export function wlaczDropdowny(root) {
  const ulepsz = (sel) => {
    if (!(sel instanceof HTMLSelectElement) || sel.dataset.dd) return;
    sel.dataset.dd = '1';
    sel.classList.add('dd-native');
    const btn = el(`<button type="button" class="dd ${[...sel.classList].filter(c => c !== 'dd-native').join(' ')}" aria-haspopup="listbox" aria-expanded="false" title="${esc(sel.getAttribute('aria-label') || '')}"><span class="dd-label"></span>${icon('chevron', 'dd-chev')}</button>`);
    if (sel.disabled) btn.disabled = true;
    const etyk = () => { const o = sel.options[sel.selectedIndex]; btn.querySelector('.dd-label').textContent = o ? o.textContent : ''; };
    etyk();
    sel.after(btn);
    btn.onclick = (e) => {
      e.preventDefault(); e.stopPropagation();
      document.querySelectorAll('.menu').forEach(m => m.remove());
      const m = el('<div class="menu dd-menu" role="listbox"></div>');
      for (const o of sel.options) {
        const b = el(`<button type="button" role="option" aria-selected="${o.selected}" class="${o.selected ? 'on' : ''}"><span class="txt">${esc(o.textContent)}</span>${o.selected ? icon('check') : ''}</button>`);
        b.onclick = () => { m.remove(); if (sel.value !== o.value) { sel.value = o.value; sel.dispatchEvent(new Event('change', { bubbles: true })); } etyk(); btn.setAttribute('aria-expanded', 'false'); };
        m.append(b);
      }
      document.body.append(m);
      const r = btn.getBoundingClientRect(); const mr = m.getBoundingClientRect();
      m.style.minWidth = Math.max(r.width, 180) + 'px';
      let x = r.left, y = r.bottom + 6;
      if (x + mr.width > window.innerWidth - 8) x = window.innerWidth - 8 - mr.width; if (x < 8) x = 8;
      if (y + mr.height > window.innerHeight - 8) y = Math.max(8, r.top - mr.height - 6);
      m.style.left = x + 'px'; m.style.top = y + 'px';
      btn.setAttribute('aria-expanded', 'true');
      m.querySelector('.on')?.scrollIntoView({ block: 'nearest' });
      const zamknij = (ev) => { if (!m.contains(ev.target)) { m.remove(); btn.setAttribute('aria-expanded', 'false'); document.removeEventListener('mousedown', zamknij, true); document.removeEventListener('keydown', naKlawisz, true); } };
      const naKlawisz = (ev) => { if (ev.key === 'Escape') { m.remove(); btn.setAttribute('aria-expanded', 'false'); document.removeEventListener('mousedown', zamknij, true); document.removeEventListener('keydown', naKlawisz, true); btn.focus(); } };
      setTimeout(() => { document.addEventListener('mousedown', zamknij, true); document.addEventListener('keydown', naKlawisz, true); }, 0);
    };
    // zmiana wartosci z kodu (sel.value = …) -> odswiez etykiete
    sel.addEventListener('change', etyk);
  };
  root.querySelectorAll('select.input').forEach(ulepsz);
  const obs = new MutationObserver(muts => {
    for (const mu of muts) for (const n of mu.addedNodes) {
      if (!(n instanceof Element)) continue;
      if (n.matches?.('select.input')) ulepsz(n);
      n.querySelectorAll?.('select.input').forEach(ulepsz);
    }
  });
  obs.observe(root, { childList: true, subtree: true });
  return obs;
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
