// components/Progress.tsx — the bar in the status bar and beside a source being indexed.
//
// Without a percentage it runs indeterminate: a job that has not counted its work yet still has to look alive.
export function Progress({ percent }: { percent?: number }) {
  const known = typeof percent === 'number';

  return (
    <div
      className={known ? 'progress' : 'progress indeterminate'}
      role="progressbar"
      aria-valuenow={known ? percent : undefined}
      aria-valuemin={0}
      aria-valuemax={100}
    >
      <i style={{ width: `${known ? percent : 0}%` }} />
    </div>
  );
}
