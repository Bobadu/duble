// components/QualityBars.tsx — the quality score out of 100, and the five things that made it.
//
// The breakdown is the whole point: a number on its own would be something to argue with, while "resolution
// 32/40, 1024 px" is something to check.
import type { Garment } from '../bridge/contract';
import { useTranslate } from '../i18n';

export function QualityBars({ garment }: { garment: Garment }) {
  const t = useTranslate();
  const score = garment.rozpiska;

  const parts: { label: string; value: number; max: number; note: string }[] = score
    ? [
        { label: t('quality.resolution'), value: score.rozdz, max: 40, note: `${Math.round(score.rozdzPx)} px` },
        { label: t('quality.mips'), value: score.mipy, max: 20, note: `${Math.round(score.udzialMipow * 100)} %` },
        { label: t('quality.variants'), value: score.warianty, max: 20, note: String(score.liczbaWariantow ?? garment.tekstur) },
        { label: t('quality.format'), value: score.format, max: 10, note: score.zlyFormat ? `${score.zlyFormat} BC1+α` : 'ok' },
        { label: t('quality.lod'), value: score.lod, max: 10, note: String(score.lody ?? garment.lody) },
      ]
    : [];

  return (
    <>
      <div className="q-total">
        <b>{Math.round(garment.punkty)}</b>
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
export function variantLabel(texture: { litera?: string; plik: string }): string {
  return texture.litera?.toUpperCase() || texture.plik;
}

/** A path with the archive separator written the way a person reads it, shortened from the left. */
export function texturePath(path: string | undefined, max = 60): string {
  if (!path) return '';
  const readable = path.replace('|', ' › ');
  return readable.length > max ? `…${readable.slice(-(max - 1))}` : readable;
}

/** Used by both cards that show a garment's textures; keeps their wording in one place. */
export function textureTitle(texture: { plik: string; w: number; h: number; format?: string; mipy: number }, extra?: string): string {
  const base = `${texture.plik}\n${texture.w}×${texture.h} ${texture.format ?? ''} · ${texture.mipy} mip`;
  return extra ? `${base} · ${extra}` : base;
}
