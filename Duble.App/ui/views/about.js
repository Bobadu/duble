// views/about.js — O programie: logo, wersja, opis, biblioteki i licencje.
import { el } from '../ui.js';

const LICENCJE = [
  ['CodeWalker.Core (dexyfex)', 'MIT'],
  ['BCnEncoder.Net', 'MIT'],
  ['Microsoft Edge WebView2', 'Microsoft'],
  ['.NET / WPF', 'MIT'],
];

export async function render(root, { t, icon, store }) {
  const w = store.info?.wersja || '';
  root.append(el(`
    <div class="about-hero">
      ${icon('logo', 'logo')}
      <div>
        <h1>${t('app.name')} <span class="by" style="color:var(--accent);font-size:16px;font-weight:500">${t('app.by')}</span></h1>
        <div class="ver">${t('app.version', { v: w })}${store.info?.dev ? ' · dev' : ''}</div>
      </div>
    </div>`));
  root.append(el(`<p class="about-text">${t('about.text')}</p>`));
  root.append(el(`<p class="about-text" style="margin-top:12px">${t('about.engine')}</p>`));
  root.append(el(`<p class="about-text" style="margin-top:12px">${t('about.madeBy')}</p>`));
  const sek = el(`<div class="section"><div class="section-head"><h2>${t('about.licenses')}</h2></div><ul class="lic-list"></ul></div>`);
  const ul = sek.querySelector('ul');
  for (const [n, l] of LICENCJE) ul.append(el(`<li><span>${n}</span><span>${l}</span></li>`));
  root.append(sek);
}
