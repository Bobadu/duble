// views/about.js — O programie: logo, wersja, opis, strona projektu (gdy ustalona), pliki programu, biblioteki i licencje.
import { el, esc, toast, fmt } from '../ui.js';

// [nazwa, licencja, klucz opisu about.lib.*]
const LICENCJE = [
  ['CodeWalker.Core (dexyfex)', 'MIT', 'rpf'],
  ['BCnEncoder.Net', 'MIT', 'bc7'],
  ['three.js (r170)', 'MIT', '3d'],
  ['Microsoft Edge WebView2', 'Microsoft', 'webview'],
  ['.NET / WPF', 'MIT', 'platform'],
];

export async function render(root, { t, icon, store, bridge }) {
  const info = store.info || {};
  const w = info.wersja || '';
  root.append(el(`
    <div class="about-hero">
      ${icon('logo', 'logo')}
      <div>
        <h1>${t('app.name')} <span class="by" style="color:var(--accent);font-size:16px;font-weight:500">${t('app.by')}</span></h1>
        <div class="ver">${t('app.version', { v: w })}${info.dev ? ' · dev' : ''}</div>
      </div>
    </div>`));
  root.append(el(`<p class="about-text">${t('about.text')}</p>`));
  root.append(el(`<p class="about-text" style="margin-top:12px">${t('about.engine')}</p>`));
  root.append(el(`<p class="about-text" style="margin-top:12px">${t('about.madeBy')}</p>`));
  if (info.strona || info.repo) {
    const b = el(`<div class="btn-row about-links" style="margin-top:14px"></div>`);
    if (info.strona) { const x = el(`<button class="btn primary" title="${esc(info.strona)}">${icon('external')}${t('about.website')}</button>`); x.onclick = () => bridge.call('shell.openUrl', { url: info.strona }).catch(e => toast(e.message, { typ: 'warn' })); b.append(x); }
    if (info.repo) { const x = el(`<button class="btn" title="${esc(info.repo)}">${icon('external')}${t('about.repo')}</button>`); x.onclick = () => bridge.call('shell.openUrl', { url: info.repo }).catch(e => toast(e.message, { typ: 'warn' })); b.append(x); }
    b.append(el(`<span class="faint mono">${esc(info.strona || info.repo)}</span>`));
    root.append(b);
  }
  if (info.licencja) root.append(el(`<p class="about-text faint" style="margin-top:10px">${esc(t('about.appLicense', { lic: info.licencja }))}</p>`));

  const sc = info.sciezki || {};
  const sek = el(`<div class="section"><div class="section-head"><h2>${t('about.paths')}</h2></div><ul class="lic-list paths"></ul></div>`);
  const ul = sek.querySelector('ul');
  for (const [k, p] of [['about.pathSettings', sc.ustawienia], ['about.pathWebView', sc.webview2], ['about.pathProjects', sc.projekty], ['about.pathExe', sc.exe]]) {
    if (!p) continue;
    const li = el(`<li><span>${t(k)}</span><span class="mono select-text" title="${esc(p)}">${esc(fmt.sciezkaKrotka(p, 70))}</span><button class="btn ghost sm" title="${esc(t('group.showInExplorer'))}">${icon('external')}</button></li>`);
    li.querySelector('button').onclick = () => bridge.call('shell.showInExplorer', { sciezka: p }).catch(e => toast(e.message, { typ: 'warn' }));
    ul.append(li);
  }
  root.append(sek);

  const lic = el(`<div class="section"><div class="section-head"><h2>${t('about.licenses')}</h2></div><p class="help">${t('about.licenseNote')}</p><ul class="lic-list"></ul></div>`);
  const ul2 = lic.querySelector('ul');
  for (const [n, l, opis] of LICENCJE) ul2.append(el(`<li><span>${esc(n)} <span class="faint">· ${esc(t('about.lib.' + opis))}</span></span><span>${esc(l)}</span></li>`));
  root.append(lic);
}
