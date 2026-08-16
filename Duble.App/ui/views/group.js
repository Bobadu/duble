// views/group.js — karta porownania jednej grupy: kolumny czlonkow, jakosc (slupki), tekstury z dopasowaniami, decyzje, notatka.
import { el, esc, toast, fmt, confirm } from '../ui.js';
import { wipe } from '../wipe.js';
import { KLASA_WERDYKTU, powodTekst, nazwaPozycji, znaczekWerdyktu } from './duplicates.js';
import * as group3d from './group3d.js';
import { blokJakosci, kafelekTekstury, sciezkaKrotka } from './parts.js';

let odpisz = null, ctxRef = null, rootEl = null, idGrupy = null, debounceNotatki = null;
let uchwyt3d = null;   // { zniszcz } biezacej zakladki 3D
function zakladka() { return sessionStorage.getItem('group.tab') || '2d'; }

export async function render(root, ctx) {
  ctxRef = ctx; rootEl = root; idGrupy = ctx.param;
  const { store, navigate, t, icon } = ctx;
  if (!store.projekt) { navigate('duplicates'); return; }
  await odswiez();
  odpisz = store.on(() => odswiez(true));
}

export function unmount() { odpisz?.(); odpisz = null; rootEl = null; clearTimeout(debounceNotatki); uchwyt3d?.zniszcz(); uchwyt3d = null; }

async function odswiez(tylkoStan = false) {
  const ctx = ctxRef; if (!ctx || !rootEl) return;
  const { t, icon, bridge, navigate } = ctx;
  let g;
  try { g = (await bridge.call('groups.get', { id: idGrupy })).grupa; }
  catch (e) {
    if (e.code === 'not_found') { navigate('duplicates'); return; }
    if (tylkoStan) return;
    rootEl.innerHTML = ''; rootEl.append(el(`<div class="empty"><h3>${t('common.error')}</h3><p class="mono">${esc(e.message)}</p></div>`)); return;
  }
  // notatka w trakcie pisania: nie przerysowuj calej karty przy zdarzeniach w tle
  if (tylkoStan && document.activeElement?.id === 'group-note') return;
  uchwyt3d?.zniszcz(); uchwyt3d = null;
  rootEl.innerHTML = '';
  rootEl.append(naglowek(g, ctx));
  const panel = el('<div id="group-panel"></div>');
  rootEl.append(panel);
  await pokazZakladke(g, ctx, panel);
}

async function pokazZakladke(g, ctx, panel) {
  uchwyt3d?.zniszcz(); uchwyt3d = null;
  panel.innerHTML = '';
  rootEl?.querySelectorAll('.tab[data-tab]').forEach(b => b.classList.toggle('on', b.dataset.tab === zakladka()));
  if (zakladka() === '3d') uchwyt3d = await group3d.render(panel, g, ctx);
  else panel.append(kolumny(g, ctx));
}

function naglowek(g, ctx) {
  const { t, icon, bridge, navigate } = ctx;
  const r = g.rozstrzygniecie || {};
  const czl = g.czlonkowie || [];
  const nazwy = czl.map(c => `<span class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></span>`).join('<span class="sep">·</span>');
  const h = el(`
    <div class="view-head group-head">
      <div class="titles">
        <a class="back-link" href="#/duplicates" id="g-back">${icon('chevron', 'rot90')}${t('group.back')}</a>
        <h1 class="group-h1">${nazwy}</h1>
        <div class="group-sub">${znaczekWerdyktu(t, icon, g.werdykt)}<span class="group-powod">${esc(powodTekst(t, g.powod))}</span>${czl[0]?.typ ? `<span class="faint">· ${esc(t('slot.' + czl[0].typ))}</span>` : ''}</div>
      </div>
      <div class="actions">
        <div class="filtr-szukaj note"><span class="ico-wrap">${icon('file')}</span><input class="input" id="group-note" placeholder="${esc(t('group.notePlaceholder'))}" value="${esc(r.notatka || '')}" aria-label="${esc(t('group.note'))}"></div>
        <button class="btn" id="g-ign" aria-pressed="${!!r.ignoruj}">${icon(r.ignoruj ? 'ok' : 'x')}${r.ignoruj ? t('group.isDuplicate') : t('group.notDuplicate')}</button>
        ${!r.domyslna ? `<button class="btn icon" id="g-reset" title="${esc(t('group.reset'))}" aria-label="${esc(t('group.reset'))}">${icon('refresh')}</button>` : ''}
      </div>
    </div>`);
  h.querySelector('#g-back').onclick = (e) => { e.preventDefault(); navigate('duplicates'); };
  h.querySelector('#g-ign').onclick = async () => { await decyduj(ctx, { ignoruj: !r.ignoruj }); };
  h.querySelector('#g-reset')?.addEventListener('click', async () => {
    try { await bridge.call('groups.reset', { id: g.id }); toast(t('decision.saved'), { typ: 'ok', czas: 1500 }); await odswiez(); } catch (e) { toast(e.message, { typ: 'error' }); }
  });
  h.querySelector('#group-note').addEventListener('input', (e) => {
    clearTimeout(debounceNotatki);
    debounceNotatki = setTimeout(() => decyduj(ctx, { notatka: e.target.value }, true), 600);
  });
  const pasek = el(`<div class="group-bar">${r.ignoruj ? `<div class="banner">${icon('info')}<span>${t('group.ignoredBanner')}</span></div>` : ''}
    <div class="tabs"><button class="tab ${zakladka() === '2d' ? 'on' : ''}" data-tab="2d">${icon('catalog')}${t('group.tab2d')}</button><button class="tab ${zakladka() === '3d' ? 'on' : ''}" data-tab="3d">${icon('cube')}${t('group.tab3d')}</button></div>
  </div>`);
  pasek.querySelectorAll('.tab[data-tab]').forEach(b => b.onclick = async () => { sessionStorage.setItem('group.tab', b.dataset.tab); const panel = document.getElementById('group-panel'); if (panel) await pokazZakladke(g, ctx, panel); });
  const w = el('<div></div>'); w.append(h, pasek);
  return w;
}

async function decyduj(ctx, zmiana, cicho = false) {
  const { t, bridge } = ctx;
  try { await bridge.call('groups.decide', { id: idGrupy, ...zmiana }); if (!cicho) toast(t('decision.saved'), { typ: 'ok', czas: 1500 }); if (!cicho) await odswiez(); }
  catch (e) { toast(e.message, { typ: 'error' }); }
}

function kolumny(g, ctx) {
  const { t, icon, bridge } = ctx;
  const r = g.rozstrzygniecie || {};
  const czl = g.czlonkowie || [];
  // mapa sha -> partnerzy (dla podswietlania i wipe): z dopasowan par czlonkow
  const partner = new Map();   // sha -> [{sha, czlonekId}]
  for (const d of g.dopasowania || []) for (const [sa, sb] of d.pary || []) {
    if (!partner.has(sa)) partner.set(sa, []); if (!partner.has(sb)) partner.set(sb, []);
    partner.get(sa).push({ sha: sb, czlonek: d.b }); partner.get(sb).push({ sha: sa, czlonek: d.a });
  }
  const kont = el(`<div class="group-cols" style="--n:${czl.length}"></div>`);
  for (const c of czl) {
    const zostaje = r.zwyciezca === c.id; const odrzucona = !r.ignoruj && (r.odrzucone || []).includes(c.id);
    const stan = r.ignoruj ? 'neutral' : zostaje ? 'stays' : odrzucona ? 'rejected' : 'neutral';
    const kol = el(`
      <div class="group-col ${stan}">
        <div class="col-head">
          <div class="col-title"><span class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></span><span class="badge ${c.gen9 ? 'gen9' : 'legacy'}">${c.gen9 ? t('sources.formatGen9') : t('sources.formatLegacy')}</span>${stan === 'stays' ? `<span class="badge ok col-state">${icon('check')}${t('group.stays')}</span>` : stan === 'rejected' ? `<span class="badge err col-state">${icon('x')}${t('group.rejected')}</span>` : `<span class="badge unknown col-state">${t('group.neutral')}</span>`}</div>
          <div class="col-src" title="${esc(c.zrodlo)} › ${esc(c.kontener || '')}">${esc(c.zrodlo)}<span class="faint"> › ${esc(c.kontener || '')}</span></div>
          <div class="btn-row">
            ${!zostaje && !r.ignoruj ? `<button class="btn sm primary" data-akcja="keep">${icon('check')}${t('group.keepThis')}</button>` : ''}
            ${!zostaje && !r.ignoruj ? (odrzucona ? `<button class="btn sm" data-akcja="unreject">${icon('ok')}${t('group.unreject')}</button>` : `<button class="btn sm danger" data-akcja="reject">${icon('trash')}${t('group.reject')}</button>`) : ''}
          </div>
        </div>
        <div class="col-quality">${blokJakosci(c, t)}</div>
        <div class="col-facts">
          <div><span class="faint">${t('group.model')}</span> <b>${fmt.liczba(c.wierzcholki)}</b> ${t('group.verts')} · <b>${fmt.liczba(c.trojkaty)}</b> ${t('group.tris')} · ${t('group.lods')} <b>${c.lody}</b></div>
          <div><span class="faint">${t('group.size')}</span> <b>${fmt.rozmiar(c.bajty)}</b> · ${t('dup.textures', { n: c.tekstur })}</div>
          <div class="col-path"><span class="faint">${t('group.path')}</span> <span class="mono select-text" title="${esc(c.sciezkaYdd || '')}">${esc(sciezkaKrotka(c.sciezkaYdd, 10000))}</span> ${c.wArchiwum ? `<a href="#/sources" class="badge unknown" title="${esc(t('apply.tooltipArchive'))}">${t('group.inArchive')}</a>` : `<button class="btn ghost sm" data-akcja="explorer" title="${esc(t('group.showInExplorer'))}">${icon('external')}</button>`}</div>
        </div>
        <div class="col-tex-head"><span>${t('group.textures')}</span><span class="faint">${matchesTekst(t, c, partner)}</span></div>
        <div class="tex-grid"></div>
      </div>`);
    kol.querySelector('[data-akcja="keep"]')?.addEventListener('click', () => decyduj(ctx, { zwyciezca: c.id }));
    kol.querySelector('[data-akcja="reject"]')?.addEventListener('click', () => decyduj(ctx, { odrzucone: [...(r.odrzucone || []), c.id] }));
    kol.querySelector('[data-akcja="unreject"]')?.addEventListener('click', () => decyduj(ctx, { odrzucone: (r.odrzucone || []).filter(x => x !== c.id) }));
    kol.querySelector('[data-akcja="explorer"]')?.addEventListener('click', () => bridge.call('shell.showInExplorer', { sciezka: c.sciezkaYdd }).catch(e => toast(e.message, { typ: 'warn' })));
    const grid = kol.querySelector('.tex-grid');
    for (const tx of c.tekstury || []) grid.append(kafelek(tx, c, partner, ctx, g));
    kont.append(kol);
  }
  return kont;
}

function matchesTekst(t, c, partner) {
  const n = (c.tekstury || []).filter(x => partner.has(x.sha)).length;
  return n ? t('group.matches', { n }) : '';
}

function kafelek(tx, c, partner, ctx, g) {
  const { t } = ctx;
  const par = partner.get(tx.sha) || [];
  const k = kafelekTekstury(tx, { para: par.length > 0, tytul: par.length ? t('group.pair') : t('group.single') });
  k.addEventListener('mouseenter', () => { for (const p of par) document.querySelectorAll(`.tex[data-sha="${CSS.escape(p.sha)}"]`).forEach(x => x.classList.add('para-hover')); });
  k.addEventListener('mouseleave', () => { document.querySelectorAll('.tex.para-hover').forEach(x => x.classList.remove('para-hover')); });
  k.onclick = () => {
    if (!tx.zdekodowana || !tx.sha) { toast(t('wipe.noPreview'), { typ: 'warn' }); return; }
    const podpisA = `${c.zrodlo} · ${tx.plik} · ${tx.w}×${tx.h} ${tx.format}`;
    if (par.length) {
      const p = par[0];
      const cz = (g.czlonkowie || []).find(x => x.id === p.czlonek);
      const tb = cz?.tekstury?.find(x => x.sha === p.sha);
      const podpisB = cz && tb ? `${cz.zrodlo} · ${tb.plik} · ${tb.w}×${tb.h} ${tb.format}` : p.sha;
      wipe({ sha: tx.sha, podpis: podpisA }, { sha: p.sha, podpis: podpisB });
    } else wipe({ sha: tx.sha, podpis: podpisA }, null);
  };
  return k;
}
