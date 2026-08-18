// views/settings/Thresholds.tsx — the ten numbers the comparison is decided by.
//
// Each is sent on its own as it is changed, because Core validates the whole set and answers with the state
// that resulted; a threshold the engine refuses leaves the others exactly as they were.
import { useState } from 'react';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { ProjectSettingsState, Thresholds as ThresholdValues } from '../../bridge/contract';
import { Button } from '../../components/Button';
import { useConfirm } from '../../components/Confirm';
import { useToast } from '../../components/Toast';
import { useTranslate } from '../../i18n';

interface Field {
  group: 'geo' | 'tex' | 'cover';
  key: keyof ThresholdValues;
  step: number;
  min: number;
  max: number;
  /** Decimal places, so the field shows 0.02 rather than 0.019999999. */
  places: number;
}

const FIELDS: readonly Field[] = [
  { group: 'geo', key: 'geometryIdentical', step: 0.001, min: 0, max: 1, places: 3 },
  { group: 'geo', key: 'geometrySimilar', step: 0.01, min: 0, max: 1, places: 3 },
  { group: 'geo', key: 'geometryTriangleTolerance', step: 0.01, min: 0, max: 1, places: 2 },
  { group: 'geo', key: 'geometryBoundsTolerance', step: 0.01, min: 0, max: 1, places: 2 },
  { group: 'tex', key: 'textureHashDistance', step: 1, min: 0, max: 256, places: 0 },
  { group: 'tex', key: 'textureColorDistance', step: 0.1, min: 0, max: 100, places: 1 },
  { group: 'tex', key: 'flatTextureVariance', step: 0.5, min: 0, max: 255, places: 1 },
  { group: 'tex', key: 'flatTextureColorDistance', step: 0.1, min: 0, max: 100, places: 1 },
  { group: 'cover', key: 'fullCoverage', step: 0.01, min: 0, max: 1, places: 2 },
  { group: 'cover', key: 'partialCoverage', step: 0.01, min: 0, max: 1, places: 2 },
];

const GROUPS = ['geo', 'tex', 'cover'] as const;

export function Thresholds({ state }: { state: ProjectSettingsState }) {
  const t = useTranslate();
  const toast = useToast();
  const confirm = useConfirm();
  const [rejected, setRejected] = useState<ReadonlySet<string>>(new Set());

  const save = async (field: Field, typed: string) => {
    const value = Number(typed.replace(',', '.'));
    if (!Number.isFinite(value)) {
      toast.warn(t('settings.thresholdInvalid', { pole: t(`settings.th.${field.key}`) }));
      return;
    }

    try {
      const saved = await bridge.call('project.settings.set', { progi: { [field.key]: value } });
      setRejected((previous) => without(previous, field.key));
      toast.ok(saved.porownanie ? t('settings.thresholdSavedCompare') : saved.porownanie === false ? t('sources.busy') : t('settings.saved'), {
        duration: 2200,
      });
    } catch (failure) {
      setRejected((previous) => new Set(previous).add(field.key));
      // bad_args answers with the names of the fields Core refused, which are worth naming back
      toast.warn(
        errorCodeOf(failure) === ErrorCode.BadArguments
          ? t('settings.thresholdInvalid', { pole: fieldNames(messageOf(failure), t) })
          : messageOf(failure),
      );
    }
  };

  const restoreDefaults = async () => {
    const sure = await confirm({
      title: t('settings.thresholds'),
      text: t('settings.restoreConfirm'),
      confirmLabel: t('settings.restoreDefaults'),
    });
    if (!sure) return;

    try {
      const saved = await bridge.call('project.settings.resetProgi');
      setRejected(new Set());
      toast.ok(saved.porownanie ? t('settings.thresholdSavedCompare') : t('settings.saved'));
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  return (
    <div className="th-block">
      <div className="th-head">
        <h3>{t('settings.thresholds')}</h3>
        {state.progiZmienione ? (
          <Button small icon="refresh" onClick={restoreDefaults}>
            {t('settings.restoreDefaults')}
          </Button>
        ) : (
          <span className="faint">{t('settings.thresholdsDefault')}</span>
        )}
      </div>
      <p className="help">{t('settings.thresholdsHelp')}</p>

      <div className="th-grid">
        {GROUPS.map((group) => (
          <div key={group} className="contents">
            <div className="th-group">{t(`settings.${group}`)}</div>
            {FIELDS.filter((field) => field.group === group).map((field) => (
              <ThresholdField
                key={field.key}
                field={field}
                value={state.progi[field.key]}
                fallback={state.progiDomyslne[field.key]}
                bad={rejected.has(field.key)}
                onSave={(typed) => void save(field, typed)}
              />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

function ThresholdField({
  field,
  value,
  fallback,
  bad,
  onSave,
}: {
  field: Field;
  value: number;
  fallback: number;
  bad: boolean;
  onSave: (typed: string) => void;
}) {
  const t = useTranslate();
  const shown = value.toFixed(field.places);
  const [typed, setTyped] = useState(shown);

  // the engine is the source of truth: whatever it last accepted is what the field shows
  const [lastShown, setLastShown] = useState(shown);
  if (shown !== lastShown) {
    setLastShown(shown);
    setTyped(shown);
  }

  const changed = value !== fallback;

  return (
    <label className={changed ? 'th-field changed' : 'th-field'}>
      <span className="th-name">{t(`settings.th.${field.key}`)}</span>
      <input
        className={bad ? 'input sm bad' : 'input sm'}
        type="number"
        step={field.step}
        min={field.min}
        max={field.max}
        value={typed}
        onChange={(event) => setTyped(event.target.value)}
        onBlur={() => typed !== shown && onSave(typed)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
        }}
      />
      <span className="th-desc">
        {t(`settings.thd.${field.key}`)}
        {changed && (
          <span className="faint">
            {' '}
            ({t('settings.default')}: {fallback.toFixed(field.places)})
          </span>
        )}
      </span>
    </label>
  );
}

function without(set: ReadonlySet<string>, key: string): Set<string> {
  const next = new Set(set);
  next.delete(key);
  return next;
}

/** Core answers bad_args with "TextureHashDistance,FullCoverage"; the user should read their own labels. */
function fieldNames(message: string, t: (key: string) => string): string {
  return message
    .split(',')
    .map((name) => t(`settings.th.${name.charAt(0).toLowerCase()}${name.slice(1)}`))
    .join(', ');
}
