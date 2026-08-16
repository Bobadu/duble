// i18n.js — slownik UI+Core z https://duble.data/i18n/<jezyk>.json; t(klucz, params) i podmiana atrybutow data-i18n*.
let slownik = {};
let jezyk = 'pl';

export const i18n = {
  async load(j) {
    const r = await fetch(`https://duble.data/i18n/${j}.json`, { cache: 'no-store' });
    slownik = await r.json();
    jezyk = j;
    document.documentElement.lang = j;
  },
  get jezyk() { return jezyk; },
  has(k) { return slownik[k] !== undefined; },
  t(k, p) {
    let s = slownik[k];
    if (s === undefined) return `[${k}]`;
    if (p) for (const [a, b] of Object.entries(p)) s = s.replaceAll(`{${a}}`, String(b));
    return s;
  },
  applyDom(root = document) {
    for (const el of root.querySelectorAll('[data-i18n]')) el.textContent = i18n.t(el.dataset.i18n);
    for (const el of root.querySelectorAll('[data-i18n-title]')) el.title = i18n.t(el.dataset.i18nTitle);
    for (const el of root.querySelectorAll('[data-i18n-placeholder]')) el.placeholder = i18n.t(el.dataset.i18nPlaceholder);
    for (const el of root.querySelectorAll('[data-i18n-aria]')) el.setAttribute('aria-label', i18n.t(el.dataset.i18nAria));
  },
};
export const t = (k, p) => i18n.t(k, p);
