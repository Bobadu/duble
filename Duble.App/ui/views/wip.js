// views/wip.js — ekran "w budowie" dla widokow z kolejnych etapow.
import { el } from '../ui.js';

export async function render(root, { t, icon, nazwa }) {
  const ik = { duplicates: 'duplicates', catalog: 'catalog', history: 'history' }[nazwa] || 'cube';
  root.append(el(`<div class="wip">${icon(ik)}<h2>${t('nav.' + nazwa)} — ${t('wip.title')}</h2><p>${t('wip.text')}</p></div>`));
}
