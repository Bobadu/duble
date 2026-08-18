// views/group/Group.tsx — the card of one group: everything known about the candidates, and the decision.
//
// Two tabs: the textures and numbers side by side, or the models themselves in 3D. The decision is saved as it
// is made — there is no "save" here, which is why the note is written back after a pause rather than on every
// keystroke.
import { useEffect, useState } from 'react';
import { navigate, routeToHash } from '../../app/router';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { CommandArgs } from '../../bridge/contract';
import { useCommand } from '../../bridge/hooks';
import { VerdictBadge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { Icon } from '../../components/Icon';
import { useToast } from '../../components/Toast';
import { useTranslate } from '../../i18n';
import { garmentName, reasonText } from '../duplicates/GroupCard';
import { GroupColumns } from './GroupColumns';
import { LazyModelTab } from './LazyModelTab';
import { useModelTab } from './useModelTab';

export function Group({ id }: { id: string }) {
  const t = useTranslate();
  const toast = useToast();
  const [tab, setTab] = useModelTab('group.tab');

  const group = useCommand('groups.get', { id }, { reloadOn: ['groups.changed', 'compare.done'] });

  // a group can stop existing while it is open — after applying, or after a re-index
  useEffect(() => {
    if (errorCodeOf(group.error) === ErrorCode.NotFound) navigate('duplicates');
  }, [group.error]);

  const decide = async (change: Omit<CommandArgs<'groups.decide'>, 'id'>, quiet = false) => {
    try {
      await bridge.call('groups.decide', { id, ...change });
      if (!quiet) toast.ok(t('decision.saved'), { duration: 1500 });
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  const reset = async () => {
    try {
      await bridge.call('groups.reset', { id });
      toast.ok(t('decision.saved'), { duration: 1500 });
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  if (group.error && errorCodeOf(group.error) !== ErrorCode.NotFound)
    return <EmptyState icon="warn" title={t('common.error')} hint={messageOf(group.error)} />;

  if (!group.data) return null;

  const { grupa } = group.data;
  const resolution = grupa.rozstrzygniecie;
  const slot = grupa.czlonkowie[0]?.typ;

  return (
    <>
      <div className="view-head group-head">
        <div className="titles">
          <a
            className="back-link"
            href={routeToHash('duplicates')}
            onClick={(event) => {
              event.preventDefault();
              navigate('duplicates');
            }}
          >
            <Icon name="chevron" className="rot90" />
            {t('group.back')}
          </a>

          <h1 className="group-h1">
            {grupa.czlonkowie.map((member, index) => (
              <span key={member.id}>
                {index > 0 && <span className="sep">·</span>}
                <span className="nm">
                  {garmentName(member)}
                  <sub>{member.sufiks ?? ''}</sub>
                </span>
              </span>
            ))}
          </h1>

          <div className="group-sub">
            <VerdictBadge verdict={grupa.werdykt} />
            <span className="group-powod">{reasonText(t, grupa.powod)}</span>
            {slot && <span className="faint">· {t(`slot.${slot}`)}</span>}
          </div>
        </div>

        <div className="actions">
          <Note value={resolution.notatka ?? ''} onChange={(notatka) => void decide({ notatka }, true)} />

          <Button
            icon={resolution.ignoruj ? 'ok' : 'x'}
            aria-pressed={resolution.ignoruj}
            onClick={() => void decide({ ignoruj: !resolution.ignoruj })}
          >
            {t(resolution.ignoruj ? 'group.isDuplicate' : 'group.notDuplicate')}
          </Button>

          <Button
            icon="refresh"
            title={t('group.reset')}
            aria-label={t('group.reset')}
            style={resolution.domyslna ? { visibility: 'hidden' } : undefined}
            onClick={reset}
          />
        </div>
      </div>

      <div className="group-bar">
        {resolution.ignoruj && (
          <div className="banner">
            <Icon name="info" />
            <span>{t('group.ignoredBanner')}</span>
          </div>
        )}

        <div className="tabs">
          <button type="button" className={tab === '2d' ? 'tab on' : 'tab'} onClick={() => setTab('2d')}>
            <Icon name="catalog" />
            {t('group.tab2d')}
          </button>
          <button type="button" className={tab === '3d' ? 'tab on' : 'tab'} onClick={() => setTab('3d')}>
            <Icon name="cube" />
            {t('group.tab3d')}
          </button>
        </div>
      </div>

      {tab === '3d' ? (
        <LazyModelTab members={grupa.czlonkowie} />
      ) : (
        <GroupColumns group={grupa} onDecide={(change) => void decide(change)} />
      )}
    </>
  );
}

/** The user's own words about a group. Written back after a pause: every keystroke would be a disk write. */
function Note({ value, onChange }: { value: string; onChange: (note: string) => void }) {
  const t = useTranslate();
  const [typed, setTyped] = useState(value);

  useEffect(() => setTyped(value), [value]);

  useEffect(() => {
    if (typed === value) return;
    const timer = setTimeout(() => onChange(typed), 600);
    return () => clearTimeout(timer);
  }, [typed, value, onChange]);

  return (
    <div className="filtr-szukaj note">
      <span className="ico-wrap">
        <Icon name="file" />
      </span>
      <input
        className="input"
        placeholder={t('group.notePlaceholder')}
        aria-label={t('group.note')}
        value={typed}
        onChange={(event) => setTyped(event.target.value)}
      />
    </div>
  );
}
