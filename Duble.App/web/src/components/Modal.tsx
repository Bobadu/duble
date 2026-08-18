// components/Modal.tsx — a modal dialog: the backdrop, Escape, a click outside, and the buttons along the
// bottom.
//
// It is a component rather than a function that returns a promise, so what is on screen follows from state
// like everything else. The one case that reads better as a call — a yes/no question — is Confirm.tsx, which
// is built on this.
import { useEffect, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

export function Modal({
  title,
  wide,
  onClose,
  footer,
  children,
}: {
  title: string;
  wide?: boolean;
  /** Escape, the backdrop and the close button all end here. */
  onClose: () => void;
  footer?: ReactNode;
  children: ReactNode;
}) {
  const dialog = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      event.preventDefault();
      onClose();
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  // the first thing that can be typed into or pressed, so the keyboard lands somewhere useful
  useEffect(() => {
    const focusable = dialog.current?.querySelector<HTMLElement>('input, textarea, select, button.primary, button');
    focusable?.focus();
  }, []);

  return createPortal(
    <div
      className="dialog-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div ref={dialog} className={wide ? 'dialog wide' : 'dialog'} role="dialog" aria-modal="true" aria-label={title}>
        <header>
          <h2>{title}</h2>
        </header>
        <div className="body">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>,
    document.body,
  );
}
