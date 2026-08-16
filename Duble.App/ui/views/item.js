// views/item.js — karta jednej pozycji z Katalogu: naglowek, zakladki Tekstury (2D: jakosc, fakty, tekstury) / Model (3D), grupy z ta pozycja.
import { el, esc, toast, fmt } from '../ui.js';
import { wipe } from '../wipe.js';
import { KLASA_WERDYKTU, powodTekst, nazwaPozycji } from './duplicates.js';
import { blokJakosci, kafelekTekstury, sciezkaKrotka } from './parts.js';
import * as group3d from './group3d.js';

let odpisz = null, ctxRef = null, rootEl = null, idPozycji = null, uchwyt3d = null;
function zakladka() { return sessionStorage.getItem('item.tab') || '2d'; }

export async function render(root, ctx) {
  ctxRef = ctx; rootEl = root; idPozycji = ctx.param;
  const { store, navigate } = ctx;
  if (!store.projekt) { navigate('catalog'); return; }
  await odswiez();
  odpisz = store.on(() => { if (store.zadanie?.stan !== 'postep') odswiez(); });
}

export function unmount() { odpisz?.(); odpisz = null; rootEl = null; uchwyt3d?.zniszcz(); uchwyt3d = null; }

async function odswiez() {
  const ctx = ctxRef; if (!ctx || !rootEl) return;
  const { t, icon, bridge, navigate } = ctx;
  let r;
  try { r = await bridge.call('catalog.item', { id: idPozycji }); }
  catch (e) {
    if (e.code === 'not_found') { navigate('catalog'); return; }
    rootEl.innerHTML = ''; rootEl.append(el(`<div class="empty"><h3>${t('common.error')}</h3><p class="mono">${esc(e.message)}</p></div>`)); return;
  }
  if (!rootEl) return;
  uchwyt3d?.zniszcz(); uchwyt3d = null;
  rootEl.innerHTML = '';
  rootEl.append(naglowek(r, ctx));
  const panel = el('<div id="item-panel"></div>');
  rootEl.append(panel);
  await pokazZakladke(r, ctx, panel);
}

async function pokazZakladke(r, ctx, panel) {
  uchwyt3d?.zniszcz(); uchwyt3d = null;
  panel.innerHTML = '';
  rootEl?.querySelectorAll('.tab[data-tab]').forEach(b => b.classList.toggle('on', b.dataset.tab === zakladka()));
  if (zakladka() === '3d') uchwyt3d = await group3d.render(panel, { czlonkowie: [r.pozycja] }, ctx);
  else panel.append(karta2d(r, ctx));
}

function naglowek(r, ctx) {
  const { t, icon, bridge, navigate } = ctx;
  const c = r.pozycja;
  const h = el(`
    <div class="group-head">
      <button class="btn ghost" id="i-back">${icon('chevron', 'rot90')}${t('item.back')}</button>
      <div class="group-title item-title">
        <span class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></span>
        <span class="badge ${c.gen9 ? 'gen9' : 'legacy'}">${c.gen9 ? t('sources.formatGen9') : t('sources.formatLegacy')}</span>
        <span class="faint">${esc(c.zrodlo)} · ${esc(c.kontener || '')}</span>
        ${c.wArchiwum ? `<span class="badge unknown">${t('group.inArchive')}</span>` : `<button class="btn ghost sm" id="i-explorer" title="${esc(t('group.showInExplorer'))}">${icon('external')}${t('group.showInExplorer')}</button>`}
      </div>
    </div>`);
  h.querySelector('#i-back').onclick = () => navigate('catalog');
  h.querySelector('#i-explorer')?.addEventListener('click', () => bridge.call('shell.showInExplorer', { sciezka: c.sciezkaYdd }).catch(e => toast(e.message, { typ: 'warn' })));
  const pasek = el(`<div class="group-sub"><div class="tabs"><button class="tab ${zakladka() === '2d' ? 'on' : ''}" data-tab="2d">${icon('catalog')}${t('group.tab2d')}</button><button class="tab ${zakladka() === '3d' ? 'on' : ''}" data-tab="3d">${icon('cube')}${t('group.tab3d')}</button></div></div>`);
  pasek.querySelectorAll('.tab[data-tab]').forEach(b => b.onclick = async () => { sessionStorage.setItem('item.tab', b.dataset.tab); const panel = document.getElementById('item-panel'); if (panel) await pokazZakladke(r, ctx, panel); });
  const w = el('<div></div>'); w.append(h, pasek);
  return w;
}

function karta2d(r, ctx) {
  const { t, icon, navigate } = ctx;
  const c = r.pozycja;
  const kont = el(`
    <div class="item-2d">
      <div class="group-col neutral item-col">
        <div class="col-quality">${blokJakosci(c, t)}</div>
        <div class="col-facts">
          <div><span class="faint">${t('group.model')}</span> <b>${fmt.liczba(c.wierzcholki)}</b> ${t('group.verts')} · <b>${fmt.liczba(c.trojkaty)}</b> ${t('group.tris')} · ${t('group.lods')} <b>${c.lody}</b></div>
          <div><span class="faint">${t('group.size')}</span> <b>${fmt.rozmiar(c.bajty)}</b> · ${t('dup.textures', { n: c.tekstur })}</div>
          <div class="col-path"><span class="faint">${t('group.path')}</span> <span class="mono select-text" title="${esc(c.sciezkaYdd || '')}">${esc(sciezkaKrotka(c.sciezkaYdd, 90))}</span></div>
        </div>
        <div class="col-tex-head"><span>${t('group.textures')}</span></div>
        <div class="tex-grid item-tex"></div>
      </div>
      <div class="item-groups">
        <div class="section-head"><h2>${t('item.groups')}</h2></div>
        <div class="item-groups-list"></div>
      </div>
    </div>`);
  const grid = kont.querySelector('.tex-grid');
  for (const tx of c.tekstury || []) {
    const k = kafelekTekstury(tx, { tytul: t('group.single') });
    k.onclick = () => { if (!tx.zdekodowana || !tx.sha) { toast(t('wipe.noPreview'), { typ: 'warn' }); return; } wipe({ sha: tx.sha, podpis: `${c.zrodlo} · ${tx.plik} · ${tx.w}×${tx.h} ${tx.format}` }, null); };
    grid.append(k);
  }
  const lista = kont.querySelector('.item-groups-list');
  if (!r.grupy?.length) lista.append(el(`<p class="muted">${icon('ok')} ${t('item.noGroups')}</p>`));
  for (const g of r.grupy || []) {
    const stan = g.ignoruj ? t('dup.ignored') : g.stan === 'zostaje' ? t('group.stays') : g.stan === 'odrzucona' ? t('group.rejected') : t('group.neutral');
    const row = el(`
      <div class="card item-group clickable" tabindex="0">
        <div class="card-body">
          <div class="dup-card-head"><span class="badge ${KLASA_WERDYKTU[g.werdykt] || ''}">${esc(t('werdykt.' + g.werdykt))}</span><span class="dup-powod">${esc(powodTekst(t, g.powod))}</span><span class="badge ${g.stan === 'zostaje' ? 'ok' : g.stan === 'odrzucona' ? 'err' : 'unknown'}">${esc(stan)}</span></div>
          <div class="item-group-with"><span class="faint">${t('item.with')}</span> ${(g.inni || []).map(i => `<span class="mono">${esc(i.nazwa)}<sub>${esc(i.sufiks || '')}</sub></span> <span class="faint">(${esc(i.zrodlo)})</span>`).join(', ')}</div>
          <div class="btn-row"><button class="btn sm">${icon('duplicates')}${t('item.openGroup')}</button></div>
        </div>
      </div>`);
    const otworz = () => navigate('duplicates/' + encodeURIComponent(g.id));
    row.querySelector('button').onclick = (e) => { e.stopPropagation(); otworz(); };
    row.addEventListener('click', otworz);
    row.addEventListener('keydown', e => { if (e.key === 'Enter') otworz(); });
    lista.append(row);
  }
  return kont;
}
