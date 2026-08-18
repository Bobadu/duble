// components/Menu.tsx — the little menu behind a "…" button.
//
// It positions itself next to whatever opened it and closes on a click elsewhere or on Escape. Like Select, it
// goes through a portal so that a card with hidden overflow cannot cut it off.
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Icon, type IconName } from './Icon';

export interface MenuItem {
  label: string;
  icon?: IconName;
  danger?: boolean;
  run: () => void;
}

export function Menu({
  items,
  anchor,
  onClose,
}: {
  items: readonly MenuItem[];
  /** The element the menu belongs to; the panel is placed under its right edge. */
  anchor: HTMLElement;
  onClose: () => void;
}) {
  const panel = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ left: -9999, top: -9999 });

  useLayoutEffect(() => {
    if (!panel.current) return;

    const from = anchor.getBoundingClientRect();
    const menu = panel.current.getBoundingClientRect();

    let left = from.right - menu.width;
    if (left < 8) left = 8;

    let top = from.bottom + 6;
    if (top + menu.height > window.innerHeight - 8) top = from.top - menu.height - 6;

    setPosition({ left, top });
  }, [anchor]);

  useEffect(() => {
    const closeOnOutside = (event: MouseEvent) => {
      if (!panel.current?.contains(event.target as Node)) onClose();
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };

    // on the next tick: the click that opened the menu is still on its way up
    const timer = setTimeout(() => {
      document.addEventListener('mousedown', closeOnOutside, true);
      document.addEventListener('keydown', closeOnEscape, true);
    }, 0);

    return () => {
      clearTimeout(timer);
      document.removeEventListener('mousedown', closeOnOutside, true);
      document.removeEventListener('keydown', closeOnEscape, true);
    };
  }, [onClose]);

  return createPortal(
    <div ref={panel} className="menu" role="menu" style={position}>
      {items.map((item) => (
        <button
          key={item.label}
          type="button"
          role="menuitem"
          className={item.danger ? 'danger' : undefined}
          onClick={() => {
            onClose();
            item.run();
          }}
        >
          {item.icon && <Icon name={item.icon} />}
          <span>{item.label}</span>
        </button>
      ))}
    </div>,
    document.body,
  );
}

/** The "…" button with its menu, which is how every card offers what can be done to it. */
export function MenuButton({ items, title }: { items: readonly MenuItem[]; title?: string }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);

  return (
    <>
      <button
        type="button"
        className="btn ghost icon more"
        title={title}
        onClick={(event) => {
          event.stopPropagation();
          setAnchor((open) => (open ? null : event.currentTarget));
        }}
      >
        <Icon name="more" />
      </button>
      {anchor && <Menu items={items} anchor={anchor} onClose={() => setAnchor(null)} />}
    </>
  );
}
