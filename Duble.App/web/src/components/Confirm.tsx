// components/Confirm.tsx — "are you sure?", as a call that answers true or false.
//
// A yes/no question is the one dialog that reads better awaited than rendered: the code that asks it is in the
// middle of doing something, and wants to carry on or stop.
import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { useTranslate } from '../i18n';
import { Button } from './Button';
import { Modal } from './Modal';

export interface ConfirmRequest {
  title?: string;
  text: string;
  /** The label of the button that goes ahead; defaults to the plain "OK". */
  confirmLabel?: string;
  cancelLabel?: string;
  /** Colours the confirming button as destructive — for anything that moves or deletes files. */
  danger?: boolean;
}

type Ask = (request: ConfirmRequest) => Promise<boolean>;

const ConfirmContext = createContext<Ask | null>(null);

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const t = useTranslate();
  const [request, setRequest] = useState<ConfirmRequest | null>(null);
  const answer = useRef<(confirmed: boolean) => void>(() => undefined);

  const ask = useCallback<Ask>(
    (next) =>
      new Promise<boolean>((resolve) => {
        answer.current = resolve;
        setRequest(next);
      }),
    [],
  );

  const close = useCallback((confirmed: boolean) => {
    setRequest(null);
    answer.current(confirmed);
  }, []);

  return (
    <ConfirmContext value={ask}>
      {children}
      {request && (
        <Modal
          title={request.title ?? ''}
          onClose={() => close(false)}
          footer={
            <>
              <Button onClick={() => close(false)}>{request.cancelLabel ?? t('common.cancel')}</Button>
              <Button variant={request.danger ? 'danger' : 'primary'} onClick={() => close(true)}>
                {request.confirmLabel ?? t('common.ok')}
              </Button>
            </>
          }
        >
          <p className="lead">{request.text}</p>
        </Modal>
      )}
    </ConfirmContext>
  );
}

export function useConfirm(): Ask {
  const ask = useContext(ConfirmContext);
  if (!ask) throw new Error('useConfirm outside ConfirmProvider');
  return ask;
}
