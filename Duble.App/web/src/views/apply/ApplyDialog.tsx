// views/apply/ApplyDialog.tsx — the last look before anything moves: what would go, where to, and what will
// be left alone.
//
// The plan is asked for again whenever the bin folder changes, because that changes where every file lands.
// Nothing here decides anything: the rejections come from the decisions already made on the Duplicates screen.
import { useState } from 'react';
import { navigate } from '../../app/router';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { ApplyPlan } from '../../bridge/contract';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { Modal } from '../../components/Modal';
import { useToast } from '../../components/Toast';
import { formatSize, shortenPath, useI18n, useTranslate } from '../../i18n';
import { RenumberingHelp } from './RenumberingHelp';

/** Whether to open the bin folder when it is done; remembered because it is a habit, not a decision. */
const OPEN_AFTER_KEY = 'apply.openAfter';

export function shouldOpenBinAfterApply(): boolean {
  return sessionStorage.getItem(OPEN_AFTER_KEY) !== '0';
}

export function ApplyDialog({ onClose }: { onClose: () => void }) {
  const t = useTranslate();
  const { language } = useI18n();
  const toast = useToast();

  const [bin, setBin] = useState<{ folder: string | null } | null>(null);
  const [openAfter, setOpenAfter] = useState(shouldOpenBinAfterApply);
  const [helpOpen, setHelpOpen] = useState(false);
  const [starting, setStarting] = useState(false);

  // without a chosen bin this is a plain preview; choosing one saves it on the project and re-plans
  const preview = useCommand('apply.preview', bin ? { bin: bin.folder, setBin: true } : null);
  const plan = preview.data;

  const chooseBin = async () => {
    try {
      const picked = await bridge.call('dialogs.pickFolder', plan?.bin ? { start: plan.bin } : {});
      if (picked.path) setBin({ folder: picked.path });
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  const apply = async () => {
    setStarting(true);
    sessionStorage.setItem(OPEN_AFTER_KEY, openAfter ? '1' : '0');
    try {
      const started = await bridge.call('apply.run', { bin: plan?.bin ?? null, setBin: true });
      if (!started.started) {
        toast.warn(t('apply.nothing'));
        setStarting(false);
        return;
      }
      toast.info(t('apply.running'), { duration: 2500 });
      onClose();
    } catch (failure) {
      toast.error(
        errorCodeOf(failure) === ErrorCode.Busy ? t('sources.busy') : t('apply.failed', { error: messageOf(failure) }),
      );
      setStarting(false);
    }
  };

  return (
    <>
      <Modal
        title={t('apply.title')}
        wide
        onClose={onClose}
        footer={
          <>
            <Button onClick={onClose}>{t('common.cancel')}</Button>
            <Button variant="primary" disabled={!plan?.files || starting} onClick={apply}>
              {t('apply.go', { n: plan?.files ?? 0 })}
            </Button>
          </>
        }
      >
        {plan && (
          <>
            <div className="apply-head">
              <p className="lead">
                <Icon name="trash" />{' '}
                {t('apply.summary', {
                  garments: plan.garments,
                  files: plan.files,
                  mb: formatSize(plan.bytes, language),
                })}
              </p>
              <Notes plan={plan} />
            </div>

            <div className="apply-where">
              <div className="apply-label">{t('apply.where')}</div>

              <label className="radio-row">
                <input type="radio" name="apply-bin" checked={!plan.bin} onChange={() => setBin({ folder: null })} />
                <span>{t('apply.besideSource')}</span>
                <span className="faint mono">
                  {!plan.bin && plan.bins[0]
                    ? shortenPath(plan.bins[0].bin, 70)
                    : `…\\_rejected\\<${t('dup.sourcesFilter').toLowerCase()}>`}
                </span>
              </label>

              <label className="radio-row">
                <input type="radio" name="apply-bin" checked={!!plan.bin} onChange={() => void chooseBin()} />
                <span>{t('apply.customFolder')}</span>
                <span className="faint mono">{plan.bin ? shortenPath(plan.bin, 70) : ''}</span>
                <Button
                  small
                  icon="folder"
                  onClick={(event) => {
                    event.preventDefault();
                    void chooseBin();
                  }}
                >
                  {t('apply.pick')}
                </Button>
              </label>
            </div>

            <PlanList plan={plan} onShowSources={() => { onClose(); navigate('sources'); }} />

            <div className="apply-warn">
              <Icon name="warn" />
              <div>
                <b>{t('apply.warnTitle')}</b>
                <p>
                  {t('apply.warnText')}{' '}
                  <a
                    href="#"
                    onClick={(event) => {
                      event.preventDefault();
                      setHelpOpen(true);
                    }}
                  >
                    {t('apply.warnMore')}
                  </a>
                </p>
              </div>
            </div>

            <label className="check-row">
              <input type="checkbox" checked={openAfter} onChange={(event) => setOpenAfter(event.target.checked)} />
              <span>{t('apply.openAfter')}</span>
            </label>
          </>
        )}
      </Modal>

      {helpOpen && <RenumberingHelp onClose={() => setHelpOpen(false)} />}
    </>
  );
}

/** The things worth saying about a plan before it runs, each only when it applies. */
function Notes({ plan }: { plan: ApplyPlan }) {
  const t = useTranslate();

  const notes = [
    plan.shared > 0 && { icon: 'info' as const, text: t('apply.shared', { n: plan.shared }) },
    plan.inArchive > 0 && { icon: 'archive' as const, text: t('apply.inArchive', { n: plan.inArchive }) },
    plan.missing > 0 && { icon: 'warn' as const, text: t('apply.missing', { n: plan.missing }) },
    plan.missingSources.length > 0 && {
      icon: 'warn' as const,
      text: t('apply.missingSources', { list: plan.missingSources.join(', ') }),
      warning: true,
    },
  ].filter((note) => note !== false);

  if (notes.length === 0) return null;

  return (
    <ul className="apply-notes">
      {notes.map((note) => (
        <li key={note.text} className={note.warning ? 'warn-txt' : undefined}>
          <Icon name={note.icon} /> {note.text}
        </li>
      ))}
    </ul>
  );
}

function PlanList({ plan, onShowSources }: { plan: ApplyPlan; onShowSources: () => void }) {
  const t = useTranslate();
  const { language, formatNumber } = useI18n();
  const garments = plan.list ?? [];

  return (
    <div className="apply-listwrap">
      <div className="apply-label">
        {t('apply.list')} <span className="faint">({formatNumber(garments.length)})</span>
      </div>

      <div className="apply-list">
        {garments.length === 0 && <p className="faint">{t('dup.nothingToReject')}</p>}

        {garments.map((garment) => (
          <div key={garment.id} className={garment.files ? 'apply-row' : 'apply-row skip'}>
            <div className="thumbnail">
              {garment.thumbnail ? (
                <img src={`https://duble.data/thumb/${garment.thumbnail}.png`} alt="" loading="lazy" />
              ) : (
                <Icon name="cube" />
              )}
            </div>

            <div className="who">
              <span className="nm">
                {garment.name}
                <sub>{garment.suffix ?? ''}</sub>
              </span>
              <span className="src" title={`${garment.source} · ${garment.container ?? ''}`}>
                {garment.source}
                <span className="faint"> · {garment.container ?? ''}</span>
              </span>
            </div>

            <div className="to" title={garment.bin ?? ''}>
              {garment.bin && (
                <>
                  <Icon name="chevron" className="rot270" />
                  <span className="mono">{shortenPath(garment.bin, 44)}</span>
                </>
              )}
            </div>

            <div className="cnt">
              {garment.files > 0 && (
                <>
                  <b>{t('apply.files', { n: garment.files })}</b>
                  <span className="faint">{formatSize(garment.bytes, language)}</span>
                </>
              )}
              {garment.shared > 0 && (
                <span className="badge unknown" title={t('apply.shared', { n: garment.shared })}>
                  {garment.shared} <Icon name="info" />
                </span>
              )}
              {garment.inArchive > 0 && (
                <button type="button" className="badge unknown" title={t('apply.tooltipArchive')} onClick={onShowSources}>
                  {t('group.inArchive')}
                </button>
              )}
              {garment.missing > 0 && <span className="badge err">{t('apply.missing', { n: garment.missing })}</span>}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
