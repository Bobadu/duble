// views/group.js — karta porownania jednej grupy: kolumny czlonkow, jakosc (slupki), tekstury z dopasowaniami, decyzje, notatka.
import { el, esc, toast, fmt, confirm } from '../ui.js';
import { wipe } from '../wipe.js';
import { KLASA_WERDYKTU, powodTekst, nazwaPozycji } from './duplicates.js';

let odpisz = null, ctxRef = null, rootEl = null, idGrupy = null, debounceNotatki = null;

export async function render(root, ctx) {
  ctxRef = ctx; rootEl = root; idGrupy = ctx.param;
  const { store, navigate, t, icon } = ctx;
  if (!store.projekt) { navigate('duplicates'); return; }
  await odswiez();
  odpisz = store.on(() => odswiez(true));
}

export function unmount() { odpisz?.(); odpisz = null; rootEl = null; clearTimeout(debounceNotatki); }

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
  rootEl.innerHTML = '';
  rootEl.append(naglowek(g, ctx));
  rootEl.append(kolumny(g, ctx));
}

function naglowek(g, ctx) {
  const { t, icon, bridge, navigate } = ctx;
  const r = g.rozstrzygniecie || {};
  const h = el(`
    <div class="group-head">
      <button class="btn ghost" id="g-back">${icon('chevron', 'rot90')}${t('group.back')}</button>
      <div class="group-title">
        <span class="badge ${KLASA_WERDYKTU[g.werdykt] || ''}">${esc(t('werdykt.' + g.werdykt))}</span>
        <span class="group-powod">${esc(powodTekst(t, g.powod))}</span>
      </div>
      <div class="group-actions">
        <button class="chip" id="g-ign" aria-pressed="${!!r.ignoruj}">${icon(r.ignoruj ? 'ok' : 'x')}${r.ignoruj ? t('group.isDuplicate') : t('group.notDuplicate')}</button>
        ${!r.domyslna ? `<button class="btn ghost sm" id="g-reset">${icon('refresh')}${t('group.reset')}</button>` : ''}
      </div>
    </div>`);
  h.querySelector('#g-back').onclick = () => navigate('duplicates');
  h.querySelector('#g-ign').onclick = async () => { await decyduj(ctx, { ignoruj: !r.ignoruj }); };
  h.querySelector('#g-reset')?.addEventListener('click', async () => {
    try { await bridge.call('groups.reset', { id: g.id }); toast(t('decision.saved'), { typ: 'ok', czas: 1500 }); await odswiez(); } catch (e) { toast(e.message, { typ: 'error' }); }
  });
  const pasek = el(`<div class="group-sub">${r.ignoruj ? `<div class="banner">${icon('info')} ${t('group.ignoredBanner')}</div>` : ''}
    <div class="group-note"><label for="group-note">${icon('file')} ${t('group.note')}</label><input class="input" id="group-note" placeholder="${esc(t('group.notePlaceholder'))}" value="${esc(r.notatka || '')}"></div>
    <div class="tabs"><button class="tab on">${icon('catalog')}${t('group.tab2d')}</button><button class="tab" disabled title="${esc(t('wip.title'))}">${icon('cube')}${t('group.tab3d')} <span class="faint">· ${t('wip.title')}</span></button></div>
  </div>`);
  pasek.querySelector('#group-note').addEventListener('input', (e) => {
    clearTimeout(debounceNotatki);
    debounceNotatki = setTimeout(() => decyduj(ctx, { notatka: e.target.value }, true), 600);
  });
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
    const q = c.rozpiska || {};
    const kol = el(`
      <div class="group-col ${stan}">
        <div class="col-head">
          <div class="col-title"><span class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></span><span class="badge ${c.gen9 ? 'gen9' : 'legacy'}">${c.gen9 ? t('sources.formatGen9') : t('sources.formatLegacy')}</span></div>
          <div class="col-src" title="${esc(c.zrodlo)}">${esc(c.zrodlo)} <span class="faint">· ${esc(c.kontener || '')}</span></div>
          <div class="col-state ${stan}">${stan === 'stays' ? `${icon('check')} ${t('group.stays')}` : stan === 'rejected' ? `${icon('x')} ${t('group.rejected')}` : `${t('group.neutral')}`}</div>
          <div class="btn-row">
            ${!zostaje && !r.ignoruj ? `<button class="btn sm primary" data-akcja="keep">${icon('check')}${t('group.keepThis')}</button>` : ''}
            ${!zostaje && !r.ignoruj ? (odrzucona ? `<button class="btn sm" data-akcja="unreject">${icon('ok')}${t('group.unreject')}</button>` : `<button class="btn sm danger" data-akcja="reject">${icon('trash')}${t('group.reject')}</button>`) : ''}
          </div>
        </div>
        <div class="col-quality">
          <div class="q-total"><b>${Math.round(c.punkty)}</b><span>/100 ${t('quality.total')}</span></div>
          ${slupek(t('quality.resolution'), q.rozdz, 40, `${Math.round(q.rozdzPx || 0)} px`)}
          ${slupek(t('quality.mips'), q.mipy, 20, `${Math.round((q.udzialMipow || 0) * 100)} %`)}
          ${slupek(t('quality.variants'), q.warianty, 20, `${q.liczbaWariantow ?? c.tekstur}`)}
          ${slupek(t('quality.format'), q.format, 10, q.zlyFormat ? `${q.zlyFormat} BC1+α` : 'ok')}
          ${slupek(t('quality.lod'), q.lod, 10, `${q.lody ?? c.lody}`)}
        </div>
        <div class="col-facts">
          <div><span class="faint">${t('group.model')}</span> <b>${fmt.liczba(c.wierzcholki)}</b> ${t('group.verts')} · <b>${fmt.liczba(c.trojkaty)}</b> ${t('group.tris')} · ${t('group.lods')} <b>${c.lody}</b></div>
          <div><span class="faint">${t('group.size')}</span> <b>${fmt.rozmiar(c.bajty)}</b> · ${t('dup.textures', { n: c.tekstur })}</div>
          <div class="col-path"><span class="faint">${t('group.path')}</span> <span class="mono select-text" title="${esc(c.sciezkaYdd || '')}">${esc(sciezkaKrotka(c.sciezkaYdd))}</span> ${c.wArchiwum ? `<span class="badge unknown">${t('group.inArchive')}</span>` : `<button class="btn ghost sm" data-akcja="explorer" title="${esc(t('group.showInExplorer'))}">${icon('external')}</button>`}</div>
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

function slupek(etyk, wartosc, maks, opis) {
  const v = Math.max(0, Math.min(maks, Number(wartosc) || 0));
  return `<div class="q-row"><span class="q-lab">${esc(etyk)}</span><div class="q-bar"><i style="width:${(v / maks) * 100}%"></i></div><span class="q-val">${Math.round(v)}/${maks}</span><span class="q-desc faint">${esc(opis)}</span></div>`;
}

function sciezkaKrotka(p) { if (!p) return ''; const s = p.replace('|', ' › '); return s.length > 60 ? '…' + s.slice(-59) : s; }

function kafelek(tx, c, partner, ctx, g) {
  const { t, icon } = ctx;
  const par = partner.get(tx.sha) || [];
  const zn = [];
  if (tx.mipy <= 1) zn.push('!mip'); if (tx.format === 'BC1' && tx.alfa > 0.02) zn.push('!BC1α');
  const k = el(`
    <button class="tex ${par.length ? 'has-pair' : ''}" data-sha="${esc(tx.sha || '')}" title="${esc(tx.plik)}&#10;${tx.w}×${tx.h} ${esc(tx.format)} · ${tx.mipy} mip · ${esc(par.length ? t('group.pair') : t('group.single'))}">
      <div class="tex-img">${tx.zdekodowana && tx.sha ? `<img src="https://duble.data/thumb/${esc(tx.sha)}.png" alt="" loading="lazy">` : `<span class="tex-nopreview">${esc(tx.format || '?')}</span>`}${par.length ? `<span class="tex-dot" aria-hidden="true"></span>` : ''}</div>
      <div class="tex-cap"><span class="tex-name">${esc(literaZPliku(tx.plik))}</span><span class="tex-meta">${tx.w}×${tx.h} ${esc(tx.format || '')}${zn.length ? ` <span class="warn-txt">${zn.join(' ')}</span>` : ''}</span></div>
    </button>`);
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

function literaZPliku(plik) { const m = /_diff_\d{3}_([a-z])_/i.exec(plik || ''); return m ? m[1].toUpperCase() : (plik || ''); }
