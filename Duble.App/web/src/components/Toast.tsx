// components/Toast.tsx — the short message in the corner, with an optional action ("Undo", "Show").
//
// Showing one is a side effect of something the user did, so it is an imperative call rather than a piece of
// state a view has to hold: `const toast = useToast(); toast.error(...)`.
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import { Icon, type IconName } from './Icon';

export type ToastKind = 'info' | 'ok' | 'warn' | 'error';

export interface ToastOptions {
  /** A second, quieter line — a path, usually. */
  detail?: string;
  /** A button inside the toast. A toast with one lives longer, because it asks for a decision. */
  action?: { label: string; run: () => void };
  /** Milliseconds; 0 keeps it until it is dismissed. */
  duration?: number;
}

interface Toast extends ToastOptions {
  id: number;
  kind: ToastKind;
  text: string;
}

export interface Toaster {
  show: (kind: ToastKind, text: string, options?: ToastOptions) => void;
  info: (text: string, options?: ToastOptions) => void;
  ok: (text: string, options?: ToastOptions) => void;
  warn: (text: string, options?: ToastOptions) => void;
  error: (text: string, options?: ToastOptions) => void;
}

const ToastContext = createContext<Toaster | null>(null);

const iconFor: Record<ToastKind, IconName> = { info: 'info', ok: 'ok', warn: 'warn', error: 'warn' };

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(0);

  const dismiss = useCallback((id: number) => setToasts((all) => all.filter((toast) => toast.id !== id)), []);

  const show = useCallback(
    (kind: ToastKind, text: string, options: ToastOptions = {}) => {
      const id = ++nextId.current;
      const duration = options.duration ?? (options.action ? 9000 : 4200);
      setToasts((all) => [...all, { ...options, id, kind, text }]);
      if (duration > 0) setTimeout(() => dismiss(id), duration);
    },
    [dismiss],
  );

  const toaster = useMemo<Toaster>(
    () => ({
      show,
      info: (text, options) => show('info', text, options),
      ok: (text, options) => show('ok', text, options),
      warn: (text, options) => show('warn', text, options),
      error: (text, options) => show('error', text, options),
    }),
    [show],
  );

  return (
    <ToastContext value={toaster}>
      {children}
      <div className="toast-layer">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast ${toast.kind}`} role="status">
            <Icon name={iconFor[toast.kind]} />
            <div className="txt">
              <span>{toast.text}</span>
              {toast.detail && <span className="description">{toast.detail}</span>}
            </div>
            {toast.action && (
              <button
                type="button"
                className="act"
                onClick={() => {
                  dismiss(toast.id);
                  toast.action?.run();
                }}
              >
                {toast.action.label}
              </button>
            )}
            <button type="button" className="close" onClick={() => dismiss(toast.id)} aria-label="×">
              <Icon name="x" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext>
  );
}

export function useToast(): Toaster {
  const toaster = useContext(ToastContext);
  if (!toaster) throw new Error('useToast outside ToastProvider');
  return toaster;
}
