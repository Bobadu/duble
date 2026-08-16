// views/history.js — Historia zastosowan: karty wpisow (kiedy, ile, dokad), Cofnij wszystko / te pozycje, szczegoly; eksport raportu HTML i CSV.
import { el, esc, toast, fmt, confirm } from '../ui.js';

let odpisz = null, odpiszHist = null, ctxRef = null, listaEl = null;
const rozwiniete = new Set();   // pliki wpisow z otwartymi szczegolami

export async function render(root, ctx) {
  ctxRef = ctx;
  const { t, icon, store, navigate, bridge } = ctx;
  if (!store.projekt) {
    root.append(el(`<div class="view-head"><div class="titles"><h1>${t('history.title')}</h1><p class="sub">${t('history.subtitle')}</p></div></div>`));
    const e = el(`<div class="empty">${icon('file')}<h3>${t('status.noProject')}</h3><p>${t('start.empty')}</p><div class="btn-row" style="justify-content:center"><button class="btn primary" id="do-startu">${icon('home')}${t('nav.start')}</button></div></div>`);
    e.querySelector('#do-startu').onclick = () => navigate('start');
    root.append(e);
    return;
  }
  const head = el(`
    <div class="view-head">
      <div class="titles"><h1>${t('history.title')}</h1><p class="sub">${t('history.subtitle')}</p></div>
      <div class="actions">
        <span class="faint">${t('history.export')}</span>
        <button class="btn" id="exp-html">${icon('file')}${t('history.exportHtml')}</button>
        <button class="btn" id="exp-csv">${icon('catalog')}${t('history.exportCsv')}</button>
      </div>
    </div>`);
  head.querySelector('#exp-html').onclick = () => eksport(ctx, 'report.exportHtml');
  head.querySelector('#exp-csv').onclick = () => eksport(ctx, 'report.exportCsv');
  root.append(head);
  listaEl = el('<div id="hist-lista"></div>');
  root.append(listaEl);
  await odswiez();
  // nie store.on: kazdy tik postepu zadania odswiezalby liste (czytanie plikow cofek); wystarcza zdarzenia historii i koniec cofania
  odpiszHist = bridge.on('history.changed', () => odswiez());
  odpisz = bridge.on('undo.done', () => odswiez());
}

export function unmount() { odpisz?.(); odpisz = null; odpiszHist?.(); odpiszHist = null; listaEl = null; }

async function eksport(ctx, cmd) {
  const { t, bridge } = ctx;
  try {
    const r = await bridge.call(cmd, {});
    if (r?.anulowano) return;
    if (cmd === 'report.exportHtml' && r?.uruchomiono) toast(t('history.exporting'), { typ: 'info', czas: 2500 });
  } catch (e) { toast(e.code === 'busy' ? t('sources.busy') : e.code === 'not_found' ? t('dup.noResult') : e.message, { typ: 'warn' }); }
}

async function odswiez() {
  const ctx = ctxRef; if (!ctx || !listaEl) return;
  const { t, icon, bridge } = ctx;
  let wpisy = [];
  try { wpisy = (await bridge.call('history.list')).wpisy || []; } catch (e) { if (e.code === 'no_project') { listaEl.innerHTML = ''; return; } toast(e.message, { typ: 'error' }); return; }
  listaEl.innerHTML = '';
  if (!wpisy.length) { listaEl.append(el(`<div class="empty">${icon('history')}<h3>${t('history.empty')}</h3><p>${t('history.emptyHint')}</p></div>`)); return; }
  const lista = el('<div class="hist-list"></div>');
  for (const w of wpisy) lista.append(karta(w, ctx));
  listaEl.append(lista);
}

function karta(w, ctx) {
  const { t, icon, bridge } = ctx;
  if (w.uszkodzony) return el(`<div class="card hist-card"><div class="card-body"><span class="badge err">${t('common.error')}</span> <span class="mono">${esc(w.nazwa)}</span> <span class="faint">${esc(w.blad || '')}</span></div></div>`);
  const stan = w.cofnieto ? `<span class="badge ok">${t('history.undone')}</span>` : w.czesciowo ? `<span class="badge unknown">${t('history.partly')}</span>` : '';
  const przerw = w.przerwano ? `<span class="badge err" title="${esc(w.blad || '')}">${t('history.interrupted')}</span>` : '';
  const k = el(`
    <div class="card hist-card ${w.cofnieto ? 'undone' : ''}" data-plik="${esc(w.plik)}">
      <div class="card-body">
        <div class="hist-top">
          <div class="ico-box">${icon('history')}</div>
          <div class="info">
            <div class="name">${esc(fmt.data(w.kiedy))} <span class="faint">· ${esc(w.opis || '')}</span> ${stan} ${przerw}</div>
            <div class="meta">${esc(t('history.entry', { pozycje: fmt.liczba(w.pozycje), pliki: fmt.liczba(w.pliki), mb: fmt.rozmiar(w.bajty) }))}${w.wspoldzielone || w.wArchiwum || w.brakujace ? ` <span class="faint">· ${[w.wspoldzielone ? t('apply.shared', { n: w.wspoldzielone }) : '', w.wArchiwum ? t('apply.inArchive', { n: w.wArchiwum }) : '', w.brakujace ? t('apply.missing', { n: w.brakujace }) : ''].filter(Boolean).map(esc).join(' · ')}</span>` : ''}</div>
            <div class="meta mono">${(w.kosze || []).map(k => `${esc(t('history.to', { kosz: fmt.sciezkaKrotka(k, 70) }))}`).join('<br>')}</div>
          </div>
          <div class="btn-row">
            ${w.kosze?.length && !w.cofnieto ? `<button class="btn ghost sm" data-akcja="folder">${icon('external')}${t('history.showFolder')}</button>` : ''}
            <button class="btn ghost sm" data-akcja="szczegoly">${icon('chevron', rozwiniete.has(w.plik) ? 'rot180' : '')}${t('history.details')}</button>
            ${w.moznaCofnac ? `<button class="btn sm primary" data-akcja="cofnij">${icon('refresh')}${t('history.undoAll')}</button>` : (!w.cofnieto ? `<span class="faint">${t('history.gone')}</span>` : '')}
          </div>
        </div>
        <div class="hist-details" hidden></div>
      </div>
    </div>`);
  k.querySelector('[data-akcja="folder"]')?.addEventListener('click', () => bridge.call('shell.openFolder', { sciezka: w.kosze[0] }).catch(e => toast(e.message, { typ: 'warn' })));
  k.querySelector('[data-akcja="cofnij"]')?.addEventListener('click', () => cofnij(ctx, w.plik, null, w.pliki));
  const det = k.querySelector('.hist-details');
  const btnDet = k.querySelector('[data-akcja="szczegoly"]');
  const pokaz = async () => {
    det.hidden = false; det.innerHTML = `<p class="faint">${t('common.loading')}</p>`;
    try {
      const r = await bridge.call('history.get', { plik: w.plik });
      det.innerHTML = '';
      det.append(tabela(r.wpis, ctx));
    } catch (e) { det.innerHTML = `<p class="warn-txt">${esc(e.message)}</p>`; }
  };
  btnDet.onclick = () => { if (rozwiniete.has(w.plik)) { rozwiniete.delete(w.plik); det.hidden = true; btnDet.querySelector('.ico').classList.remove('rot180'); } else { rozwiniete.add(w.plik); btnDet.querySelector('.ico').classList.add('rot180'); pokaz(); } };
  if (rozwiniete.has(w.plik)) pokaz();
  return k;
}

function tabela(w, ctx) {
  const { t, icon } = ctx;
  const tb = el(`<table class="hist-table"><thead><tr><th>${t('history.colItem')}</th><th>${t('history.colSource')}</th><th>${t('history.colFiles')}</th><th></th></tr></thead><tbody></tbody></table>`);
  const body = tb.querySelector('tbody');
  for (const p of w.lista || []) {
    const wrocily = p.pliki.filter(f => f.cofniety).length;
    const tr = el(`<tr class="${p.moznaCofnac ? '' : 'done'}">
      <td><span class="nm mono">${esc(p.nazwa)}</span></td>
      <td><span title="${esc(p.kosz || '')}">${esc(p.zrodlo || '')}</span></td>
      <td>${p.pliki.length}${wrocily ? ` <span class="faint">(${esc(t('history.returned', { n: wrocily }))})</span>` : ''}</td>
      <td class="act">${p.moznaCofnac ? `<button class="btn ghost sm">${icon('refresh')}${t('history.undoOne')}</button>` : (wrocily === p.pliki.length ? `<span class="badge ok">${t('history.undone')}</span>` : `<span class="faint">${t('history.gone')}</span>`)}</td>
    </tr>`);
    tr.querySelector('button')?.addEventListener('click', () => cofnij(ctx, w.plik, [p.id], p.pliki.length));
    body.append(tr);
  }
  return tb;
}

async function cofnij(ctx, plik, pozycje, n) {
  const { t, bridge } = ctx;
  if (!await confirm(t('history.confirmUndo', { n: fmt.liczba(n) }), { ok: pozycje ? t('history.undoOne') : t('history.undoAll'), tytul: t('history.title') })) return;
  try {
    const r = await bridge.call('history.undo', { plik, pozycje: pozycje || [] });
    if (!r?.uruchomiono) toast(t('history.gone'), { typ: 'warn' }); else toast(t('history.undoing'), { typ: 'info', czas: 2500 });
  } catch (e) { toast(e.code === 'busy' ? t('sources.busy') : e.message, { typ: 'error' }); }
}
