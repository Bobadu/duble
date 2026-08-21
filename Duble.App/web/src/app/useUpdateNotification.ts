// app/useUpdateNotification.ts — the toast that says a newer Duble is out.
//
// The host raises `update.available` at most once per run, after its quiet check at start. The toast points
// at About, where the release stays on show — AppState keeps it — with its notes and the download button.
import { useBridgeEvent } from '../bridge/hooks';
import { useToast } from '../components/Toast';
import { useTranslate } from '../i18n';
import { navigate } from './router';

export function useUpdateNotification(): void {
  const t = useTranslate();
  const toast = useToast();

  useBridgeEvent('update.available', (update) => {
    toast.info(t('update.available', { version: update.version }), {
      action: { label: t('update.see'), run: () => navigate('about') },
    });
  });
}
