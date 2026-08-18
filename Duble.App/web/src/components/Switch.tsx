// components/Switch.tsx — a labelled on/off pill, with an optional count beside the label.
import { Icon } from './Icon';

export function Switch({
  on,
  label,
  count,
  title,
  onChange,
}: {
  on: boolean;
  label: string;
  count?: number;
  title?: string;
  onChange: (on: boolean) => void;
}) {
  return (
    <button
      type="button"
      className={on ? 'switch on' : 'switch'}
      role="switch"
      aria-checked={on}
      title={title}
      onClick={() => onChange(!on)}
    >
      <span>
        {label}
        {count ? <span className="n">{count}</span> : null}
      </span>
      <Icon name={on ? 'toggleOn' : 'toggleOff'} />
    </button>
  );
}
