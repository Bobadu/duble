// views/duplicates/GroupFilters.tsx — the one bar above the list: verdict, slot, source, and a way back to
// everything. Searching lives in the heading, as it does in the catalog.
import type { SlotFilter, SourceFilter, Verdict } from '../../bridge/contract';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { Segmented, type Segment } from '../../components/Segmented';
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
  const chosenVerdict = filters.verdicts.length === 1 ? (filters.verdicts[0] as Verdict) : '';

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
        className="seg-verdict"
        segments={segments}
        value={chosenVerdict}
        onChange={(verdict) => set('verdicts', verdict ? [verdict] : [])}
      />

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

      {sources.length > 1 && (
        <Select
          label={t('dup.sourcesFilter')}
          value={filters.sources[0] ?? ''}
          onChange={(source) => set('sources', source ? [source] : [])}
          options={[
            { value: '', label: t('dup.sourceAll') },
            ...sources.map((source) => ({ value: source.id, label: `${source.name} (${source.n})` })),
          ]}
        />
      )}

      <div className="end">
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
  );
}
