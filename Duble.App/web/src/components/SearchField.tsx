// components/SearchField.tsx — a search box that keeps up with typing without asking the host on every letter.
//
// The field holds what was typed and reports it after a pause. This is also why the old interface needed a
// trick to put the caret back: it rebuilt the whole filter bar on every keystroke, and the input under the
// cursor was a different element each time. Here the input is the same element throughout.
import { useEffect, useState } from 'react';
import { Icon } from './Icon';

export function SearchField({
  value,
  onChange,
  placeholder,
  label,
  delay = 220,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  label?: string;
  delay?: number;
}) {
  const [typed, setTyped] = useState(value);

  // the filter can also change from outside — cleared by a button, restored from the session
  useEffect(() => setTyped(value), [value]);

  useEffect(() => {
    if (typed === value) return;
    const timer = setTimeout(() => onChange(typed), delay);
    return () => clearTimeout(timer);
  }, [typed, value, delay, onChange]);

  return (
    <div className="filter-search">
      <span className="ico-wrap">
        <Icon name="search" />
      </span>
      <input
        className="input"
        value={typed}
        placeholder={placeholder}
        aria-label={label}
        onChange={(event) => setTyped(event.target.value)}
      />
    </div>
  );
}
