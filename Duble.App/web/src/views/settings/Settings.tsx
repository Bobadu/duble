// views/settings/Settings.tsx — the settings of the program (language, theme) and of the project (where
// rejected files go, the comparison thresholds, the cache).
import { useState } from 'react';
import { useApp, type Theme } from '../../app/AppState';
import { navigate } from '../../app/router';
import { bridge, messageOf } from '../../bridge/bridge';
import type { ProjectSettingsState, UpdateCheck } from '../../bridge/contract';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import { Icon } from '../../components/Icon';
import { Segmented } from '../../components/Segmented';
import { useToast } from '../../components/Toast';
import { formatSize, shortenPath, useI18n, useTranslate, type Language } from '../../i18n';
import { Calibration } from './Calibration';
import { Thresholds } from './Thresholds';

export function Settings() {
  const t = useTranslate();
  const { project } = useApp();

  return (
    <>
      <div className="view-head">
        <div className="titles">
          <h1>{t('settings.title')}</h1>
        </div>
      </div>

      <ProgramSettings />

      <div className="settings-section">
        <h2>
          {t('settings.project')}
          {project && <span className="faint"> · {project.name}</span>}
        </h2>
        {project ? <ProjectSettings /> : <NoProject />}
      </div>
    </>
  );
}

function ProgramSettings() {
  const t = useTranslate();
  const { settings, setLanguage, setTheme } = useApp();
  const toast = useToast();

  const saved = () => toast.ok(t('settings.saved'), { duration: 1800 });

  return (
    <div className="settings-section">
      <h2>{t('settings.program')}</h2>
      <div className="settings-grid">
        <div className="card setting">
          <div className="card-body">
            <div className="label">{t('settings.language')}</div>
            <Segmented
              value={settings.chosenLanguage ?? 'system'}
              segments={[
                { value: 'system', label: t('settings.languageSystem') },
                { value: 'pl', label: 'Polski' },
                { value: 'en', label: 'English' },
              ]}
              onChange={(chosen) => void setLanguage(chosen as Language | 'system').then(saved)}
            />
          </div>
        </div>

        <div className="card setting">
          <div className="card-body">
            <div className="label">{t('settings.theme')}</div>
            <Segmented
              value={settings.theme}
              segments={[
                { value: 'system', label: t('settings.themeSystem') },
                { value: 'dark', label: t('settings.themeDark') },
                { value: 'light', label: t('settings.themeLight') },
              ]}
              onChange={(chosen) => void setTheme(chosen as Theme).then(saved)}
            />
          </div>
        </div>

        <Updates onSaved={saved} />
      </div>
    </div>
  );
}

/** The update check: whether it runs at start, and the button that runs it right now. */
function Updates({ onSaved }: { onSaved: () => void }) {
  const t = useTranslate();
  const { settings, setCheckUpdates } = useApp();
  const toast = useToast();
  const [checking, setChecking] = useState(false);
  const [checked, setChecked] = useState<UpdateCheck | undefined>();

  const check = async () => {
    setChecking(true);
    try {
      setChecked(await bridge.call('update.check'));
    } catch (failure) {
      toast.error(t('update.failed', { error: messageOf(failure) }));
    } finally {
      setChecking(false);
    }
  };

  const openRelease = (url: string) =>
    void bridge.call('shell.openUrl', { url }).catch((failure: unknown) => toast.warn(messageOf(failure)));

  return (
    <div className="card setting">
      <div className="card-body">
        <div className="label">{t('settings.updates')}</div>
        <p className="help">{t('settings.updatesHelp')}</p>

        <label className="check-row">
          <input
            type="checkbox"
            checked={settings.checkUpdates}
            onChange={(event) => void setCheckUpdates(event.target.checked).then(onSaved)}
          />
          <span>{t('settings.updatesCheck')}</span>
        </label>

        <div className="btn-row">
          <Button small icon="refresh" disabled={checking} onClick={() => void check()}>
            {t('settings.updatesNow')}
          </Button>
          {checked && !checked.newer && <span className="faint">{t('update.latest')}</span>}
          {checked?.newer && (
            <>
              <span>{t('update.available', { version: checked.version })}</span>
              <Button small variant="primary" icon="external" onClick={() => openRelease(checked.url)}>
                {t('update.download')}
              </Button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function NoProject() {
  const t = useTranslate();

  return (
    <EmptyState icon="file" title={t('status.noProject')} hint={t('settings.noProject')}>
      <Button variant="primary" icon="home" onClick={() => navigate('start')}>
        {t('nav.start')}
      </Button>
    </EmptyState>
  );
}

function ProjectSettings() {
  const settings = useCommand('project.settings.get', null, {
    reloadOn: ['settings.changed', 'project.opened', 'compare.done'],
  });

  if (!settings.data) return null;

  return (
    <div className="settings-stack">
      <BinFolder state={settings.data} />
      <Advanced state={settings.data} />
      <Cache state={settings.data} />
    </div>
  );
}

function BinFolder({ state }: { state: ProjectSettingsState }) {
  const t = useTranslate();
  const toast = useToast();
  const chosen = !!state.bin;

  const set = async (bin: string | null) => {
    try {
      await bridge.call('project.settings.set', { bin: bin });
      toast.ok(t('settings.saved'), { duration: 1500 });
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  const pick = async () => {
    try {
      const picked = await bridge.call('dialogs.pickFolder', state.bin ? { start: state.bin } : {});
      if (picked.path) await set(picked.path);
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  return (
    <div className="card setting">
      <div className="card-body">
        <div className="label">
          <Icon name="trash" /> {t('settings.bin')}
        </div>
        <p className="help">{t('settings.binHelp')}</p>

        <label className="radio-row">
          <input type="radio" name="bin" checked={!chosen} onChange={() => void set(null)} />
          <span>{t('settings.binBeside')}</span>
          <span className="faint mono">…\_rejected\&lt;{t('dup.sourcesFilter').toLowerCase()}&gt;\</span>
        </label>

        <label className="radio-row">
          <input type="radio" name="bin" checked={chosen} onChange={() => void pick()} />
          <span>{t('settings.binCustom')}</span>
          <span className="faint mono">{state.bin ? shortenPath(state.bin, 70) : ''}</span>
          <Button
            small
            icon="folder"
            onClick={(event) => {
              event.preventDefault();
              void pick();
            }}
          >
            {t('settings.binPick')}
          </Button>
        </label>
      </div>
    </div>
  );
}

/** Thresholds and calibration, folded away: most people never need to touch either. */
function Advanced({ state }: { state: ProjectSettingsState }) {
  const t = useTranslate();
  const [open, setOpen] = useState(() => sessionStorage.getItem('settings.advanced') === '1');

  const toggle = () => {
    const next = !open;
    setOpen(next);
    sessionStorage.setItem('settings.advanced', next ? '1' : '0');
  };

  return (
    <div className="card setting adv">
      <div className="card-body">
        <button type="button" className="adv-toggle" aria-expanded={open} onClick={toggle}>
          <Icon name="chevron" className={open ? 'rot180' : undefined} />
          <span className="label">{t('settings.advanced')}</span>
          {state.thresholdsChanged && <span className="badge ok">{t('settings.thresholdsChanged')}</span>}
        </button>

        {open && (
          <div className="adv-body">
            <Thresholds state={state} />
            <Calibration />
          </div>
        )}
      </div>
    </div>
  );
}

function Cache({ state }: { state: ProjectSettingsState }) {
  const t = useTranslate();
  const { language, formatNumber } = useI18n();
  const toast = useToast();

  const part = (name: string) => state.cache[name] ?? { files: 0, bytes: 0 };
  const rebuildable = part('tex').files + part('mesh').files;

  const clear = async () => {
    try {
      const cleared = await bridge.call('cache.clear', { textures: true, meshes: true });
      toast.ok(t('settings.cacheCleared', { mb: formatSize(cleared.bytes, language) }));
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  const rows: [labelKey: string, name: string][] = [
    ['settings.cacheThumbs', 'thumbs'],
    ['settings.cacheTex', 'tex'],
    ['settings.cacheMesh', 'mesh'],
    ['settings.cacheHistory', 'historia'],
  ];

  return (
    <div className="card setting">
      <div className="card-body">
        <div className="label">
          <Icon name="server" /> {t('settings.cache')}
        </div>
        <p className="help">{t('settings.cacheHelp')}</p>

        <div className="kv cache-kv">
          {rows.map(([labelKey, name]) => (
            <span key={name}>
              {t(labelKey)} <b>{formatSize(part(name).bytes, language)}</b>{' '}
              <span className="faint">({formatNumber(part(name).files)})</span>
            </span>
          ))}
          <span>
            {t('settings.cacheTotal')} <b>{formatSize(part('total').bytes, language)}</b>
          </span>
        </div>

        <p className="help">{t('settings.cacheThumbsNote')}</p>

        <div className="btn-row">
          <Button small icon="trash" disabled={rebuildable === 0} onClick={clear}>
            {t('settings.cacheClear')}
          </Button>
          <Button
            variant="ghost"
            small
            icon="external"
            onClick={() =>
              void bridge
                .call('shell.openFolder', { path: state.cacheFolder })
                .catch((failure: unknown) => toast.warn(messageOf(failure)))
            }
          >
            {t('settings.openFolder')}
          </Button>
          <span className="faint mono">{shortenPath(state.cacheFolder, 60)}</span>
        </div>
      </div>
    </div>
  );
}
