// views/duplicates/Duplicates.tsx — the screen the whole program is for: the groups the comparison found, the
// decision that hangs off each one, and what applying them all would move.
//
// The list comes from one command and is asked for again whenever the host says something changed. Nothing
// here keeps a copy of the groups: the engine is the only thing that knows them.
import { useState, type ReactNode } from 'react';
import { useApp } from '../../app/AppState';
import { navigate } from '../../app/router';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { Switch } from '../../components/Switch';
import { useToast } from '../../components/Toast';
import { useI18n, useTranslate } from '../../i18n';
import { ApplyDialog } from '../apply/ApplyDialog';
import { DecisionBar } from './DecisionBar';
import { GroupCard } from './GroupCard';
import { GroupFilters } from './GroupFilters';
import { useGroupFilters } from './useGroupFilters';

export function Duplicates() {
  const t = useTranslate();
  const { formatNumber } = useI18n();
  const { project, busy } = useApp();
  const toast = useToast();
  const filters = useGroupFilters();
  const [applying, setApplying] = useState(false);

  const groups = useCommand('groups.list', filters.filters, {
    enabled: !!project,
    reloadOn: ['compare.done', 'groups.changed', 'apply.done', 'undo.done', 'project.changed'],
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

  const compare = async () => {
    try {
      await bridge.call('compare.run');
    } catch (failure) {
      toast.warn(errorCodeOf(failure) === ErrorCode.Busy ? t('sources.busy') : messageOf(failure));
    }
  };

  // the very first render of the screen, before any answer has arrived
  if (!groups.data) {
    return (
      <>
        <Head onCompare={compare} busy={busy} />
        {groups.error && errorCodeOf(groups.error) !== ErrorCode.NoProject && (
          <EmptyState icon="warn" title={t('common.error')} hint={messageOf(groups.error)} />
        )}
      </>
    );
  }

  const { podsumowanie: summary, filtry, grupy } = groups.data;
  const compared = summary.grup !== undefined;

  return (
    <>
      <Head
        onCompare={compare}
        busy={busy}
        summary={
          compared
            ? t('dup.summary', {
                grup: formatNumber(summary.grup),
                duplikat: formatNumber(summary.duplikat),
                nadzbior: formatNumber(summary.nadzbior),
                wglad: formatNumber(summary.wglad),
                przemalowanie: formatNumber(summary.przemalowanie),
              })
            : undefined
        }
        ignored={
          compared ? (
            <IgnoredSwitch
              on={filters.filters.zignorowane}
              count={summary.zignorowane}
              onChange={(on) => filters.set('zignorowane', on)}
            />
          ) : undefined
        }
      />

      {compared && (
        <GroupFilters
          state={filters}
          counts={{
            all: summary.grup ?? 0,
            duplicate: summary.duplikat,
            superset: summary.nadzbior,
            needsReview: summary.wglad,
            retexture: summary.przemalowanie,
          }}
          slots={filtry.sloty}
          sources={filtry.zrodla}
        />
      )}

      {/* the list is the part that scrolls, so that the decision bar stays where the eye expects it */}
      <div className="dup-list">
        {!compared ? (
          <EmptyState icon="duplicates" title={t('dup.noResult')} hint={t('dup.noResultHint')}>
            <Button variant="primary" icon="play" disabled={busy} onClick={compare}>
              {t('dup.compareNow')}
            </Button>
          </EmptyState>
        ) : grupy.length === 0 ? (
          <NothingFound filtered={filters.any} indexed={project.pozycje > 0} />
        ) : (
          <div className="dup-grupy">
            {grupy.map((group) => (
              <GroupCard key={group.id} group={group} onOpen={(id) => navigate('duplicates', id)} />
            ))}
          </div>
        )}
      </div>

      {compared && <DecisionBar plan={summary.doOdrzucenia} busy={busy} onApply={() => setApplying(true)} />}

      {applying && <ApplyDialog onClose={() => setApplying(false)} />}
    </>
  );
}

function Head({
  summary,
  onCompare,
  busy,
  ignored,
}: {
  summary?: string;
  onCompare?: () => void;
  busy?: boolean;
  ignored?: ReactNode;
}) {
  const t = useTranslate();

  return (
    <div className="view-head">
      <div className="titles">
        <h1>{t('dup.title')}</h1>
        <p className="sub">{summary ?? t('dup.subtitle')}</p>
      </div>
      {onCompare && (
        <div className="actions">
          {ignored}
          <Button icon="refresh" disabled={busy} onClick={onCompare}>
            {t('dup.recompare')}
          </Button>
        </div>
      )}
    </div>
  );
}

function IgnoredSwitch({ on, count, onChange }: { on: boolean; count: number; onChange: (on: boolean) => void }) {
  const t = useTranslate();
  const { formatNumber } = useI18n();

  return (
    <Switch
      on={on}
      label={t('dup.showIgnored')}
      count={count}
      title={t('dup.showIgnoredHint', { n: formatNumber(count) })}
      onChange={onChange}
    />
  );
}

function NothingFound({ filtered, indexed }: { filtered: boolean; indexed: boolean }) {
  const t = useTranslate();

  // nothing in the catalog means nothing has been indexed; "no duplicates" would then be a lie
  if (!filtered && !indexed) return <EmptyState icon="duplicates" title={t('dup.notIndexed')} hint={t('dup.notIndexedHint')} />;

  return <EmptyState icon={filtered ? 'search' : 'ok'} title={t(filtered ? 'dup.emptyFiltered' : 'dup.empty')} />;
}
