// views/start.js — ekran startowy: logo, opis, nowy/otworz projekt, ostatnie projekty. (Komendy project.* — Zadanie 4.)
import { el, toast } from '../ui.js';

export async function render(root, ctx) {
  const { t, icon, bridge } = ctx;
  root.append(el(`
    <div class="hero">
      ${icon('logo', 'logo')}
      <div>
        <h1>${t('start.title')}<span class="by">${t('app.by')}</span></h1>
        <p class="sub">${t('start.subtitle')}</p>
      </div>
    </div>`));
  const akcje = el(`<div class="hero-actions"><button class="btn primary lg" id="nowy">${icon('plus')}${t('start.new')}</button><button class="btn lg" id="otworz">${icon('folder')}${t('start.open')}</button></div>`);
  akcje.querySelector('#nowy').onclick = () => toast(t('wip.text'), { typ: 'info' });
  akcje.querySelector('#otworz').onclick = () => toast(t('wip.text'), { typ: 'info' });
  root.append(akcje);
  root.append(el(`<div class="section"><div class="section-head"><h2>${t('start.recent')}</h2></div><div class="empty">${icon('file')}<p>${t('start.empty')}</p></div></div>`));
}
