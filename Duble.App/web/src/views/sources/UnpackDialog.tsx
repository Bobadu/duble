// views/sources/UnpackDialog.tsx — copy an archived source out into a folder of plain files, which is the only
// way its garments can ever be moved.
import { useState } from 'react';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { Source } from '../../bridge/contract';
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import { useToast } from '../../components/Toast';
import { useTranslate } from '../../i18n';

/** Where the last unpacking went; offered again, because a second one usually goes next to the first. */
const LAST_FOLDER_KEY = 'unpack.folder';

export function UnpackDialog({ source, onClose }: { source: Source; onClose: () => void }) {
  const t = useTranslate();
  const toast = useToast();

  const [folder, setFolder] = useState(() => sessionStorage.getItem(LAST_FOLDER_KEY) ?? '');
  const [addAsSource, setAddAsSource] = useState(true);
  const [starting, setStarting] = useState(false);

  const browse = async () => {
    try {
      const picked = await bridge.call('dialogs.pickFolder', folder ? { start: folder } : {});
      if (picked.path) setFolder(picked.path);
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  const unpack = async () => {
    const target = folder.trim();
    if (!target) {
      toast.warn(t('unpack.needFolder'));
      return;
    }

    setStarting(true);
    sessionStorage.setItem(LAST_FOLDER_KEY, target);
    try {
      await bridge.call('sources.unpack', { id: source.id, folder: target, addAsSource: addAsSource });
      toast.info(t('unpack.running'), { duration: 2500 });
      onClose();
    } catch (failure) {
      const busy = errorCodeOf(failure) === ErrorCode.Busy;
      toast.error(busy ? t('sources.busy') : t('unpack.failed', { error: messageOf(failure) }), { duration: 8000 });
      setStarting(false);
    }
  };

  return (
    <Modal
      title={t('unpack.title')}
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button variant="primary" disabled={starting} onClick={unpack}>
            {t('unpack.go')}
          </Button>
        </>
      }
    >
      <p className="lead">{t('unpack.text', { name: source.name })}</p>

      <div className="field">
        <label htmlFor="unpack-folder">{t('unpack.folder')}</label>
        <div className="row">
          <input
            id="unpack-folder"
            className="input mono"
            placeholder="C:\…"
            value={folder}
            onChange={(event) => setFolder(event.target.value)}
          />
          <Button icon="folder" onClick={browse}>
            {t('apply.pick')}
          </Button>
        </div>
        <p className="help">{t('unpack.folderHint', { name: copyFolderName(source) })}</p>
      </div>

      <label className="check-row">
        <input type="checkbox" checked={addAsSource} onChange={(event) => setAddAsSource(event.target.checked)} />
        <span>{t('unpack.addSource')}</span>
      </label>
    </Modal>
  );
}

/**
 * What the copy will be called, the same way SourceCommands.CopyFolderName works it out in C#: a `dlc.rpf`
 * takes the name of the source, any other archive the name of its file, a folder its own name.
 */
function copyFolderName(source: Source): string {
  if (source.kind !== 'rpf') return source.name;
  const file = source.path.split(/[\\/]/).pop() ?? source.name;
  return /^dlc\.rpf$/i.test(file) ? source.name : file;
}
