// views/sources/Sources.tsx — the packs Duble is working on: adding them, reading them, and what it found.
import { useEffect, useState, type ReactNode } from 'react';
import { useApp } from '../../app/AppState';
import { navigate } from '../../app/router';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { AddedSources, Source } from '../../bridge/contract';
import { useBridgeEvent, useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { useConfirm } from '../../components/Confirm';
import { EmptyState } from '../../components/EmptyState';
import { Icon } from '../../components/Icon';
import { MenuButton, type MenuItem } from '../../components/Menu';
import { useToast } from '../../components/Toast';
import { useTranslate } from '../../i18n';
import { DetectGamesDialog } from './DetectGamesDialog';
import { SourceCard } from './SourceCard';
import { UnpackDialog } from './UnpackDialog';

/** Where App leaves the paths of files dropped on the window, since the drop lands on whatever screen is up. */
const DROPPED_KEY = 'dropped';

export function Sources() {
  const t = useTranslate();
  const { project, job } = useApp();
  const toast = useToast();
  const confirm = useConfirm();

  const [detecting, setDetecting] = useState(false);
  const [unpacking, setUnpacking] = useState<Source | null>(null);

  const sources = useCommand('sources.list', null, {
    enabled: !!project,
    reloadOn: ['sources.changed', 'project.changed'],
  });

  const announce = (added: AddedSources) => {
    if (added.added.length) toast.ok(t('sources.added', { n: added.added.length }));
    if (added.skipped.length) toast.warn(t('sources.skipped', { n: added.skipped.length }));
  };

  const add = async (call: Promise<AddedSources>) => {
    try {
      announce(await call);
    } catch (failure) {
      toast.error(errorCodeOf(failure) === ErrorCode.NoProject ? t('status.noProject') : messageOf(failure));
    }
  };

  const index = async (args: { ids?: string[]; force?: boolean } = {}) => {
    try {
      const started = await bridge.call('sources.index', args);
      if (!started.started) toast.warn(t('sources.empty'));
    } catch (failure) {
      toast.warn(errorCodeOf(failure) === ErrorCode.Busy ? t('sources.busy') : messageOf(failure));
    }
  };

  // A drop that happened on another screen is left here by App and picked up on arrival; a drop that happens
  // while this screen is already up arrives as the event. Only one of the two ever fires for a given drop.
  useEffect(() => {
    const dropped = sessionStorage.getItem(DROPPED_KEY);
    if (!dropped || !project) return;
    sessionStorage.removeItem(DROPPED_KEY);
    void add(bridge.call('sources.add', { paths: JSON.parse(dropped) as string[] }));
  }, [project]);

  useBridgeEvent('files.dropped', (data) => {
    void add(bridge.call('sources.add', { paths: data.paths }));
  });

  // how indexing ended, said once
  useBridgeEvent('job', (finished) => {
    if (finished.kind !== 'index') return;
    if (finished.state === 'done')
      toast.ok(
        t('sources.done', {
          garments: project?.garments ?? 0,
          textures: project?.textures ?? 0,
        }),
      );
    if (finished.state === 'cancelled') toast.warn(t('sources.cancelled'));
    if (finished.state === 'failed') toast.error(t('sources.failed', { error: finished.error ?? '' }), { duration: 8000 });
  });

  if (!project) {
    return (
      <>
        <Head />
        <EmptyState icon="file" title={t('status.noProject')} hint={t('start.empty')}>
          <Button variant="primary" icon="home" onClick={() => navigate('start')}>
            {t('nav.start')}
          </Button>
        </EmptyState>
      </>
    );
  }

  const list = sources.data?.sources ?? [];
  const running = job?.kind === 'index' && (job.state === 'start' || job.state === 'progress') ? job : undefined;

  const actionsFor = (source: Source): MenuItem[] => [
    {
      label: source.indexedAt ? t('sources.reindex') : t('sources.index'),
      icon: 'play',
      run: () => void index({ ids: [source.id] }),
    },
    { label: t('sources.forceAll'), icon: 'refresh', run: () => void index({ ids: [source.id], force: true }) },
    {
      label: t('sources.openFolder'),
      icon: 'external',
      run: () =>
        void bridge
          .call('shell.showInExplorer', { path: source.path })
          .catch((failure: unknown) => toast.warn(messageOf(failure))),
    },
    ...(source.kind === 'rpf' || source.inArchives > 0
      ? [{ label: t('unpack.menu'), icon: 'archive' as const, run: () => setUnpacking(source) }]
      : []),
    {
      label: t('sources.remove'),
      icon: 'trash',
      danger: true,
      run: () => {
        void (async () => {
          const sure = await confirm({
            title: t('sources.remove'),
            text: t('sources.confirmRemove', { name: source.name }),
            confirmLabel: t('common.remove'),
            danger: true,
          });
          if (!sure) return;
          try {
            await bridge.call('sources.remove', { id: source.id });
          } catch (failure) {
            toast.error(messageOf(failure));
          }
        })();
      },
    },
  ];

  return (
    <>
      <Head>
        <Button icon="folder" onClick={() => void add(bridge.call('sources.pickFolder'))}>
          {t('sources.addFolder')}
        </Button>
        <Button icon="archive" onClick={() => void add(bridge.call('sources.pickRpf'))}>
          {t('sources.addRpf')}
        </Button>
        <Button icon="gamepad" onClick={() => setDetecting(true)}>
          {t('sources.detect')}
        </Button>
        <Button variant="primary" icon="play" onClick={() => void index()}>
          {t('sources.indexAll')}
        </Button>
        <MenuButton
          title={t('common.more')}
          items={[
            { label: t('sources.indexChanged'), icon: 'play', run: () => void index() },
            { label: t('sources.forceAll'), icon: 'refresh', run: () => void index({ force: true }) },
          ]}
        />
      </Head>

      {list.length === 0 ? (
        <div className="empty dropzone">
          <Icon name="drop" />
          <h3>{t('sources.dropHint')}</h3>
          <p>{t('sources.empty')}</p>
        </div>
      ) : (
        <>
          <div className="grid-cards">
            {list.map((source) => (
              <SourceCard
                key={source.id}
                source={source}
                job={running?.text === source.name ? running : undefined}
                actions={actionsFor(source)}
                onToggle={(enabled) => {
                  bridge
                    .call('sources.toggle', { id: source.id, enabled: enabled })
                    .catch((failure: unknown) => toast.error(messageOf(failure)));
                }}
              />
            ))}
          </div>
          <p className="faint dropzone hint">
            <Icon name="drop" /> {t('sources.dropHint')}
          </p>
        </>
      )}

      {detecting && (
        <DetectGamesDialog
          onClose={() => setDetecting(false)}
          onAdd={(paths) => void add(bridge.call('sources.add', { paths: paths }))}
        />
      )}
      {unpacking && <UnpackDialog source={unpacking} onClose={() => setUnpacking(null)} />}
    </>
  );
}

function Head({ children }: { children?: ReactNode }) {
  const t = useTranslate();

  return (
    <div className="view-head">
      <div className="titles">
        <h1>{t('sources.title')}</h1>
        <p className="sub">{t('sources.subtitle')}</p>
      </div>
      {children && <div className="actions">{children}</div>}
    </div>
  );
}
