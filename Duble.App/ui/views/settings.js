// views/settings.js — jezyk i motyw (etap 2); kosz, progi, kalibracja — kolejne etapy.
import { el, toast } from '../ui.js';

export async function render(root, ctx) {
  const { t, bridge, store, ustawMotyw, zmienJezyk } = ctx;
  const u = store.ustawienia || {};
  const jezykUst = u.jezykUstawiony || 'system';
  const motyw = u.motyw || 'system';

  root.append(el(`<div class="view-head"><div class="titles"><h1>${t('settings.title')}</h1></div></div>`));
  const grid = el('<div class="settings-grid"></div>');

  const seg = (opcje, wartosc, naZmiane) => {
    const s = el('<div class="seg" role="radiogroup"></div>');
    for (const o of opcje) {
      const b = el(`<button role="radio" aria-checked="${o.v === wartosc}" class="${o.v === wartosc ? 'on' : ''}">${o.tekst}</button>`);
      b.onclick = async () => { s.querySelectorAll('button').forEach(x => { x.classList.remove('on'); x.setAttribute('aria-checked', 'false'); }); b.classList.add('on'); b.setAttribute('aria-checked', 'true'); await naZmiane(o.v); };
      s.append(b);
    }
    return s;
  };

  const kJezyk = el(`<div class="card setting"><div class="card-body"><div class="label">${t('settings.language')}</div></div></div>`);
  kJezyk.querySelector('.card-body').append(seg([
    { v: 'system', tekst: t('settings.languageSystem') }, { v: 'pl', tekst: 'Polski' }, { v: 'en', tekst: 'English' },
  ], jezykUst, async v => {
    const r = await bridge.call('settings.set', { jezyk: v });
    store.ustawienia = { ...store.ustawienia, jezyk: r.jezyk, jezykUstawiony: r.jezykUstawiony };
    await zmienJezyk(r.jezyk);
    toast(t('settings.saved'), { typ: 'ok', czas: 1800 });
  }));

  const kMotyw = el(`<div class="card setting"><div class="card-body"><div class="label">${t('settings.theme')}</div></div></div>`);
  kMotyw.querySelector('.card-body').append(seg([
    { v: 'system', tekst: t('settings.themeSystem') }, { v: 'dark', tekst: t('settings.themeDark') }, { v: 'light', tekst: t('settings.themeLight') },
  ], motyw, async v => {
    ustawMotyw(v);
    const r = await bridge.call('settings.set', { motyw: v });
    store.ustawienia = { ...store.ustawienia, motyw: r.motyw };
    toast(t('settings.saved'), { typ: 'ok', czas: 1800 });
  }));

  grid.append(kJezyk, kMotyw);
  root.append(grid);
  root.append(el(`<p class="muted section" style="max-width:70ch">${t('settings.more')}</p>`));
}
