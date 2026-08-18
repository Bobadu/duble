// views/NotPortedYet.tsx — a placeholder while the screens are moved across one at a time.
//
// It is deliberately unmistakable: nothing here should ever reach a release, and the last thing to do in this
// piece of work is to delete this file.
import { EmptyState } from '../components/EmptyState';

export function NotPortedYet({ view }: { view: string }) {
  return <EmptyState icon="warn" title={`${view} — not ported yet`} hint="This screen is still the old interface." />;
}
