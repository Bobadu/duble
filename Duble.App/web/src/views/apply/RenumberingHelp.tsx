// views/apply/RenumberingHelp.tsx — "what happens after clothes are removed?": the gap left in the game's
// slot numbering, and what to do about the pack's .ymt or .meta.
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import { useTranslate } from '../../i18n';

const PARAGRAPHS = ['help.renumber1', 'help.renumber2', 'help.renumber3'] as const;

export function RenumberingHelp({ onClose }: { onClose: () => void }) {
  const t = useTranslate();

  return (
    <Modal
      title={t('help.renumberTitle')}
      wide
      onClose={onClose}
      footer={
        <Button variant="primary" onClick={onClose}>
          {t('common.ok')}
        </Button>
      }
    >
      <div className="help-text">
        {PARAGRAPHS.map((key) => (
          <p key={key}>{t(key)}</p>
        ))}
      </div>
    </Modal>
  );
}
