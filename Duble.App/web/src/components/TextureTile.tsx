// components/TextureTile.tsx — one texture: its thumbnail, which colour variant it is, and anything wrong
// with it (no mipmaps, BC1 carrying alpha).
import type { Texture } from '../bridge/contract';
import { textureTitle, variantLabel } from './QualityBars';

export function TextureTile({
  texture,
  /** True when the same graphic was found on the other side of the comparison. */
  paired,
  note,
  highlighted,
  onClick,
  onHover,
}: {
  texture: Texture;
  paired?: boolean;
  note?: string;
  highlighted?: boolean;
  onClick?: () => void;
  onHover?: (over: boolean) => void;
}) {

  const problems = [texture.mipmaps <= 1 && '!mip', texture.format === 'BC1' && texture.alpha > 0.02 && '!BC1α'].filter(Boolean);

  const classes = ['tex', paired ? 'has-pair' : '', highlighted ? 'para-hover' : ''].filter(Boolean).join(' ');

  return (
    <button
      type="button"
      className={classes}
      title={textureTitle(texture, note)}
      onClick={onClick}
      onMouseEnter={() => onHover?.(true)}
      onMouseLeave={() => onHover?.(false)}
    >
      <div className="tex-img">
        {texture.decoded && texture.sha ? (
          <img src={`https://duble.data/thumb/${texture.sha}.png`} alt="" loading="lazy" />
        ) : (
          <span className="tex-nopreview">{texture.format ?? '?'}</span>
        )}
        {paired && <span className="tex-dot" aria-hidden />}
      </div>

      <div className="tex-cap">
        <span className="tex-name">{variantLabel(texture)}</span>
        <span className="tex-meta">
          {texture.width}×{texture.height} {texture.format ?? ''}
          {problems.length > 0 && <span className="warn-txt"> {problems.join(' ')}</span>}
        </span>
      </div>
    </button>
  );
}
