// views/sources/DetectGamesDialog.tsx — the installed games that were found, and the folders in them worth
// indexing. Everything found is ticked; the user unticks what they do not want.
import { useState } from 'react';
import { useCommand } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import { useTranslate } from '../../i18n';

export function DetectGamesDialog({ onClose, onAdd }: { onClose: () => void; onAdd: (paths: string[]) => void }) {
  const t = useTranslate();
  const detected = useCommand('sources.detectGames', null);
  const games = detected.data?.gry ?? [];

  const suggested = games.flatMap((game) => game.propozycje.map((folder) => folder.sciezka));
  const [unticked, setUnticked] = useState<ReadonlySet<string>>(new Set());
  const chosen = suggested.filter((path) => !unticked.has(path));

  const toggle = (path: string) =>
    setUnticked((previous) => {
      const next = new Set(previous);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });

  return (
    <Modal
      title={t('sources.detectTitle')}
      wide
      onClose={onClose}
      footer={
        games.length ? (
          <>
            <Button onClick={onClose}>{t('common.cancel')}</Button>
            <Button
              variant="primary"
              disabled={chosen.length === 0}
              onClick={() => {
                onAdd(chosen);
                onClose();
              }}
            >
              {t('sources.detectAdd')}
            </Button>
          </>
        ) : (
          <Button variant="primary" onClick={onClose}>
            {t('common.close')}
          </Button>
        )
      }
    >
      {detected.loading ? null : games.length === 0 ? (
        <p className="lead">{t('sources.detectNone')}</p>
      ) : (
        games.map((game) => (
          <div key={game.sciezka} className="section detected-game">
            <div className="section-head">
              <h3>{t(game.gra === 'enhanced' ? 'sources.detectEnhanced' : 'sources.detectLegacy')}</h3>
              <span className="count mono">{game.sciezka}</span>
            </div>

            {game.propozycje.length === 0 ? (
              <p className="faint">{t('sources.detectNoFolders')}</p>
            ) : (
              game.propozycje.map((folder) => (
                <label key={folder.sciezka} className="chip detected-folder">
                  <input type="checkbox" checked={!unticked.has(folder.sciezka)} onChange={() => toggle(folder.sciezka)} />
                  <span>{folder.nazwa}</span>
                  <span className="n mono">{folder.sciezka}</span>
                </label>
              ))
            )}
          </div>
        ))
      )}
    </Modal>
  );
}
