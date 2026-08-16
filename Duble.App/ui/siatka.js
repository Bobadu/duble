// siatka.js — SiatkaWirtualna: siatka kafelkow o stalej wysokosci, rysowane tylko widoczne wiersze (+bufor).
//
// Katalog ma tysiace pozycji; DOM z tysiacami <img> to zacinanie sie przy przewijaniu. Tu w DOM sa tylko wiersze w oknie
// (i po 2 zapasowe z kazdej strony); przy przewinieciu/zmianie rozmiaru rysujemy od nowa (bez recyklingu — proste i wystarczajace).
export class SiatkaWirtualna {
  /**
   * @param {HTMLElement} kontener element przewijany (overflow:auto), dostaje wewnetrzne cialo o pelnej wysokosci
   * @param {{wysokosc:number, minSzerokosc:number, odstep:number, renderuj:(item:any, i:number)=>HTMLElement, pusty?:()=>HTMLElement}} o
   */
  constructor(kontener, o) {
    this.kont = kontener; this.o = o; this.items = [];
    this.body = document.createElement('div'); this.body.className = 'vgrid-body';
    this.kont.append(this.body);
    this.kont.classList.add('vgrid');
    this._od = -1; this._do = -1; this._kol = 0; this._raf = 0;
    this._naScroll = () => this._zaplanuj();
    this.kont.addEventListener('scroll', this._naScroll, { passive: true });
    this._ro = new ResizeObserver(() => { this._kol = 0; this._zaplanuj(); });
    this._ro.observe(this.kont);
  }

  ustaw(items) { this.items = items || []; this._od = this._do = -1; this._kol = 0; this.odswiez(); }

  get kolumny() {
    if (this._kol) return this._kol;
    const szer = this.kont.clientWidth - 2;   // -2: zapas na obramowanie
    const { minSzerokosc, odstep } = this.o;
    this._kol = Math.max(1, Math.floor((szer + odstep) / (minSzerokosc + odstep)));
    return this._kol;
  }

  _zaplanuj() { if (this._raf) return; this._raf = requestAnimationFrame(() => { this._raf = 0; this.odswiez(); }); }

  odswiez() {
    if (!this.kont.isConnected) return;
    const n = this.items.length;
    const kol = this.kolumny; const h = this.o.wysokosc + this.o.odstep;
    const wierszy = Math.ceil(n / kol);
    this.body.style.height = Math.max(0, wierszy * h - this.o.odstep) + 'px';
    if (!n) { this.body.innerHTML = ''; if (this.o.pusty) this.body.append(this.o.pusty()); this._od = this._do = -1; return; }
    const top = this.kont.scrollTop, wys = this.kont.clientHeight;
    const od = Math.max(0, Math.floor(top / h) - 2), doW = Math.min(wierszy - 1, Math.ceil((top + wys) / h) + 2);
    if (od === this._od && doW === this._do && this.body.dataset.kol == kol) return;
    this._od = od; this._do = doW; this.body.dataset.kol = kol;
    this.body.innerHTML = '';
    const frag = document.createDocumentFragment();
    for (let r = od; r <= doW; r++) {
      const row = document.createElement('div');
      row.className = 'vgrid-row'; row.style.top = (r * h) + 'px'; row.style.height = this.o.wysokosc + 'px';
      row.style.gridTemplateColumns = `repeat(${kol}, minmax(0, 1fr))`; row.style.gap = this.o.odstep + 'px';
      for (let c = 0; c < kol; c++) { const i = r * kol + c; if (i >= n) break; row.append(this.o.renderuj(this.items[i], i)); }
      frag.append(row);
    }
    this.body.append(frag);
  }

  /** Przewin do pozycji o indeksie i (na gore okna). */
  przewinDo(i) { const kol = this.kolumny; const h = this.o.wysokosc + this.o.odstep; this.kont.scrollTop = Math.floor(i / kol) * h; }

  zniszcz() { this.kont.removeEventListener('scroll', this._naScroll); this._ro.disconnect(); if (this._raf) cancelAnimationFrame(this._raf); this.body.remove(); this.kont.classList.remove('vgrid'); }
}
