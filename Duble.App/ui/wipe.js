// wipe.js — dialog porownania dwoch tekstur (A pod spodem, B na wierzchu z clip-path) z suwakiem, trybami A/B/oba i zoomem.
import { el, esc, dialog } from './ui.js';
import { t } from './i18n.js';
import { icon } from './icons.js';

/** a, b: { sha, podpis } (b moze byc null = pojedynczy podglad). */
export function wipe(a, b) {
  return dialog({
    tytul: t('wipe.title'), szeroki: true,
    tresc: (body, zamknij) => {
      const dwa = !!b;
      body.innerHTML = `
        <div class="wipe">
          <div class="wipe-tools">
            <div class="wipe-caps"><span class="cap a">A</span><span class="mono" title="${esc(a.podpis || '')}">${esc(a.podpis || '')}</span>${dwa ? `<span class="cap b">B</span><span class="mono" title="${esc(b.podpis || '')}">${esc(b.podpis || '')}</span>` : ''}</div>
            <div class="seg" role="radiogroup">${dwa ? `<button data-tryb="both" class="on">${t('wipe.both')}</button><button data-tryb="a">${t('wipe.onlyA')}</button><button data-tryb="b">${t('wipe.onlyB')}</button>` : ''}</div>
            <div class="seg" role="radiogroup"><button data-zoom="fit" class="on">${t('wipe.zoomFit')}</button><button data-zoom="1">${t('wipe.zoom1')}</button></div>
          </div>
          <div class="wipe-stage checker" tabindex="0">
            <div class="wipe-imgs">
              <img class="ia" alt="A" draggable="false"><img class="ib" alt="B" draggable="false" ${dwa ? '' : 'hidden'}>
              ${dwa ? '<div class="wipe-line"></div>' : ''}
            </div>
            <div class="wipe-loading">${icon('refresh')} ${t('wipe.loading')}</div>
          </div>
          ${dwa ? `<input type="range" class="wipe-range" min="0" max="100" value="50" aria-label="A/B">` : ''}
          <p class="help">${dwa ? t('wipe.hint') : t('group.single')}</p>
        </div>`;
      const stage = body.querySelector('.wipe-stage'), imgs = body.querySelector('.wipe-imgs');
      const ia = body.querySelector('.ia'), ib = body.querySelector('.ib'), line = body.querySelector('.wipe-line'), range = body.querySelector('.wipe-range');
      const loading = body.querySelector('.wipe-loading');
      let tryb = 'both', pos = 50, zoom = 'fit';
      let gotowe = 0; const potrzebne = dwa ? 2 : 1;
      const zaladowano = () => { if (++gotowe >= potrzebne) { loading.remove(); uloz(); } };
      const blad = (img, kto) => { img.hidden = true; loading.innerHTML = `${icon('warn')} ${esc(t('wipe.noPreview'))} (${kto})`; loading.classList.add('err'); };
      ia.onload = zaladowano; ib.onload = zaladowano;
      ia.onerror = () => blad(ia, 'A'); ib.onerror = () => blad(ib, 'B');
      ia.src = `https://duble.data/tex/${encodeURIComponent(a.sha)}.png`;
      if (dwa) ib.src = `https://duble.data/tex/${encodeURIComponent(b.sha)}.png`;

      function uloz() {
        const nat = Math.max(ia.naturalWidth || 0, ib?.naturalWidth || 0) || 512;
        const natH = Math.max(ia.naturalHeight || 0, ib?.naturalHeight || 0) || 512;
        if (zoom === 'fit') {
          // dopasuj do sceny (szerokosc dialogu x ~60 % wysokosci okna), bez powiekszania ponad 1:1 malych tekstur
          const W = stage.clientWidth - 2, H = Math.max(200, Math.floor(window.innerHeight * 0.6));
          const sk = Math.min(W / nat, H / natH, 1);
          imgs.style.width = Math.round(nat * sk) + 'px'; imgs.style.height = Math.round(natH * sk) + 'px';
        } else { imgs.style.width = nat + 'px'; imgs.style.height = natH + 'px'; }
        if (!dwa) return;
        if (tryb === 'a') { ib.style.clipPath = 'inset(0 0 0 100%)'; line.style.left = '100%'; }
        else if (tryb === 'b') { ib.style.clipPath = 'inset(0 0 0 0)'; line.style.left = '0%'; }
        else { ib.style.clipPath = `inset(0 0 0 ${pos}%)`; line.style.left = pos + '%'; }
      }
      body.querySelectorAll('[data-tryb]').forEach(btn => btn.onclick = () => { body.querySelectorAll('[data-tryb]').forEach(x => x.classList.remove('on')); btn.classList.add('on'); tryb = btn.dataset.tryb; uloz(); });
      body.querySelectorAll('[data-zoom]').forEach(btn => btn.onclick = () => { body.querySelectorAll('[data-zoom]').forEach(x => x.classList.remove('on')); btn.classList.add('on'); zoom = btn.dataset.zoom; uloz(); });
      if (range) {
        range.oninput = () => { pos = Number(range.value); if (tryb !== 'both') { tryb = 'both'; body.querySelectorAll('[data-tryb]').forEach(x => x.classList.toggle('on', x.dataset.tryb === 'both')); } uloz(); };
        const zMyszy = (e) => { const r = imgs.getBoundingClientRect(); pos = Math.max(0, Math.min(100, (e.clientX - r.left) / r.width * 100)); range.value = String(Math.round(pos)); if (tryb !== 'both') { tryb = 'both'; body.querySelectorAll('[data-tryb]').forEach(x => x.classList.toggle('on', x.dataset.tryb === 'both')); } uloz(); };
        let ciagnie = false;
        imgs.addEventListener('mousedown', e => { ciagnie = true; zMyszy(e); });
        window.addEventListener('mousemove', e => { if (ciagnie) zMyszy(e); });
        window.addEventListener('mouseup', () => { ciagnie = false; });
      }
      uloz();
    },
    przyciski: [{ tekst: t('common.close'), rola: 'primary' }],
  });
}
