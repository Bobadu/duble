// components/Select.tsx — a dropdown of our own, because the system popup of a native <select> cannot be
// styled and looked like a different application inside the window.
//
// It is a listbox: the button opens a panel positioned next to it, Escape and a click outside close it, and
// the chosen option is marked. The panel is rendered in a portal so no ancestor's overflow can clip it.
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Icon } from './Icon';

export interface Option<T extends string> {
  value: T;
  label: string;
}

export function Select<T extends string>({
  options,
  value,
  onChange,
  label,
  disabled,
}: {
  options: readonly Option<T>[];
  value: T;
  onChange: (value: T) => void;
  label?: string;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState({ left: 0, top: 0, minWidth: 0 });
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);

  const chosen = options.find((option) => option.value === value);

  useLayoutEffect(() => {
    if (!open || !button.current || !panel.current) return;

    const anchor = button.current.getBoundingClientRect();
    const menu = panel.current.getBoundingClientRect();

    let left = anchor.left;
    if (left + menu.width > window.innerWidth - 8) left = window.innerWidth - 8 - menu.width;
    if (left < 8) left = 8;

    let top = anchor.bottom + 6;
    // no room below: above, which is where a dropdown at the bottom of the window belongs
    if (top + menu.height > window.innerHeight - 8) top = Math.max(8, anchor.top - menu.height - 6);

    setPosition({ left, top, minWidth: Math.max(anchor.width, 180) });
  }, [open]);

  useEffect(() => {
    if (!open) return;

    const closeOnOutside = (event: MouseEvent) => {
      if (!panel.current?.contains(event.target as Node) && !button.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setOpen(false);
      button.current?.focus();
    };

    document.addEventListener('mousedown', closeOnOutside, true);
    document.addEventListener('keydown', closeOnEscape, true);
    return () => {
      document.removeEventListener('mousedown', closeOnOutside, true);
      document.removeEventListener('keydown', closeOnEscape, true);
    };
  }, [open]);

  return (
    <>
      <button
        ref={button}
        type="button"
        className="dd input sm"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={label}
        title={label}
        disabled={disabled}
        onClick={() => setOpen((was) => !was)}
      >
        <span className="dd-label">{chosen?.label ?? ''}</span>
        <Icon name="chevron" className="dd-chev" />
      </button>

      {open &&
        createPortal(
          <div
            ref={panel}
            className="menu dd-menu"
            role="listbox"
            style={{ left: position.left, top: position.top, minWidth: position.minWidth }}
          >
            {options.map((option) => {
              const selected = option.value === value;
              return (
                <button
                  key={option.value}
                  type="button"
                  role="option"
                  aria-selected={selected}
                  className={selected ? 'on' : ''}
                  onClick={() => {
                    setOpen(false);
                    if (option.value !== value) onChange(option.value);
                  }}
                >
                  <span className="txt">{option.label}</span>
                  {selected && <Icon name="check" />}
                </button>
              );
            })}
          </div>,
          document.body,
        )}
    </>
  );
}
