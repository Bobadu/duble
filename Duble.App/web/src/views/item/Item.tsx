// views/item/Item.tsx — the card of one garment from the catalog: what it is made of, and which groups it
// turned up in.
//
// The same two tabs as a group card, because it is the same question asked of one thing instead of several.
import { useEffect, useState } from 'react';
import { navigate, routeToHash } from '../../app/router';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { GarmentGroupRef } from '../../bridge/contract';
import { useCommand } from '../../bridge/hooks';
import { Badge, VerdictBadge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { Icon } from '../../components/Icon';
import { QualityBars, texturePath, variantLabel } from '../../components/QualityBars';
import { TextureTile } from '../../components/TextureTile';
import { useToast } from '../../components/Toast';
import { formatSize, useI18n, useTranslate } from '../../i18n';
import { garmentName, reasonText } from '../duplicates/GroupCard';
import { LazyModelTab } from '../group/LazyModelTab';
import { useModelTab } from '../group/useModelTab';
import { TextureWipe, type WipeSide } from '../group/TextureWipe';

export function Item({ id }: { id: string }) {
  const t = useTranslate();
  const { language, formatNumber } = useI18n();
  const toast = useToast();
  const [tab, setTab] = useModelTab('item.tab');
  const [wipe, setWipe] = useState<WipeSide[] | null>(null);

  const item = useCommand('catalog.item', { id }, { reloadOn: ['compare.done', 'groups.changed', 'project.changed'] });

  // the garment can disappear from under the card — after applying, or after a re-index
  useEffect(() => {
    if (errorCodeOf(item.error) === ErrorCode.NotFound) navigate('catalog');
  }, [item.error]);

  if (item.error && errorCodeOf(item.error) !== ErrorCode.NotFound)
    return <EmptyState icon="warn" title={t('common.error')} hint={messageOf(item.error)} />;

  if (!item.data) return null;

  const garment = item.data.garment;
  const textures = garment.textures ?? [];

  return (
    <>
      <div className="view-head group-head">
        <div className="titles">
          <a
            className="back-link"
            href={routeToHash('catalog')}
            onClick={(event) => {
              event.preventDefault();
              navigate('catalog');
            }}
          >
            <Icon name="chevron" className="rot90" />
            {t('item.back')}
          </a>

          <h1 className="group-h1">
            <span className="nm">
              {garmentName(garment)}
              <sub>{garment.suffix ?? ''}</sub>
            </span>
          </h1>

          <div className="group-sub">
            <Badge tone={garment.gen9 ? 'gen9' : 'legacy'}>
              {t(garment.gen9 ? 'sources.formatGen9' : 'sources.formatLegacy')}
            </Badge>
            {garment.inArchive && <Badge tone="unknown">{t('group.inArchive')}</Badge>}
            <span>
              {garment.source}
              <span className="faint"> › {garment.container ?? ''}</span>
            </span>
            {garment.slot && <span className="faint">· {t(`slot.${garment.slot}`)}</span>}
          </div>
        </div>

        {!garment.inArchive && (
          <div className="actions">
            <Button
              icon="external"
              title={t('group.showInExplorer')}
              onClick={() =>
                void bridge
                  .call('shell.showInExplorer', { path: garment.modelPath ?? '' })
                  .catch((failure: unknown) => toast.warn(messageOf(failure)))
              }
            >
              {t('group.showInExplorer')}
            </Button>
          </div>
        )}
      </div>

      <div className="group-bar">
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
        <LazyModelTab members={[garment]} />
      ) : (
        <div className="item-2d">
          <div className="group-col neutral item-col">
            <div className="col-quality">
              <QualityBars garment={garment} />
            </div>

            <div className="col-facts">
              <div>
                <span className="faint">{t('group.model')}</span> <b>{formatNumber(garment.vertices)}</b> {t('group.verts')} ·{' '}
                <b>{formatNumber(garment.triangles)}</b> {t('group.tris')} · {t('group.lods')} <b>{garment.lods}</b>
              </div>
              <div>
                <span className="faint">{t('group.size')}</span> <b>{formatSize(garment.bytes, language)}</b> ·{' '}
                {t('dup.textures', { n: garment.textureCount })}
              </div>
              <div className="col-path">
                <span className="faint">{t('group.path')}</span>{' '}
                <span className="mono select-text" title={garment.modelPath ?? ''}>
                  {texturePath(garment.modelPath, 10000)}
                </span>
              </div>
            </div>

            <div className="col-tex-head">
              <span>{t('group.textures')}</span>
            </div>

            <div className="tex-grid item-tex">
              {textures.map((texture) => (
                <TextureTile
                  key={texture.sha ?? texture.file}
                  texture={texture}
                  note={t('group.single')}
                  onClick={() => {
                    if (!texture.decoded || !texture.sha) {
                      toast.warn(t('wipe.noPreview'));
                      return;
                    }
                    setWipe([
                      {
                        sha: texture.sha,
                        name: garmentName(garment),
                        variant: texture.variant ? t('wipe.variant', { x: variantLabel(texture) }) : '',
                        file: texture.file,
                        width: texture.width,
                        height: texture.height,
                        format: texture.format,
                        mipmaps: texture.mipmaps,
                      },
                    ]);
                  }}
                />
              ))}
            </div>
          </div>

          <div className="item-groups">
            <div className="section-head">
              <h2>{t('item.groups')}</h2>
            </div>
            <div className="item-groups-list">
              {item.data.groups.length === 0 ? (
                <p className="muted">
                  <Icon name="ok" /> {t('item.noGroups')}
                </p>
              ) : (
                item.data.groups.map((group) => <GroupRow key={group.id} group={group} />)
              )}
            </div>
          </div>
        </div>
      )}

      {wipe && <TextureWipe sides={wipe} onClose={() => setWipe(null)} />}
    </>
  );
}

function GroupRow({ group }: { group: GarmentGroupRef }) {
  const t = useTranslate();

  const standing = group.ignored
    ? { label: t('dup.ignored'), tone: 'unknown' as const }
    : group.standing === 'stays'
      ? { label: t('group.stays'), tone: 'ok' as const }
      : group.standing === 'rejected'
        ? { label: t('group.rejected'), tone: 'warn' as const }
        : { label: t('group.neutral'), tone: 'unknown' as const };

  const open = () => navigate('duplicates', group.id);

  return (
    <div
      className="card item-group clickable"
      tabIndex={0}
      role="button"
      onClick={open}
      onKeyDown={(event) => {
        if (event.key === 'Enter') open();
      }}
    >
      <div className="card-body">
        <div className="dup-card-head">
          <VerdictBadge verdict={group.verdict} />
          <span className="dup-reason">{reasonText(t, group.reason)}</span>
          <span className={group.standing === 'rejected' && !group.ignored ? 'badge err' : `badge ${standing.tone}`}>
            {standing.label}
          </span>
        </div>

        <div className="item-group-with">
          <span className="faint">{t('item.with')}</span>{' '}
          {group.others.map((other, index) => (
            <span key={other.id}>
              {index > 0 && ', '}
              <span className="mono">
                {other.name}
                <sub>{other.suffix ?? ''}</sub>
              </span>{' '}
              <span className="faint">({other.source})</span>
            </span>
          ))}
        </div>

        <div className="btn-row">
          <Button
            small
            icon="duplicates"
            onClick={(event) => {
              event.stopPropagation();
              open();
            }}
          >
            {t('item.openGroup')}
          </Button>
        </div>
      </div>
    </div>
  );
}
