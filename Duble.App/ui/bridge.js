// bridge.js — kanal do C# (Mostek). call() zwraca Promise z result; on() subskrybuje zdarzenia {event,data}.
const oczekujace = new Map();
const nasluch = new Map();
let licznik = 0;
const wv = window.chrome?.webview;

function odbierz(e) {
  const m = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
  if (m && m.id !== undefined && oczekujace.has(m.id)) {
    const { resolve, reject } = oczekujace.get(m.id); oczekujace.delete(m.id);
    if (m.ok) resolve(m.result);
    else { const err = new Error(m.error?.message || 'blad'); err.code = m.error?.code || 'internal'; reject(err); }
    return;
  }
  if (m && m.event) for (const fn of nasluch.get(m.event) || []) { try { fn(m.data); } catch (x) { console.error(x); } }
}
wv?.addEventListener('message', odbierz);

export const bridge = {
  call(cmd, args = null) {
    if (!wv) return Promise.reject(Object.assign(new Error('brak WebView2'), { code: 'no_host' }));
    const id = String(++licznik);
    return new Promise((resolve, reject) => { oczekujace.set(id, { resolve, reject }); wv.postMessage({ id, cmd, args }); });
  },
  emit(cmd, args = null) { wv?.postMessage({ id: String(++licznik), cmd, args }); },
  on(event, fn) { if (!nasluch.has(event)) nasluch.set(event, new Set()); nasluch.get(event).add(fn); return () => nasluch.get(event)?.delete(fn); },
  get dostepny() { return !!wv; },
};
