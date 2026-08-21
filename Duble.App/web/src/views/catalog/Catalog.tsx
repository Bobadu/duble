// views/catalog/Catalog.tsx — every indexed garment as a grid of thumbnails, with the filters to find one.
//
// The whole list comes over in a single command — at 5 000 garments that is well under a megabyte — and the
// grid draws only the rows on screen.
import { useApp } from '../../app/AppState';
import { navigate } from '../../app/router';
import { ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { Icon } from '../../components/Icon';
import { SearchField } from '../../components/SearchField';
import { Segmented, type Segment } from '../../components/Segmented';
import { Select } from '../../components/Select';
import { Switch } from '../../components/Switch';
import { VirtualGrid } from '../../components/VirtualGrid';
import { useTranslate } from '../../i18n';
import { GarmentTile } from './GarmentTile';
import { useCatalogFilters } from './useCatalogFilters';

/** The tiles are a fixed size, which is what lets the grid work out what is on screen without measuring them. */
const TILE = { rowHeight: 200, minColumnWidth: 150, gap: 12 };

/** The order slots are listed in: the way a body is dressed, rather than alphabetically. */
const SLOT_ORDER = [
  'jbib', 'uppr', 'lowr', 'feet', 'accs', 'task', 'decl', 'teef', 'hand', 'hair', 'berd',
  'p_head', 'p_eyes', 'p_ears', 'p_mouth', 'p_lhand', 'p_rhand', 'p_lwrist', 'p_rwrist', 'p_hip',
];

export function Catalog() {
  const t = useTranslate();
  const { project } = useApp();
  const { filters, set, clear, any } = useCatalogFilters();

  const catalog = useCommand('catalog.list', filters, {
    enabled: !!project,
    reloadOn: ['project.changed', 'compare.done', 'groups.changed'],
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

  if (!catalog.data) {
    return (
      <>
        <Head search={{ value: filters.search, onChange: (text) => set('search', text) }} />
        {catalog.error && errorCodeOf(catalog.error) !== ErrorCode.NoProject && (
          <EmptyState icon="warn" title={t('common.error')} hint={messageOf(catalog.error)} />
        )}
      </>
    );
  }

  const { total, textures, shown, filters: buckets, garments } = catalog.data;

  const counted = t('catalog.count', { n: total, t: textures });
  const summary = shown === total ? counted : `${counted} · ${t('catalog.shown', { n: shown, m: total })}`;

  const formats: Segment<string>[] = [{ value: '', label: t('dup.allVerdicts'), count: total }];
  if (buckets.formats.legacy)
    formats.push({ value: 'legacy', label: t('sources.formatLegacy'), count: buckets.formats.legacy, icon: <i className="dot legacy" /> });
  if (buckets.formats.gen9)
    formats.push({ value: 'gen9', label: t('sources.formatGen9'), count: buckets.formats.gen9, icon: <i className="dot gen9" /> });

  const slots = [...buckets.slots].sort((a, b) => SLOT_ORDER.indexOf(a.slot) - SLOT_ORDER.indexOf(b.slot));

  return (
    <>
      <Head summary={summary} search={{ value: filters.search, onChange: (text) => set('search', text) }} />

      <div className="filterbar cat-filters">
        {/* one format is not a choice: with only Legacy or only Enhanced there is nothing to pick between */}
        {formats.length > 2 && (
          <Segmented
            segments={formats}
            value={filters.formats[0] ?? ''}
            onChange={(format) => set('formats', format ? [format] : [])}
          />
        )}

        {buckets.sources.length > 1 && (
          <Select
            label={t('dup.sourcesFilter')}
            value={filters.sources[0] ?? ''}
            onChange={(source) => set('sources', source ? [source] : [])}
            options={[
              { value: '', label: t('dup.sourceAll') },
              ...buckets.sources.map((source) => ({ value: source.id, label: `${source.name} (${source.n})` })),
            ]}
          />
        )}

        {slots.length > 1 && (
          <Select
            label={t('dup.slots')}
            value={filters.slots[0] ?? ''}
            onChange={(slot) => set('slots', slot ? [slot] : [])}
            options={[
              { value: '', label: t('dup.slotAll') },
              ...slots.map((slot) => ({ value: slot.slot, label: `${t(`slot.${slot.slot}`)} (${slot.n})` })),
            ]}
          />
        )}

        <div className="end">
          <Switch on={filters.problems} label={t('catalog.problems')} onChange={(on) => set('problems', on)} />
          <Switch on={filters.inGroup} label={t('catalog.inGroups')} onChange={(on) => set('inGroup', on)} />

          <Button
            variant="ghost"
            icon="x"
            className="clear"
            title={t('dup.clearFilters')}
            style={any ? undefined : { visibility: 'hidden' }}
            onClick={clear}
          />
        </div>
      </div>

      <VirtualGrid
        items={garments}
        {...TILE}
        scrollKey="catalog.scroll"
        renderItem={(garment) => (
          <GarmentTile key={garment.id} garment={garment} onOpen={(id) => navigate('catalog', id)} />
        )}
        empty={
          <div className="empty">
            <Icon name={any ? 'search' : 'catalog'} />
            <h3>{t(any ? 'catalog.emptyFiltered' : 'catalog.empty')}</h3>
          </div>
        }
      />
    </>
  );
}

function Head({
  summary,
  search,
}: {
  summary?: string;
  search?: { value: string; onChange: (value: string) => void };
}) {
  const t = useTranslate();

  return (
    <div className="view-head">
      <div className="titles">
        <h1>{t('catalog.title')}</h1>
        <p className="sub">{summary ?? t('catalog.subtitle')}</p>
      </div>
      {search && (
        <div className="actions">
          <SearchField
            value={search.value}
            onChange={search.onChange}
            placeholder={t('dup.searchPlaceholder')}
            label={t('dup.search')}
          />
        </div>
      )}
    </div>
  );
}
