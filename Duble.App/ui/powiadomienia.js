// powiadomienia.js — globalne toasty po zadaniach w tle (niezaleznie od widoku): zastosuj, cofnij, rozpakuj, raport.
import { toast, fmt } from './ui.js';
import { czyOtworzycFolderPoZastosowaniu } from './views/apply.js';

let zarejestrowane = false;

export function zarejestruj(ctx) {
  if (zarejestrowane) return; zarejestrowane = true;
  const { bridge, t, navigate } = ctx;

  bridge.on('apply.done', d => {
    if (d.przerwano) toast(t('apply.interrupted') + (d.blad ? ' ' + d.blad : ''), { typ: 'warn', czas: 12000, akcja: { tekst: t('nav.history'), fn: () => navigate('history') } });
    else toast(t('apply.done', { n: fmt.liczba(d.przeniesione) }), { typ: 'ok', akcja: { tekst: t('apply.undo'), fn: () => bridge.call('history.undo', { plik: d.plik }).catch(e => toast(e.message, { typ: 'error' })) } });
    if (czyOtworzycFolderPoZastosowaniu() && d.kosze?.length && d.przeniesione > 0) bridge.call('shell.openFolder', { sciezka: d.kosze[0] }).catch(() => {});
  });
  bridge.on('undo.done', d => toast(t('history.undoDone', { n: fmt.liczba(d.wrocilo) }) + (d.pominieto ? ` (${t('history.skipped', { n: d.pominieto })})` : ''), { typ: 'ok' }));
  bridge.on('unpack.done', d => {
    toast(t('unpack.done', { pliki: fmt.liczba(d.pliki), archiwa: fmt.liczba(d.archiwa), folder: fmt.sciezkaKrotka(d.folder, 50) }), { typ: d.bledy?.length ? 'warn' : 'ok', akcja: { tekst: t('history.showFolder'), fn: () => bridge.call('shell.openFolder', { sciezka: d.folder }).catch(() => {}) } });
    if (d.bledy?.length) toast(t('unpack.errors', { n: d.bledy.length }) + ' ' + d.bledy[0], { typ: 'warn', czas: 10000 });
  });
  bridge.on('report.done', d => toast(t('history.exported', { plik: fmt.sciezkaKrotka(d.plik, 50) }), { typ: 'ok', akcja: { tekst: t('history.show'), fn: () => bridge.call('shell.showInExplorer', { sciezka: d.plik }).catch(() => {}) } }));
  bridge.on('job', d => {
    const nazwy = { zastosuj: 'apply.failed', cofnij: 'history.undoFailed', rozpakuj: 'unpack.failed', raport: 'history.exportFailed' };
    if (!nazwy[d.typ]) return;
    if (d.stan === 'blad') toast(t(nazwy[d.typ], { blad: d.blad || '' }), { typ: 'error', czas: 10000 });
    if (d.stan === 'anulowano') toast(t('sources.cancelled'), { typ: 'warn' });
  });
}
