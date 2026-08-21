// views/About.tsx — what this program is, where its files are, what it is built on, and what is new in it.
import { useState } from 'react';
import { useApp, type UpdateAvailable } from '../app/AppState';
import { bridge, messageOf } from '../bridge/bridge';
import { useCommand } from '../bridge/hooks';
import { Button } from '../components/Button';
import { Icon } from '../components/Icon';
import { Markdown } from '../components/Markdown';
import { useToast } from '../components/Toast';
import { UpdateAction } from '../components/UpdateAction';
import { useTranslate } from '../i18n';

/** What Duble is built on, and under what licence — the same list as THIRD-PARTY-NOTICES.md. */
const LIBRARIES: readonly [name: string, licence: string][] = [
  ['CodeWalker.Core', 'MIT'],
  ['BCnEncoder.Net', 'MIT'],
  ['three.js', 'MIT'],
  ['React', 'MIT'],
  ['WebView2', 'Microsoft'],
];

/** The three sentences that say how Duble works, each with the icon of the screen it happens on. */
const PRINCIPLES = [
  { icon: 'duplicates', title: 'about.how1t', text: 'about.how1' },
  { icon: 'palette', title: 'about.how2t', text: 'about.how2' },
  { icon: 'restore', title: 'about.how3t', text: 'about.how3' },
] as const;

export function About() {
  const t = useTranslate();
  const { info, update } = useApp();
  const toast = useToast();

  const open = (url: string) =>
    void bridge.call('shell.openUrl', { url }).catch((failure: unknown) => toast.warn(messageOf(failure)));

  const paths = info.paths;

  return (
    <div className="about">
      {update && <UpdateBanner update={update} current={info.version} />}

      <div className="card about-card">
        <div className="card-body">
          <div className="about-hero">
            <Icon name="logo" className="logo" />
            <div className="about-id">
              <h1>
                {t('app.name')} <span className="by">{t('app.by')}</span>
              </h1>
              <div className="about-chips">
                <span className="pill">{t('app.version', { v: info.version })}</span>
                {info.dev && <span className="pill">dev</span>}
                {info.licence && <span className="pill">{info.licence}</span>}
              </div>
              <p className="about-tag">{t('about.tagline')}</p>
              <p className="about-compat">{t('about.compat')}</p>

              <div className="btn-row about-actions">
                {info.website && (
                  <Button variant="primary" icon="external" title={info.website} onClick={() => open(info.website)}>
                    {t('about.website')}
                  </Button>
                )}
                {info.repository && (
                  <>
                    <Button icon="external" title={info.repository} onClick={() => open(info.repository)}>
                      {t('about.repo')}
                    </Button>
                    <Button icon="warn" title={`${info.repository}/issues`} onClick={() => open(`${info.repository}/issues`)}>
                      {t('about.issues')}
                    </Button>
                  </>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="section about-sec">
        <div className="section-head">
          <h2>{t('about.how')}</h2>
        </div>
        <div className="about-how">
          {PRINCIPLES.map((principle) => (
            <div key={principle.title} className="card">
              <div className="card-body">
                <Icon name={principle.icon} />
                <div>
                  <h3>{t(principle.title)}</h3>
                  <p>{t(principle.text)}</p>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      <Changelog />

      {(paths.projects || paths.settings) && (
        <div className="section about-sec">
          <div className="section-head">
            <h2>{t('about.files')}</h2>
          </div>
          <ul className="about-list">
            {paths.projects && <PathRow label={t('about.pathProjects')} path={paths.projects} />}
            {paths.settings && <PathRow label={t('about.pathSettings')} path={paths.settings} />}
          </ul>
        </div>
      )}

      {(paths.executable || paths.webView2) && (
        <TechnicalPaths executable={paths.executable} webView2={paths.webView2} />
      )}

      <div className="section about-sec">
        <div className="section-head">
          <h2>{t('about.credits')}</h2>
        </div>
        <p className="about-note">{t('about.licenseNote')}</p>
        <div className="about-libs">
          {LIBRARIES.map(([name, licence]) => (
            <span key={name} className="pill">
              {name}
              <span className="faint">{licence}</span>
            </span>
          ))}
        </div>
      </div>

      <div className="about-foot">
        <span>
          {t('app.name')} {info.version}
        </span>
        <span>{t('about.copyright')}</span>
        {info.licence && <span>{t('about.appLicense', { lic: info.licence })}</span>}
      </div>
    </div>
  );
}

/** A newer release, announced by the check at start: its notes, and the way to it. */
function UpdateBanner({ update, current }: { update: UpdateAvailable; current: string }) {
  const t = useTranslate();

  return (
    <div className="card update-card">
      <div className="card-body">
        <div className="update-head">
          <Icon name="info" />
          <h2>{t('update.title', { version: update.version })}</h2>
          <span className="faint">{t('update.yours', { version: current })}</span>
          <UpdateAction url={update.url} canApply={update.canApply} />
        </div>
        {update.notes && (
          <div className="update-notes">
            <Markdown text={update.notes} />
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * The changelog of the running build, from its first release section down — the file's own preamble repeats
 * what this screen already says. Folded away like the technical paths, and fetched only when unfolded.
 */
function Changelog() {
  const t = useTranslate();
  const [open, setOpen] = useState(() => sessionStorage.getItem('about.changelog') === '1');
  const log = useCommand('app.changelog', null, { enabled: open });

  const toggle = () => {
    const next = !open;
    setOpen(next);
    sessionStorage.setItem('about.changelog', next ? '1' : '0');
  };

  const releases = log.data ? log.data.markdown.slice(Math.max(0, log.data.markdown.indexOf('## ['))) : '';

  return (
    <div className="section about-sec">
      <button type="button" className="adv-toggle" aria-expanded={open} onClick={toggle}>
        <Icon name="chevron" className={open ? 'rot180' : undefined} />
        <span className="label">{t('about.changelog')}</span>
      </button>
      {open && releases && (
        <div className="adv-body changelog">
          <Markdown text={releases} />
        </div>
      )}
    </div>
  );
}

function PathRow({ label, path }: { label: string; path: string }) {
  const t = useTranslate();
  const toast = useToast();

  return (
    <li>
      <span className="lab">{label}</span>
      <span className="mono select-text" title={path}>
        {path}
      </span>
      <Button
        variant="ghost"
        icon="external"
        title={t('about.open')}
        aria-label={t('about.open')}
        onClick={() =>
          void bridge.call('shell.showInExplorer', { path: path }).catch((failure: unknown) => toast.warn(messageOf(failure)))
        }
      />
    </li>
  );
}

/** Where the executable and WebView2 keep themselves — folded away, because most people never need it. */
function TechnicalPaths({ executable, webView2 }: { executable?: string; webView2?: string }) {
  const t = useTranslate();
  const [open, setOpen] = useState(() => sessionStorage.getItem('about.technical') === '1');

  const toggle = () => {
    const next = !open;
    setOpen(next);
    sessionStorage.setItem('about.technical', next ? '1' : '0');
  };

  return (
    <div className="section about-sec">
      <button type="button" className="adv-toggle" aria-expanded={open} onClick={toggle}>
        <Icon name="chevron" className={open ? 'rot180' : undefined} />
        <span className="label">{t('about.tech')}</span>
      </button>
      {open && (
        <div className="adv-body">
          <ul className="about-list">
            {executable && <PathRow label={t('about.pathExe')} path={executable} />}
            {webView2 && <PathRow label={t('about.pathWebView')} path={webView2} />}
          </ul>
        </div>
      )}
    </div>
  );
}
