// views/catalog/GarmentTile.tsx — one garment in the catalog grid: its thumbnail, what it is, and anything
// worth fixing about it.
import type { CatalogGarment } from '../../bridge/contract';
import { verdictClassName, verdictIcon } from '../../components/Badge';
import { Icon } from '../../components/Icon';
import { useTranslate } from '../../i18n';
import { garmentName } from '../duplicates/GroupCard';

export function GarmentTile({ garment, onOpen }: { garment: CatalogGarment; onOpen: (id: string) => void }) {
  const t = useTranslate();

  const name = garmentName(garment);
  const title = [name, garment.suffix, garment.source, garment.container, garment.verdict && t(`verdict.${garment.verdict}`)]
    .filter(Boolean)
    .join(' · ');

  return (
    <button
      type="button"
      className={garment.verdict ? 'cat-tile in-group' : 'cat-tile'}
      title={title}
      onClick={() => onOpen(garment.id)}
    >
      <div className="thumbnail">
        {garment.thumbnail ? (
          <img src={`https://duble.data/thumb/${garment.thumbnail}.png`} alt="" loading="lazy" />
        ) : (
          <Icon name="cube" />
        )}
        {garment.verdict && (
          <span className={`vico ${verdictClassName(garment.verdict)}`} title={t(`verdict.${garment.verdict}`)}>
            <Icon name={verdictIcon(garment.verdict)} />
          </span>
        )}
        {garment.inArchive && (
          <span className="arch" title={t('group.inArchive')}>
            <Icon name="archive" />
          </span>
        )}
      </div>

      <div className="nm">
        {name}
        <sub>{garment.suffix ?? ''}</sub>
      </div>
      <div className="src" title={garment.source}>
        {garment.source}
      </div>

      <div className="tile-badges">
        <span className={garment.gen9 ? 'badge gen9' : 'badge legacy'}>
          {t(garment.gen9 ? 'sources.formatGen9' : 'sources.formatLegacy')}
        </span>
        <span className="faint">{t('dup.textures', { n: garment.textureCount })}</span>
        {garment.noMipmaps && (
          <span className="badge err" title={t('catalog.problemMips')}>
            !mip
          </span>
        )}
        {garment.bc1WithAlpha && (
          <span className="badge err" title={t('catalog.problemBc1')}>
            BC1α
          </span>
        )}
      </div>
    </button>
  );
}
