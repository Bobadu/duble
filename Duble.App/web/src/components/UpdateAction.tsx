// components/UpdateAction.tsx — the button that gets the newer version.
//
// A copy the Setup installed updates itself: the download reports percent, then the program restarts as the
// new version. The portable exe cannot swap the file it is running from, so its button opens the release
// page in the browser instead — the same component, deciding by `canApply`.
import { useState } from 'react';
import { bridge, messageOf } from '../bridge/bridge';
import { useBridgeEvent } from '../bridge/hooks';
import { useTranslate } from '../i18n';
import { Button } from './Button';
import { useToast } from './Toast';

export function UpdateAction({ url, canApply, small }: { url: string; canApply: boolean; small?: boolean }) {
  const t = useTranslate();
  const toast = useToast();
  const [applying, setApplying] = useState(false);
  const [percent, setPercent] = useState(0);

  useBridgeEvent('update.progress', (data) => setPercent(data.percent));

  const apply = async () => {
    setApplying(true);
    try {
      await bridge.call('update.apply'); // answered only on failure — success replaces the process
    } catch (failure) {
      toast.error(t('update.failed', { error: messageOf(failure) }));
      setApplying(false);
      setPercent(0);
    }
  };

  if (!canApply)
    return (
      <Button
        variant="primary"
        small={small}
        icon="external"
        onClick={() => void bridge.call('shell.openUrl', { url }).catch((failure: unknown) => toast.warn(messageOf(failure)))}
      >
        {t('update.download')}
      </Button>
    );

  return (
    <Button variant="primary" small={small} icon="refresh" disabled={applying} onClick={() => void apply()}>
      {applying ? t('update.installing', { percent }) : t('update.install')}
    </Button>
  );
}
