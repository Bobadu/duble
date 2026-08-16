// views/parts.js — kawalki wspolne dla karty grupy (group.js) i karty pozycji (item.js): slupek jakosci, kafelek tekstury, skroty nazw.
import { el, esc } from '../ui.js';

/** Slupek skladnika oceny jakosci (0..maks) z opisem. */
export function slupek(etyk, wartosc, maks, opis) {
  const v = Math.max(0, Math.min(maks, Number(wartosc) || 0));
  return `<div class="q-row"><span class="q-lab">${esc(etyk)}</span><div class="q-bar"><i style="width:${(v / maks) * 100}%"></i></div><span class="q-val">${Math.round(v)}/${maks}</span><span class="q-desc faint">${esc(opis)}</span></div>`;
}

/** Litera wariantu z nazwy pliku tekstury (jbib_diff_027_b_uni.ytd -> B), inaczej cala nazwa. */
export function literaZPliku(plik) { const m = /_diff_\d{3}_([a-z])_/i.exec(plik || ''); return m ? m[1].toUpperCase() : (plik || ''); }

/** Skrocona sciezka (archiwum|wewnatrz -> archiwum › wewnatrz), z lewej „…". */
export function sciezkaKrotka(p, maks = 60) { if (!p) return ''; const s = p.replace('|', ' › '); return s.length > maks ? '…' + s.slice(-(maks - 1)) : s; }

/** Blok jakosci: suma + piec slupkow (rozdzielczosc 40, mipy 20, warianty 20, format 10, LOD 10). c = czlonek/pozycja z rozpiska. */
export function blokJakosci(c, t) {
  const q = c.rozpiska || {};
  return `
    <div class="q-total"><b>${Math.round(c.punkty)}</b><span>/100 ${t('quality.total')}</span></div>
    ${slupek(t('quality.resolution'), q.rozdz, 40, `${Math.round(q.rozdzPx || 0)} px`)}
    ${slupek(t('quality.mips'), q.mipy, 20, `${Math.round((q.udzialMipow || 0) * 100)} %`)}
    ${slupek(t('quality.variants'), q.warianty, 20, `${q.liczbaWariantow ?? c.tekstur}`)}
    ${slupek(t('quality.format'), q.format, 10, q.zlyFormat ? `${q.zlyFormat} BC1+α` : 'ok')}
    ${slupek(t('quality.lod'), q.lod, 10, `${q.lody ?? c.lody}`)}`;
}

/** Kafelek tekstury (miniatura + litera + wymiary/format + znaczki „bez mipow"/„BC1 z alfa"). para = ma odpowiednik po drugiej stronie. */
export function kafelekTekstury(tx, { para = false, tytul = '' } = {}) {
  const zn = [];
  if (tx.mipy <= 1) zn.push('!mip'); if (tx.format === 'BC1' && tx.alfa > 0.02) zn.push('!BC1α');
  return el(`
    <button class="tex ${para ? 'has-pair' : ''}" data-sha="${esc(tx.sha || '')}" title="${esc(tx.plik)}&#10;${tx.w}×${tx.h} ${esc(tx.format || '')} · ${tx.mipy} mip${tytul ? ' · ' + esc(tytul) : ''}">
      <div class="tex-img">${tx.zdekodowana && tx.sha ? `<img src="https://duble.data/thumb/${esc(tx.sha)}.png" alt="" loading="lazy">` : `<span class="tex-nopreview">${esc(tx.format || '?')}</span>`}${para ? `<span class="tex-dot" aria-hidden="true"></span>` : ''}</div>
      <div class="tex-cap"><span class="tex-name">${esc(literaZPliku(tx.plik))}</span><span class="tex-meta">${tx.w}×${tx.h} ${esc(tx.format || '')}${zn.length ? ` <span class="warn-txt">${zn.join(' ')}</span>` : ''}</span></div>
    </button>`);
}
