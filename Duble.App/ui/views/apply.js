// views/apply.js — dialog „Zastosuj decyzje": plan przenosin (apply.preview), wybor kosza, ostrzezenie o numeracji + pomoc,
// „otworz folder po zakonczeniu", start zadania (apply.run). Wynik zadania obsluguje powiadomienia.js (toast + Cofnij).
import { el, esc, dialog, toast, fmt } from '../ui.js';
import { pomocPrzenumerowanie } from './help.js';

const KLUCZ_OTWORZ = 'apply.openAfter';

/** Otwiera dialog Zastosuj. Zwraca true, gdy zadanie wystartowalo. */
export async function otworzZastosuj(ctx) {
  const { t, icon, bridge, navigate } = ctx;
  let plan;
  try { plan = await bridge.call('apply.preview'); } catch (e) { toast(e.message, { typ: 'error' }); return false; }
  let wystartowalo = false;
  await dialog({
    tytul: t('apply.title'), szeroki: true,
    tresc: (body, zamknij) => {
      const rysuj = () => {
        body.innerHTML = '';
        body.append(naglowek(plan, ctx));
        body.append(wyborKosza(plan, ctx, async (kosz) => {
          try { plan = await bridge.call('apply.preview', { kosz, ustawKosz: true }); rysuj(); }
          catch (e) { toast(e.message, { typ: 'error' }); }
        }));
        body.append(lista(plan, ctx, () => { zamknij(); navigate('sources'); }));
        const warn = el(`<div class="apply-warn">${icon('warn')}<div><b>${t('apply.warnTitle')}</b><p>${t('apply.warnText')} <a href="#" id="apply-help">${t('apply.warnMore')}</a></p></div></div>`);
        warn.querySelector('#apply-help').onclick = (e) => { e.preventDefault(); pomocPrzenumerowanie(ctx); };
        body.append(warn);
        const otw = el(`<label class="check-row"><input type="checkbox" id="apply-open" ${sessionStorage.getItem(KLUCZ_OTWORZ) !== '0' ? 'checked' : ''}><span>${t('apply.openAfter')}</span></label>`);
        body.append(otw);
        const go = body.closest('.dialog')?.querySelector('footer .btn.primary');
        if (go) { go.textContent = t('apply.go', { n: fmt.liczba(plan.pliki) }); go.disabled = !plan.pliki; }
      };
      rysuj();
    },
    przyciski: [
      { tekst: t('common.cancel') },
      { tekst: t('apply.go', { n: fmt.liczba(plan.pliki) }), rola: 'primary', akcja: async () => {
          const otworz = document.getElementById('apply-open')?.checked !== false;
          sessionStorage.setItem(KLUCZ_OTWORZ, otworz ? '1' : '0');
          try {
            const r = await bridge.call('apply.run', { kosz: plan.kosz || null, ustawKosz: true });
            if (!r?.uruchomiono) { toast(t('apply.nothing'), { typ: 'warn' }); return; }
            wystartowalo = true;
            toast(t('apply.running'), { typ: 'info', czas: 2500 });
          } catch (e) { toast(e.code === 'busy' ? t('sources.busy') : t('apply.failed', { blad: e.message }), { typ: 'error' }); return false; }
        } },
    ],
  });
  return wystartowalo;
}

export function czyOtworzycFolderPoZastosowaniu() { return sessionStorage.getItem(KLUCZ_OTWORZ) !== '0'; }

function naglowek(plan, ctx) {
  const { t, icon } = ctx;
  const w = el(`<div class="apply-head"><p class="lead">${icon('trash')} ${esc(t('apply.summary', { pozycje: fmt.liczba(plan.pozycje), pliki: fmt.liczba(plan.pliki), mb: fmt.rozmiar(plan.bajty) }))}</p><ul class="apply-notes"></ul></div>`);
  const ul = w.querySelector('ul');
  if (plan.wspoldzielone) ul.append(el(`<li>${icon('info')} ${esc(t('apply.shared', { n: plan.wspoldzielone }))}</li>`));
  if (plan.wArchiwum) ul.append(el(`<li>${icon('archive')} ${esc(t('apply.inArchive', { n: plan.wArchiwum }))}</li>`));
  if (plan.brakujace) ul.append(el(`<li>${icon('warn')} ${esc(t('apply.missing', { n: plan.brakujace }))}</li>`));
  if (plan.brakujaceZrodla?.length) ul.append(el(`<li class="warn-txt">${icon('warn')} ${esc(t('apply.missingSources', { lista: plan.brakujaceZrodla.join(', ') }))}</li>`));
  if (!ul.children.length) ul.remove();
  return w;
}

function wyborKosza(plan, ctx, zmien) {
  const { t, icon, bridge } = ctx;
  const wlasny = !!plan.kosz;
  const w = el(`
    <div class="apply-where">
      <div class="apply-label">${t('apply.where')}</div>
      <label class="radio-row"><input type="radio" name="kosz" value="obok" ${wlasny ? '' : 'checked'}><span>${t('apply.besideSource')}</span><span class="faint mono">${esc(!wlasny && plan.kosze?.[0]?.kosz ? fmt.sciezkaKrotka(plan.kosze[0].kosz, 70) : '…\\_rejected\\<' + t('dup.sourcesFilter').toLowerCase() + '>')}</span></label>
      <label class="radio-row"><input type="radio" name="kosz" value="wlasny" ${wlasny ? 'checked' : ''}><span>${t('apply.customFolder')}</span><span class="faint mono" id="kosz-sciezka">${esc(plan.kosz ? fmt.sciezkaKrotka(plan.kosz, 70) : '')}</span><button class="btn sm" id="kosz-pick">${icon('folder')}${t('apply.pick')}</button></label>
    </div>`);
  const wybierz = async () => {
    try {
      const r = await bridge.call('dialogs.pickFolder', { start: plan.kosz || null });
      if (r?.sciezka) zmien(r.sciezka); else if (!plan.kosz) w.querySelector('input[value=obok]').checked = true;
    } catch (e) { toast(e.message, { typ: 'error' }); }
  };
  w.querySelector('#kosz-pick').onclick = (e) => { e.preventDefault(); wybierz(); };
  w.querySelector('input[value=obok]').onchange = () => { if (plan.kosz) zmien(null); };
  w.querySelector('input[value=wlasny]').onchange = () => { if (!plan.kosz) wybierz(); };
  return w;
}

function lista(plan, ctx, doZrodel) {
  const { t, icon } = ctx;
  const w = el(`<div class="apply-listwrap"><div class="apply-label">${t('apply.list')} <span class="faint">(${fmt.liczba(plan.lista?.length || 0)})</span></div><div class="apply-list"></div></div>`);
  const l = w.querySelector('.apply-list');
  for (const p of plan.lista || []) {
    const uwagi = [];
    if (p.wspoldzielone) uwagi.push(`<span class="badge unknown" title="${esc(t('apply.shared', { n: p.wspoldzielone }))}">${p.wspoldzielone} ${icon('info')}</span>`);
    if (p.wArchiwum) uwagi.push(`<span class="badge unknown" title="${esc(t('apply.tooltipArchive'))}">${t('group.inArchive')}</span>`);
    if (p.brakujace) uwagi.push(`<span class="badge err">${esc(t('apply.missing', { n: p.brakujace }))}</span>`);
    const row = el(`
      <div class="apply-row ${p.pliki ? '' : 'skip'}">
        <div class="thumb">${p.thumb ? `<img src="https://duble.data/thumb/${esc(p.thumb)}.png" alt="" loading="lazy">` : icon('cube')}</div>
        <div class="who"><span class="nm">${esc(p.nazwa)}<sub>${esc(p.sufiks || '')}</sub></span><span class="src" title="${esc(p.zrodlo)} · ${esc(p.kontener || '')}">${esc(p.zrodlo)}<span class="faint"> · ${esc(p.kontener || '')}</span></span></div>
        <div class="to" title="${esc(p.kosz || '')}">${p.kosz ? `${icon('chevron', 'rot270')}<span class="mono">${esc(fmt.sciezkaKrotka(p.kosz, 44))}</span>` : ''}</div>
        <div class="cnt">${p.pliki ? `<b>${esc(t('apply.files', { n: p.pliki }))}</b><span class="faint">${fmt.rozmiar(p.bajty)}</span>` : ''}${uwagi.join(' ')}</div>
      </div>`);
    if (p.wArchiwum && !p.pliki) row.querySelector('.badge')?.addEventListener('click', doZrodel);
    l.append(row);
  }
  if (!plan.lista?.length) l.append(el(`<p class="faint">${t('dup.nothingToReject')}</p>`));
  return w;
}
