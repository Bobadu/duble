// components/Histogram.tsx — a distribution as bars, with the thresholds drawn across it.
//
// Plain CSS and divs, no charting library: bar height is linear in the count, and each bar says its range and
// how many fell in it. It is only ever used for calibration, where the shape of the data is the whole point.
import type { CSSProperties } from 'react';
import type { Distribution } from '../bridge/contract';

/** Roughly how much of the chart's width one threshold label takes; marks closer than this would overprint. */
const LABEL_WIDTH = 22;

export interface ThresholdMark {
  value: number | undefined;
  label: string;
  /** m-a / m-b, which is what colours the line. */
  className?: string;
}

export function Histogram({
  distribution,
  marks = [],
  format = String,
  height = 90,
  tone,
  emptyText,
}: {
  distribution: Distribution | undefined;
  marks?: readonly ThresholdMark[];
  format?: (value: number) => string;
  height?: number;
  /** pos or neg: whether more to the left is good news or bad. */
  tone?: 'pos' | 'neg';
  emptyText: string;
}) {
  if (!distribution?.n || distribution.buckets.length === 0) {
    return (
      <div className={tone ? `chart ${tone}` : 'chart'}>
        <div className="chart-empty">{emptyText}</div>
      </div>
    );
  }

  const tallest = Math.max(1, ...distribution.buckets);
  const width = (distribution.to - distribution.from) / distribution.buckets.length;
  const span = distribution.to - distribution.from;

  // two thresholds can sit on the same value — "identical ≤ 0.00" and "similar ≤ 0.00" after a calibration on
  // a small catalog. Their lines coincide, so the labels are stacked instead of printed over each other, and
  // a label near the right edge hangs off its line to the left rather than off the card.
  const placed: { mark: ThresholdMark; left: number; row: number }[] = [];
  for (const mark of marks) {
    if (mark.value === undefined) continue;
    const left = Math.max(0, Math.min(100, ((mark.value - distribution.from) / span) * 100));
    placed.push({ mark, left, row: placed.filter((other) => Math.abs(other.left - left) < LABEL_WIDTH).length });
  }

  return (
    <div className={tone ? `chart ${tone}` : 'chart'}>
      <div className="chart-bars" style={{ height }}>
        {distribution.buckets.map((count, index) => {
          const from = distribution.from + index * width;
          const last = index === distribution.buckets.length - 1;
          return (
            <div
              key={index}
              className="chart-bar"
              title={`${format(from)} – ${format(from + width)}${last ? '+' : ''}: ${count}`}
            >
              {/* a bucket that has anything in it gets at least a sliver, or it reads as empty */}
              <i style={{ height: count ? Math.max(2, Math.round((count / tallest) * (height - 4))) : 0 }} />
            </div>
          );
        })}
      </div>

      {placed.map(({ mark, left, row }) => (
        <div
          key={mark.label}
          className={['chart-mark', mark.className, left > 100 - LABEL_WIDTH ? 'flip' : null].filter(Boolean).join(' ')}
          style={{ left: `${left}%`, '--row': row } as CSSProperties}
          title={`${mark.label}: ${format(mark.value ?? 0)}`}
        >
          <span>{mark.label}</span>
        </div>
      ))}

      <div className="chart-axis">
        <span>{format(distribution.from)}</span>
        <span>{format((distribution.from + distribution.to) / 2)}</span>
        <span>{format(distribution.to)}+</span>
      </div>
    </div>
  );
}
