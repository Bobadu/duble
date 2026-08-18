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
  const title = [name, garment.sufiks, garment.zrodlo, garment.kontener, garment.grupa && t(`verdict.${garment.grupa}`)]
    .filter(Boolean)
    .join(' · ');

  return (
    <button
      type="button"
      className={garment.grupa ? 'cat-tile in-group' : 'cat-tile'}
      title={title}
      onClick={() => onOpen(garment.id)}
    >
      <div className="thumb">
        {garment.thumb ? (
          <img src={`https://duble.data/thumb/${garment.thumb}.png`} alt="" loading="lazy" />
        ) : (
          <Icon name="cube" />
        )}
        {garment.grupa && (
          <span className={`vico ${verdictClassName(garment.grupa)}`} title={t(`verdict.${garment.grupa}`)}>
            <Icon name={verdictIcon(garment.grupa)} />
          </span>
        )}
        {garment.wArchiwum && (
          <span className="arch" title={t('group.inArchive')}>
            <Icon name="archive" />
          </span>
        )}
      </div>

      <div className="nm">
        {name}
        <sub>{garment.sufiks ?? ''}</sub>
      </div>
      <div className="src" title={garment.zrodlo}>
        {garment.zrodlo}
      </div>

      <div className="tile-badges">
        <span className={garment.gen9 ? 'badge gen9' : 'badge legacy'}>
          {t(garment.gen9 ? 'sources.formatGen9' : 'sources.formatLegacy')}
        </span>
        <span className="faint">{t('dup.textures', { n: garment.tekstur })}</span>
        {garment.bezMipow && (
          <span className="badge err" title={t('catalog.problemMips')}>
            !mip
          </span>
        )}
        {garment.bc1Alfa && (
          <span className="badge err" title={t('catalog.problemBc1')}>
            BC1α
          </span>
        )}
      </div>
    </button>
  );
}
