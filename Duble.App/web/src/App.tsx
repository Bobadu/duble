// App.tsx — the application: the frame, the screen the route points at, and the few things that belong to the
// window rather than to any one screen (files dragged in, keyboard shortcuts).
import { useEffect } from 'react';
import { useAnnounceReady, useApp } from './app/AppState';
import { Rail, StatusBar, TitleBar } from './app/Shell';
import { navigate, useRoute, type ViewName } from './app/router';
import { bridge, messageOf } from './bridge/bridge';
import { useBridgeEvent } from './bridge/hooks';
import { useToast } from './components/Toast';
import { Duplicates } from './views/duplicates/Duplicates';
import { NotPortedYet } from './views/NotPortedYet';

export function App() {
  const route = useRoute();

  useAnnounceReady();
  useDroppedFiles();
  useShortcuts();

  return (
    <>
      <TitleBar />
      <Rail />
      <main className="main">
        <div className="wrap" data-view={route.param ? detailViewOf(route.view) : route.view}>
          <Screen view={route.view} param={route.param} />
        </div>
      </main>
      <StatusBar />
    </>
  );
}

/** A section showing one item lays out differently from the list it came from, and says so to the stylesheet. */
function detailViewOf(view: ViewName): string {
  if (view === 'duplicates') return 'group';
  if (view === 'catalog') return 'item';
  return view;
}

function Screen({ view, param }: { view: ViewName; param?: string }) {
  switch (view) {
    case 'duplicates':
      return param ? <NotPortedYet view="group" /> : <Duplicates />;
    default:
      return <NotPortedYet view={view} />;
  }
}

/**
 * Files dragged from Explorer. The page cannot learn their paths — an HTML5 drop does not carry one — so the
 * File objects are handed to the host, which reads the paths and sends them back as an event.
 */
function useDroppedFiles(): void {
  const toast = useToast();

  useBridgeEvent('files.dropped', (data) => {
    // the Sources screen is where a source is added, so that is where a drop lands
    sessionStorage.setItem('dropped', JSON.stringify(data.sciezki));
    navigate('sources');
  });

  useEffect(() => {
    let depth = 0;

    const enter = (event: DragEvent) => {
      depth++;
      document.body.classList.add('dragging');
      event.preventDefault();
    };
    const leave = () => {
      if (--depth <= 0) {
        depth = 0;
        document.body.classList.remove('dragging');
      }
    };
    const over = (event: DragEvent) => event.preventDefault();
    const drop = (event: DragEvent) => {
      event.preventDefault();
      depth = 0;
      document.body.classList.remove('dragging');

      const files = event.dataTransfer?.files;
      const host = window.chrome?.webview;
      if (!files?.length || !host) return;

      try {
        host.postMessageWithAdditionalObjects({ id: 'drop', cmd: 'files.drop', args: null }, [...files]);
      } catch (failure) {
        toast.error(messageOf(failure));
      }
    };

    document.addEventListener('dragenter', enter);
    document.addEventListener('dragleave', leave);
    document.addEventListener('dragover', over);
    document.addEventListener('drop', drop);
    return () => {
      document.removeEventListener('dragenter', enter);
      document.removeEventListener('dragleave', leave);
      document.removeEventListener('dragover', over);
      document.removeEventListener('drop', drop);
    };
  }, [toast]);
}

function useShortcuts(): void {
  const { project } = useApp();
  const toast = useToast();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === 'o') {
        event.preventDefault();
        void bridge.call('project.pickOpen').catch(() => undefined);
      }
      if (event.key === 'F5' && project) {
        event.preventDefault();
        bridge.call('sources.index', {}).catch((failure: unknown) => toast.warn(messageOf(failure)));
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [project, toast]);
}
