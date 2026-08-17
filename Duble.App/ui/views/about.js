// views/about.js — O programie: karta programu (logo, wersja, linki), trzy zasady dzialania, pliki uzytkownika,
// szczegoly techniczne w rozwijce, podziekowania dla bibliotek i stopka z licencja.
import { el, esc, toast } from '../ui.js';

// [nazwa, licencja]
const BIBLIOTEKI = [
  ['CodeWalker.Core', 'MIT'],
  ['BCnEncoder.Net', 'MIT'],
  ['three.js', 'MIT'],
  ['WebView2', 'Microsoft'],
];

export async function render(root, { t, icon, store, bridge }) {
  const info = store.info || {};
  const otworzUrl = (url) => bridge.call('shell.openUrl', { url }).catch(e => toast(e.message, { typ: 'warn' }));
  const pokazPlik = (sciezka) => bridge.call('shell.showInExplorer', { sciezka }).catch(e => toast(e.message, { typ: 'warn' }));
  const widok = el('<div class="about"></div>');

  // ---- karta programu ----
  const hero = el(`
    <div class="card about-card">
      <div class="card-body">
        <div class="about-hero">
          ${icon('logo', 'logo')}
          <div class="about-id">
            <h1>${t('app.name')} <span class="by">${t('app.by')}</span></h1>
            <div class="about-chips"><span class="pill">${t('app.version', { v: info.wersja || '' })}</span>${info.dev ? '<span class="pill">dev</span>' : ''}${info.licencja ? `<span class="pill">${esc(info.licencja)}</span>` : ''}</div>
            <p class="about-tag">${t('about.tagline')}</p>
            <p class="about-compat">${t('about.compat')}</p>
            <div class="btn-row about-actions"></div>
          </div>
        </div>
      </div>
    </div>`);
  const akcje = hero.querySelector('.about-actions');
  if (info.strona) {
    const b = el(`<button class="btn primary" title="${esc(info.strona)}">${icon('external')}${t('about.website')}</button>`);
    b.onclick = () => otworzUrl(info.strona);
    akcje.append(b);
  }
  if (info.repo) {
    const b = el(`<button class="btn" title="${esc(info.repo)}">${icon('external')}${t('about.repo')}</button>`);
    b.onclick = () => otworzUrl(info.repo);
    const z = el(`<button class="btn" title="${esc(info.repo + '/issues')}">${icon('warn')}${t('about.issues')}</button>`);
    z.onclick = () => otworzUrl(info.repo + '/issues');
    akcje.append(b, z);
  }
  widok.append(hero);

  // ---- trzy zasady ----
  const jak = el(`<div class="section about-sec"><div class="section-head"><h2>${t('about.how')}</h2></div><div class="about-how"></div></div>`);
  const siatka = jak.querySelector('.about-how');
  for (const [ik, tyt, txt] of [['duplicates', 'about.how1t', 'about.how1'], ['palette', 'about.how2t', 'about.how2'], ['restore', 'about.how3t', 'about.how3']])
    siatka.append(el(`<div class="card"><div class="card-body">${icon(ik)}<div><h3>${t(tyt)}</h3><p>${t(txt)}</p></div></div></div>`));
  widok.append(jak);

  // ---- pliki uzytkownika ----
  const sc = info.sciezki || {};
  const wiersz = (etykieta, sciezka) => {
    const li = el(`<li><span class="lab">${esc(etykieta)}</span><span class="mono select-text" title="${esc(sciezka)}">${esc(sciezka)}</span><button class="btn ghost icon" title="${esc(t('about.open'))}" aria-label="${esc(t('about.open'))}">${icon('external')}</button></li>`);
    li.querySelector('button').onclick = () => pokazPlik(sciezka);
    return li;
  };
  if (sc.projekty || sc.ustawienia) {
    const pliki = el(`<div class="section about-sec"><div class="section-head"><h2>${t('about.files')}</h2></div><ul class="about-list"></ul></div>`);
    const ul = pliki.querySelector('ul');
    if (sc.projekty) ul.append(wiersz(t('about.pathProjects'), sc.projekty));
    if (sc.ustawienia) ul.append(wiersz(t('about.pathSettings'), sc.ustawienia));
    widok.append(pliki);
  }

  // ---- szczegoly techniczne (rozwijka) ----
  if (sc.exe || sc.webview2) {
    let otwarte = sessionStorage.getItem('about.tech') === '1';
    const sek = el(`
      <div class="section about-sec">
        <button class="adv-toggle" aria-expanded="${otwarte}">${icon('chevron', otwarte ? 'rot180' : '')}<span class="label">${t('about.tech')}</span></button>
        <div class="adv-body" ${otwarte ? '' : 'hidden'}><ul class="about-list"></ul></div>
      </div>`);
    const ul = sek.querySelector('ul');
    if (sc.exe) ul.append(wiersz(t('about.pathExe'), sc.exe));
    if (sc.webview2) ul.append(wiersz(t('about.pathWebView'), sc.webview2));
    const tog = sek.querySelector('.adv-toggle'), body = sek.querySelector('.adv-body');
    tog.onclick = () => {
      otwarte = !otwarte;
      sessionStorage.setItem('about.tech', otwarte ? '1' : '0');
      body.hidden = !otwarte;
      tog.setAttribute('aria-expanded', otwarte);
      tog.querySelector('.ico').classList.toggle('rot180', otwarte);
    };
    widok.append(sek);
  }

  // ---- podziekowania + stopka ----
  widok.append(el(`
    <div class="section about-sec">
      <div class="section-head"><h2>${t('about.credits')}</h2></div>
      <p class="about-note">${t('about.licenseNote')}</p>
      <div class="about-libs">${BIBLIOTEKI.map(([n, l]) => `<span class="pill">${esc(n)}<span class="faint">${esc(l)}</span></span>`).join('')}</div>
    </div>`));
  widok.append(el(`<div class="about-foot"><span>${t('app.name')} ${esc(info.wersja || '')}</span><span>${t('about.copyright')}</span>${info.licencja ? `<span>${t('about.appLicense', { lic: info.licencja })}</span>` : ''}</div>`));
  root.append(widok);
}
