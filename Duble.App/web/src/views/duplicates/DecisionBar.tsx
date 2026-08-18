// views/duplicates/DecisionBar.tsx — the bar along the bottom: what applying the decisions would move, and
// the button that opens the dialog for it.
import type { ApplyPlan } from '../../bridge/contract';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { formatSize, useI18n, useTranslate } from '../../i18n';
import { routeToHash } from '../../app/router';

export function DecisionBar({ plan, busy, onApply }: { plan: ApplyPlan; busy: boolean; onApply: () => void }) {
  const t = useTranslate();
  const { language, formatNumber } = useI18n();

  return (
    <div className="decision-bar">
      <div className="decision-text">
        <Icon name="trash" />
        <span>
          {plan.pliki
            ? t('dup.toReject', {
                pozycje: formatNumber(plan.pozycje),
                pliki: formatNumber(plan.pliki),
                mb: formatSize(plan.bajty, language),
              })
            : t('dup.nothingToReject')}
        </span>
        {plan.wArchiwum > 0 && (
          <a href={routeToHash('sources')} className="faint" title={t('apply.tooltipArchive')}>
            · {t('dup.inArchive', { n: plan.wArchiwum })}
          </a>
        )}
        {plan.wspoldzielone > 0 && <span className="faint">· {t('apply.shared', { n: plan.wspoldzielone })}</span>}
      </div>

      <Button
        variant="primary"
        icon="check"
        disabled={!plan.pliki || busy}
        title={plan.pliki ? t('apply.title') : t('apply.nothing')}
        onClick={onApply}
      >
        {t('dup.apply')}
      </Button>
    </div>
  );
}
