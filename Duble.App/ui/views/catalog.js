// views/catalog.js — Katalog: wszystkie zaindeksowane pozycje jako siatka miniatur (wirtualizowana), filtry (zrodlo/slot/format/problemy/w grupach), szukajka.
import { el, esc, toast, fmt } from '../ui.js';
import { SiatkaWirtualna } from '../siatka.js';
import { KLASA_WERDYKTU, IKONA_WERDYKTU, nazwaPozycji } from './duplicates.js';

const KLUCZ_FILTROW = 'cat.filtry';
const KLUCZ_SCROLL = 'cat.scroll';
const SLOTY_KOLEJNOSC = ['jbib', 'uppr', 'lowr', 'feet', 'accs', 'task', 'decl', 'teef', 'hand', 'hair', 'berd', 'p_head', 'p_eyes', 'p_ears', 'p_mouth', 'p_lhand', 'p_rhand', 'p_lwrist', 'p_rwrist', 'p_hip'];

let odpisz = null, ctxRef = null, filtry = null, siatka = null;
let podsumEl = null, filtryEl = null, gridEl = null, debounce = null;

function wczytajFiltry() {
  try { return { ...{ zrodla: [], sloty: [], formaty: [], problemy: false, wGrupie: false, szukaj: '' }, ...JSON.parse(sessionStorage.getItem(KLUCZ_FILTROW) || '{}') }; }
  catch { return { zrodla: [], sloty: [], formaty: [], problemy: false, wGrupie: false, szukaj: '' }; }
}
function zapiszFiltry() { sessionStorage.setItem(KLUCZ_FILTROW, JSON.stringify(filtry)); }
function czyFiltr() { return filtry.zrodla.length || filtry.sloty.length || filtry.formaty.length || filtry.problemy || filtry.wGrupie || filtry.szukaj; }

export async function render(root, ctx) {
  ctxRef = ctx;
  const { t, icon, store, navigate } = ctx;
  filtry = wczytajFiltry();
  if (!store.projekt) {
    root.append(el(`<div class="view-head"><div class="titles"><h1>${t('catalog.title')}</h1><p class="sub">${t('catalog.subtitle')}</p></div></div>`));
    const e = el(`<div class="empty">${icon('file')}<h3>${t('status.noProject')}</h3><p>${t('start.empty')}</p><div class="btn-row" style="justify-content:center"><button class="btn primary" id="do-startu">${icon('home')}${t('nav.start')}</button></div></div>`);
    e.querySelector('#do-startu').onclick = () => navigate('start');
    root.append(e);
    return;
  }
  const head = el(`
    <div class="view-head">
      <div class="titles"><h1>${t('catalog.title')}</h1><p class="sub" id="cat-podsum">${t('catalog.subtitle')}</p></div>
      <div class="actions">
        <div class="filtr-szukaj"><span class="ico-wrap">${icon('search')}</span><input class="input" id="cat-szukaj" placeholder="${esc(t('dup.searchPlaceholder'))}" value="${esc(filtry.szukaj || '')}" aria-label="${esc(t('dup.search'))}"></div>
      </div>
    </div>`);
  head.querySelector('#cat-szukaj').addEventListener('input', (e) => { clearTimeout(debounce); debounce = setTimeout(() => { filtry.szukaj = e.target.value; zapiszFiltry(); odswiez(); }, 220); });
  root.append(head);
  podsumEl = head.querySelector('#cat-podsum');
  filtryEl = el('<div class="filterbar cat-filtry" id="cat-filtry"></div>');
  root.append(filtryEl);
  gridEl = el('<div class="cat-grid" id="cat-grid"></div>');
  root.append(gridEl);
  siatka = new SiatkaWirtualna(gridEl, {
    wysokosc: 200, minSzerokosc: 150, odstep: 12,
    renderuj: (p) => kafelek(p, ctx),
    pusty: () => el(`<div class="empty">${icon(czyFiltr() ? 'search' : 'catalog')}<h3>${czyFiltr() ? t('catalog.emptyFiltered') : t('catalog.empty')}</h3></div>`),
  });
  await odswiez(true);
  odpisz = store.on(() => { if (store.zadanie?.stan !== 'postep') odswiez(); });
}

export function unmount() {
  odpisz?.(); odpisz = null; clearTimeout(debounce);
  if (gridEl) sessionStorage.setItem(KLUCZ_SCROLL, String(gridEl.scrollTop));
  siatka?.zniszcz(); siatka = null; gridEl = podsumEl = filtryEl = null;
}

async function odswiez(pierwszy = false) {
  const ctx = ctxRef; if (!ctx || !gridEl) return;
  const { t, icon, bridge } = ctx;
  const bylFokus = document.activeElement?.id === 'cat-szukaj';
  let r;
  try { r = await bridge.call('catalog.list', filtry); }
  catch (e) { if (e.code === 'no_project') return; toast(e.message, { typ: 'error' }); return; }
  if (!gridEl) return;
  podsumEl.textContent = r.pokazane === r.razem
    ? t('catalog.count', { n: fmt.liczba(r.razem), t: fmt.liczba(r.tekstury) })
    : `${t('catalog.count', { n: fmt.liczba(r.razem), t: fmt.liczba(r.tekstury) })} · ${t('catalog.shown', { n: fmt.liczba(r.pokazane), m: fmt.liczba(r.razem) })}`;

  // filtry: jeden pasek — format (segment), zrodlo i slot (listy), przelaczniki problemy / w grupach, wyczysc (szukajka jest w naglowku)
  filtryEl.innerHTML = '';
  const zrodla = r.filtry?.zrodla || []; const sloty = (r.filtry?.sloty || []).slice().sort((a, b) => SLOTY_KOLEJNOSC.indexOf(a.typ) - SLOTY_KOLEJNOSC.indexOf(b.typ));
  const fm = r.filtry?.formaty || {};
  const seg = el('<div class="seg" role="radiogroup"></div>');
  const wybranyFmt = filtry.formaty.length === 1 ? filtry.formaty[0] : '';
  const opcje = [['', t('dup.allVerdicts'), r.razem]];
  if (fm.legacy) opcje.push(['legacy', t('sources.formatLegacy'), fm.legacy]);
  if (fm.gen9) opcje.push(['gen9', t('sources.formatGen9'), fm.gen9]);
  for (const [k, nazwa, n] of opcje) {
    const b = el(`<button role="radio" aria-checked="${wybranyFmt === k}" class="${wybranyFmt === k ? 'on' : ''}">${k ? `<i class="dot ${k}"></i>` : ''}${esc(nazwa)} <span class="n">${fmt.liczba(n)}</span></button>`);
    b.onclick = () => { filtry.formaty = k ? [k] : []; zapiszFiltry(); odswiez(); };
    seg.append(b);
  }
  if (opcje.length > 2) filtryEl.append(seg);
  if (zrodla.length > 1) {
    const sel = el(`<select class="input sm select" aria-label="${esc(t('dup.sourcesFilter'))}"><option value="">${esc(t('dup.sourceAll'))}</option>${zrodla.map(z => `<option value="${esc(z.id)}" ${filtry.zrodla[0] === z.id ? 'selected' : ''}>${esc(z.nazwa)} (${z.n})</option>`).join('')}</select>`);
    sel.onchange = () => { filtry.zrodla = sel.value ? [sel.value] : []; zapiszFiltry(); odswiez(); };
    filtryEl.append(sel);
  }
  if (sloty.length > 1) {
    const sel = el(`<select class="input sm select" aria-label="${esc(t('dup.slots'))}"><option value="">${esc(t('dup.slotAll'))}</option>${sloty.map(s => `<option value="${esc(s.typ)}" ${filtry.sloty[0] === s.typ ? 'selected' : ''}>${esc(t('slot.' + s.typ))} (${s.n})</option>`).join('')}</select>`);
    sel.onchange = () => { filtry.sloty = sel.value ? [sel.value] : []; zapiszFiltry(); odswiez(); };
    filtryEl.append(sel);
  }
  const bp = el(`<button class="switch ${filtry.problemy ? 'on' : ''}"><span>${t('catalog.problems')}</span>${icon(filtry.problemy ? 'toggleOn' : 'toggleOff')}</button>`);
  bp.onclick = () => { filtry.problemy = !filtry.problemy; zapiszFiltry(); odswiez(); };
  const bg = el(`<button class="switch ${filtry.wGrupie ? 'on' : ''}"><span>${t('catalog.inGroups')}</span>${icon(filtry.wGrupie ? 'toggleOn' : 'toggleOff')}</button>`);
  bg.onclick = () => { filtry.wGrupie = !filtry.wGrupie; zapiszFiltry(); odswiez(); };
  filtryEl.append(bp, bg);
  const c = el(`<button class="btn ghost icon clear" title="${esc(t('dup.clearFilters'))}" ${czyFiltr() ? '' : 'style="visibility:hidden"'}>${icon('x')}</button>`);
  c.onclick = () => { filtry = { zrodla: [], sloty: [], formaty: [], problemy: false, wGrupie: false, szukaj: '' }; zapiszFiltry(); const inp = document.getElementById('cat-szukaj'); if (inp) inp.value = ''; odswiez(); };
  filtryEl.append(c);
  if (bylFokus) { const inp = document.getElementById('cat-szukaj'); if (inp) { inp.focus(); inp.setSelectionRange(inp.value.length, inp.value.length); } }

  siatka.ustaw(r.pozycje || []);
  if (pierwszy) { const sc = Number(sessionStorage.getItem(KLUCZ_SCROLL) || 0); if (sc > 0) { gridEl.scrollTop = sc; siatka.odswiez(); } }
}

function przelacz(lista, x) { const i = lista.indexOf(x); if (i >= 0) lista.splice(i, 1); else lista.push(x); zapiszFiltry(); }

function kafelek(p, ctx) {
  const { t, icon, navigate } = ctx;
  const problemy = [];
  if (p.bezMipow) problemy.push(`<span class="badge err" title="${esc(t('catalog.problemMips'))}">!mip</span>`);
  if (p.bc1Alfa) problemy.push(`<span class="badge err" title="${esc(t('catalog.problemBc1'))}">BC1α</span>`);
  const k = el(`
    <button class="cat-tile ${p.grupa ? 'in-group' : ''}" data-id="${esc(p.id)}" title="${esc(nazwaPozycji(p))} ${esc(p.sufiks || '')} · ${esc(p.zrodlo)} · ${esc(p.kontener || '')}${p.grupa ? ' · ' + esc(t('verdict.' + p.grupa)) : ''}">
      <div class="thumb">${p.thumb ? `<img src="https://duble.data/thumb/${esc(p.thumb)}.png" alt="" loading="lazy">` : icon('cube')}${p.grupa ? `<span class="vico ${KLASA_WERDYKTU[p.grupa] || ''}" title="${esc(t('verdict.' + p.grupa))}">${icon(IKONA_WERDYKTU[p.grupa] || 'duplicates')}</span>` : ''}${p.wArchiwum ? `<span class="arch" title="${esc(t('group.inArchive'))}">${icon('archive')}</span>` : ''}</div>
      <div class="nm">${esc(nazwaPozycji(p))}<sub>${esc(p.sufiks || '')}</sub></div>
      <div class="src" title="${esc(p.zrodlo)}">${esc(p.zrodlo)}</div>
      <div class="tile-badges"><span class="badge ${p.gen9 ? 'gen9' : 'legacy'}">${p.gen9 ? t('sources.formatGen9') : t('sources.formatLegacy')}</span><span class="faint">${t('dup.textures', { n: p.tekstur })}</span>${problemy.join('')}</div>
    </button>`);
  k.onclick = () => navigate('catalog/' + encodeURIComponent(p.id));
  return k;
}
