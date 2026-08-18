// views/duplicates/GroupCard.tsx — one group of duplicates: the verdict, why, and the garments side by side.
import type { Garment, Group, Reason, Resolution } from '../../bridge/contract';
import { Badge, VerdictBadge } from '../../components/Badge';
import { Icon } from '../../components/Icon';
import { useTranslate, type Translate } from '../../i18n';

/** A reason travels as a code with parameters; the sentence is written here, in the reader's language. */
export function reasonText(t: Translate, reason: Reason | undefined): string {
  return reason ? t(`reason.${reason.code}`, reason.parameters) : '';
}

/** How a garment is named on screen: slot and number, the way the game names the file. */
export function garmentName(garment: Pick<Garment, 'slot' | 'number'>): string {
  return `${garment.slot}_${String(garment.number).padStart(3, '0')}`;
}

export function GroupCard({ group, onOpen }: { group: Group; onOpen: (id: string) => void }) {
  const t = useTranslate();
  const resolution = group.resolution;

  return (
    <div
      className={`card dup-card clickable${resolution.ignored ? ' ignored' : ''}`}
      tabIndex={0}
      role="button"
      onClick={() => onOpen(group.id)}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onOpen(group.id);
        }
      }}
    >
      <div className="dup-card-head">
        <VerdictBadge verdict={group.verdict} />
        <span className="dup-reason">{reasonText(t, group.reason)}</span>
        <span className="dup-meta">
          {resolution.ignored && <Badge tone="unknown">{t('dup.ignored')}</Badge>}
          {!resolution.isDefault && !resolution.ignored && <Badge tone="ok">{t('dup.custom')}</Badge>}
          {resolution.note && (
            <span className="faint" title={resolution.note}>
              <Icon name="file" />
            </span>
          )}
        </span>
      </div>

      <div className="dup-members">
        {group.members.map((member, index) => (
          <Member key={member.id} member={member} resolution={resolution} separator={index > 0} />
        ))}
      </div>
    </div>
  );
}

function Member({ member, resolution, separator }: { member: Garment; resolution: Resolution; separator: boolean }) {
  const t = useTranslate();

  const stays = resolution.winner === member.id && !resolution.ignored;
  const rejected = !resolution.ignored && resolution.rejected.includes(member.id);

  return (
    <>
      {separator && <div className="dup-eq">=</div>}
      <div className={`dup-member${stays ? ' stays' : ''}${rejected ? ' rejected' : ''}`}>
        <div className="thumbnail">
          {member.thumbnail ? (
            <img src={`https://duble.data/thumb/${member.thumbnail}.png`} alt="" loading="lazy" />
          ) : (
            <Icon name="cube" />
          )}
          {stays && (
            <span className="crown" title={t('dup.winner')}>
              ★
            </span>
          )}
        </div>

        <div className="dup-member-info">
          <div className="nm">
            {garmentName(member)}
            <sub>{member.suffix ?? ''}</sub>
          </div>
          <div className="src" title={member.source}>
            {member.source}
          </div>
          <div className="pts">
            <b>{Math.round(member.score)}</b> {t('dup.points')} ·{' '}
            <Badge tone={member.gen9 ? 'gen9' : 'legacy'}>
              {t(member.gen9 ? 'sources.formatGen9' : 'sources.formatLegacy')}
            </Badge>
          </div>
          <div className="st">
            {rejected ? (
              <span className="rej">
                <Icon name="x" />
                {t('dup.rejected')}
              </span>
            ) : stays ? (
              <span className="keep">
                <Icon name="check" />
                {t('dup.kept')}
              </span>
            ) : null}
          </div>
        </div>
      </div>
    </>
  );
}
