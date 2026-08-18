// views/duplicates/GroupFilters.tsx — the one bar above the list: verdict, slot, source, search, and a way
// back to everything.
import type { SlotFilter, SourceFilter, Verdict } from '../../bridge/contract';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { Segmented, type Segment } from '../../components/Segmented';
import { SearchField } from '../../components/SearchField';
import { Select } from '../../components/Select';
import { verdictClassName, verdictIcon } from '../../components/Badge';
import { useTranslate } from '../../i18n';
import type { GroupFiltersState } from './useGroupFilters';

const VERDICTS: Verdict[] = ['duplicate', 'superset', 'needsReview', 'retexture'];

export interface VerdictCounts {
  all: number;
  duplicate: number;
  superset: number;
  needsReview: number;
  retexture: number;
}

export function GroupFilters({
  state,
  counts,
  slots,
  sources,
}: {
  state: GroupFiltersState;
  counts: VerdictCounts;
  slots: SlotFilter[];
  sources: SourceFilter[];
}) {
  const t = useTranslate();
  const { filters, set, clear, any } = state;

  // one verdict at a time: the segmented control is a choice, not a set of checkboxes
  const chosenVerdict = filters.werdykty.length === 1 ? (filters.werdykty[0] as Verdict) : '';

  const segments: Segment<Verdict | ''>[] = [
    { value: '', label: t('dup.allVerdicts'), count: counts.all },
    ...VERDICTS.map((verdict) => ({
      value: verdict,
      label: t(`verdict.${verdict}`),
      count: counts[verdict],
      className: verdictClassName(verdict),
      icon: <Icon name={verdictIcon(verdict)} />,
    })),
  ];

  return (
    <div className="filterbar">
      <Segmented
        className="seg-werdykt"
        segments={segments}
        value={chosenVerdict}
        onChange={(verdict) => set('werdykty', verdict ? [verdict] : [])}
      />

      {slots.length > 1 && (
        <Select
          label={t('dup.slots')}
          value={filters.sloty[0] ?? ''}
          onChange={(slot) => set('sloty', slot ? [slot] : [])}
          options={[
            { value: '', label: t('dup.slotAll') },
            ...slots.map((slot) => ({ value: slot.typ, label: `${t(`slot.${slot.typ}`)} (${slot.n})` })),
          ]}
        />
      )}

      {sources.length > 1 && (
        <Select
          label={t('dup.sourcesFilter')}
          value={filters.zrodla[0] ?? ''}
          onChange={(source) => set('zrodla', source ? [source] : [])}
          options={[
            { value: '', label: t('dup.sourceAll') },
            ...sources.map((source) => ({ value: source.id, label: `${source.nazwa} (${source.n})` })),
          ]}
        />
      )}

      <SearchField
        value={filters.szukaj}
        onChange={(text) => set('szukaj', text)}
        placeholder={t('dup.searchPlaceholder')}
        label={t('dup.search')}
      />

      <Button
        variant="ghost"
        icon="x"
        className="clear"
        title={t('dup.clearFilters')}
        style={any ? undefined : { visibility: 'hidden' }}
        onClick={clear}
      />
    </div>
  );
}
