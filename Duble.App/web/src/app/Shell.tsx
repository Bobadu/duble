// app/Shell.tsx — the frame around whichever screen is showing: the title bar the application draws itself,
// the rail down the side, and the status bar.
import { bridge } from '../bridge/bridge';
import type { ViewName } from './router';
import { Icon, type IconName } from '../components/Icon';
import { Progress } from '../components/Progress';
import { useI18n, useTranslate } from '../i18n';
import { useApp } from './AppState';
import { routeToHash, useRoute } from './router';

/** The order of the rail, with the gap that pushes settings and about to the bottom. */
const RAIL: (ViewName | 'gap')[] = ['start', 'sources', 'duplicates', 'catalog', 'history', 'gap', 'settings', 'about'];

const RAIL_ICONS: Record<ViewName, IconName> = {
  start: 'home',
  sources: 'sources',
  duplicates: 'duplicates',
  catalog: 'catalog',
  history: 'history',
  settings: 'settings',
  about: 'info',
};

export function TitleBar() {
  const t = useTranslate();
  const { project, windowMaximized } = useApp();

  return (
    <header
      className="titlebar"
      id="titlebar"
      onDoubleClick={(event) => {
        if (!(event.target as HTMLElement).closest('.win')) void bridge.call('window.maximize');
      }}
    >
      <div className="brand">
        <Icon name="logo" />
        <span>{t('app.name')}</span>
        <span className="by">{t('app.by')}</span>
      </div>
      {project && <div className="project">{project.name}</div>}
      <div className="spacer" />
      <div className="win">
        <button type="button" title={t('win.minimize')} onClick={() => void bridge.call('window.minimize')}>
          <Icon name="minus" />
        </button>
        <button
          type="button"
          title={t(windowMaximized ? 'win.restore' : 'win.maximize')}
          onClick={() => void bridge.call('window.maximize')}
        >
          <Icon name={windowMaximized ? 'restore' : 'square'} />
        </button>
        <button type="button" className="close" title={t('win.close')} onClick={() => void bridge.call('window.close')}>
          <Icon name="x" />
        </button>
      </div>
    </header>
  );
}

export function Rail() {
  const t = useTranslate();
  const route = useRoute();

  return (
    <nav className="rail" aria-label="Duble">
      {RAIL.map((entry, index) =>
        entry === 'gap' ? (
          <div key={`gap-${index}`} className="grow" />
        ) : (
          <a key={entry} href={routeToHash(entry)} className={route.view === entry ? 'active' : ''}>
            <Icon name={RAIL_ICONS[entry]} />
            <span>{t(`nav.${entry}`)}</span>
          </a>
        ),
      )}
    </nav>
  );
}

export function StatusBar() {
  const t = useTranslate();
  const { formatNumber } = useI18n();
  const { project, job, busy } = useApp();

  return (
    <footer className="statusbar">
      {project ? (
        <>
          <span>
            <b>{project.name}</b>
          </span>
          <span className="sep" />
          <span>{t('status.sources', { n: formatNumber(project.sources) })}</span>
          <span className="sep" />
          <span>{t('status.items', { n: formatNumber(project.garments) })}</span>
          <span className="sep" />
          <span>{t('status.textures', { n: formatNumber(project.textures) })}</span>
        </>
      ) : (
        <span>{t('status.noProject')}</span>
      )}

      <div className="right">
        {busy && job ? (
          <>
            <span>
              {job.state === 'progress' && job.total
                ? t('sources.indexingOf', {
                    stage: job.stage ? t(`stage.${job.stage}`) : '',
                    done: formatNumber(job.done),
                    total: formatNumber(job.total),
                  })
                : t('status.working')}
            </span>
            <Progress percent={job.state === 'progress' ? (job.percent ?? 0) : undefined} />
            <button type="button" className="btn ghost sm" onClick={() => void bridge.call('sources.cancel')}>
              {t('sources.cancel')}
            </button>
          </>
        ) : (
          <span className="idle">{t('status.idle')}</span>
        )}
      </div>
    </footer>
  );
}
