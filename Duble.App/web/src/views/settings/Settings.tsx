// views/settings/Settings.tsx — the settings of the program (language, theme) and of the project (where
// rejected files go, the comparison thresholds, the cache).
import { useState } from 'react';
import { useApp, type Theme } from '../../app/AppState';
import { navigate } from '../../app/router';
import { bridge, messageOf } from '../../bridge/bridge';
import type { ProjectSettingsState } from '../../bridge/contract';
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
          {project && <span className="faint"> · {project.nazwa}</span>}
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
              value={settings.jezykUstawiony ?? 'system'}
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
              value={settings.motyw}
              segments={[
                { value: 'system', label: t('settings.themeSystem') },
                { value: 'dark', label: t('settings.themeDark') },
                { value: 'light', label: t('settings.themeLight') },
              ]}
              onChange={(chosen) => void setTheme(chosen as Theme).then(saved)}
            />
          </div>
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
  const chosen = !!state.kosz;

  const set = async (bin: string | null) => {
    try {
      await bridge.call('project.settings.set', { kosz: bin });
      toast.ok(t('settings.saved'), { duration: 1500 });
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  const pick = async () => {
    try {
      const picked = await bridge.call('dialogs.pickFolder', state.kosz ? { start: state.kosz } : {});
      if (picked.sciezka) await set(picked.sciezka);
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
          <span className="faint mono">{state.kosz ? shortenPath(state.kosz, 70) : ''}</span>
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
          {state.progiZmienione && <span className="badge ok">{t('settings.thresholdsChanged')}</span>}
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

  const part = (name: string) => state.cache[name] ?? { pliki: 0, bajty: 0 };
  const rebuildable = part('tex').pliki + part('mesh').pliki;

  const clear = async () => {
    try {
      const cleared = await bridge.call('cache.clear', { tex: true, mesh: true });
      toast.ok(t('settings.cacheCleared', { mb: formatSize(cleared.bajty, language) }));
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
              {t(labelKey)} <b>{formatSize(part(name).bajty, language)}</b>{' '}
              <span className="faint">({formatNumber(part(name).pliki)})</span>
            </span>
          ))}
          <span>
            {t('settings.cacheTotal')} <b>{formatSize(part('razem').bajty, language)}</b>
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
                .call('shell.openFolder', { sciezka: state.folderCache })
                .catch((failure: unknown) => toast.warn(messageOf(failure)))
            }
          >
            {t('settings.openFolder')}
          </Button>
          <span className="faint mono">{shortenPath(state.folderCache, 60)}</span>
        </div>
      </div>
    </div>
  );
}
