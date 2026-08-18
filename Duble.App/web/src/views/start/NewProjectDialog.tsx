// views/start/NewProjectDialog.tsx — name it, say where it goes, create it.
import { useState } from 'react';
import { navigate } from '../../app/router';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import { useTranslate } from '../../i18n';

export function NewProjectDialog({ onClose }: { onClose: () => void }) {
  const t = useTranslate();
  const recent = useCommand('project.recent', null);

  const [name, setName] = useState('');
  const [folder, setFolder] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  // the default folder is only known once the host answers, and the user may have typed their own by then
  const shownFolder = folder ?? recent.data?.folderDomyslny ?? '';

  const browse = async () => {
    const picked = await bridge.call('project.pickFolder');
    if (picked.sciezka) setFolder(picked.sciezka);
  };

  const create = async () => {
    const trimmed = name.trim();
    if (!trimmed) {
      setError(t('start.nameRequired'));
      return;
    }

    setCreating(true);
    try {
      await bridge.call('project.new', { nazwa: trimmed, folder: shownFolder.trim() });
      onClose();
      navigate('sources');
    } catch (failure) {
      // the only failure worth its own sentence: a project of that name is already there
      setError(errorCodeOf(failure) === ErrorCode.Io ? t('start.exists') : messageOf(failure));
      setCreating(false);
    }
  };

  return (
    <Modal
      title={t('start.new')}
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button variant="primary" disabled={creating} onClick={create}>
            {t('start.create')}
          </Button>
        </>
      }
    >
      <div className="field">
        <label htmlFor="project-name">{t('start.projectName')}</label>
        <input
          id="project-name"
          className="input"
          autoComplete="off"
          placeholder={t('start.projectNamePlaceholder')}
          value={name}
          onChange={(event) => {
            setName(event.target.value);
            setError(null);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') void create();
          }}
        />
      </div>

      <div className="field">
        <label htmlFor="project-folder">{t('start.projectFolder')}</label>
        <div className="row">
          <input
            id="project-folder"
            className="input"
            value={shownFolder}
            onChange={(event) => setFolder(event.target.value)}
          />
          <Button icon="folder" onClick={browse}>
            {t('common.browse')}
          </Button>
        </div>
      </div>

      {error && <div className="error-text">{error}</div>}
    </Modal>
  );
}
