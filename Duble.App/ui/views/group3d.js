// views/group3d.js — zakladka "Model (3D)" karty grupy: modele czlonkow obok siebie (kazdy w swoim Widok3D, wspolna kamera)
// albo tryb "naloz A na B" (jeden widok, dwa modele przelaczane). Wariant tekstury per czlonek, wireframe, jasne tlo, wysrodkuj.
import { el, esc, toast, fmt } from '../ui.js';
import { nazwaPozycji } from './duplicates.js';

let stan = { tryb: 'obok', sync: true, wire: false, jasne: false };

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
  const warianty = new Map();   // id czlonka -> wybrana litera
  for (const c of czl) { const litery = literyCzlonka(c); warianty.set(c.id, litery[0] || null); }
  const sync = new v3d.Synchronizator();
  sync.wlacz(stan.sync);
  const widoki = [];   // { widok, czlonekId, el }
  let overlayWidok = null; let overlayPokazany = 'A';

  const root = el('<div class="v3d-root"></div>');
  const tools = el(`
    <div class="v3d-tools">
      ${czl.length >= 2 ? `<div class="seg"><button data-tryb="obok" class="${stan.tryb === 'obok' ? 'on' : ''}">${icon('catalog')}${t('view3d.sideBySide')}</button><button data-tryb="ab" class="${stan.tryb === 'ab' ? 'on' : ''}">${icon('duplicates')}${t('view3d.overlay')}</button></div>` : ''}
      <button class="chip" id="v3d-sync" aria-pressed="${stan.sync}">${icon('refresh')}${t('view3d.sync')}</button>
      <button class="chip" id="v3d-wire" aria-pressed="${stan.wire}">${icon('catalog')}${t('view3d.wireframe')}</button>
      <button class="chip" id="v3d-bg" aria-pressed="${stan.jasne}">${icon('square')}${t('view3d.background')}</button>
      <button class="btn sm" id="v3d-reset">${icon('search')}${t('view3d.reset')}</button>
      <span class="faint v3d-hint">${t('view3d.hint')}</span>
    </div>`);
  root.append(tools);
  const grid = el('<div class="v3d-grid"></div>');
  root.append(grid);
  kont.append(root);

  const zastosujStan = () => {
    for (const w of widoki) w.widok.ustawWireframe(stan.wire);
    overlayWidok?.ustawWireframe(stan.wire);
    root.querySelectorAll('.v3d').forEach(x => x.classList.toggle('light', stan.jasne));
    tools.querySelector('#v3d-wire').setAttribute('aria-pressed', stan.wire);
    tools.querySelector('#v3d-bg').setAttribute('aria-pressed', stan.jasne);
    tools.querySelector('#v3d-sync').setAttribute('aria-pressed', stan.sync);
  };
  tools.querySelector('#v3d-sync').onclick = () => { stan.sync = !stan.sync; sync.wlacz(stan.sync); zastosujStan(); };
  tools.querySelector('#v3d-wire').onclick = () => { stan.wire = !stan.wire; zastosujStan(); };
  tools.querySelector('#v3d-bg').onclick = () => { stan.jasne = !stan.jasne; zastosujStan(); };
  tools.querySelector('#v3d-reset').onclick = () => { const w = widoki[0]?.widok || overlayWidok; if (w) { w.dopasujKamere(); sync.rozglos(w); } };
  tools.querySelectorAll('[data-tryb]').forEach(b => b.onclick = () => { stan.tryb = b.dataset.tryb; tools.querySelectorAll('[data-tryb]').forEach(x => x.classList.toggle('on', x === b)); zbuduj(); });

  function literyCzlonka(c) { return [...new Set((c.tekstury || []).map(tx => literaZPliku(tx.plik)).filter(Boolean))]; }

  function kartaWidoku(c, { etykieta = null } = {}) {
    const litery = literyCzlonka(c);
    const k = el(`
      <div class="v3d-card">
        <div class="v3d-head">
          <div class="v3d-title">${etykieta ? `<span class="cap ${etykieta === 'A' ? 'a' : 'b'}">${etykieta}</span>` : ''}<span class="nm">${esc(nazwaPozycji(c))}<sub>${esc(c.sufiks || '')}</sub></span><span class="faint">${esc(c.zrodlo)}</span></div>
          <div class="v3d-ctl">
            ${litery.length > 1 ? `<label class="faint">${t('view3d.variant')} <select class="input sm" data-czlonek="${esc(c.id)}">${litery.map(l => `<option value="${l}" ${warianty.get(c.id) === l ? 'selected' : ''}>${l.toUpperCase()}</option>`).join('')}</select></label>` : ''}
            <span class="v3d-stats faint" data-stats="${esc(c.id)}"></span>
          </div>
        </div>
        <div class="v3d ${stan.jasne ? 'light' : ''}"><div class="v3d-overlay">${icon('refresh')} ${t('view3d.loading')}</div></div>
      </div>`);
    return k;
  }

  function pokazStaty(root2, id, s) { const e = root2.querySelector(`[data-stats="${CSS.escape(id)}"]`); if (e) e.textContent = `${fmt.liczba(s.wierzcholki)} ${t('view3d.verts')} · ${fmt.liczba(s.trojkaty)} ${t('view3d.tris')}`; }

  async function zaladujDo(widok, c, slot, kartaEl, dopasuj) {
    const overlay = kartaEl.querySelector('.v3d-overlay');
    if (overlay) { overlay.hidden = false; overlay.innerHTML = `${icon('refresh')} ${t('view3d.loading')}`; }
    try {
      await widok.zaladuj(urlModelu(c, warianty.get(c.id)), { slot, dopasuj, pokaz: slot === 'glowny' || (stan.tryb === 'ab' && slot === overlayPokazany) });
      overlay?.remove();
      pokazStaty(kartaEl, c.id, widok.modele[slot]?.statystyki || { wierzcholki: 0, trojkaty: 0 });
    } catch (e) {
      if (overlay) { overlay.hidden = false; overlay.innerHTML = `${icon('warn')} ${esc(t('view3d.error'))}`; overlay.classList.add('err'); }
      console.error(e);
    }
  }

  function wyczysc() {
    for (const w of widoki) w.widok.zniszcz();
    widoki.length = 0;
    overlayWidok?.zniszcz(); overlayWidok = null;
    sync.widoki = [];
    grid.innerHTML = '';
    document.removeEventListener('keydown', naSpacje);
  }

  function naSpacje(e) {
    if (e.code !== 'Space' || stan.tryb !== 'ab' || !overlayWidok) return;
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) return;
    e.preventDefault(); przelaczAB();
  }
  function przelaczAB(kto) {
    overlayPokazany = kto || (overlayPokazany === 'A' ? 'B' : 'A');
    overlayWidok?.pokaz(overlayPokazany);
    root.querySelectorAll('[data-ab]').forEach(b => b.classList.toggle('on', b.dataset.ab === overlayPokazany));
    const c = overlayPokazany === 'A' ? czl[0] : czl[1];
    const st = overlayWidok?.modele[overlayPokazany]?.statystyki; if (st && c) pokazStaty(root, 'ab', st);
    root.querySelector('.v3d-ab-nazwa').textContent = c ? `${nazwaPozycji(c)} · ${c.zrodlo}` : '';
  }

  async function zbuduj() {
    wyczysc();
    grid.classList.toggle('single', stan.tryb === 'ab');
    if (stan.tryb === 'ab' && czl.length >= 2) {
      const a = czl[0], b = czl[1];
      const k = el(`
        <div class="v3d-card">
          <div class="v3d-head">
            <div class="v3d-title"><div class="seg"><button data-ab="A" class="on"><span class="cap a">A</span>${esc(nazwaPozycji(a))}</button><button data-ab="B"><span class="cap b">B</span>${esc(nazwaPozycji(b))}</button></div><span class="v3d-ab-nazwa faint"></span></div>
            <div class="v3d-ctl">
              ${literyCzlonka(a).length > 1 ? `<label class="faint">A ${t('view3d.variant')} <select class="input sm" data-czlonek="${esc(a.id)}">${literyCzlonka(a).map(l => `<option value="${l}" ${warianty.get(a.id) === l ? 'selected' : ''}>${l.toUpperCase()}</option>`).join('')}</select></label>` : ''}
              ${literyCzlonka(b).length > 1 ? `<label class="faint">B ${t('view3d.variant')} <select class="input sm" data-czlonek="${esc(b.id)}">${literyCzlonka(b).map(l => `<option value="${l}" ${warianty.get(b.id) === l ? 'selected' : ''}>${l.toUpperCase()}</option>`).join('')}</select></label>` : ''}
              <span class="v3d-stats faint" data-stats="ab"></span>
            </div>
          </div>
          <div class="v3d ${stan.jasne ? 'light' : ''}"><div class="v3d-overlay">${icon('refresh')} ${t('view3d.loading')}</div></div>
          <p class="help">${t('view3d.overlayHint')}</p>
        </div>`);
      grid.append(k);
      overlayWidok = new v3d.Widok3D(k.querySelector('.v3d'));
      overlayWidok.ustawWireframe(stan.wire);
      overlayPokazany = 'A';
      k.querySelectorAll('[data-ab]').forEach(btn => btn.onclick = () => przelaczAB(btn.dataset.ab));
      k.querySelectorAll('select[data-czlonek]').forEach(sel => sel.onchange = async () => { warianty.set(sel.dataset.czlonek, sel.value); const c = czl.find(x => x.id === sel.dataset.czlonek); await zaladujDo(overlayWidok, c, c === a ? 'A' : 'B', k, false); przelaczAB(overlayPokazany); });
      await zaladujDo(overlayWidok, a, 'A', k, true);
      await zaladujDo(overlayWidok, b, 'B', k, false);
      przelaczAB('A');
      document.addEventListener('keydown', naSpacje);
      return;
    }
    grid.style.setProperty('--n', String(czl.length));
    let pierwszy = true;
    for (const c of czl) {
      const k = kartaWidoku(c);
      grid.append(k);
      const widok = new v3d.Widok3D(k.querySelector('.v3d'));
      widok.ustawWireframe(stan.wire);
      widoki.push({ widok, czlonekId: c.id, el: k });
      sync.dodaj(widok);
      k.querySelector('select[data-czlonek]')?.addEventListener('change', async (e) => { warianty.set(c.id, e.target.value); await zaladujDo(widok, c, 'glowny', k, false); });
      const dopasuj = pierwszy; pierwszy = false;
      zaladujDo(widok, c, 'glowny', k, dopasuj).then(() => { if (dopasuj) sync.rozglos(widok); else if (stan.sync && widoki[0]) widok.przyjmijKamere(widoki[0].widok); });
    }
  }

  await zbuduj();
  zastosujStan();
  return { zniszcz() { wyczysc(); } };
}
