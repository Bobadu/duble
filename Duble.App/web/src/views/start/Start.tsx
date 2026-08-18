// views/start/Start.tsx — the first screen: what Duble is, and the projects to carry on with.
import { useState } from 'react';
import { navigate } from '../../app/router';
import { bridge, messageOf } from '../../bridge/bridge';
import type { RecentProject } from '../../bridge/contract';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { Icon } from '../../components/Icon';
import { MenuButton } from '../../components/Menu';
import { useToast } from '../../components/Toast';
import { shortenPath, useI18n, useTranslate } from '../../i18n';
import { NewProjectDialog } from './NewProjectDialog';

export function Start() {
  const t = useTranslate();
  const toast = useToast();
  const [creating, setCreating] = useState(false);

  const recent = useCommand('project.recent', null, { reloadOn: ['project.opened', 'project.closed'] });
  const projects = recent.data?.recent ?? [];

  const open = async () => {
    try {
      const answer = await bridge.call('project.pickOpen');
      if (answer.project) navigate('sources');
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  return (
    <>
      <div className="hero">
        <Icon name="logo" className="logo" />
        <div>
          <h1>
            {t('start.title')}
            <span className="by">{t('app.by')}</span>
          </h1>
          <p className="sub">{t('start.subtitle')}</p>
        </div>
      </div>

      <div className="hero-actions">
        <Button variant="primary" icon="plus" className="lg" onClick={() => setCreating(true)}>
          {t('start.new')}
        </Button>
        <Button icon="folder" className="lg" onClick={open}>
          {t('start.open')}
        </Button>
      </div>

      <div className="section">
        <div className="section-head">
          <h2>{t('start.recent')}</h2>
          <span className="count">{projects.length || ''}</span>
        </div>

        {projects.length === 0 ? (
          <div className="empty">
            <Icon name="file" />
            <p>{t('start.empty')}</p>
          </div>
        ) : (
          <div className="grid-cards">
            {projects.map((project) => (
              <ProjectCard key={project.path} project={project} onForgotten={recent.reload} />
            ))}
          </div>
        )}
      </div>

      {creating && <NewProjectDialog onClose={() => setCreating(false)} />}
    </>
  );
}

function ProjectCard({ project, onForgotten }: { project: RecentProject; onForgotten: () => void }) {
  const t = useTranslate();
  const { formatDate } = useI18n();
  const toast = useToast();

  const open = async () => {
    if (!project.exists) return;
    try {
      await bridge.call('project.open', { path: project.path });
      navigate('sources');
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  return (
    <div
      className={project.exists ? 'card proj-card clickable' : 'card proj-card clickable missing'}
      tabIndex={0}
      role="button"
      onClick={open}
      onKeyDown={(event) => {
        if (event.key === 'Enter') void open();
      }}
    >
      <div className="card-body">
        <div className="ico-box">
          <Icon name={project.exists ? 'file' : 'warn'} />
        </div>

        <div className="info">
          <div className="name">{project.name}</div>
          <div className="path mono" title={project.path}>
            {shortenPath(folderOf(project.path), 34)}
          </div>
          <div className="meta">
            <Icon name="history" />{' '}
            {project.exists ? t('start.lastOpened', { d: formatDate(project.lastOpened) }) : t('start.missing')}
          </div>
        </div>

        <div onClick={(event) => event.stopPropagation()}>
          <MenuButton
            title={t('common.more')}
            items={[
              {
                label: t('sources.openFolder'),
                icon: 'external',
                run: () => void bridge.call('shell.showInExplorer', { path: project.path }).catch(() => undefined),
              },
              {
                label: t('start.remove'),
                icon: 'trash',
                danger: true,
                run: () => {
                  void bridge.call('project.forget', { path: project.path }).then(onForgotten);
                },
              },
            ]}
          />
        </div>
      </div>
    </div>
  );
}

/** The folder a project file sits in, which is what the card shows under its name. */
function folderOf(file: string): string {
  return file.replace(/[\\/][^\\/]*$/, '');
}
