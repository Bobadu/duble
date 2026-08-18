// app/useJobNotifications.ts — the toasts that belong to the whole application rather than to one screen.
//
// Applying, undoing, unpacking and exporting all finish in the background, and the user may well be somewhere
// else by then. So the news is announced once, here, wherever they are.
import { bridge, messageOf } from '../bridge/bridge';
import type { JobKind } from '../bridge/contract';
import { useBridgeEvent } from '../bridge/hooks';
import { useToast } from '../components/Toast';
import { shortenPath, useI18n, useTranslate } from '../i18n';
import { shouldOpenBinAfterApply } from '../views/apply/ApplyDialog';
import { navigate } from './router';

/** The jobs whose failure is worth a message of its own, and what to call it. */
const FAILURE_MESSAGES: Partial<Record<JobKind, string>> = {
  apply: 'apply.failed',
  undo: 'history.undoFailed',
  unpack: 'unpack.failed',
  report: 'history.exportFailed',
};

export function useJobNotifications(): void {
  const t = useTranslate();
  const { formatNumber } = useI18n();
  const toast = useToast();

  useBridgeEvent('apply.done', (done) => {
    if (done.aborted) {
      toast.warn(t('apply.interrupted') + (done.error ? ` ${done.error}` : ''), {
        duration: 12000,
        action: { label: t('nav.history'), run: () => navigate('history') },
      });
      return;
    }

    toast.ok(t('apply.done', { n: formatNumber(done.moved) }), {
      action: {
        label: t('apply.undo'),
        run: () => {
          bridge.call('history.undo', { file: done.file }).catch((failure: unknown) => toast.error(messageOf(failure)));
        },
      },
    });

    // the bin folder, if that is the habit — the files are there and usually want looking at
    const bin = done.bins[0];
    if (shouldOpenBinAfterApply() && bin && done.moved > 0)
      void bridge.call('shell.openFolder', { path: bin }).catch(() => undefined);
  });

  useBridgeEvent('undo.done', (done) => {
    const skipped = done.skipped ? ` (${t('history.skipped', { n: done.skipped })})` : '';
    toast.ok(t('history.undoDone', { n: formatNumber(done.restored) }) + skipped);
  });

  useBridgeEvent('unpack.done', (done) => {
    toast.show(done.errors.length ? 'warn' : 'ok', t('unpack.done', { files: formatNumber(done.files), archives: formatNumber(done.archives) }), {
      detail: shortenPath(done.folder, 48),
      action: {
        label: t('history.showFolder'),
        run: () => void bridge.call('shell.openFolder', { path: done.folder }).catch(() => undefined),
      },
    });

    const first = done.errors[0];
    if (first) toast.warn(`${t('unpack.errors', { n: done.errors.length })} ${first}`, { duration: 10000 });
  });

  useBridgeEvent('report.done', (done) => {
    toast.ok(t('history.exported'), {
      detail: shortenPath(done.file, 48),
      action: {
        label: t('history.show'),
        run: () => void bridge.call('shell.showInExplorer', { path: done.file }).catch(() => undefined),
      },
    });
  });

  useBridgeEvent('job', (job) => {
    const failure = FAILURE_MESSAGES[job.kind];
    if (!failure) return;
    if (job.state === 'failed') toast.error(t(failure, { error: job.error ?? '' }), { duration: 10000 });
    if (job.state === 'cancelled') toast.warn(t('sources.cancelled'));
  });
}
