// views/help.js — krotka pomoc w aplikacji: co sie dzieje z numeracja po usunieciu ubran (YMT/.meta, nasz lancuch).
import { el, dialog } from '../ui.js';

/** Dialog „Co po usunięciu ubrań?" — cztery akapity z i18n help.renumber1..4. */
export function pomocPrzenumerowanie(ctx) {
  const { t, icon } = ctx;
  return dialog({
    tytul: t('help.renumberTitle'), szeroki: true,
    tresc: (body) => {
      const w = el('<div class="help-text"></div>');
      for (const k of ['help.renumber1', 'help.renumber2', 'help.renumber3', 'help.renumber4']) w.append(el(`<p>${t(k)}</p>`));
      body.append(w);
    },
    przyciski: [{ tekst: t('common.ok'), rola: 'primary' }],
  });
}
