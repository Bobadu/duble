// components/QualityBars.tsx — the quality score out of 100, and the five things that made it.
//
// The breakdown is the whole point: a number on its own would be something to argue with, while "resolution
// 32/40, 1024 px" is something to check.
import type { Garment } from '../bridge/contract';
import { useTranslate } from '../i18n';

export function QualityBars({ garment }: { garment: Garment }) {
  const t = useTranslate();
  const score = garment.quality;

  const parts: { label: string; value: number; max: number; note: string }[] = score
    ? [
        { label: t('quality.resolution'), value: score.resolution, max: 40, note: `${Math.round(score.resolutionPx)} px` },
        { label: t('quality.mips'), value: score.mipmaps, max: 20, note: `${Math.round(score.mipmapShare * 100)} %` },
        { label: t('quality.variants'), value: score.variants, max: 20, note: String(score.variantCount ?? garment.textureCount) },
        { label: t('quality.format'), value: score.format, max: 10, note: score.wrongFormat ? `${score.wrongFormat} BC1+α` : 'ok' },
        { label: t('quality.lod'), value: score.lod, max: 10, note: String(score.lodLevels ?? garment.lods) },
      ]
    : [];

  return (
    <>
      <div className="q-total">
        <b>{Math.round(garment.score)}</b>
        <span>/100 {t('quality.total')}</span>
      </div>
      {parts.map((part) => (
        <Bar key={part.label} {...part} />
      ))}
    </>
  );
}

function Bar({ label, value, max, note }: { label: string; value: number; max: number; note: string }) {
  const clamped = Math.max(0, Math.min(max, value || 0));

  return (
    <div className="q-row">
      <span className="q-lab">{label}</span>
      <div className="q-bar">
        <i style={{ width: `${(clamped / max) * 100}%` }} />
      </div>
      <span className="q-val">
        {Math.round(clamped)}/{max}
      </span>
      <span className="q-desc faint">{note}</span>
    </div>
  );
}

/** The letter of the colour variant, worked out by the engine; without one, the file name has to do. */
export function variantLabel(texture: { variant?: string; file: string }): string {
  return texture.variant?.toUpperCase() || texture.file;
}

/** A path with the archive separator written the way a person reads it, shortened from the left. */
export function texturePath(path: string | undefined, max = 60): string {
  if (!path) return '';
  const readable = path.replace('|', ' › ');
  return readable.length > max ? `…${readable.slice(-(max - 1))}` : readable;
}

/** Used by both cards that show a garment's textures; keeps their wording in one place. */
export function textureTitle(texture: { file: string; width: number; height: number; format?: string; mipmaps: number }, extra?: string): string {
  const base = `${texture.file}\n${texture.width}×${texture.height} ${texture.format ?? ''} · ${texture.mipmaps} mip`;
  return extra ? `${base} · ${extra}` : base;
}
