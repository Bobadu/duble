// wykres.js — prosty wykres slupkowy z rozkladu (Rozklad z Duble.Core: kubelki w zakresie od..do) + pionowe kreski progow.
// Czysty CSS/DOM, bez bibliotek: slupek = div o wysokosci proporcjonalnej do licznosci (skala liniowa), tooltip z przedzialem i n.
import { el, esc } from './ui.js';

/**
 * @param {{n:number, od:number, do:number, kubelki:number[], min:number, p05:number, p50:number, p95:number, max:number}} r rozklad
 * @param {{progi?: {wartosc:number, etykieta:string, klasa?:string}[], format?: (v:number)=>string, wysokosc?: number, kolor?: string, pusty?: string}} o
 */
export function slupki(r, { progi = [], format = v => String(v), wysokosc = 90, kolor = '', pusty = '' } = {}) {
  const w = el(`<div class="chart ${kolor}"></div>`);
  if (!r || !r.n || !r.kubelki?.length) { w.append(el(`<div class="chart-empty">${esc(pusty)}</div>`)); return w; }
  const maks = Math.max(1, ...r.kubelki);
  const szer = (r.do - r.od) / r.kubelki.length;
  const bars = el(`<div class="chart-bars" style="height:${wysokosc}px"></div>`);
  r.kubelki.forEach((n, i) => {
    const od = r.od + i * szer, doo = od + szer;
    const h = n ? Math.max(2, Math.round((n / maks) * (wysokosc - 4))) : 0;
    const b = el(`<div class="chart-bar" title="${esc(format(od))} – ${esc(format(doo))}${i === r.kubelki.length - 1 ? '+' : ''}: ${n}"><i style="height:${h}px"></i></div>`);
    bars.append(b);
  });
  w.append(bars);
  // kreski progow (pozycja w % zakresu)
  for (const p of progi) {
    if (typeof p.wartosc !== 'number') continue;
    const x = Math.max(0, Math.min(100, ((p.wartosc - r.od) / (r.do - r.od)) * 100));
    const m = el(`<div class="chart-mark ${p.klasa || ''}" style="left:${x}%" title="${esc(p.etykieta)}: ${esc(format(p.wartosc))}"><span>${esc(p.etykieta)}</span></div>`);
    w.append(m);
  }
  w.append(el(`<div class="chart-axis"><span>${esc(format(r.od))}</span><span>${esc(format((r.od + r.do) / 2))}</span><span>${esc(format(r.do))}+</span></div>`));
  return w;
}
