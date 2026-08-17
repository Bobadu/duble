// wipe.js — porownywarka tekstur: A pod spodem, B na wierzchu z clip-path, suwak podzialu, tryby A/B, zoom.
// Przy 3+ modelach kazda strona ma wlasna liste wyboru (mozna porownac dowolna pare), przy dwoch — same podpisy.
import { el, esc, dialog } from './ui.js';
import { t } from './i18n.js';
import { icon } from './icons.js';

const URL_TEX = (sha) => `https://duble.data/tex/${encodeURIComponent(sha)}.png`;

/** Podpis strony: „256×256 BC7” (+ „bez mipmap”). */
function meta(s) {
  const cz = [`${s.w}×${s.h}`, s.format || ''].filter(Boolean).join(' ');
  return s.mipy <= 1 ? `${cz} · ${t('wipe.noMips')}` : cz;
}
function etykieta(s) { return s.litera ? `${s.nazwa} · ${t('wipe.variant', { x: s.litera })}` : s.nazwa; }

/**
 * strony: [{ sha, nazwa, zrodlo, litera, plik, w, h, format, mipy }] — jedna = podglad, wiele = porownanie.
 * ai, bi: indeksy stron na start (bi = null -> sam podglad).
 */
export function wipe(strony, ai = 0, bi = null) {
  const S = (strony || []).filter(s => s && s.sha);
  if (!S.length) return Promise.resolve();
  let a = Math.max(0, Math.min(S.length - 1, ai));
  let b = bi == null ? null : Math.max(0, Math.min(S.length - 1, bi));
  if (b === a) b = S.findIndex((_, i) => i !== a);
  if (b < 0) b = null;
  const dwa = b != null;
  const wybor = dwa && S.length > 2;

  return dialog({
    tytul: dwa ? t('wipe.title') : t('wipe.titleOne'), szeroki: true,
    tresc: (body) => {
      body.closest('.dialog')?.classList.add('dialog-wipe');
      const lista = (rola, idx) => wybor
        ? `<select class="input sm select" data-strona="${rola}" aria-label="${esc(t(rola === 'a' ? 'wipe.chooseA' : 'wipe.chooseB'))}">${S.map((s, i) => `<option value="${i}" ${i === idx ? 'selected' : ''}>${esc(etykieta(s))}</option>`).join('')}</select>`
        : `<span class="nm">${esc(etykieta(S[idx]))}</span>`;
      body.innerHTML = `
        <div class="wipe">
          <div class="wipe-bar">
            <div class="wipe-side"><span class="cap a">A</span><div class="who">${lista('a', a)}<span class="meta" data-meta="a"></span></div></div>
            ${dwa ? `<span class="wipe-eq" data-eq hidden></span><div class="wipe-side right"><div class="who">${lista('b', b)}<span class="meta" data-meta="b"></span></div><span class="cap b">B</span></div>` : ''}
          </div>
          <div class="wipe-stage checker" tabindex="0">
            <div class="wipe-imgs">
              <img class="ia" alt="A" draggable="false"><img class="ib" alt="B" draggable="false" ${dwa ? '' : 'hidden'}>
              ${dwa ? '<div class="wipe-line"></div>' : ''}
            </div>
            <div class="wipe-loading">${icon('refresh')} ${t('wipe.loading')}</div>
          </div>
          <div class="wipe-foot">
            ${dwa ? `<input type="range" class="wipe-range" min="0" max="100" value="50" aria-label="${esc(t('wipe.both'))}">` : '<span class="grow"></span>'}
            ${dwa ? `<div class="seg" role="radiogroup"><button data-tryb="both" class="on">${t('wipe.both')}</button><button data-tryb="a">${t('wipe.onlyA')}</button><button data-tryb="b">${t('wipe.onlyB')}</button></div>` : ''}
            <div class="seg" role="radiogroup"><button data-zoom="fit" class="on">${t('wipe.zoomFit')}</button><button data-zoom="1">${t('wipe.zoom1')}</button></div>
          </div>
          ${dwa ? `<p class="help">${t('wipe.hint')}</p>` : ''}
        </div>`;

      const stage = body.querySelector('.wipe-stage'), imgs = body.querySelector('.wipe-imgs');
      const ia = body.querySelector('.ia'), ib = body.querySelector('.ib');
      const line = body.querySelector('.wipe-line'), range = body.querySelector('.wipe-range');
      const eq = body.querySelector('[data-eq]');
      let tryb = 'both', pos = 50, zoom = 'fit', czekam = 0, bladKto = null;

      function loading(pokaz, tekst, blad = false) {
        let l = body.querySelector('.wipe-loading');
        if (!pokaz) { l?.remove(); return; }
        if (!l) { l = el('<div class="wipe-loading"></div>'); stage.append(l); }
        l.classList.toggle('err', blad);
        l.innerHTML = blad ? `${icon('warn')} ${esc(tekst)}` : `${icon('refresh')} ${esc(tekst)}`;
      }
      function zaladowano() { if (--czekam <= 0 && !bladKto) loading(false); uloz(); }

      function ustaw(img, i, kto) {
        czekam++;
        if (bladKto === kto) bladKto = null;
        loading(true, bladKto ? `${t('wipe.noPreview')} (${bladKto})` : t('wipe.loading'), !!bladKto);
        img.onload = zaladowano;
        img.onerror = () => { czekam--; bladKto = kto; loading(true, `${t('wipe.noPreview')} (${kto})`, true); };
        img.src = URL_TEX(S[i].sha);
      }
      function podpisy() {
        body.querySelector('[data-meta="a"]').textContent = meta(S[a]);
        if (dwa) body.querySelector('[data-meta="b"]').textContent = meta(S[b]);
        if (eq) { const same = S[a].sha === S[b].sha; eq.hidden = !same; eq.className = 'wipe-eq badge ok'; eq.textContent = t('wipe.identical'); }
      }

      function uloz() {
        const nat = Math.max(ia.naturalWidth || 0, (dwa && ib.naturalWidth) || 0) || 256;
        const natH = Math.max(ia.naturalHeight || 0, (dwa && ib.naturalHeight) || 0) || 256;
        const W = Math.max(64, stage.clientWidth - 24), H = Math.max(64, stage.clientHeight - 24);
        const sk = zoom === 'fit' ? Math.min(W / nat, H / natH, 8) : 1;
        imgs.style.width = Math.round(nat * sk) + 'px';
        imgs.style.height = Math.round(natH * sk) + 'px';
        imgs.classList.toggle('pixel', sk >= 2);
        if (!dwa) return;
        if (tryb === 'a') { ib.style.clipPath = 'inset(0 0 0 100%)'; line.style.left = '100%'; }
        else if (tryb === 'b') { ib.style.clipPath = 'inset(0 0 0 0)'; line.style.left = '0%'; }
        else { ib.style.clipPath = `inset(0 0 0 ${pos}%)`; line.style.left = pos + '%'; }
        line.hidden = tryb !== 'both';
      }

      function naBoth() {
        if (tryb === 'both') return;
        tryb = 'both';
        body.querySelectorAll('[data-tryb]').forEach(x => x.classList.toggle('on', x.dataset.tryb === 'both'));
      }
      function przesun(nowa) { pos = Math.max(0, Math.min(100, nowa)); if (range) range.value = String(Math.round(pos)); naBoth(); uloz(); }

      body.querySelectorAll('[data-tryb]').forEach(btn => btn.onclick = () => {
        body.querySelectorAll('[data-tryb]').forEach(x => x.classList.remove('on')); btn.classList.add('on'); tryb = btn.dataset.tryb; uloz();
      });
      body.querySelectorAll('[data-zoom]').forEach(btn => btn.onclick = () => {
        body.querySelectorAll('[data-zoom]').forEach(x => x.classList.remove('on')); btn.classList.add('on'); zoom = btn.dataset.zoom; uloz();
      });
      body.querySelectorAll('[data-strona]').forEach(sel => sel.onchange = () => {
        const i = Number(sel.value);
        if (sel.dataset.strona === 'a') { a = i; ustaw(ia, a, 'A'); } else { b = i; ustaw(ib, b, 'B'); }
        podpisy();
      });
      if (range) {
        range.oninput = () => przesun(Number(range.value));
        const zMyszy = (e) => { const r = imgs.getBoundingClientRect(); przesun((e.clientX - r.left) / r.width * 100); };
        let ciagnie = false;
        imgs.addEventListener('mousedown', e => { e.preventDefault(); ciagnie = true; zMyszy(e); stage.focus(); });
        const ruch = (e) => { if (!body.isConnected) { window.removeEventListener('mousemove', ruch); window.removeEventListener('mouseup', koniec); return; } if (ciagnie) zMyszy(e); };
        const koniec = () => { ciagnie = false; };
        window.addEventListener('mousemove', ruch); window.addEventListener('mouseup', koniec);
        stage.addEventListener('keydown', e => {
          const k = e.key === 'ArrowLeft' ? -1 : e.key === 'ArrowRight' ? 1 : 0;
          if (!k) return;
          e.preventDefault(); przesun(pos + k * (e.shiftKey ? 10 : 2));
        });
      }
      const naRozmiar = () => { if (!body.isConnected) { window.removeEventListener('resize', naRozmiar); return; } uloz(); };
      window.addEventListener('resize', naRozmiar);

      podpisy();
      ustaw(ia, a, 'A');
      if (dwa) ustaw(ib, b, 'B');
      uloz();
      setTimeout(() => stage.focus(), 0);
    },
    przyciski: [{ tekst: t('common.close'), rola: 'primary' }],
  });
}
