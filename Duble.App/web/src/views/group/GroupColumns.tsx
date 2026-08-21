// views/group/GroupColumns.tsx — the members of a group side by side: quality, facts, textures, and the
// decision for each.
//
// A texture that has a match on another model is marked, and hovering one lights up its partners; clicking it
// opens the wipe with the matching texture on the other side. That pairing comes from the engine, which
// compared the graphics — nothing here guesses it from file names.
import { useMemo, useState } from 'react';
import { bridge, messageOf } from '../../bridge/bridge';
import type { Garment, Group, Texture } from '../../bridge/contract';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { QualityBars, texturePath, variantLabel } from '../../components/QualityBars';
import { TextureTile } from '../../components/TextureTile';
import { useToast } from '../../components/Toast';
import { formatSize, useI18n, useTranslate } from '../../i18n';
import { routeToHash } from '../../app/router';
import { garmentName } from '../duplicates/GroupCard';
import { TextureWipe, type WipeSide } from './TextureWipe';

/** sha -> the shas it was matched with, and on which member they live. */
type Partners = Map<string, { sha: string; memberId: string }[]>;

export function GroupColumns({ group, onDecide }: { group: Group; onDecide: (change: DecisionChange) => void }) {
  const [hovered, setHovered] = useState<ReadonlySet<string>>(new Set());
  const [wipe, setWipe] = useState<{ sides: WipeSide[]; a: number; b: number | null } | null>(null);

  const partners = useMemo(() => partnersOf(group), [group]);

  return (
    <>
      <div className="group-cols" style={{ '--n': group.members.length } as React.CSSProperties}>
        {group.members.map((member) => (
          <MemberColumn
            key={member.id}
            group={group}
            member={member}
            partners={partners}
            hovered={hovered}
            onHover={setHovered}
            onDecide={onDecide}
            onWipe={setWipe}
          />
        ))}
      </div>

      {wipe && <TextureWipe sides={wipe.sides} first={wipe.a} second={wipe.b} onClose={() => setWipe(null)} />}
    </>
  );
}

export interface DecisionChange {
  winner?: string;
  rejected?: string[];
}

function MemberColumn({
  group,
  member,
  partners,
  hovered,
  onHover,
  onDecide,
  onWipe,
}: {
  group: Group;
  member: Garment;
  partners: Partners;
  hovered: ReadonlySet<string>;
  onHover: (shas: ReadonlySet<string>) => void;
  onDecide: (change: DecisionChange) => void;
  onWipe: (wipe: { sides: WipeSide[]; a: number; b: number | null }) => void;
}) {
  const t = useTranslate();
  const { language, formatNumber } = useI18n();
  const toast = useToast();

  const resolution = group.resolution;
  const stays = resolution.winner === member.id;
  const rejected = !resolution.ignored && resolution.rejected.includes(member.id);
  const standing = resolution.ignored ? 'neutral' : stays ? 'stays' : rejected ? 'rejected' : 'neutral';

  const textures = member.textures ?? [];
  const matched = textures.filter((texture) => texture.sha && partners.has(texture.sha)).length;

  const openWipe = (texture: Texture) => {
    if (!texture.decoded || !texture.sha) {
      toast.warn(t('wipe.noPreview'));
      return;
    }

    const sides = wipeSides(group, member, texture, partners, t);
    const a = Math.max(0, sides.findIndex((side) => side.memberId === member.id));
    const partnerSha = partners.get(texture.sha)?.[0]?.sha;
    let b = partnerSha ? sides.findIndex((side, index) => index !== a && side.sha === partnerSha) : -1;
    if (b < 0) b = sides.findIndex((_, index) => index !== a);

    onWipe({ sides, a, b: b < 0 ? null : b });
  };

  return (
    <div className={`group-col ${standing}`}>
      <div className="col-head">
        <div className="col-title">
          <span className="nm">
            {garmentName(member)}
            <sub>{member.suffix ?? ''}</sub>
          </span>
          <Badge tone={member.gen9 ? 'gen9' : 'legacy'}>{t(member.gen9 ? 'sources.formatGen9' : 'sources.formatLegacy')}</Badge>
          {standing === 'stays' ? (
            <span className="badge ok col-state">
              <Icon name="check" />
              {t('group.stays')}
            </span>
          ) : standing === 'rejected' ? (
            <span className="badge err col-state">
              <Icon name="x" />
              {t('group.rejected')}
            </span>
          ) : (
            <span className="badge unknown col-state">{t('group.neutral')}</span>
          )}
        </div>

        <div className="col-src" title={`${member.source} › ${member.container ?? ''}`}>
          {member.source}
          <span className="faint"> › {member.container ?? ''}</span>
        </div>

        <div className="btn-row col-actions">
          <Button small icon="check" disabled={resolution.ignored || stays} onClick={() => onDecide({ winner: member.id })}>
            {t('group.keepThis')}
          </Button>
          {!stays &&
            (rejected ? (
              <Button
                small
                icon="ok"
                disabled={resolution.ignored}
                onClick={() => onDecide({ rejected: resolution.rejected.filter((id) => id !== member.id) })}
              >
                {t('group.unreject')}
              </Button>
            ) : (
              <Button
                small
                variant="danger"
                icon="trash"
                disabled={resolution.ignored}
                onClick={() => onDecide({ rejected: [...resolution.rejected, member.id] })}
              >
                {t('group.reject')}
              </Button>
            ))}
        </div>
      </div>

      <div className="col-quality">
        <QualityBars garment={member} />
      </div>

      <div className="col-facts">
        <div>
          <span className="faint">{t('group.model')}</span> <b>{formatNumber(member.vertices)}</b>{' '}
          {t('group.verts', { n: member.vertices })} · <b>{formatNumber(member.triangles)}</b>{' '}
          {t('group.tris', { n: member.triangles })} · {t('group.lods')} <b>{member.lods}</b>
        </div>
        <div>
          <span className="faint">{t('group.size')}</span> <b>{formatSize(member.bytes, language)}</b> ·{' '}
          {t('dup.textures', { n: member.textureCount })}
        </div>
        <div className="col-path">
          <span className="faint">{t('group.path')}</span>{' '}
          <span className="mono select-text" title={member.modelPath ?? ''}>
            {texturePath(member.modelPath, 10000)}
          </span>{' '}
          {member.inArchive ? (
            <a href={routeToHash('sources')} className="badge unknown" title={t('apply.tooltipArchive')}>
              {t('group.inArchive')}
            </a>
          ) : (
            <Button
              variant="ghost"
              small
              icon="external"
              title={t('group.showInExplorer')}
              onClick={() =>
                void bridge
                  .call('shell.showInExplorer', { path: member.modelPath ?? '' })
                  .catch((failure: unknown) => toast.warn(messageOf(failure)))
              }
            />
          )}
        </div>
      </div>

      <div className="col-tex-head">
        <span>{t('group.textures')}</span>
        <span className="faint">{matched > 0 ? t('group.matches', { n: matched }) : ''}</span>
      </div>

      <div className="tex-grid">
        {textures.map((texture) => {
          const pairs = texture.sha ? (partners.get(texture.sha) ?? []) : [];
          return (
            <TextureTile
              key={texture.sha ?? texture.file}
              texture={texture}
              paired={pairs.length > 0}
              note={t(pairs.length ? 'group.pair' : 'group.single')}
              highlighted={!!texture.sha && hovered.has(texture.sha)}
              onHover={(over) => onHover(over ? new Set(pairs.map((pair) => pair.sha)) : new Set())}
              onClick={() => openWipe(texture)}
            />
          );
        })}
      </div>
    </div>
  );
}

/** Every matched pair, both ways round, so a texture on either side can find its partners. */
function partnersOf(group: Group): Partners {
  const partners: Partners = new Map();

  const add = (from: string | null, to: string | null, memberId: string) => {
    if (!from || !to) return;
    const known = partners.get(from) ?? [];
    known.push({ sha: to, memberId });
    partners.set(from, known);
  };

  for (const match of group.matches ?? [])
    for (const [left, right] of match.pairs) {
      add(left, right, match.b);
      add(right, left, match.a);
    }

  return partners;
}

/**
 * One side per model in the group: its matching texture where there is one, and otherwise the same colour
 * variant — so a wipe of three models compares like with like.
 */
function wipeSides(
  group: Group,
  member: Garment,
  texture: Texture,
  partners: Partners,
  t: ReturnType<typeof useTranslate>,
): (WipeSide & { memberId: string })[] {
  const matched = new Set((texture.sha ? (partners.get(texture.sha) ?? []) : []).map((pair) => pair.sha));

  const sides: (WipeSide & { memberId: string })[] = [];
  for (const other of group.members) {
    const theirs =
      other.id === member.id
        ? texture
        : (other.textures ?? []).find((each) => each.sha && matched.has(each.sha)) ??
          (texture.variant ? (other.textures ?? []).find((each) => each.variant === texture.variant) : undefined);

    if (!theirs?.decoded || !theirs.sha) continue;

    sides.push({
      memberId: other.id,
      sha: theirs.sha,
      name: garmentName(other),
      variant: theirs.variant ? t('wipe.variant', { x: variantLabel(theirs) }) : '',
      file: theirs.file,
      width: theirs.width,
      height: theirs.height,
      format: theirs.format,
      mipmaps: theirs.mipmaps,
    });
  }

  return sides;
}
