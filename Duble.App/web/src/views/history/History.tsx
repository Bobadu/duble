// views/history/History.tsx — everything that has been applied, and the way back from any of it. Also where
// the report is exported from, because that too is a record of what was decided.
import type { ReactNode } from 'react';
import { useApp } from '../../app/AppState';
import { navigate } from '../../app/router';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { CommandName, DamagedHistoryEntry, HistoryEntry } from '../../bridge/contract';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { useConfirm } from '../../components/Confirm';
import { EmptyState } from '../../components/EmptyState';
import { useToast } from '../../components/Toast';
import { useTranslate } from '../../i18n';
import { HistoryCard } from './HistoryCard';

export function History() {
  const t = useTranslate();
  const { project } = useApp();
  const toast = useToast();
  const confirm = useConfirm();

  // deliberately not on every project change: each read opens every undo log on disk
  const history = useCommand('history.list', null, {
    enabled: !!project,
    reloadOn: ['history.changed', 'undo.done'],
  });

  const exportTo = async (command: Extract<CommandName, 'report.exportHtml' | 'report.exportCsv'>) => {
    try {
      const done = await bridge.call(command, {});
      if ('started' in done && done.started) toast.info(t('history.exporting'), { duration: 2500 });
    } catch (failure) {
      const code = errorCodeOf(failure);
      toast.warn(
        code === ErrorCode.Busy ? t('sources.busy') : code === ErrorCode.NotFound ? t('dup.noResult') : messageOf(failure),
      );
    }
  };

  const undo = async (file: string, garmentIds: string[] | null, files: number) => {
    const sure = await confirm({
      title: t('history.title'),
      text: t('history.confirmUndo', { n: files }),
      confirmLabel: garmentIds ? t('history.undoOne') : t('history.undoAll'),
    });
    if (!sure) return;

    try {
      const started = await bridge.call('history.undo', { file: file, garments: garmentIds ?? [] });
      if (started.started) toast.info(t('history.undoing'), { duration: 2500 });
      else toast.warn(t('history.gone'));
    } catch (failure) {
      toast.error(errorCodeOf(failure) === ErrorCode.Busy ? t('sources.busy') : messageOf(failure));
    }
  };

  if (!project) {
    return (
      <>
        <Head />
        <EmptyState icon="file" title={t('status.noProject')} hint={t('start.empty')}>
          <Button variant="primary" icon="home" onClick={() => navigate('start')}>
            {t('nav.start')}
          </Button>
        </EmptyState>
      </>
    );
  }

  const entries = history.data?.entries ?? [];

  return (
    <>
      <Head>
        <span className="faint">{t('history.export')}</span>
        <Button icon="file" onClick={() => void exportTo('report.exportHtml')}>
          {t('history.exportHtml')}
        </Button>
        <Button icon="catalog" onClick={() => void exportTo('report.exportCsv')}>
          {t('history.exportCsv')}
        </Button>
      </Head>

      {entries.length === 0 ? (
        <EmptyState icon="history" title={t('history.empty')} hint={t('history.emptyHint')} />
      ) : (
        <div className="hist-list">
          {entries.map((entry) =>
            isDamaged(entry) ? (
              <DamagedCard key={entry.file} entry={entry} />
            ) : (
              <HistoryCard
                key={entry.file}
                entry={entry}
                onUndo={(garmentIds, files) => void undo(entry.file, garmentIds, files)}
              />
            ),
          )}
        </div>
      )}
    </>
  );
}

function isDamaged(entry: HistoryEntry | DamagedHistoryEntry): entry is DamagedHistoryEntry {
  return 'damaged' in entry;
}

/** A log that will not parse. It is still listed: the files it describes are sitting in a bin folder. */
function DamagedCard({ entry }: { entry: DamagedHistoryEntry }) {
  const t = useTranslate();

  return (
    <div className="card hist-card">
      <div className="card-body">
        <span className="badge err">{t('common.error')}</span> <span className="mono">{entry.name}</span>{' '}
        <span className="faint">{entry.error}</span>
      </div>
    </div>
  );
}

function Head({ children }: { children?: ReactNode }) {
  const t = useTranslate();

  return (
    <div className="view-head">
      <div className="titles">
        <h1>{t('history.title')}</h1>
        <p className="sub">{t('history.subtitle')}</p>
      </div>
      {children && <div className="actions">{children}</div>}
    </div>
  );
}
