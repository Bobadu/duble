// views/sources.js — Zrodla: lista, dodawanie, indeksowanie. (Komendy sources.* — Zadanie 5; tu szkielet.)
import { el, toast } from '../ui.js';

export async function render(root, ctx) {
  const { t, icon } = ctx;
  root.append(el(`
    <div class="view-head">
      <div class="titles"><h1>${t('sources.title')}</h1><p class="sub">${t('sources.subtitle')}</p></div>
      <div class="actions">
        <button class="btn">${icon('folder')}${t('sources.addFolder')}</button>
        <button class="btn">${icon('archive')}${t('sources.addRpf')}</button>
        <button class="btn">${icon('gamepad')}${t('sources.detect')}</button>
        <button class="btn primary">${icon('play')}${t('sources.indexAll')}</button>
      </div>
    </div>`));
  root.append(el(`<div class="empty dropzone">${icon('drop')}<h3>${t('sources.dropHint')}</h3><p>${t('sources.empty')}</p></div>`));
}

export function dodajSciezki(sciezki, ctx) { toast(String(sciezki.length)); }
