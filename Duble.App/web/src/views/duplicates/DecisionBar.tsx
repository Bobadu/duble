// views/duplicates/DecisionBar.tsx — the bar along the bottom: what applying the decisions would move, and
// the button that opens the dialog for it.
import type { ApplyPlan } from '../../bridge/contract';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { formatSize, useI18n, useTranslate } from '../../i18n';
import { routeToHash } from '../../app/router';

export function DecisionBar({ plan, busy, onApply }: { plan: ApplyPlan; busy: boolean; onApply: () => void }) {
  const t = useTranslate();
  const { language } = useI18n();

  return (
    <div className="decision-bar">
      <div className="decision-text">
        <Icon name="trash" />
        <span>
          {plan.files
            ? t('dup.toReject', {
                garments: plan.garments,
                files: plan.files,
                mb: formatSize(plan.bytes, language),
              })
            : t('dup.nothingToReject')}
        </span>
        {plan.inArchive > 0 && (
          <a href={routeToHash('sources')} className="faint" title={t('apply.tooltipArchive')}>
            · {t('dup.inArchive', { n: plan.inArchive })}
          </a>
        )}
        {plan.shared > 0 && <span className="faint">· {t('apply.shared', { n: plan.shared })}</span>}
      </div>

      <Button
        variant="primary"
        icon="check"
        disabled={!plan.files || busy}
        title={plan.files ? t('apply.title') : t('apply.nothing')}
        onClick={onApply}
      >
        {t('dup.apply')}
      </Button>
    </div>
  );
}
