// views/About.tsx — what this program is, where its files are, and what it is built on.
import { useState } from 'react';
import { useApp } from '../app/AppState';
import { bridge, messageOf } from '../bridge/bridge';
import { Button } from '../components/Button';
import { Icon } from '../components/Icon';
import { useToast } from '../components/Toast';
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
  const { info } = useApp();
  const toast = useToast();

  const open = (url: string) =>
    void bridge.call('shell.openUrl', { url }).catch((failure: unknown) => toast.warn(messageOf(failure)));

  const paths = info.sciezki;

  return (
    <div className="about">
      <div className="card about-card">
        <div className="card-body">
          <div className="about-hero">
            <Icon name="logo" className="logo" />
            <div className="about-id">
              <h1>
                {t('app.name')} <span className="by">{t('app.by')}</span>
              </h1>
              <div className="about-chips">
                <span className="pill">{t('app.version', { v: info.wersja })}</span>
                {info.dev && <span className="pill">dev</span>}
                {info.licencja && <span className="pill">{info.licencja}</span>}
              </div>
              <p className="about-tag">{t('about.tagline')}</p>
              <p className="about-compat">{t('about.compat')}</p>

              <div className="btn-row about-actions">
                {info.strona && (
                  <Button variant="primary" icon="external" title={info.strona} onClick={() => open(info.strona)}>
                    {t('about.website')}
                  </Button>
                )}
                {info.repo && (
                  <>
                    <Button icon="external" title={info.repo} onClick={() => open(info.repo)}>
                      {t('about.repo')}
                    </Button>
                    <Button icon="warn" title={`${info.repo}/issues`} onClick={() => open(`${info.repo}/issues`)}>
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

      {(paths.projekty || paths.ustawienia) && (
        <div className="section about-sec">
          <div className="section-head">
            <h2>{t('about.files')}</h2>
          </div>
          <ul className="about-list">
            {paths.projekty && <PathRow label={t('about.pathProjects')} path={paths.projekty} />}
            {paths.ustawienia && <PathRow label={t('about.pathSettings')} path={paths.ustawienia} />}
          </ul>
        </div>
      )}

      {(paths.exe || paths.webview2) && (
        <TechnicalPaths exe={paths.exe} webView2={paths.webview2} />
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
          {t('app.name')} {info.wersja}
        </span>
        <span>{t('about.copyright')}</span>
        {info.licencja && <span>{t('about.appLicense', { lic: info.licencja })}</span>}
      </div>
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
          void bridge.call('shell.showInExplorer', { sciezka: path }).catch((failure: unknown) => toast.warn(messageOf(failure)))
        }
      />
    </li>
  );
}

/** Where the executable and WebView2 keep themselves — folded away, because most people never need it. */
function TechnicalPaths({ exe, webView2 }: { exe?: string; webView2?: string }) {
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
            {exe && <PathRow label={t('about.pathExe')} path={exe} />}
            {webView2 && <PathRow label={t('about.pathWebView')} path={webView2} />}
          </ul>
        </div>
      )}
    </div>
  );
}
