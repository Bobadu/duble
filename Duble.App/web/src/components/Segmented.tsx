// components/Segmented.tsx — one row of mutually exclusive choices, each with how many it would show.
import type { ReactNode } from 'react';
import { useI18n } from '../i18n';

export interface Segment<T extends string> {
  value: T;
  label: string;
  count?: number;
  /** Extra class, so a verdict segment carries the verdict's colour. */
  className?: string;
  icon?: ReactNode;
}

export function Segmented<T extends string>({
  segments,
  value,
  onChange,
  className,
}: {
  segments: readonly Segment<T>[];
  value: T;
  onChange: (value: T) => void;
  className?: string;
}) {
  const { formatNumber } = useI18n();

  return (
    <div className={className ? `seg ${className}` : 'seg'} role="radiogroup">
      {segments.map((segment) => {
        const chosen = segment.value === value;
        return (
          <button
            key={segment.value}
            type="button"
            role="radio"
            aria-checked={chosen}
            className={[chosen ? 'on' : '', segment.className ?? ''].filter(Boolean).join(' ')}
            onClick={() => onChange(segment.value)}
          >
            {segment.icon}
            {segment.label}
            {segment.count !== undefined && <span className="n">{formatNumber(segment.count)}</span>}
          </button>
        );
      })}
    </div>
  );
}
