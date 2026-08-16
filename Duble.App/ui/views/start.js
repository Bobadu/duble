// views/start.js — ekran startowy: logo, opis, nowy/otworz projekt, ostatnie projekty.
import { el, esc, dialog, toast, fmt, menu } from '../ui.js';

let odpiszStore = null;

export async function render(root, ctx) {
  const { t, icon, bridge, navigate, store } = ctx;
  root.append(el(`
    <div class="hero">
      ${icon('logo', 'logo')}
      <div>
        <h1>${t('start.title')}<span class="by">${t('app.by')}</span></h1>
        <p class="sub">${t('start.subtitle')}</p>
      </div>
    </div>`));
  const akcje = el(`<div class="hero-actions"><button class="btn primary lg" id="nowy">${icon('plus')}${t('start.new')}</button><button class="btn lg" id="otworz">${icon('folder')}${t('start.open')}</button></div>`);
  akcje.querySelector('#nowy').onclick = () => nowyProjekt(ctx);
  akcje.querySelector('#otworz').onclick = async () => {
    try { const r = await bridge.call('project.pickOpen'); if (r?.projekt) navigate('sources'); }
    catch (e) { toast(e.message, { typ: 'error' }); }
  };
  root.append(akcje);

  const sekcja = el(`<div class="section"><div class="section-head"><h2>${t('start.recent')}</h2><span class="count" id="ile"></span></div><div id="ostatnie"></div></div>`);
  root.append(sekcja);
  await odswiezOstatnie(sekcja.querySelector('#ostatnie'), sekcja.querySelector('#ile'), ctx);
  odpiszStore = store.on(() => odswiezOstatnie(sekcja.querySelector('#ostatnie'), sekcja.querySelector('#ile'), ctx));
}

export function unmount() { odpiszStore?.(); odpiszStore = null; }

async function odswiezOstatnie(kont, ile, ctx) {
  const { t, icon, bridge, navigate } = ctx;
  let ostatnie = [];
  try { ostatnie = (await bridge.call('project.recent')).ostatnie || []; } catch { }
  kont.innerHTML = '';
  ile.textContent = ostatnie.length ? String(ostatnie.length) : '';
  if (!ostatnie.length) { kont.append(el(`<div class="empty">${icon('file')}<p>${t('start.empty')}</p></div>`)); return; }
  const grid = el('<div class="grid-cards"></div>');
  for (const o of ostatnie) {
    const k = el(`
      <div class="card proj-card clickable ${o.istnieje ? '' : 'missing'}" tabindex="0">
        <div class="card-body">
          <div class="ico-box">${icon(o.istnieje ? 'file' : 'warn')}</div>
          <div class="info">
            <div class="name">${esc(o.nazwa)}</div>
            <div class="path" title="${esc(o.sciezka)}">${esc(o.sciezka)}</div>
            <div class="meta">${o.istnieje ? t('start.lastOpened', { d: fmt.data(o.ostatnio) }) : t('start.missing')}</div>
          </div>
          <button class="btn ghost icon more" data-i18n-title="common.more">${icon('more')}</button>
        </div>
      </div>`);
    const otworz = async () => {
      if (!o.istnieje) return;
      try { await bridge.call('project.open', { sciezka: o.sciezka }); navigate('sources'); }
      catch (e) { toast(e.message, { typ: 'error' }); }
    };
    k.addEventListener('click', e => { if (!e.target.closest('.more')) otworz(); });
    k.addEventListener('keydown', e => { if (e.key === 'Enter') otworz(); });
    k.querySelector('.more').onclick = (e) => {
      e.stopPropagation();
      menu(e.currentTarget, [
        { tekst: t('sources.openFolder'), ikona: 'external', akcja: () => bridge.call('shell.showInExplorer', { sciezka: o.sciezka }).catch(() => {}) },
        { tekst: t('start.remove'), ikona: 'trash', niebezpieczna: true, akcja: async () => { await bridge.call('project.forget', { sciezka: o.sciezka }); await odswiezOstatnie(kont, ile, ctx); } },
      ]);
    };
    grid.append(k);
  }
  kont.append(grid);
}

async function nowyProjekt(ctx) {
  const { t, icon, bridge, navigate } = ctx;
  let folderDomyslny = '';
  try { folderDomyslny = (await bridge.call('project.recent')).folderDomyslny || ''; } catch { }
  await dialog({
    tytul: t('start.new'),
    tresc: (body) => {
      body.innerHTML = `
        <div class="field"><label for="pn">${t('start.projectName')}</label><input class="input" id="pn" data-i18n-placeholder="start.projectNamePlaceholder" autocomplete="off"></div>
        <div class="field"><label for="pf">${t('start.projectFolder')}</label><div class="row"><input class="input" id="pf" value="${esc(folderDomyslny)}"><button class="btn" id="pfb">${icon('folder')}${t('common.browse')}</button></div></div>
        <div class="error-text" id="pe" hidden></div>`;
      body.querySelector('#pn').placeholder = t('start.projectNamePlaceholder');
      body.querySelector('#pfb').onclick = async () => { const r = await bridge.call('project.pickFolder'); if (r?.sciezka) body.querySelector('#pf').value = r.sciezka; };
      setTimeout(() => body.querySelector('#pn').focus(), 50);
    },
    przyciski: [
      { tekst: t('common.cancel') },
      { tekst: t('start.create'), rola: 'primary', akcja: async (zamknij) => {
          const body = document.querySelector('.dialog .body');
          const nazwa = body.querySelector('#pn').value.trim(); const folder = body.querySelector('#pf').value.trim();
          const err = body.querySelector('#pe');
          if (!nazwa) { err.hidden = false; err.textContent = t('start.nameRequired'); return false; }
          try { await bridge.call('project.new', { nazwa, folder }); zamknij(true); navigate('sources'); }
          catch (e) { err.hidden = false; err.textContent = e.code === 'io' ? t('start.exists') : e.message; return false; }
          return false;
        } },
    ],
  });
}
