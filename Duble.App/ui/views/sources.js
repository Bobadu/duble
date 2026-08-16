// views/sources.js — Zrodla: karty zrodel, dodawanie (folder/.rpf/przeciagniecie/wykrycie gier), indeksowanie z postepem.
import { el, esc, dialog, confirm, toast, fmt, menu } from '../ui.js';

let odpisz = null;
let ctxRef = null;
let listaEl = null;

const IKONA_TYPU = { folder: 'folder', rpf: 'archive', fivem: 'server' };
const SLOTY_KOLEJNOSC = ['jbib', 'uppr', 'lowr', 'feet', 'accs', 'task', 'decl', 'teef', 'hand', 'hair', 'berd', 'p_head', 'p_eyes', 'p_ears', 'p_mouth', 'p_lhand', 'p_rhand', 'p_lwrist', 'p_rwrist', 'p_hip'];

export async function render(root, ctx) {
  ctxRef = ctx;
  const { t, icon, bridge, store, navigate } = ctx;
  if (!store.projekt) {
    root.append(el(`<div class="view-head"><div class="titles"><h1>${t('sources.title')}</h1><p class="sub">${t('sources.subtitle')}</p></div></div>`));
    const e = el(`<div class="empty">${icon('file')}<h3>${t('status.noProject')}</h3><p>${t('start.empty')}</p><div class="btn-row" style="justify-content:center"><button class="btn primary" id="do-startu">${icon('home')}${t('nav.start')}</button></div></div>`);
    e.querySelector('#do-startu').onclick = () => navigate('start');
    root.append(e);
    return;
  }
  const head = el(`
    <div class="view-head">
      <div class="titles"><h1>${t('sources.title')}</h1><p class="sub">${t('sources.subtitle')}</p></div>
      <div class="actions">
        <button class="btn" id="add-folder">${icon('folder')}${t('sources.addFolder')}</button>
        <button class="btn" id="add-rpf">${icon('archive')}${t('sources.addRpf')}</button>
        <button class="btn" id="detect">${icon('gamepad')}${t('sources.detect')}</button>
        <button class="btn primary" id="index-all">${icon('play')}${t('sources.indexAll')}</button>
        <button class="btn icon" id="index-more" data-i18n-title="common.more">${icon('chevron')}</button>
      </div>
    </div>`);
  head.querySelector('#add-folder').onclick = () => wynikDodania(bridge.call('sources.pickFolder'), ctx);
  head.querySelector('#add-rpf').onclick = () => wynikDodania(bridge.call('sources.pickRpf'), ctx);
  head.querySelector('#detect').onclick = () => wykryjGry(ctx);
  head.querySelector('#index-all').onclick = () => indeksuj(ctx, {});
  head.querySelector('#index-more').onclick = (e) => menu(e.currentTarget, [
    { tekst: t('sources.indexChanged'), ikona: 'play', akcja: () => indeksuj(ctx, {}) },
    { tekst: t('sources.forceAll'), ikona: 'refresh', akcja: () => indeksuj(ctx, { wymus: true }) },
  ]);
  root.append(head);
  listaEl = el('<div id="lista-zrodel"></div>');
  root.append(listaEl);
  await odswiez();
  odpisz = store.on(() => odswiez());

  const drop = sessionStorage.getItem('drop');
  if (drop) { sessionStorage.removeItem('drop'); dodajSciezki(JSON.parse(drop), ctx); }
}

export function unmount() { odpisz?.(); odpisz = null; listaEl = null; }

export async function dodajSciezki(sciezki, ctx) {
  await wynikDodania(ctx.bridge.call('sources.add', { sciezki }), ctx);
}

async function wynikDodania(obietnica, ctx) {
  const { t } = ctx;
  try {
    const r = await obietnica;
    if (!r) return;
    if (r.dodane?.length) toast(t('sources.added', { n: r.dodane.length }), { typ: 'ok' });
    if (r.pominiete?.length) toast(t('sources.skipped', { n: r.pominiete.length }), { typ: 'warn' });
    await odswiez();
  } catch (e) { toast(e.code === 'no_project' ? t('status.noProject') : e.message, { typ: 'error' }); }
}

async function indeksuj(ctx, { ids, wymus } = {}) {
  const { t, bridge } = ctx;
  try {
    const r = await bridge.call('sources.index', { ids, wymus: !!wymus });
    if (r && r.uruchomiono === false) toast(t('sources.empty'), { typ: 'warn' });
  } catch (e) { toast(e.code === 'busy' ? t('sources.busy') : e.message, { typ: 'warn' }); }
}

let ostatnieZadanie = null;
async function odswiez() {
  const ctx = ctxRef; if (!ctx || !listaEl) return;
  const { t, icon, bridge, store } = ctx;
  // toasty po zakonczeniu zadania (raz na zdarzenie)
  const z = store.zadanie;
  if (z && z !== ostatnieZadanie && z.typ === 'indeks') {
    ostatnieZadanie = z;
    if (z.stan === 'koniec') { const p = store.projekt || {}; toast(t('sources.done', { pozycje: fmt.liczba(p.pozycje), tekstury: fmt.liczba(p.tekstury) }), { typ: 'ok' }); }
    if (z.stan === 'anulowano') toast(t('sources.cancelled'), { typ: 'warn' });
    if (z.stan === 'blad') toast(t('sources.failed', { blad: z.blad || '' }), { typ: 'error', czas: 8000 });
  }
  let zrodla = [];
  try { zrodla = (await bridge.call('sources.list')).zrodla || []; } catch (e) { if (e.code === 'no_project') { listaEl.innerHTML = ''; return; } }
  listaEl.innerHTML = '';
  if (!zrodla.length) {
    listaEl.append(el(`<div class="empty dropzone">${icon('drop')}<h3>${t('sources.dropHint')}</h3><p>${t('sources.empty')}</p></div>`));
    return;
  }
  const grid = el('<div class="grid-cards"></div>');
  const wToku = z && (z.stan === 'start' || z.stan === 'postep') ? z : null;
  for (const s of zrodla) grid.append(karta(s, wToku, ctx));
  listaEl.append(grid);
  listaEl.append(el(`<p class="faint dropzone" style="margin-top:14px;padding:10px 14px;border:1px dashed var(--border-2);border-radius:10px;text-align:center">${icon('drop')} ${t('sources.dropHint')}</p>`));
}

function karta(s, wToku, ctx) {
  const { t, icon, bridge } = ctx;
  const fmtKlasa = { gen9: 'gen9', legacy: 'legacy', mieszany: 'mixed' }[s.format] || 'unknown';
  const fmtTekst = { gen9: t('sources.formatGen9'), legacy: t('sources.formatLegacy'), mieszany: t('sources.formatMixed') }[s.format] || t('sources.formatUnknown');
  const typTekst = { folder: t('sources.typeFolder'), rpf: t('sources.typeRpf'), fivem: t('sources.typeFivem') }[s.typ] || s.typ;
  const sloty = Object.entries(s.perSlot || {}).sort((a, b) => SLOTY_KOLEJNOSC.indexOf(a[0]) - SLOTY_KOLEJNOSC.indexOf(b[0]));
  const pokaz = sloty.slice(0, 8); const reszta = sloty.length - pokaz.length;
  const indeksowane = wToku && wToku.tekst === s.nazwa;
  const k = el(`
    <div class="card src-card ${s.wlaczone ? '' : 'disabled'} ${s.istnieje ? '' : 'missing'}" data-id="${esc(s.id)}">
      <div class="card-body">
        <div class="top">
          <div class="ico-box">${icon(IKONA_TYPU[s.typ] || 'folder')}</div>
          <div class="info">
            <div class="name"><span title="${esc(s.nazwa)}">${esc(s.nazwa)}</span>${s.format ? `<span class="badge ${fmtKlasa}">${esc(fmtTekst)}</span>` : ''}</div>
            <div class="path" title="${esc(s.sciezka)}">${esc(s.sciezka)}</div>
          </div>
          <button class="btn ghost icon menu-btn" data-i18n-title="common.more">${icon('more')}</button>
        </div>
        ${s.istnieje ? '' : `<div class="missing-text">${icon('warn')} ${t('sources.missing')}</div>`}
        <div class="kv"><span><b>${fmt.liczba(s.pozycje)}</b> ${t('sources.items')}</span><span><b>${fmt.liczba(s.tekstury)}</b> ${t('sources.textures')}</span><span class="faint">${esc(typTekst)}</span></div>
        <div class="slots">${pokaz.map(([typ, n]) => `<span class="chip static">${esc(t('slot.' + typ))} <span class="n">${n}</span></span>`).join('')}${reszta > 0 ? `<span class="chip static">+${reszta}</span>` : ''}</div>
        ${indeksowane ? `<div class="indexing"><span>${wToku.stan === 'postep' && wToku.wszystkie ? esc(t('sources.indexingOf', { etap: wToku.etap, zrobione: fmt.liczba(wToku.zrobione), wszystkie: fmt.liczba(wToku.wszystkie) })) : t('sources.indexing')}</span><div class="progress ${wToku.stan === 'postep' && wToku.wszystkie ? '' : 'indeterminate'}"><i style="width:${wToku.procent || 0}%"></i></div></div>` : ''}
        <div class="foot">
          <span>${s.zaindeksowano ? t('sources.indexed', { d: fmt.data(s.zaindeksowano) }) : t('sources.never')}</span>
          <span class="grow"></span>
          <button class="switch ${s.wlaczone ? 'on' : ''}" title="${s.wlaczone ? t('sources.enabled') : t('sources.disabled')}">${icon(s.wlaczone ? 'toggleOn' : 'toggleOff')}<span>${s.wlaczone ? t('sources.enabled') : t('sources.disabled')}</span></button>
        </div>
      </div>
    </div>`);
  k.querySelector('.switch').onclick = async () => { try { await bridge.call('sources.toggle', { id: s.id, wlaczone: !s.wlaczone }); } catch (e) { toast(e.message, { typ: 'error' }); } };
  k.querySelector('.menu-btn').onclick = (e) => menu(e.currentTarget, [
    { tekst: s.zaindeksowano ? t('sources.reindex') : t('sources.index'), ikona: 'play', akcja: () => indeksuj(ctx, { ids: [s.id] }) },
    { tekst: t('sources.forceAll'), ikona: 'refresh', akcja: () => indeksuj(ctx, { ids: [s.id], wymus: true }) },
    { tekst: t('sources.openFolder'), ikona: 'external', akcja: () => bridge.call('shell.showInExplorer', { sciezka: s.sciezka }).catch(err => toast(err.message, { typ: 'warn' })) },
    { sep: true },
    { tekst: t('sources.remove'), ikona: 'trash', niebezpieczna: true, akcja: async () => {
        if (await confirm(t('sources.confirmRemove', { nazwa: s.nazwa }), { ok: t('common.remove'), niebezpieczne: true, tytul: t('sources.remove') }))
          try { await bridge.call('sources.remove', { id: s.id }); } catch (err) { toast(err.message, { typ: 'error' }); }
      } },
  ]);
  return k;
}

async function wykryjGry(ctx) {
  const { t, icon, bridge } = ctx;
  let gry = [];
  try { gry = (await bridge.call('sources.detectGames')).gry || []; } catch (e) { toast(e.message, { typ: 'error' }); return; }
  await dialog({
    tytul: t('sources.detectTitle'), szeroki: true,
    tresc: (body) => {
      if (!gry.length) { body.innerHTML = `<p class="lead">${t('sources.detectNone')}</p>`; return; }
      for (const g of gry) {
        const blok = el(`<div class="section" style="margin-top:14px"><div class="section-head"><h3>${g.gra === 'enhanced' ? t('sources.detectEnhanced') : t('sources.detectLegacy')}</h3><span class="count mono">${esc(g.sciezka)}</span></div></div>`);
        if (!g.propozycje.length) blok.append(el(`<p class="faint">${t('sources.detectNoFolders')}</p>`));
        for (const p of g.propozycje) {
          const w = el(`<label class="chip" style="display:flex;margin:6px 0;padding:8px 12px;cursor:default"><input type="checkbox" checked data-sciezka="${esc(p.sciezka)}"><span>${esc(p.nazwa)}</span><span class="n mono">${esc(p.sciezka)}</span></label>`);
          blok.append(w);
        }
        body.append(blok);
      }
    },
    przyciski: gry.length ? [
      { tekst: t('common.cancel') },
      { tekst: t('sources.detectAdd'), rola: 'primary', akcja: async () => {
          const sciezki = [...document.querySelectorAll('.dialog input[type=checkbox]:checked')].map(i => i.dataset.sciezka);
          if (sciezki.length) await dodajSciezki(sciezki, ctx);
        } },
    ] : [{ tekst: t('common.close'), rola: 'primary' }],
  });
}
