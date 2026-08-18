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
  const { language, formatNumber } = useI18n();
  const toast = useToast();

  const [bin, setBin] = useState<{ folder: string | null } | null>(null);
  const [openAfter, setOpenAfter] = useState(shouldOpenBinAfterApply);
  const [helpOpen, setHelpOpen] = useState(false);
  const [starting, setStarting] = useState(false);

  // without a chosen bin this is a plain preview; choosing one saves it on the project and re-plans
  const preview = useCommand('apply.preview', bin ? { kosz: bin.folder, ustawKosz: true } : null);
  const plan = preview.data;

  const chooseBin = async () => {
    try {
      const picked = await bridge.call('dialogs.pickFolder', plan?.kosz ? { start: plan.kosz } : {});
      if (picked.sciezka) setBin({ folder: picked.sciezka });
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  const apply = async () => {
    setStarting(true);
    sessionStorage.setItem(OPEN_AFTER_KEY, openAfter ? '1' : '0');
    try {
      const started = await bridge.call('apply.run', { kosz: plan?.kosz ?? null, ustawKosz: true });
      if (!started.uruchomiono) {
        toast.warn(t('apply.nothing'));
        setStarting(false);
        return;
      }
      toast.info(t('apply.running'), { duration: 2500 });
      onClose();
    } catch (failure) {
      toast.error(
        errorCodeOf(failure) === ErrorCode.Busy ? t('sources.busy') : t('apply.failed', { blad: messageOf(failure) }),
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
            <Button variant="primary" disabled={!plan?.pliki || starting} onClick={apply}>
              {t('apply.go', { n: formatNumber(plan?.pliki ?? 0) })}
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
                  pozycje: formatNumber(plan.pozycje),
                  pliki: formatNumber(plan.pliki),
                  mb: formatSize(plan.bajty, language),
                })}
              </p>
              <Notes plan={plan} />
            </div>

            <div className="apply-where">
              <div className="apply-label">{t('apply.where')}</div>

              <label className="radio-row">
                <input type="radio" name="apply-bin" checked={!plan.kosz} onChange={() => setBin({ folder: null })} />
                <span>{t('apply.besideSource')}</span>
                <span className="faint mono">
                  {!plan.kosz && plan.kosze[0]
                    ? shortenPath(plan.kosze[0].kosz, 70)
                    : `…\\_rejected\\<${t('dup.sourcesFilter').toLowerCase()}>`}
                </span>
              </label>

              <label className="radio-row">
                <input type="radio" name="apply-bin" checked={!!plan.kosz} onChange={() => void chooseBin()} />
                <span>{t('apply.customFolder')}</span>
                <span className="faint mono">{plan.kosz ? shortenPath(plan.kosz, 70) : ''}</span>
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
    plan.wspoldzielone > 0 && { icon: 'info' as const, text: t('apply.shared', { n: plan.wspoldzielone }) },
    plan.wArchiwum > 0 && { icon: 'archive' as const, text: t('apply.inArchive', { n: plan.wArchiwum }) },
    plan.brakujace > 0 && { icon: 'warn' as const, text: t('apply.missing', { n: plan.brakujace }) },
    plan.brakujaceZrodla.length > 0 && {
      icon: 'warn' as const,
      text: t('apply.missingSources', { lista: plan.brakujaceZrodla.join(', ') }),
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
  const garments = plan.lista ?? [];

  return (
    <div className="apply-listwrap">
      <div className="apply-label">
        {t('apply.list')} <span className="faint">({formatNumber(garments.length)})</span>
      </div>

      <div className="apply-list">
        {garments.length === 0 && <p className="faint">{t('dup.nothingToReject')}</p>}

        {garments.map((garment) => (
          <div key={garment.id} className={garment.pliki ? 'apply-row' : 'apply-row skip'}>
            <div className="thumb">
              {garment.thumb ? (
                <img src={`https://duble.data/thumb/${garment.thumb}.png`} alt="" loading="lazy" />
              ) : (
                <Icon name="cube" />
              )}
            </div>

            <div className="who">
              <span className="nm">
                {garment.nazwa}
                <sub>{garment.sufiks ?? ''}</sub>
              </span>
              <span className="src" title={`${garment.zrodlo} · ${garment.kontener ?? ''}`}>
                {garment.zrodlo}
                <span className="faint"> · {garment.kontener ?? ''}</span>
              </span>
            </div>

            <div className="to" title={garment.kosz ?? ''}>
              {garment.kosz && (
                <>
                  <Icon name="chevron" className="rot270" />
                  <span className="mono">{shortenPath(garment.kosz, 44)}</span>
                </>
              )}
            </div>

            <div className="cnt">
              {garment.pliki > 0 && (
                <>
                  <b>{t('apply.files', { n: garment.pliki })}</b>
                  <span className="faint">{formatSize(garment.bajty, language)}</span>
                </>
              )}
              {garment.wspoldzielone > 0 && (
                <span className="badge unknown" title={t('apply.shared', { n: garment.wspoldzielone })}>
                  {garment.wspoldzielone} <Icon name="info" />
                </span>
              )}
              {garment.wArchiwum > 0 && (
                <button type="button" className="badge unknown" title={t('apply.tooltipArchive')} onClick={onShowSources}>
                  {t('group.inArchive')}
                </button>
              )}
              {garment.brakujace > 0 && <span className="badge err">{t('apply.missing', { n: garment.brakujace })}</span>}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
