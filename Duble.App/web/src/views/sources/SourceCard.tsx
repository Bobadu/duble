// views/sources/SourceCard.tsx — one source: where it is, what was found in it, and what can be done to it.
import type { JobEvent, Source } from '../../bridge/contract';
import { Icon, type IconName } from '../../components/Icon';
import { MenuButton, type MenuItem } from '../../components/Menu';
import { Progress } from '../../components/Progress';
import { Switch } from '../../components/Switch';
import { shortenPath, useI18n, useTranslate } from '../../i18n';

const TYPE_ICONS: Record<string, IconName> = { folder: 'folder', rpf: 'archive', fivem: 'server' };

/** How many slots fit on the one line before the rest go into a tooltip. */
const SLOTS_SHOWN = 6;

export function SourceCard({
  source,
  job,
  actions,
  onToggle,
}: {
  source: Source;
  /** The running job, when it is this source being read. */
  job?: JobEvent;
  actions: readonly MenuItem[];
  onToggle: (enabled: boolean) => void;
}) {
  const t = useTranslate();
  const { formatNumber, formatDate } = useI18n();

  const format = source.format ?? 'unknown';
  const formatLabel = {
    gen9: t('sources.formatGen9'),
    legacy: t('sources.formatLegacy'),
    mixed: t('sources.formatMixed'),
    unknown: t('sources.formatUnknown'),
  }[format];

  const kind =
    { folder: t('sources.typeFolder'), rpf: t('sources.typeRpf'), fivem: t('sources.typeFivem') }[source.kind] ?? source.kind;

  // the busiest slots first: this is a glance at what the pack holds, not a full listing
  const slots = Object.entries(source.perSlot).sort(([, a], [, b]) => b - a);
  const shown = slots.slice(0, SLOTS_SHOWN);
  const rest = slots.slice(SLOTS_SHOWN);

  const className = ['card', 'src-card', source.enabled ? '' : 'disabled', source.exists ? '' : 'missing']
    .filter(Boolean)
    .join(' ');

  return (
    <div className={className}>
      <div className="card-body">
        <div className="top">
          <div className="ico-box">
            <Icon name={TYPE_ICONS[source.kind] ?? 'folder'} />
          </div>
          <div className="info">
            <div className="name" title={source.name}>
              {source.name}
            </div>
            <div className="path mono" title={source.path}>
              {shortenPath(source.path, 32)}
            </div>
          </div>
          <MenuButton items={actions} title={t('common.more')} />
        </div>

        <div className="pills">
          {source.format && (
            <span className={`pill fmt ${format}`}>
              <i className="dot" />
              {formatLabel}
            </span>
          )}
          <span className="pill">{kind}</span>
          {source.kind === 'rpf' && (
            <span className="pill" title={t('sources.archiveOnly')}>
              {t('sources.readOnly')}
            </span>
          )}
        </div>

        {!source.exists && (
          <div className="missing-text">
            <Icon name="warn" /> {t('sources.missing')}
          </div>
        )}

        <div className="stats">
          <b>{formatNumber(source.garments)}</b> {t('sources.items')}
          <i>·</i>
          <b>{formatNumber(source.textures)}</b> {t('sources.textures')}
          {source.kind !== 'rpf' && source.inArchives > 0 && (
            <>
              <i>·</i>
              <span className="faint" title={t('sources.hasArchives')}>
                {formatNumber(source.inArchives)} {t('sources.inArchives')}
              </span>
            </>
          )}
        </div>

        {slots.length > 0 && (
          <div className="slots">
            {shown.map(([slot, count], index) => (
              <span key={slot}>
                {index > 0 && <i>·</i>}
                {t(`slot.${slot}`)} <b>{formatNumber(count)}</b>
              </span>
            ))}
            {rest.length > 0 && (
              <>
                <i>·</i>
                <span title={rest.map(([slot, count]) => `${t(`slot.${slot}`)} ${count}`).join(', ')}>+{rest.length}</span>
              </>
            )}
          </div>
        )}

        {job && (
          <div className="indexing">
            <span>
              {job.state === 'progress' && job.total
                ? t('sources.indexingOf', {
                    stage: job.stage ? t(`stage.${job.stage}`) : '',
                    done: formatNumber(job.done),
                    total: formatNumber(job.total),
                  })
                : t('sources.indexing')}
            </span>
            <Progress percent={job.state === 'progress' && job.total ? (job.percent ?? 0) : undefined} />
          </div>
        )}

        <div className="foot">
          <span
            className="when"
            title={source.indexedAt ? t('sources.indexed', { d: formatDate(source.indexedAt) }) : t('sources.never')}
          >
            {source.indexedAt ? (
              <>
                <Icon name="history" /> {formatDate(source.indexedAt)}
              </>
            ) : (
              t('sources.never')
            )}
          </span>
          <span className="grow" />
          <Switch
            on={source.enabled}
            label={t(source.enabled ? 'sources.enabled' : 'sources.disabled')}
            title={t(source.enabled ? 'sources.enabled' : 'sources.disabled')}
            onChange={onToggle}
          />
        </div>
      </div>
    </div>
  );
}
