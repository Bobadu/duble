// app/useJobNotifications.ts — the toasts that belong to the whole application rather than to one screen.
//
// Applying, undoing, unpacking and exporting all finish in the background, and the user may well be somewhere
// else by then. So the news is announced once, here, wherever they are.
import { bridge, messageOf } from '../bridge/bridge';
import type { JobEvent } from '../bridge/contract';
import { useBridgeEvent } from '../bridge/hooks';
import { useToast } from '../components/Toast';
import { shortenPath, useI18n, useTranslate } from '../i18n';
import { shouldOpenBinAfterApply } from '../views/apply/ApplyDialog';
import { navigate } from './router';

/** The jobs whose failure is worth a message of its own, and what to call it. */
const FAILURE_MESSAGES: Partial<Record<JobEvent['typ'], string>> = {
  zastosuj: 'apply.failed',
  cofnij: 'history.undoFailed',
  rozpakuj: 'unpack.failed',
  raport: 'history.exportFailed',
};

export function useJobNotifications(): void {
  const t = useTranslate();
  const { formatNumber } = useI18n();
  const toast = useToast();

  useBridgeEvent('apply.done', (done) => {
    if (done.przerwano) {
      toast.warn(t('apply.interrupted') + (done.blad ? ` ${done.blad}` : ''), {
        duration: 12000,
        action: { label: t('nav.history'), run: () => navigate('history') },
      });
      return;
    }

    toast.ok(t('apply.done', { n: formatNumber(done.przeniesione) }), {
      action: {
        label: t('apply.undo'),
        run: () => {
          bridge.call('history.undo', { plik: done.plik }).catch((failure: unknown) => toast.error(messageOf(failure)));
        },
      },
    });

    // the bin folder, if that is the habit — the files are there and usually want looking at
    const bin = done.kosze[0];
    if (shouldOpenBinAfterApply() && bin && done.przeniesione > 0)
      void bridge.call('shell.openFolder', { sciezka: bin }).catch(() => undefined);
  });

  useBridgeEvent('undo.done', (done) => {
    const skipped = done.pominieto ? ` (${t('history.skipped', { n: done.pominieto })})` : '';
    toast.ok(t('history.undoDone', { n: formatNumber(done.wrocilo) }) + skipped);
  });

  useBridgeEvent('unpack.done', (done) => {
    toast.show(done.bledy.length ? 'warn' : 'ok', t('unpack.done', { pliki: formatNumber(done.pliki), archiwa: formatNumber(done.archiwa) }), {
      detail: shortenPath(done.folder, 48),
      action: {
        label: t('history.showFolder'),
        run: () => void bridge.call('shell.openFolder', { sciezka: done.folder }).catch(() => undefined),
      },
    });

    const first = done.bledy[0];
    if (first) toast.warn(`${t('unpack.errors', { n: done.bledy.length })} ${first}`, { duration: 10000 });
  });

  useBridgeEvent('report.done', (done) => {
    toast.ok(t('history.exported'), {
      detail: shortenPath(done.plik, 48),
      action: {
        label: t('history.show'),
        run: () => void bridge.call('shell.showInExplorer', { sciezka: done.plik }).catch(() => undefined),
      },
    });
  });

  useBridgeEvent('job', (job) => {
    const failure = FAILURE_MESSAGES[job.typ];
    if (!failure) return;
    if (job.stan === 'blad') toast.error(t(failure, { blad: job.blad ?? '' }), { duration: 10000 });
    if (job.stan === 'anulowano') toast.warn(t('sources.cancelled'));
  });
}
