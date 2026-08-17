// views/group3d.js — zakladka „Model (3D)" karty grupy: modele obok siebie (kazdy w swoim Widok3D, wspolna kamera)
// albo „Nałóż A na B" — jeden widok, dwa modele i suwak przenikania (jak porownywarka tekstur; przy 3+ modelach wybor stron).
import { el, esc, fmt } from '../ui.js';
import { nazwaPozycji } from './duplicates.js';

let stan = { tryb: 'obok', sync: true, wire: false, jasne: false, mix: 50 };

function literaZPliku(plik) { const m = /_diff_\d{3}_([a-z])_/i.exec(plik || ''); return m ? m[1].toLowerCase() : null; }
function urlModelu(c, litera) { return `https://duble.data/mesh/${encodeURIComponent(c.id)}.glb${litera ? '?w=' + encodeURIComponent(litera) : ''}`; }

/** Renderuje zakladke do kontenera; zwraca { zniszcz }. */
export async function render(kont, g, ctx) {
  const { t, icon } = ctx;
  let v3d;
  try { v3d = await import('../view3d.js'); }
  catch (e) { kont.append(el(`<div class="empty">${icon('warn')}<h3>${t('view3d.error')}</h3><p class="mono">${esc(e.message)}</p></div>`)); return { zniszcz() {} }; }
  if (!v3d.webglDostepny()) { kont.append(el(`<div class="empty">${icon('warn')}<h3>${t('view3d.webgl')}</h3></div>`)); return { zniszcz() {} }; }

  const czl = g.czlonkowie || [];
  const warianty = new Map();   // id czlonka -> wybrana litera wariantu
  for (const c of czl) warianty.set(c.id, literyCzlonka(c)[0] || null);
  const sync = new v3d.Synchronizator();
  sync.wlacz(stan.sync);
  const widoki = [];            // { widok, czlonekId, el } — tryb „obok siebie"
  let porownanie = null;        // { widok, el } — tryb „nałóż"
  let idA = czl[0]?.id, idB = czl[1]?.id;
  const czlonek = (id) => czl.find(c => c.id === id) || czl[0];

  const root = el('<div class="v3d-root"></div>');
  const tools = el(`
    <div class="filterbar v3d-tools">
      ${czl.length >= 2 ? `<div class="seg" role="radiogroup"><button data-tryb="obok" class="${stan.tryb === 'obok' ? 'on' : ''}">${icon('catalog')}${t('view3d.sideBySide')}</button><button data-tryb="ab" class="${stan.tryb === 'ab' ? 'on' : ''}">${icon('layers')}${t('view3d.overlay')}</button></div>` : ''}
      <span id="v3d-sync-slot"></span>
      <button class="switch ${stan.wire ? 'on' : ''}" id="v3d-wire"><span>${t('view3d.wireframe')}</span>${icon(stan.wire ? 'toggleOn' : 'toggleOff')}</button>
      <button class="switch ${stan.jasne ? 'on' : ''}" id="v3d-bg"><span>${t('view3d.background')}</span>${icon(stan.jasne ? 'toggleOn' : 'toggleOff')}</button>
      <button class="btn" id="v3d-reset">${icon('search')}${t('view3d.reset')}</button>
      <span class="faint v3d-hint">${t('view3d.hint')}</span>
    </div>`);
  root.append(tools);
  const grid = el('<div class="v3d-grid"></div>');
  root.append(grid);
  kont.append(root);

  function literyCzlonka(c) { return [...new Set((c.tekstury || []).map(tx => literaZPliku(tx.plik)).filter(Boolean))]; }
  function przelacznik(btn, wl) { btn.classList.toggle('on', wl); btn.querySelector('.ico')?.replaceWith(el(icon(wl ? 'toggleOn' : 'toggleOff'))); }

  const zastosujStan = () => {
    for (const w of widoki) w.widok.ustawWireframe(stan.wire);
    porownanie?.widok.ustawWireframe(stan.wire);
    root.querySelectorAll('.v3d').forEach(x => x.classList.toggle('light', stan.jasne));
    przelacznik(tools.querySelector('#v3d-wire'), stan.wire);
    przelacznik(tools.querySelector('#v3d-bg'), stan.jasne);
    const s = tools.querySelector('#v3d-sync'); if (s) przelacznik(s, stan.sync);
  };
  tools.querySelector('#v3d-wire').onclick = () => { stan.wire = !stan.wire; zastosujStan(); };
  tools.querySelector('#v3d-bg').onclick = () => { stan.jasne = !stan.jasne; zastosujStan(); };
  tools.querySelector('#v3d-reset').onclick = () => { const w = widoki[0]?.widok || porownanie?.widok; if (w) { w.dopasujKamere(); sync.rozglos(w); } };
  tools.querySelectorAll('[data-tryb]').forEach(b => b.onclick = () => { stan.tryb = b.dataset.tryb; tools.querySelectorAll('[data-tryb]').forEach(x => x.classList.toggle('on', x === b)); zbuduj(); });

  /** „Obrót razem" ma sens tylko przy kilku widokach obok siebie. */
  function odswiezSync() {
    const slot = tools.querySelector('#v3d-sync-slot');
    slot.innerHTML = '';
    if (stan.tryb === 'ab' || czl.length < 2) return;
    const b = el(`<button class="switch ${stan.sync ? 'on' : ''}" id="v3d-sync"><span>${t('view3d.sync')}</span>${icon(stan.sync ? 'toggleOn' : 'toggleOff')}</button>`);
    b.onclick = () => { stan.sync = !stan.sync; sync.wlacz(stan.sync); zastosujStan(); };
    slot.append(b);
  }

  function pokazStaty(gdzie, klucz, s) {
    const e = gdzie.querySelector(`[data-stats="${CSS.escape(klucz)}"]`);
    if (e) e.textContent = s ? `${fmt.liczba(s.wierzcholki)} ${t('view3d.verts')} · ${fmt.liczba(s.trojkaty)} ${t('view3d.tris')}` : '';
  }

  async function zaladujDo(widok, c, slot, kartaEl, { dopasuj = false, pokaz = true } = {}) {
    const scena = kartaEl.querySelector('.v3d');
    let overlay = scena.querySelector('.v3d-overlay');
    if (!overlay) { overlay = el('<div class="v3d-overlay"></div>'); scena.append(overlay); }
    overlay.classList.remove('err');
    overlay.innerHTML = `${icon('refresh')} ${t('view3d.loading')}`;
    try {
      await widok.zaladuj(urlModelu(c, warianty.get(c.id)), { slot, dopasuj, pokaz });
      overlay.remove();
      return widok.modele[slot]?.statystyki || null;
    } catch (e) {
      overlay.innerHTML = `${icon('warn')} ${esc(t('view3d.error'))}`;
      overlay.classList.add('err');
      console.error(e);
      return null;
    }
  }

  function wyczysc() {
    for (const w of widoki) w.widok.zniszcz();
    widoki.length = 0;
    porownanie?.widok.zniszcz(); porownanie = null;
    sync.widoki = [];
    grid.innerHTML = '';
    document.removeEventListener('keydown', naSpacje);
  }

  function naSpacje(e) {
    if (e.code !== 'Space' || stan.tryb !== 'ab' || !porownanie) return;
    if (['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].includes(document.activeElement?.tagName)) return;
    e.preventDefault();
    ustawMix(stan.mix < 50 ? 100 : 0);
  }

  function ustawMix(v) {
    stan.mix = Math.max(0, Math.min(100, Math.round(v)));
    porownanie?.widok.mieszaj(stan.mix / 100);
    const k = porownanie?.el; if (!k) return;
    const r = k.querySelector('.wipe-range'); if (r && Number(r.value) !== stan.mix) r.value = String(stan.mix);
    k.querySelectorAll('[data-mix]').forEach(b => b.classList.toggle('on', Number(b.dataset.mix) === stan.mix));
  }

  /** Jedna strona porownania: znacznik A/B, wybor modelu (przy 3+), wybor wariantu, statystyki. */
  function stronaHtml(rola, id) {
    const c = czlonek(id);
    const litery = literyCzlonka(c);
    const model = czl.length > 2
      ? `<select class="input sm select" data-strona="${rola}" aria-label="${esc(t(rola === 'A' ? 'view3d.chooseA' : 'view3d.chooseB'))}">${czl.map(x => `<option value="${esc(x.id)}" ${x.id === id ? 'selected' : ''}>${esc(nazwaPozycji(x))}</option>`).join('')}</select>`
      : `<span class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></span>`;
    const wariant = litery.length > 1
      ? `<select class="input sm select" data-wariant="${rola}" aria-label="${esc(t('view3d.variant'))}">${litery.map(l => `<option value="${l}" ${warianty.get(c.id) === l ? 'selected' : ''}>${t('wipe.variant', { x: l.toUpperCase() })}</option>`).join('')}</select>`
      : '';
    const cap = `<span class="cap ${rola === 'A' ? 'a' : 'b'}">${rola}</span>`;
    const who = `<div class="who"><div class="row">${model}${wariant}</div><span class="meta" data-stats="${rola}"></span></div>`;
    return rola === 'A' ? `<div class="wipe-side">${cap}${who}</div>` : `<div class="wipe-side right">${who}${cap}</div>`;
  }

  async function zbudujPorownanie() {
    const k = el(`
      <div class="v3d-card v3d-cmp">
        <div class="wipe-bar">
          ${stronaHtml('A', idA)}
          <span class="v3d-eq badge ok" hidden>${t('view3d.sameMesh')}</span>
          ${stronaHtml('B', idB)}
        </div>
        <div class="v3d ${stan.jasne ? 'light' : ''}"><div class="v3d-overlay">${icon('refresh')} ${t('view3d.loading')}</div></div>
        <div class="wipe-foot">
          <input type="range" class="wipe-range" min="0" max="100" value="${stan.mix}" aria-label="${esc(t('view3d.blend'))}">
          <div class="seg" role="radiogroup"><button data-mix="0">${t('view3d.showA')}</button><button data-mix="50">${t('view3d.overlayBoth')}</button><button data-mix="100">${t('view3d.showB')}</button></div>
        </div>
        <p class="help">${t('view3d.overlayHint')}</p>
      </div>`);
    grid.append(k);
    const widok = new v3d.Widok3D(k.querySelector('.v3d'));
    widok.ustawWireframe(stan.wire);
    porownanie = { widok, el: k };

    const staty = { A: null, B: null };
    const rownosc = () => {
      const e = k.querySelector('.v3d-eq');
      e.hidden = !(staty.A && staty.B && staty.A.wierzcholki === staty.B.wierzcholki && staty.A.trojkaty === staty.B.trojkaty);
    };
    const wczytaj = async (rola, dopasuj) => {
      staty[rola] = await zaladujDo(widok, czlonek(rola === 'A' ? idA : idB), rola, k, { dopasuj, pokaz: false });
      pokazStaty(k, rola, staty[rola]);
      rownosc();
      ustawMix(stan.mix);
    };

    k.querySelector('.wipe-range').oninput = (e) => ustawMix(Number(e.target.value));
    k.querySelectorAll('[data-mix]').forEach(b => b.onclick = () => ustawMix(Number(b.dataset.mix)));
    podepnijStrone(k, 'A', wczytaj);
    podepnijStrone(k, 'B', wczytaj);

    document.addEventListener('keydown', naSpacje);
    await wczytaj('A', true);
    await wczytaj('B', false);
  }

  const stronaEl = (k, rola) => k.querySelector(rola === 'B' ? '.wipe-side.right' : '.wipe-side:not(.right)');

  /** Podpina listy strony (model, wariant); zmiana modelu przerysowuje strone, bo inny model = inne warianty. */
  function podepnijStrone(k, rola, wczytaj) {
    const side = stronaEl(k, rola);
    side.querySelector('[data-strona]')?.addEventListener('change', async (e) => {
      if (rola === 'A') idA = e.target.value; else idB = e.target.value;
      stronaEl(k, rola).replaceWith(el(stronaHtml(rola, rola === 'A' ? idA : idB)));
      podepnijStrone(k, rola, wczytaj);
      await wczytaj(rola, false);
    });
    side.querySelector('[data-wariant]')?.addEventListener('change', async (e) => {
      warianty.set(rola === 'A' ? idA : idB, e.target.value);
      await wczytaj(rola, false);
    });
  }

  function kartaWidoku(c) {
    const litery = literyCzlonka(c);
    return el(`
      <div class="v3d-card">
        <div class="v3d-head">
          <div class="v3d-title"><span class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></span><span class="faint">${esc(c.zrodlo)}</span></div>
          <div class="v3d-ctl">
            ${litery.length > 1 ? `<select class="input sm select" data-czlonek="${esc(c.id)}" aria-label="${esc(t('view3d.variant'))}">${litery.map(l => `<option value="${l}" ${warianty.get(c.id) === l ? 'selected' : ''}>${t('wipe.variant', { x: l.toUpperCase() })}</option>`).join('')}</select>` : ''}
            <span class="v3d-stats faint" data-stats="${esc(c.id)}"></span>
          </div>
        </div>
        <div class="v3d ${stan.jasne ? 'light' : ''}"><div class="v3d-overlay">${icon('refresh')} ${t('view3d.loading')}</div></div>
      </div>`);
  }

  async function zbuduj() {
    wyczysc();
    odswiezSync();
    grid.classList.toggle('single', stan.tryb === 'ab' && czl.length >= 2);
    if (stan.tryb === 'ab' && czl.length >= 2) { await zbudujPorownanie(); zastosujStan(); return; }
    grid.style.setProperty('--n', String(czl.length));
    let pierwszy = true;
    for (const c of czl) {
      const k = kartaWidoku(c);
      grid.append(k);
      const widok = new v3d.Widok3D(k.querySelector('.v3d'));
      widok.ustawWireframe(stan.wire);
      widoki.push({ widok, czlonekId: c.id, el: k });
      sync.dodaj(widok);
      k.querySelector('select[data-czlonek]')?.addEventListener('change', async (e) => {
        warianty.set(c.id, e.target.value);
        pokazStaty(k, c.id, await zaladujDo(widok, c, 'glowny', k));
      });
      const dopasuj = pierwszy; pierwszy = false;
      zaladujDo(widok, c, 'glowny', k, { dopasuj }).then((s) => {
        pokazStaty(k, c.id, s);
        if (dopasuj) sync.rozglos(widok); else if (stan.sync && widoki[0]) widok.przyjmijKamere(widoki[0].widok);
      });
    }
  }

  await zbuduj();
  zastosujStan();
  return { zniszcz() { wyczysc(); } };
}
