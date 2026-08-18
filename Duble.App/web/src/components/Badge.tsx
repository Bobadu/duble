// components/Badge.tsx — the small coloured label, and the verdict badge every list of groups shows.
import type { ReactNode } from 'react';
import type { Verdict } from '../bridge/contract';
import { useTranslate } from '../i18n';
import { Icon, type IconName } from './Icon';

export type BadgeTone = 'neutral' | 'ok' | 'warn' | 'unknown' | 'gen9' | 'legacy';

export function Badge({ tone = 'neutral', icon, children }: { tone?: BadgeTone; icon?: IconName; children: ReactNode }) {
  return (
    <span className={tone === 'neutral' ? 'badge' : `badge ${tone}`}>
      {icon && <Icon name={icon} />}
      {children}
    </span>
  );
}

/** The colour and the icon each verdict is recognised by; the word itself comes from the engine's dictionary. */
const verdicts: Record<Verdict, { className: string; icon: IconName }> = {
  duplicate: { className: 'w-dup', icon: 'duplicates' },
  superset: { className: 'w-nad', icon: 'layers' },
  needsReview: { className: 'w-wgl', icon: 'eye' },
  retexture: { className: 'w-prz', icon: 'palette' },
};

export function VerdictBadge({ verdict }: { verdict: Verdict }) {
  const t = useTranslate();
  const look = verdicts[verdict];

  return (
    <span className={`badge ${look.className}`}>
      <Icon name={look.icon} />
      {t(`verdict.${verdict}`)}
    </span>
  );
}

export function verdictClassName(verdict: Verdict): string {
  return verdicts[verdict].className;
}

export function verdictIcon(verdict: Verdict): IconName {
  return verdicts[verdict].icon;
}
