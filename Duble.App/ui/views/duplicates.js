// views/duplicates.js — lista grup duplikatow: filtry (werdykt / slot / zrodlo / szukaj / zignorowane), karty grup, pasek decyzji.
import { el, esc, toast, fmt } from '../ui.js';
import { otworzZastosuj } from './apply.js';

const WERDYKTY = ['DUPLIKAT', 'DUPLIKAT-NADZBIOR', 'DO WGLADU', 'PRZEMALOWANIE'];
export const KLASA_WERDYKTU = { 'DUPLIKAT': 'w-dup', 'DUPLIKAT-NADZBIOR': 'w-nad', 'DO WGLADU': 'w-wgl', 'PRZEMALOWANIE': 'w-prz' };
const KLUCZ_FILTROW = 'dup.filtry';

let odpisz = null;
let ctxRef = null;
let filtry = null;
let listaEl = null, podsumEl = null, pasekEl = null, filtryEl = null;
let ostatnieZadanie = null;
let debounce = null;

function wczytajFiltry() {
  try { return { ...{ werdykty: [], sloty: [], zrodla: [], szukaj: '', zignorowane: false }, ...JSON.parse(sessionStorage.getItem(KLUCZ_FILTROW) || '{}') }; }
  catch { return { werdykty: [], sloty: [], zrodla: [], szukaj: '', zignorowane: false }; }
}
function zapiszFiltry() { sessionStorage.setItem(KLUCZ_FILTROW, JSON.stringify(filtry)); }

export function powodTekst(t, powod) { return powod?.kod ? t('powod.' + powod.kod, powod.p || {}) : ''; }
export function nazwaPozycji(c) { return `${c.typ}_${String(c.numer).padStart(3, '0')}`; }

export async function render(root, ctx) {
  ctxRef = ctx;
  const { t, icon, store, navigate } = ctx;
  filtry = wczytajFiltry();
  if (!store.projekt) {
    root.append(el(`<div class="view-head"><div class="titles"><h1>${t('dup.title')}</h1><p class="sub">${t('dup.subtitle')}</p></div></div>`));
    const e = el(`<div class="empty">${icon('file')}<h3>${t('status.noProject')}</h3><p>${t('start.empty')}</p><div class="btn-row" style="justify-content:center"><button class="btn primary" id="do-startu">${icon('home')}${t('nav.start')}</button></div></div>`);
    e.querySelector('#do-startu').onclick = () => navigate('start');
    root.append(e);
    return;
  }
  const head = el(`
    <div class="view-head">
      <div class="titles"><h1>${t('dup.title')}</h1><p class="sub" id="dup-podsum">${t('dup.subtitle')}</p></div>
      <div class="actions">
        <button class="chip" id="dup-ign" aria-pressed="${filtry.zignorowane}">${icon('history')}${t('dup.showIgnored')}</button>
        <button class="btn" id="dup-recompare">${icon('refresh')}${t('dup.recompare')}</button>
      </div>
    </div>`);
  head.querySelector('#dup-recompare').onclick = () => porownaj(ctx);
  head.querySelector('#dup-ign').onclick = (e) => { filtry.zignorowane = !filtry.zignorowane; e.currentTarget.setAttribute('aria-pressed', filtry.zignorowane); zapiszFiltry(); odswiez(); };
  root.append(head);
  podsumEl = head.querySelector('#dup-podsum');
  filtryEl = el('<div class="dup-filtry" id="dup-filtry"></div>');
  root.append(filtryEl);
  listaEl = el('<div id="dup-lista"></div>');
  root.append(listaEl);
  pasekEl = el('<div class="decision-bar" id="dup-pasek" hidden></div>');
  root.append(pasekEl);
  await odswiez();
  odpisz = store.on(() => odswiez());
}

export function unmount() { odpisz?.(); odpisz = null; listaEl = podsumEl = pasekEl = filtryEl = null; clearTimeout(debounce); }

async function porownaj(ctx) {
  const { t, bridge } = ctx;
  try { await bridge.call('compare.run'); }
  catch (e) { toast(e.code === 'busy' ? t('sources.busy') : e.message, { typ: 'warn' }); }
}

async function odswiez() {
  const ctx = ctxRef; if (!ctx || !listaEl) return;
  const { t, icon, bridge, store, navigate } = ctx;
  const z = store.zadanie;
  if (z && z !== ostatnieZadanie && z.typ === 'porownaj') {
    ostatnieZadanie = z;
    if (z.stan === 'blad') toast(t('dup.compareFailed', { blad: z.blad || '' }), { typ: 'error', czas: 8000 });
  }
  const bylFokus = document.activeElement?.id === 'dup-szukaj';
  let r;
  try { r = await bridge.call('groups.list', filtry); }
  catch (e) { if (e.code === 'no_project') { listaEl.innerHTML = ''; return; } toast(e.message, { typ: 'error' }); return; }
  const pod = r.podsumowanie || {};
  const wToku = z && z.typ === 'porownaj' && (z.stan === 'start' || z.stan === 'postep');

  // podsumowanie w naglowku
  if (pod.grup == null) podsumEl.textContent = t('dup.subtitle');
  else podsumEl.textContent = t('dup.summary', { grup: fmt.liczba(pod.grup), duplikat: fmt.liczba(pod.duplikat), nadzbior: fmt.liczba(pod.nadzbior), wglad: fmt.liczba(pod.wglad), przemalowanie: fmt.liczba(pod.przemalowanie) });

  // filtry
  filtryEl.innerHTML = '';
  if (pod.grup != null) {
    const licz = { 'DUPLIKAT': pod.duplikat, 'DUPLIKAT-NADZBIOR': pod.nadzbior, 'DO WGLADU': pod.wglad, 'PRZEMALOWANIE': pod.przemalowanie };
    const rzad1 = el(`<div class="filtr-rzad"><span class="filtr-etyk">${t('dup.verdicts')}</span></div>`);
    for (const w of WERDYKTY) {
      const b = el(`<button class="chip ${KLASA_WERDYKTU[w]}" aria-pressed="${filtry.werdykty.includes(w)}">${t('werdykt.' + w)} <span class="n">${licz[w] ?? 0}</span></button>`);
      b.onclick = () => { przelacz(filtry.werdykty, w); odswiez(); };
      rzad1.append(b);
    }
    const szuk = el(`<div class="filtr-szukaj"><span class="ico-wrap">${icon('search')}</span><input class="input" id="dup-szukaj" placeholder="${esc(t('dup.searchPlaceholder'))}" value="${esc(filtry.szukaj || '')}" aria-label="${esc(t('dup.search'))}"></div>`);
    szuk.querySelector('input').addEventListener('input', (e) => { clearTimeout(debounce); debounce = setTimeout(() => { filtry.szukaj = e.target.value; zapiszFiltry(); odswiez(); }, 220); });
    rzad1.append(szuk);
    filtryEl.append(rzad1);
    const sloty = r.filtry?.sloty || []; const zrodla = r.filtry?.zrodla || [];
    if (sloty.length > 1) {
      const rz = el(`<div class="filtr-rzad"><span class="filtr-etyk">${t('dup.slots')}</span></div>`);
      for (const s of sloty) { const b = el(`<button class="chip" aria-pressed="${filtry.sloty.includes(s.typ)}">${esc(t('slot.' + s.typ))} <span class="n">${s.n}</span></button>`); b.onclick = () => { przelacz(filtry.sloty, s.typ); odswiez(); }; rz.append(b); }
      filtryEl.append(rz);
    }
    if (zrodla.length > 1) {
      const rz = el(`<div class="filtr-rzad"><span class="filtr-etyk">${t('dup.sourcesFilter')}</span></div>`);
      for (const s of zrodla) { const b = el(`<button class="chip" aria-pressed="${filtry.zrodla.includes(s.id)}">${esc(s.nazwa)} <span class="n">${s.n}</span></button>`); b.onclick = () => { przelacz(filtry.zrodla, s.id); odswiez(); }; rz.append(b); }
      filtryEl.append(rz);
    }
    if (filtry.werdykty.length || filtry.sloty.length || filtry.zrodla.length || filtry.szukaj) {
      const c = el(`<button class="btn ghost sm" id="dup-clear">${icon('x')}${t('dup.clearFilters')}</button>`);
      c.onclick = () => { filtry = { werdykty: [], sloty: [], zrodla: [], szukaj: '', zignorowane: filtry.zignorowane }; zapiszFiltry(); odswiez(); };
      filtryEl.append(c);
    }
    // uzytkownik pisal w polu szukania: po przerysowaniu oddaj fokus i kursor na koniec
    if (bylFokus) { const inp = filtryEl.querySelector('#dup-szukaj'); if (inp) { inp.focus(); inp.setSelectionRange(inp.value.length, inp.value.length); } }
  }

  // lista
  listaEl.innerHTML = '';
  if (pod.grup == null) {
    const e = el(`<div class="empty">${icon('duplicates')}<h3>${t('dup.noResult')}</h3><p>${t('dup.noResultHint')}</p><div class="btn-row" style="justify-content:center"><button class="btn primary" id="dup-now" ${wToku ? 'disabled' : ''}>${icon('play')}${t('dup.compareNow')}</button></div></div>`);
    e.querySelector('#dup-now').onclick = () => porownaj(ctx);
    listaEl.append(e);
    pasekEl.hidden = true;
    return;
  }
  const grupy = r.grupy || [];
  if (!grupy.length) {
    const czyFiltr = filtry.werdykty.length || filtry.sloty.length || filtry.zrodla.length || filtry.szukaj;
    listaEl.append(el(`<div class="empty">${icon(czyFiltr ? 'search' : 'ok')}<h3>${czyFiltr ? t('dup.emptyFiltered') : t('dup.empty')}</h3></div>`));
  } else {
    const lista = el('<div class="dup-grupy"></div>');
    for (const g of grupy) lista.append(kartaGrupy(g, ctx));
    listaEl.append(lista);
  }

  // pasek decyzji
  const d = pod.doOdrzucenia || {};
  const zajety = z && (z.stan === 'start' || z.stan === 'postep');
  pasekEl.hidden = false;
  pasekEl.innerHTML = `
    <div class="decision-text">${icon('trash')}<span>${d.pozycje ? esc(t('dup.toReject', { pozycje: fmt.liczba(d.pozycje), pliki: fmt.liczba(d.pliki), mb: fmt.rozmiar(d.bajty) })) : esc(t('dup.nothingToReject'))}</span>${d.wArchiwum ? `<a href="#/sources" class="faint" title="${esc(t('apply.tooltipArchive'))}">· ${esc(t('dup.inArchive', { n: d.wArchiwum }))}</a>` : ''}${d.wspoldzielone ? `<span class="faint">· ${esc(t('apply.shared', { n: d.wspoldzielone }))}</span>` : ''}</div>
    <button class="btn primary" id="dup-apply" ${!d.pliki || zajety ? 'disabled' : ''} title="${esc(d.pliki ? t('apply.title') : t('apply.nothing'))}">${icon('check')}${t('dup.apply')}</button>`;
  pasekEl.querySelector('#dup-apply').onclick = () => otworzZastosuj(ctx);
}

function przelacz(lista, x) { const i = lista.indexOf(x); if (i >= 0) lista.splice(i, 1); else lista.push(x); zapiszFiltry(); }

function kartaGrupy(g, ctx) {
  const { t, icon, navigate } = ctx;
  const r = g.rozstrzygniecie || {};
  const k = el(`
    <div class="card dup-card clickable ${r.ignoruj ? 'ignored' : ''}" tabindex="0" data-id="${esc(g.id)}">
      <div class="dup-card-head">
        <span class="badge ${KLASA_WERDYKTU[g.werdykt] || ''}">${esc(t('werdykt.' + g.werdykt))}</span>
        <span class="dup-powod">${esc(powodTekst(t, g.powod))}</span>
        <span class="dup-meta">${r.ignoruj ? `<span class="badge unknown">${esc(t('dup.ignored'))}</span>` : ''}${!r.domyslna && !r.ignoruj ? `<span class="badge ok">${esc(t('dup.custom'))}</span>` : ''}${r.notatka ? `<span class="faint" title="${esc(r.notatka)}">${icon('file')}</span>` : ''}</span>
      </div>
      <div class="dup-members"></div>
    </div>`);
  const cz = k.querySelector('.dup-members');
  g.czlonkowie.forEach((c, i) => {
    const zostaje = r.zwyciezca === c.id; const odrzucona = !r.ignoruj && (r.odrzucone || []).includes(c.id);
    if (i > 0) cz.append(el(`<div class="dup-eq">=</div>`));
    cz.append(el(`
      <div class="dup-member ${zostaje ? 'stays' : ''} ${odrzucona ? 'rejected' : ''}">
        <div class="thumb">${c.thumb ? `<img src="https://duble.data/thumb/${esc(c.thumb)}.png" alt="" loading="lazy">` : icon('cube')}${zostaje && !r.ignoruj ? `<span class="crown" title="${esc(t('dup.winner'))}">★</span>` : ''}</div>
        <div class="dup-member-info">
          <div class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></div>
          <div class="src" title="${esc(c.zrodlo)}">${esc(c.zrodlo)}</div>
          <div class="pts"><b>${Math.round(c.punkty)}</b> ${t('dup.points')} · <span class="badge ${c.gen9 ? 'gen9' : 'legacy'}">${c.gen9 ? t('sources.formatGen9') : t('sources.formatLegacy')}</span></div>
          <div class="st">${odrzucona ? `<span class="rej">${icon('x')}${t('dup.rejected')}</span>` : zostaje && !r.ignoruj ? `<span class="keep">${icon('check')}${t('dup.kept')}</span>` : ''}</div>
        </div>
      </div>`));
  });
  const otworz = () => navigate('duplicates/' + encodeURIComponent(g.id));
  k.addEventListener('click', otworz);
  k.addEventListener('keydown', e => { if (e.key === 'Enter') otworz(); });
  return k;
}
