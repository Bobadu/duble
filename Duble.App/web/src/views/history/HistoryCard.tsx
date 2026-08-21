// views/history/HistoryCard.tsx — one apply: when it happened, what it moved, where to, and how to take it back.
import { useState } from 'react';
import { bridge, messageOf } from '../../bridge/bridge';
import type { HistoryEntry } from '../../bridge/contract';
import { useCommand } from '../../bridge/hooks';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { useToast } from '../../components/Toast';
import { formatSize, shortenPath, useI18n, useTranslate } from '../../i18n';

export function HistoryCard({ entry, onUndo }: { entry: HistoryEntry; onUndo: (garmentIds: string[] | null, files: number) => void }) {
  const t = useTranslate();
  const { language, formatDate } = useI18n();
  const toast = useToast();
  const [open, setOpen] = useState(false);

  const aside = [
    entry.shared ? t('apply.shared', { n: entry.shared }) : '',
    entry.inArchive ? t('apply.inArchive', { n: entry.inArchive }) : '',
    entry.missing ? t('apply.missing', { n: entry.missing }) : '',
  ].filter(Boolean);

  return (
    <div className={entry.undoneAt ? 'card hist-card undone' : 'card hist-card'}>
      <div className="card-body">
        <div className="hist-top">
          <div className="ico-box">
            <Icon name="history" />
          </div>

          <div className="info">
            <div className="name">
              {formatDate(entry.when)} <span className="faint">· {entry.description ?? ''}</span>{' '}
              {entry.undoneAt ? (
                <Badge tone="ok">{t('history.undone')}</Badge>
              ) : entry.partlyUndone ? (
                <Badge tone="unknown">{t('history.partly')}</Badge>
              ) : null}
              {entry.aborted && (
                <span className="badge err" title={entry.error ?? ''}>
                  {t('history.interrupted')}
                </span>
              )}
            </div>

            <div className="meta">
              {t('history.entry', {
                garments: entry.garments,
                files: entry.files,
                mb: formatSize(entry.bytes, language),
              })}
              {aside.length > 0 && <span className="faint"> · {aside.join(' · ')}</span>}
            </div>

            <div className="meta mono">
              {entry.bins.map((bin) => (
                <div key={bin}>{t('history.to', { bin: shortenPath(bin, 70) })}</div>
              ))}
            </div>
          </div>

          <div className="btn-row">
            {entry.bins.length > 0 && !entry.undoneAt && (
              <Button
                variant="ghost"
                small
                icon="external"
                onClick={() =>
                  void bridge
                    .call('shell.openFolder', { path: entry.bins[0]! })
                    .catch((failure: unknown) => toast.warn(messageOf(failure)))
                }
              >
                {t('history.showFolder')}
              </Button>
            )}

            <Button variant="ghost" small onClick={() => setOpen((was) => !was)}>
              <Icon name="chevron" className={open ? 'rot180' : undefined} />
              {t('history.details')}
            </Button>

            {entry.canUndo ? (
              <Button variant="primary" small icon="refresh" onClick={() => onUndo(null, entry.files)}>
                {t('history.undoAll')}
              </Button>
            ) : (
              !entry.undoneAt && <span className="faint">{t('history.gone')}</span>
            )}
          </div>
        </div>

        {open && <Details file={entry.file} onUndo={onUndo} />}
      </div>
    </div>
  );
}

/** The garments of one apply, read only when the entry is opened: each log is a file on disk. */
function Details({ file, onUndo }: { file: string; onUndo: (garmentIds: string[], files: number) => void }) {
  const t = useTranslate();
  const details = useCommand('history.get', { file: file }, { reloadOn: ['undo.done'] });

  if (details.error) return <p className="warn-txt">{messageOf(details.error)}</p>;
  if (!details.data) return <p className="faint">{t('common.loading')}</p>;

  const garments = details.data.entry.list ?? [];

  return (
    <div className="hist-details">
      <table className="hist-table">
        <thead>
          <tr>
            <th>{t('history.colItem')}</th>
            <th>{t('history.colSource')}</th>
            <th>{t('history.colFiles')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {garments.map((garment) => {
            const returned = garment.files.filter((moved) => moved.undone).length;
            return (
              <tr key={garment.id} className={garment.canUndo ? undefined : 'done'}>
                <td>
                  <span className="nm mono">{garment.name}</span>
                </td>
                <td>
                  <span title={garment.bin ?? ''}>{garment.source}</span>
                </td>
                <td>
                  {garment.files.length}
                  {returned > 0 && <span className="faint"> ({t('history.returned', { n: returned })})</span>}
                </td>
                <td className="act">
                  {garment.canUndo ? (
                    <Button variant="ghost" small icon="refresh" onClick={() => onUndo([garment.id], garment.files.length)}>
                      {t('history.undoOne')}
                    </Button>
                  ) : returned === garment.files.length ? (
                    <Badge tone="ok">{t('history.undone')}</Badge>
                  ) : (
                    <span className="faint">{t('history.gone')}</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
