// views/group/LazyModelTab.tsx — the 3D tab, fetched only when it is opened.
//
// three.js is by far the largest thing the interface uses and most sessions never open a model, so it is a
// chunk of its own rather than part of the first load.
import { lazy, Suspense } from 'react';
import type { Garment } from '../../bridge/contract';
import { Icon } from '../../components/Icon';
import { useTranslate } from '../../i18n';

const ModelTab = lazy(() => import('./ModelTab').then((module) => ({ default: module.ModelTab })));

export function LazyModelTab({ members }: { members: Garment[] }) {
  const t = useTranslate();

  return (
    <Suspense
      fallback={
        <div className="v3d-overlay">
          <Icon name="refresh" /> {t('view3d.loading')}
        </div>
      }
    >
      <ModelTab members={members} />
    </Suspense>
  );
}
