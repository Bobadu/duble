// views/group/TextureWipe.tsx — two textures compared under a movable split: A underneath, B on top, clipped.
//
// This is how "the same graphic" is checked by eye. With three or more models each side gets its own list, so
// any pair can be put against any other; with one texture it is simply a viewer.
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { Modal } from '../../components/Modal';
import { Segmented } from '../../components/Segmented';
import { Select } from '../../components/Select';
import { useTranslate } from '../../i18n';

/** One texture as the viewer needs it: enough to load it and to caption it. */
export interface WipeSide {
  sha: string;
  name: string;
  variant: string;
  file: string;
  width: number;
  height: number;
  format?: string;
  mipmaps: number;
}

type Mode = 'both' | 'a' | 'b';

export function TextureWipe({ sides, first = 0, second = null, onClose }: { sides: readonly WipeSide[]; first?: number; second?: number | null; onClose: () => void }) {
  const t = useTranslate();

  const [a, setA] = useState(() => clamp(first, sides.length));
  const [b, setB] = useState<number | null>(() => {
    if (second == null) return null;
    const chosen = clamp(second, sides.length);
    return chosen === clamp(first, sides.length) ? sides.findIndex((_, index) => index !== chosen) : chosen;
  });

  const [mode, setMode] = useState<Mode>('both');
  const [position, setPosition] = useState(50);
  const [zoom, setZoom] = useState<'fit' | '1'>('fit');
  const [failed, setFailed] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const comparing = b != null && b >= 0;
  const left = sides[a];
  const right = comparing ? sides[b] : undefined;

  const stage = useRef<HTMLDivElement>(null);
  const images = useRef<HTMLDivElement>(null);
  const imageA = useRef<HTMLImageElement>(null);
  const imageB = useRef<HTMLImageElement>(null);
  const [size, setSize] = useState({ width: 256, height: 256 });

  /** Fits the pair to the stage, or shows them pixel for pixel. */
  const layout = useCallback(() => {
    const box = stage.current;
    if (!box) return;

    const naturalWidth = Math.max(imageA.current?.naturalWidth ?? 0, imageB.current?.naturalWidth ?? 0) || 256;
    const naturalHeight = Math.max(imageA.current?.naturalHeight ?? 0, imageB.current?.naturalHeight ?? 0) || 256;

    const room = { width: Math.max(64, box.clientWidth - 24), height: Math.max(64, box.clientHeight - 24) };
    const scale = zoom === 'fit' ? Math.min(room.width / naturalWidth, room.height / naturalHeight, 8) : 1;

    setSize({ width: Math.round(naturalWidth * scale), height: Math.round(naturalHeight * scale) });
  }, [zoom]);

  useLayoutEffect(layout, [layout, a, b]);

  useEffect(() => {
    window.addEventListener('resize', layout);
    return () => window.removeEventListener('resize', layout);
  }, [layout]);

  // dragging the split, and the arrow keys for the last few per cent
  useEffect(() => {
    if (!comparing) return;

    const box = images.current;
    if (!box) return;

    let dragging = false;
    const moveTo = (clientX: number) => {
      const bounds = box.getBoundingClientRect();
      setPosition(Math.max(0, Math.min(100, ((clientX - bounds.left) / bounds.width) * 100)));
      setMode('both');
    };

    const down = (event: MouseEvent) => {
      event.preventDefault();
      dragging = true;
      moveTo(event.clientX);
      stage.current?.focus();
    };
    const move = (event: MouseEvent) => dragging && moveTo(event.clientX);
    const up = () => {
      dragging = false;
    };

    box.addEventListener('mousedown', down);
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
    return () => {
      box.removeEventListener('mousedown', down);
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
    };
  }, [comparing]);

  if (!left) return null;

  const clip = mode === 'a' ? 'inset(0 0 0 100%)' : mode === 'b' ? 'inset(0 0 0 0)' : `inset(0 0 0 ${position}%)`;
  const identical = comparing && right && left.sha === right.sha;

  const onLoad = () => {
    setLoading(false);
    layout();
  };

  return (
    <Modal
      title={t(comparing ? 'wipe.title' : 'wipe.titleOne')}
      wide
      onClose={onClose}
      footer={
        <Button variant="primary" onClick={onClose}>
          {t('common.close')}
        </Button>
      }
    >
      <div className="wipe">
        <div className="wipe-bar">
          <div className="wipe-side">
            <span className="cap a">A</span>
            <div className="who">
              <SideChooser sides={sides} value={a} onChange={setA} label={t('wipe.chooseA')} />
              <span className="meta">{caption(left, t)}</span>
            </div>
          </div>

          {comparing && right && (
            <>
              {identical && <span className="wipe-eq badge ok">{t('wipe.identical')}</span>}
              <div className="wipe-side right">
                <div className="who">
                  <SideChooser sides={sides} value={b} onChange={setB} label={t('wipe.chooseB')} />
                  <span className="meta">{caption(right, t)}</span>
                </div>
                <span className="cap b">B</span>
              </div>
            </>
          )}
        </div>

        <div ref={stage} className="wipe-stage checker" tabIndex={0} onKeyDown={(event) => {
          const step = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0;
          if (!step || !comparing) return;
          event.preventDefault();
          setPosition((was) => Math.max(0, Math.min(100, was + step * (event.shiftKey ? 10 : 2))));
          setMode('both');
        }}>
          <div
            ref={images}
            className={size.width >= 2 * (left.width || 1) ? 'wipe-imgs pixel' : 'wipe-imgs'}
            style={{ width: size.width, height: size.height }}
          >
            <img ref={imageA} className="ia" alt="A" draggable={false} src={textureUrl(left.sha)} onLoad={onLoad} onError={() => setFailed('A')} />
            {comparing && right && (
              <img
                ref={imageB}
                className="ib"
                alt="B"
                draggable={false}
                src={textureUrl(right.sha)}
                style={{ clipPath: clip }}
                onLoad={onLoad}
                onError={() => setFailed('B')}
              />
            )}
            {comparing && mode === 'both' && <div className="wipe-line" style={{ left: `${position}%` }} />}
          </div>

          {(loading || failed) && (
            <div className={failed ? 'wipe-loading err' : 'wipe-loading'}>
              <Icon name={failed ? 'warn' : 'refresh'} /> {failed ? `${t('wipe.noPreview')} (${failed})` : t('wipe.loading')}
            </div>
          )}
        </div>

        <div className="wipe-foot">
          {comparing ? (
            <input
              type="range"
              className="wipe-range"
              min={0}
              max={100}
              value={Math.round(position)}
              aria-label={t('wipe.both')}
              onChange={(event) => {
                setPosition(Number(event.target.value));
                setMode('both');
              }}
            />
          ) : (
            <span className="grow" />
          )}

          {comparing && (
            <Segmented
              value={mode}
              onChange={setMode}
              segments={[
                { value: 'both', label: t('wipe.both') },
                { value: 'a', label: t('wipe.onlyA') },
                { value: 'b', label: t('wipe.onlyB') },
              ]}
            />
          )}

          <Segmented
            value={zoom}
            onChange={setZoom}
            segments={[
              { value: 'fit', label: t('wipe.zoomFit') },
              { value: '1', label: t('wipe.zoom1') },
            ]}
          />
        </div>

        {comparing && <p className="help">{t('wipe.hint')}</p>}
      </div>
    </Modal>
  );
}

/** With three or more models a side is a choice; with two, a caption. */
function SideChooser({
  sides,
  value,
  onChange,
  label,
}: {
  sides: readonly WipeSide[];
  value: number;
  onChange: (index: number) => void;
  label: string;
}) {
  const side = sides[value];
  if (!side) return null;
  if (sides.length <= 2) return <span className="nm">{sideLabel(side)}</span>;

  return (
    <Select
      label={label}
      value={String(value)}
      onChange={(chosen) => onChange(Number(chosen))}
      options={sides.map((each, index) => ({ value: String(index), label: sideLabel(each) }))}
    />
  );
}

function sideLabel(side: WipeSide): string {
  return side.variant ? `${side.name} · ${side.variant}` : side.name;
}

function caption(side: WipeSide, t: (key: string) => string): string {
  const size = [`${side.width}×${side.height}`, side.format ?? ''].filter(Boolean).join(' ');
  return side.mipmaps <= 1 ? `${size} · ${t('wipe.noMips')}` : size;
}

function textureUrl(sha: string): string {
  return `https://duble.data/tex/${encodeURIComponent(sha)}.png`;
}

function clamp(index: number, length: number): number {
  return Math.max(0, Math.min(length - 1, index));
}
