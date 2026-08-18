// views/group/ModelTab.tsx — the models themselves: side by side with one shared camera, or one on top of
// the other with a blend between them.
//
// Side by side answers "are these the same shape?" at a glance; the overlay answers "where exactly do they
// differ?". Both are the same ModelView class, arranged differently.
import { useCallback, useEffect, useRef, useState } from 'react';
import type { Garment } from '../../bridge/contract';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { Icon } from '../../components/Icon';
import { Segmented } from '../../components/Segmented';
import { Select } from '../../components/Select';
import { Switch } from '../../components/Switch';
import { useI18n, useTranslate } from '../../i18n';
import { CameraSync, isWebglAvailable, ModelView, type MeshStatistics } from '../../three/ModelView';
import { garmentName } from '../duplicates/GroupCard';

type Layout = 'side' | 'overlay';

export function ModelTab({ members }: { members: Garment[] }) {
  const t = useTranslate();

  const [layout, setLayout] = useState<Layout>('side');
  const [wireframe, setWireframe] = useState(false);
  const [lightBackground, setLightBackground] = useState(false);
  const [synced, setSynced] = useState(true);
  const [resetToken, setResetToken] = useState(0);

  if (!isWebglAvailable()) return <EmptyState icon="warn" title={t('view3d.webgl')} />;

  const overlay = layout === 'overlay' && members.length >= 2;

  return (
    <div className="v3d-root">
      <div className="filterbar v3d-tools">
        {members.length >= 2 && (
          <Segmented
            value={layout}
            onChange={setLayout}
            segments={[
              { value: 'side', label: t('view3d.sideBySide'), icon: <Icon name="catalog" /> },
              { value: 'overlay', label: t('view3d.overlay'), icon: <Icon name="layers" /> },
            ]}
          />
        )}

        {/* one camera for all of them only makes sense when there is more than one canvas */}
        {!overlay && members.length >= 2 && (
          <Switch on={synced} label={t('view3d.sync')} onChange={setSynced} />
        )}

        <Switch on={wireframe} label={t('view3d.wireframe')} onChange={setWireframe} />
        <Switch on={lightBackground} label={t('view3d.background')} onChange={setLightBackground} />

        <Button icon="search" onClick={() => setResetToken((token) => token + 1)}>
          {t('view3d.reset')}
        </Button>

        <span className="faint v3d-hint">{t('view3d.hint')}</span>
      </div>

      {overlay ? (
        <div className="v3d-grid single">
          <OverlayCard members={members} wireframe={wireframe} light={lightBackground} resetToken={resetToken} />
        </div>
      ) : (
        <div className="v3d-grid" style={{ '--n': members.length } as React.CSSProperties}>
          <SideBySide members={members} wireframe={wireframe} light={lightBackground} synced={synced} resetToken={resetToken} />
        </div>
      )}
    </div>
  );
}

/** One canvas per model, all looking from the same place while the cameras are linked. */
function SideBySide({
  members,
  wireframe,
  light,
  synced,
  resetToken,
}: {
  members: Garment[];
  wireframe: boolean;
  light: boolean;
  synced: boolean;
  resetToken: number;
}) {
  const sync = useRef(new CameraSync());

  useEffect(() => {
    const current = sync.current;
    current.setEnabled(synced);
  }, [synced, members]);

  // a fresh synchroniser for a fresh set of models: the old views are gone with their canvases
  useEffect(() => {
    const current = sync.current;
    return () => current.clear();
  }, [members]);

  return (
    <>
      {members.map((member, index) => (
        <ModelCard
          key={member.id}
          member={member}
          wireframe={wireframe}
          light={light}
          resetToken={resetToken}
          // the first one frames the camera; the rest follow it, so all of them start on the same view
          frame={index === 0}
          sync={sync.current}
        />
      ))}
    </>
  );
}

function ModelCard({
  member,
  wireframe,
  light,
  frame,
  sync,
  resetToken,
}: {
  member: Garment;
  wireframe: boolean;
  light: boolean;
  frame: boolean;
  sync: CameraSync;
  resetToken: number;
}) {
  const t = useTranslate();
  const { formatNumber } = useI18n();

  const variants = variantsOf(member);
  const [variant, setVariant] = useState(variants[0] ?? null);
  const [statistics, setStatistics] = useState<MeshStatistics | null>(null);
  const [state, setState] = useState<'loading' | 'ready' | 'failed'>('loading');

  const { container, view } = useModelView({ wireframe, onCreated: (created) => sync.add(created) });

  useEffect(() => {
    if (!view) return;
    setState('loading');
    view
      .load(meshUrl(member.id, variant), { slot: 'main', frame })
      .then((loaded) => {
        setStatistics(loaded);
        setState('ready');
        if (frame) sync.broadcast(view);
      })
      .catch(() => setState('failed'));
  }, [view, member.id, variant, frame, sync]);

  useEffect(() => {
    if (resetToken > 0) view?.frameCamera();
  }, [resetToken, view]);

  return (
    <div className="v3d-card">
      <div className="v3d-head">
        <div className="v3d-title">
          <span className="nm">
            {garmentName(member)}
            <sub>{member.sufiks ?? ''}</sub>
          </span>
          <span className="faint">{member.zrodlo}</span>
        </div>
        <div className="v3d-ctl">
          {variants.length > 1 && (
            <Select
              label={t('view3d.variant')}
              value={variant ?? ''}
              onChange={setVariant}
              options={variants.map((letter) => ({ value: letter, label: t('wipe.variant', { x: letter.toUpperCase() }) }))}
            />
          )}
          <span className="v3d-stats faint">
            {statistics &&
              `${formatNumber(statistics.vertices)} ${t('view3d.verts')} · ${formatNumber(statistics.triangles)} ${t('view3d.tris')}`}
          </span>
        </div>
      </div>

      <div ref={container} className={light ? 'v3d light' : 'v3d'}>
        <Overlay state={state} />
      </div>
    </div>
  );
}

/** Two models in one canvas, with a slider between them. */
function OverlayCard({
  members,
  wireframe,
  light,
  resetToken,
}: {
  members: Garment[];
  wireframe: boolean;
  light: boolean;
  resetToken: number;
}) {
  const t = useTranslate();
  const { formatNumber } = useI18n();

  const [a, setA] = useState(members[0]!.id);
  const [b, setB] = useState(members[1]!.id);
  const [variants, setVariants] = useState<Record<string, string | null>>(() =>
    Object.fromEntries(members.map((member) => [member.id, variantsOf(member)[0] ?? null])),
  );
  const [blend, setBlend] = useState(50);
  const [statistics, setStatistics] = useState<{ A: MeshStatistics | null; B: MeshStatistics | null }>({ A: null, B: null });
  const [state, setState] = useState<'loading' | 'ready' | 'failed'>('loading');

  const { container, view } = useModelView({ wireframe });

  const memberOf = useCallback((id: string) => members.find((member) => member.id === id) ?? members[0]!, [members]);

  useEffect(() => {
    if (!view) return;
    let cancelled = false;
    setState('loading');

    void (async () => {
      try {
        const first = await view.load(meshUrl(a, variants[a] ?? null), { slot: 'A', frame: true, show: false });
        const second = await view.load(meshUrl(b, variants[b] ?? null), { slot: 'B', frame: false, show: false });
        if (cancelled) return;
        setStatistics({ A: first, B: second });
        setState('ready');
        view.blend(blend / 100);
      } catch {
        if (!cancelled) setState('failed');
      }
    })();

    return () => {
      cancelled = true;
    };
    // blend is applied separately below; reloading on every slider move would be absurd
  }, [view, a, b, variants]);

  useEffect(() => {
    view?.blend(blend / 100);
  }, [view, blend]);

  useEffect(() => {
    if (resetToken > 0) view?.frameCamera();
  }, [resetToken, view]);

  // space flips between the two, which is the quickest way to see a difference
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.code !== 'Space') return;
      if (['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].includes(document.activeElement?.tagName ?? '')) return;
      event.preventDefault();
      setBlend((was) => (was < 50 ? 100 : 0));
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, []);

  const sameMesh =
    statistics.A && statistics.B && statistics.A.vertices === statistics.B.vertices && statistics.A.triangles === statistics.B.triangles;

  const side = (role: 'A' | 'B') => {
    const id = role === 'A' ? a : b;
    const member = memberOf(id);
    const letters = variantsOf(member);

    return (
      <div className="who">
        <div className="row">
          {members.length > 2 ? (
            <Select
              label={t(role === 'A' ? 'view3d.chooseA' : 'view3d.chooseB')}
              value={id}
              onChange={role === 'A' ? setA : setB}
              options={members.map((each) => ({ value: each.id, label: garmentName(each) }))}
            />
          ) : (
            <span className="nm">
              {garmentName(member)}
              <sub>{member.sufiks ?? ''}</sub>
            </span>
          )}

          {letters.length > 1 && (
            <Select
              label={t('view3d.variant')}
              value={variants[id] ?? ''}
              onChange={(letter) => setVariants((all) => ({ ...all, [id]: letter }))}
              options={letters.map((letter) => ({ value: letter, label: t('wipe.variant', { x: letter.toUpperCase() }) }))}
            />
          )}
        </div>

        <span className="meta">
          {statistics[role] &&
            `${formatNumber(statistics[role]!.vertices)} ${t('view3d.verts')} · ${formatNumber(statistics[role]!.triangles)} ${t('view3d.tris')}`}
        </span>
      </div>
    );
  };

  return (
    <div className="v3d-card v3d-cmp">
      <div className="wipe-bar">
        <div className="wipe-side">
          <span className="cap a">A</span>
          {side('A')}
        </div>
        {sameMesh && <span className="v3d-eq badge ok">{t('view3d.sameMesh')}</span>}
        <div className="wipe-side right">
          {side('B')}
          <span className="cap b">B</span>
        </div>
      </div>

      <div ref={container} className={light ? 'v3d light' : 'v3d'}>
        <Overlay state={state} />
      </div>

      <div className="wipe-foot">
        <input
          type="range"
          className="wipe-range"
          min={0}
          max={100}
          value={blend}
          aria-label={t('view3d.blend')}
          onChange={(event) => setBlend(Number(event.target.value))}
        />
        <Segmented
          value={String(blend)}
          onChange={(value) => setBlend(Number(value))}
          segments={[
            { value: '0', label: t('view3d.showA') },
            { value: '50', label: t('view3d.overlayBoth') },
            { value: '100', label: t('view3d.showB') },
          ]}
        />
      </div>

      <p className="help">{t('view3d.overlayHint')}</p>
    </div>
  );
}

function Overlay({ state }: { state: 'loading' | 'ready' | 'failed' }) {
  const t = useTranslate();
  if (state === 'ready') return null;

  return (
    <div className={state === 'failed' ? 'v3d-overlay err' : 'v3d-overlay'}>
      <Icon name={state === 'failed' ? 'warn' : 'refresh'} /> {t(state === 'failed' ? 'view3d.error' : 'view3d.loading')}
    </div>
  );
}

/** Creates a ModelView for a container and takes it down again with the component. */
function useModelView({ wireframe, onCreated }: { wireframe: boolean; onCreated?: (view: ModelView) => void }) {
  const container = useRef<HTMLDivElement>(null);
  const [view, setView] = useState<ModelView | null>(null);

  useEffect(() => {
    if (!container.current) return;

    const created = new ModelView(container.current);
    onCreated?.(created);
    setView(created);

    return () => {
      created.dispose();
      setView(null);
    };
    // deliberately once per container: a new view would mean a new canvas and a fresh GPU context
  }, []);

  useEffect(() => {
    view?.setWireframe(wireframe);
  }, [view, wireframe]);

  return { container, view };
}

/** The colour variants a garment has, as the engine worked them out — the letter goes into the mesh URL. */
function variantsOf(garment: Garment): string[] {
  return [...new Set((garment.tekstury ?? []).map((texture) => texture.litera).filter((letter): letter is string => !!letter))];
}

function meshUrl(garmentId: string, variant: string | null): string {
  const base = `https://duble.data/mesh/${encodeURIComponent(garmentId)}.glb`;
  return variant ? `${base}?w=${encodeURIComponent(variant)}` : base;
}
