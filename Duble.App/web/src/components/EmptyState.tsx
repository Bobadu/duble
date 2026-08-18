// components/EmptyState.tsx — what a screen shows instead of a list: an icon, what is going on, what to do.
import type { ReactNode } from 'react';
import { Icon, type IconName } from './Icon';

export function EmptyState({
  icon,
  title,
  hint,
  children,
}: {
  icon: IconName;
  title: string;
  hint?: string;
  /** Buttons, when there is something to offer. */
  children?: ReactNode;
}) {
  return (
    <div className="empty">
      <Icon name={icon} />
      <h3>{title}</h3>
      {hint && <p>{hint}</p>}
      {children && <div className="btn-row centred">{children}</div>}
    </div>
  );
}
