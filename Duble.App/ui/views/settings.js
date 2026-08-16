// views/settings.js — Ustawienia: Program (jezyk, motyw) / Projekt (kosz) / Zaawansowane (progi porownania + kalibracja) / Cache projektu.
import { el, esc, toast, fmt, confirm } from '../ui.js';
import { slupki } from '../wykres.js';

// progi: klucz JSON (jak project.settings.get), krok, min, max, liczba miejsc; etykiety/opisy w i18n settings.th.* / settings.thd.*
const PROGI = [
  { grupa: 'geo', k: 'geoIdentyczna', krok: 0.001, min: 0, max: 1, m: 3 },
  { grupa: 'geo', k: 'geoPodobna', krok: 0.01, min: 0, max: 1, m: 3 },
  { grupa: 'geo', k: 'geoPodobnaTri', krok: 0.01, min: 0, max: 1, m: 2 },
  { grupa: 'geo', k: 'geoPodobnaBbox', krok: 0.01, min: 0, max: 1, m: 2 },
  { grupa: 'tex', k: 'texPHash', krok: 1, min: 0, max: 256, m: 0 },
  { grupa: 'tex', k: 'texKolor', krok: 0.1, min: 0, max: 100, m: 1 },
  { grupa: 'tex', k: 'texWariancjaMin', krok: 0.5, min: 0, max: 255, m: 1 },
  { grupa: 'tex', k: 'texKolorPlaska', krok: 0.1, min: 0, max: 100, m: 1 },
  { grupa: 'cover', k: 'pelnePokrycie', krok: 0.01, min: 0, max: 1, m: 2 },
  { grupa: 'cover', k: 'czesciowePokrycie', krok: 0.01, min: 0, max: 1, m: 2 },
];

let ctxRef = null, rootEl = null, odpisz = [], stanProjektu = null, ostatniaKalibracja = null, debounceProgi = null;
let zaawansowane = sessionStorage.getItem('settings.adv') === '1';

export async function render(root, ctx) {
  ctxRef = ctx; rootEl = root;
  const { t, bridge, store } = ctx;
  zaawansowane = sessionStorage.getItem('settings.adv') === '1';
  root.append(el(`<div class="view-head"><div class="titles"><h1>${t('settings.title')}</h1></div></div>`));
  root.append(sekcjaProgram(ctx));
  const proj = el('<div id="settings-project"></div>');
  root.append(proj);
  await rysujProjekt(proj);
  odpisz.push(store.on(() => { if (store.zadanie?.stan !== 'postep') odswiezStan(); }));
  odpisz.push(bridge.on('settings.changed', () => odswiezStan()));
  odpisz.push(bridge.on('calibrate.done', d => { ostatniaKalibracja = d.wynik; rysujKalibracje(); }));
  odpisz.push(bridge.on('project.opened', () => { ostatniaKalibracja = null; }));
}

export function unmount() { for (const o of odpisz) o?.(); odpisz = []; rootEl = null; clearTimeout(debounceProgi); }

// ---------- Program ----------
function sekcjaProgram(ctx) {
  const { t, bridge, store, ustawMotyw, zmienJezyk } = ctx;
  const u = store.ustawienia || {};
  const jezykUst = u.jezykUstawiony || 'system';
  const motyw = u.motyw || 'system';
  const seg = (opcje, wartosc, naZmiane) => {
    const s = el('<div class="seg" role="radiogroup"></div>');
    for (const o of opcje) {
      const b = el(`<button role="radio" aria-checked="${o.v === wartosc}" class="${o.v === wartosc ? 'on' : ''}">${o.tekst}</button>`);
      b.onclick = async () => { s.querySelectorAll('button').forEach(x => { x.classList.remove('on'); x.setAttribute('aria-checked', 'false'); }); b.classList.add('on'); b.setAttribute('aria-checked', 'true'); await naZmiane(o.v); };
      s.append(b);
    }
    return s;
  };
  const sek = el(`<div class="settings-section"><h2>${t('settings.program')}</h2><div class="settings-grid"></div></div>`);
  const grid = sek.querySelector('.settings-grid');
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
  return sek;
}

// ---------- Projekt ----------
async function odswiezStan() {
  const ctx = ctxRef; if (!ctx || !rootEl) return;
  const proj = rootEl.querySelector('#settings-project'); if (!proj) return;
  // nie przerysowuj, gdy uzytkownik wpisuje prog
  if (document.activeElement?.closest?.('.th-grid')) return;
  await rysujProjekt(proj);
}

async function rysujProjekt(proj) {
  const ctx = ctxRef; const { t, icon, bridge, store, navigate } = ctx;
  proj.innerHTML = '';
  if (!store.projekt) {
    proj.append(el(`<div class="settings-section"><h2>${t('settings.project')}</h2><div class="empty" style="padding:28px">${icon('file')}<h3>${t('status.noProject')}</h3><p>${t('settings.noProject')}</p><div class="btn-row" style="justify-content:center"><button class="btn primary" id="do-startu">${icon('home')}${t('nav.start')}</button></div></div></div>`));
    proj.querySelector('#do-startu').onclick = () => navigate('start');
    return;
  }
  try { stanProjektu = await bridge.call('project.settings.get'); }
  catch (e) { if (e.code !== 'no_project') toast(e.message, { typ: 'error' }); return; }
  const st = stanProjektu;
  const sek = el(`<div class="settings-section"><h2>${t('settings.project')} <span class="faint">· ${esc(store.projekt.nazwa)}</span></h2><div class="settings-stack"></div></div>`);
  const stack = sek.querySelector('.settings-stack');
  stack.append(kartaKosz(st, ctx));
  stack.append(kartaZaawansowane(st, ctx));
  stack.append(kartaCache(st, ctx));
  proj.append(sek);
}

function kartaKosz(st, ctx) {
  const { t, icon, bridge } = ctx;
  const wlasny = !!st.kosz;
  const k = el(`
    <div class="card setting"><div class="card-body">
      <div class="label">${icon('trash')} ${t('settings.bin')}</div>
      <p class="help">${t('settings.binHelp')}</p>
      <label class="radio-row"><input type="radio" name="s-kosz" value="obok" ${wlasny ? '' : 'checked'}><span>${t('settings.binBeside')}</span><span class="faint mono">…\\_odrzucone\\&lt;${esc(t('dup.sourcesFilter').toLowerCase())}&gt;\\</span></label>
      <label class="radio-row"><input type="radio" name="s-kosz" value="wlasny" ${wlasny ? 'checked' : ''}><span>${t('settings.binCustom')}</span><span class="faint mono" id="s-kosz-sciezka">${esc(st.kosz ? fmt.sciezkaKrotka(st.kosz, 70) : '')}</span><button class="btn sm" id="s-kosz-pick">${icon('folder')}${t('settings.binPick')}</button></label>
    </div></div>`);
  const ustaw = async (kosz) => {
    try { await bridge.call('project.settings.set', { kosz }); toast(t('settings.saved'), { typ: 'ok', czas: 1500 }); } catch (e) { toast(e.message, { typ: 'error' }); }
  };
  const wybierz = async () => {
    try { const r = await bridge.call('dialogs.pickFolder', { start: st.kosz || null }); if (r?.sciezka) await ustaw(r.sciezka); else if (!st.kosz) k.querySelector('input[value=obok]').checked = true; }
    catch (e) { toast(e.message, { typ: 'error' }); }
  };
  k.querySelector('#s-kosz-pick').onclick = (e) => { e.preventDefault(); wybierz(); };
  k.querySelector('input[value=obok]').onchange = () => { if (st.kosz) ustaw(null); };
  k.querySelector('input[value=wlasny]').onchange = () => { if (!st.kosz) wybierz(); };
  return k;
}

function kartaZaawansowane(st, ctx) {
  const { t, icon } = ctx;
  const k = el(`
    <div class="card setting adv"><div class="card-body">
      <button class="adv-toggle" aria-expanded="${zaawansowane}">${icon('chevron', zaawansowane ? 'rot180' : '')}<span class="label">${t('settings.advanced')}</span>${st.progiZmienione ? `<span class="badge ok">${t('settings.thresholdsChanged')}</span>` : ''}</button>
      <div class="adv-body" ${zaawansowane ? '' : 'hidden'}></div>
    </div></div>`);
  const body = k.querySelector('.adv-body');
  const tog = k.querySelector('.adv-toggle');
  tog.onclick = () => { zaawansowane = !zaawansowane; sessionStorage.setItem('settings.adv', zaawansowane ? '1' : '0'); body.hidden = !zaawansowane; tog.setAttribute('aria-expanded', zaawansowane); tog.querySelector('.ico').classList.toggle('rot180', zaawansowane); if (zaawansowane && !body.children.length) wypelnij(); };
  const wypelnij = () => { body.innerHTML = ''; body.append(blokProgow(st, ctx)); body.append(blokKalibracji(ctx)); };
  if (zaawansowane) wypelnij();
  return k;
}

function blokProgow(st, ctx) {
  const { t, icon, bridge } = ctx;
  const p = st.progi || {}; const d = st.progiDomyslne || {};
  const w = el(`
    <div class="th-block">
      <div class="th-head"><h3>${t('settings.thresholds')}</h3>${st.progiZmienione ? `<button class="btn sm" id="th-reset">${icon('refresh')}${t('settings.restoreDefaults')}</button>` : `<span class="faint">${t('settings.thresholdsDefault')}</span>`}</div>
      <p class="help">${t('settings.thresholdsHelp')}</p>
      <div class="th-grid"></div>
    </div>`);
  const grid = w.querySelector('.th-grid');
  for (const grupa of ['geo', 'tex', 'cover']) {
    grid.append(el(`<div class="th-group">${t('settings.' + grupa)}</div>`));
    for (const f of PROGI.filter(x => x.grupa === grupa)) {
      const zm = p[f.k] !== d[f.k];
      const row = el(`
        <label class="th-field ${zm ? 'changed' : ''}">
          <span class="th-name">${t('settings.th.' + f.k)}</span>
          <input class="input sm" type="number" step="${f.krok}" min="${f.min}" max="${f.max}" value="${Number(p[f.k]).toFixed(f.m)}" data-k="${f.k}">
          <span class="th-desc">${t('settings.thd.' + f.k)}${zm ? ` <span class="faint">(${t('settings.default')}: ${Number(d[f.k]).toFixed(f.m)})</span>` : ''}</span>
        </label>`);
      const inp = row.querySelector('input');
      inp.addEventListener('change', () => zapiszProg(ctx, f.k, inp.value, inp));
      grid.append(row);
    }
  }
  w.querySelector('#th-reset')?.addEventListener('click', async () => {
    if (!await confirm(t('settings.restoreConfirm'), { ok: t('settings.restoreDefaults'), tytul: t('settings.thresholds') })) return;
    try { const r = await bridge.call('project.settings.resetProgi'); toast(r.porownanie ? t('settings.thresholdSavedCompare') : t('settings.saved'), { typ: 'ok' }); } catch (e) { toast(e.message, { typ: 'error' }); }
  });
  return w;
}

async function zapiszProg(ctx, klucz, wartosc, inp) {
  const { t, bridge } = ctx;
  const v = Number(String(wartosc).replace(',', '.'));
  if (!Number.isFinite(v)) { toast(t('settings.thresholdInvalid', { pole: t('settings.th.' + klucz) }), { typ: 'warn' }); return; }
  try {
    const r = await bridge.call('project.settings.set', { progi: { [klucz]: v } });
    inp.classList.remove('bad');
    toast(r.porownanie ? t('settings.thresholdSavedCompare') : r.porownanie === false ? t('sources.busy') : t('settings.saved'), { typ: 'ok', czas: 2200 });
    stanProjektu = r;
    // przerysuj po utracie fokusu (zeby nie zabierac kursora); wymuszamy teraz, jesli fokus juz poza polami
    if (!document.activeElement?.closest?.('.th-grid')) odswiezStan();
  } catch (e) {
    inp.classList.add('bad');
    toast(e.code === 'bad_args' ? t('settings.thresholdInvalid', { pole: (e.message || '').split(',').map(k => t('settings.th.' + k.charAt(0).toLowerCase() + k.slice(1))).join(', ') }) : e.message, { typ: 'warn' });
  }
}

function blokKalibracji(ctx) {
  const { t, icon, store } = ctx;
  const w = el(`
    <div class="th-block" id="calib-block">
      <div class="th-head"><h3>${t('settings.calib')}</h3><button class="btn sm primary" id="calib-run">${icon('play')}${t('settings.calibRun')}</button></div>
      <p class="help">${t('settings.calibHelp')}</p>
      <div id="calib-out"></div>
    </div>`);
  const btn = w.querySelector('#calib-run');
  const z = store.zadanie; const wToku = z && z.typ === 'kalibracja' && (z.stan === 'start' || z.stan === 'postep');
  if (wToku) { btn.disabled = true; btn.textContent = t('settings.calibRunning'); }
  btn.onclick = () => uruchomKalibracje(ctx, btn);
  setTimeout(rysujKalibracje, 0);
  return w;
}

async function uruchomKalibracje(ctx, btn) {
  const { t, bridge, icon } = ctx;
  try {
    btn.disabled = true; btn.textContent = t('settings.calibRunning');
    await bridge.call('calibrate.run');
  } catch (e) {
    btn.disabled = false; btn.innerHTML = `${icon('play')}${t('settings.calibRun')}`;
    toast(e.code === 'busy' ? t('sources.busy') : e.code === 'not_found' ? t('settings.calibNoData') : e.message, { typ: 'warn' });
  }
}

function rysujKalibracje() {
  const ctx = ctxRef; if (!ctx || !rootEl) return;
  const { t, icon, bridge } = ctx;
  const out = rootEl.querySelector('#calib-out'); if (!out) return;
  const btn = rootEl.querySelector('#calib-run'); if (btn) { btn.disabled = false; btn.innerHTML = `${icon('play')}${t('settings.calibRun')}`; }
  out.innerHTML = '';
  const w = ostatniaKalibracja; if (!w) return;
  const pr = w.progi || {}; const prop = w.propozycja || {};
  const f2 = v => Number(v).toFixed(2), f0 = v => String(Math.round(v)), f1 = v => Number(v).toFixed(1);
  const stat = (r, f) => r && r.n ? `${t('calib.n', { n: fmt.liczba(r.n) })} · ${t('calib.pct', { p05: f(r.p05), p50: f(r.p50), p95: f(r.p95) })}` : t('settings.calibNoData');
  const karta = (tytul, r, opcje, f) => {
    const c = el(`<div class="calib-card"><div class="calib-title"><b>${esc(tytul)}</b><span class="faint">${esc(stat(r, f))}</span></div></div>`);
    c.append(slupki(r, { ...opcje, format: f, pusty: t('settings.calibNoData') }));
    return c;
  };
  out.append(el(`<p class="muted">${esc(t('settings.calibSummary', { poz: fmt.liczba(w.pozycjeZGeometria), tex: fmt.liczba(w.teksturyZdekodowane), kiedy: fmt.data(w.kiedy) }))}</p>`));
  const g1 = el('<div class="calib-grid"></div>');
  const markiGeo = [{ wartosc: pr.geoIdentyczna, etykieta: t('calib.thIdentical'), klasa: 'm-a' }, { wartosc: pr.geoPodobna, etykieta: t('calib.thSimilar'), klasa: 'm-b' }];
  g1.append(karta(t('calib.geoNearest'), w.geoNajblizszyObcy, { progi: markiGeo, kolor: 'neg' }, f2));
  g1.append(karta(t('calib.geoSha'), w.geoIdentyczneSha, { progi: markiGeo, kolor: 'pos' }, f2));
  g1.append(karta(t('calib.geoSame'), w.geoTenSamHash, { progi: markiGeo, kolor: 'pos' }, f2));
  const markPh = [{ wartosc: pr.texPHash, etykieta: t('calib.threshold'), klasa: 'm-a' }];
  g1.append(karta(t('calib.phVariants'), w.pHashWarianty, { progi: markPh, kolor: 'neg' }, f0));
  g1.append(karta(t('calib.phSha'), w.pHashIdentyczne, { progi: markPh, kolor: 'pos' }, f0));
  g1.append(karta(t('calib.phRandom'), w.pHashLosowe, { progi: markPh, kolor: 'neg' }, f0));
  const markKol = [{ wartosc: pr.texKolor, etykieta: t('calib.threshold'), klasa: 'm-a' }];
  g1.append(karta(t('calib.colVariants'), w.kolorWarianty, { progi: markKol, kolor: 'neg' }, f1));
  g1.append(karta(t('calib.colRandom'), w.kolorLosowe, { progi: markKol, kolor: 'neg' }, f1));
  out.append(g1);
  const propTekst = t('settings.calibProposal', { geo: f2(prop.geoIdentyczna), geo4: f2(prop.geoPodobna), ph: f0(prop.texPHash), kol: f2(prop.texKolor) });
  const rozne = ['geoIdentyczna', 'geoPodobna', 'texPHash', 'texKolor'].some(k => Number(prop[k]) !== Number(pr[k]));
  const pp = el(`<div class="calib-prop"><span>${icon('info')} ${esc(propTekst)}</span>${rozne ? `<button class="btn sm" id="calib-use">${icon('check')}${t('settings.calibUse')}</button>` : `<span class="badge ok">${t('settings.calibSame')}</span>`}</div>`);
  pp.querySelector('#calib-use')?.addEventListener('click', async () => {
    try {
      const r = await bridge.call('project.settings.set', { progi: { geoIdentyczna: prop.geoIdentyczna, geoPodobna: prop.geoPodobna, texPHash: prop.texPHash, texKolor: prop.texKolor } });
      toast(r.porownanie ? t('settings.thresholdSavedCompare') : t('settings.saved'), { typ: 'ok' });
      ostatniaKalibracja.progi = { ...pr, geoIdentyczna: prop.geoIdentyczna, geoPodobna: prop.geoPodobna, texPHash: prop.texPHash, texKolor: prop.texKolor };
      odswiezStan();
    } catch (e) { toast(e.message, { typ: 'error' }); }
  });
  out.append(pp);
  if (w.geoPodejrzane) out.append(el(`<p class="help">${esc(t('settings.calibSuspicious', { n: w.geoPodejrzane }))}</p>`));
}

function kartaCache(st, ctx) {
  const { t, icon, bridge } = ctx;
  const c = st.cache || {};
  const w = (k) => c[k] || { pliki: 0, bajty: 0 };
  const k = el(`
    <div class="card setting"><div class="card-body">
      <div class="label">${icon('server')} ${t('settings.cache')}</div>
      <p class="help">${t('settings.cacheHelp')}</p>
      <div class="kv cache-kv">
        <span>${t('settings.cacheThumbs')} <b>${fmt.rozmiar(w('thumbs').bajty)}</b> <span class="faint">(${fmt.liczba(w('thumbs').pliki)})</span></span>
        <span>${t('settings.cacheTex')} <b>${fmt.rozmiar(w('tex').bajty)}</b> <span class="faint">(${fmt.liczba(w('tex').pliki)})</span></span>
        <span>${t('settings.cacheMesh')} <b>${fmt.rozmiar(w('mesh').bajty)}</b> <span class="faint">(${fmt.liczba(w('mesh').pliki)})</span></span>
        <span>${t('settings.cacheHistory')} <b>${fmt.rozmiar(w('historia').bajty)}</b> <span class="faint">(${fmt.liczba(w('historia').pliki)})</span></span>
        <span>${t('settings.cacheTotal')} <b>${fmt.rozmiar(w('razem').bajty)}</b></span>
      </div>
      <p class="help">${t('settings.cacheThumbsNote')}</p>
      <div class="btn-row"><button class="btn sm" id="cache-clear" ${w('tex').pliki + w('mesh').pliki ? '' : 'disabled'}>${icon('trash')}${t('settings.cacheClear')}</button><button class="btn ghost sm" id="cache-open">${icon('external')}${t('settings.openFolder')}</button><span class="faint mono">${esc(fmt.sciezkaKrotka(st.folderCache || '', 60))}</span></div>
    </div></div>`);
  k.querySelector('#cache-clear').onclick = async () => {
    try { const r = await bridge.call('cache.clear', { tex: true, mesh: true }); toast(t('settings.cacheCleared', { mb: fmt.rozmiar(r.bajty) }), { typ: 'ok' }); } catch (e) { toast(e.message, { typ: 'error' }); }
  };
  k.querySelector('#cache-open').onclick = () => bridge.call('shell.openFolder', { sciezka: st.folderCache }).catch(e => toast(e.message, { typ: 'warn' }));
  return k;
}
