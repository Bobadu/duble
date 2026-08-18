// vite.config.ts — the interface is built into dist\ and embedded in the executable by Duble.App.csproj.
//
// The page is served from https://duble.app/ by the application itself, so asset URLs have to be relative
// (base './'): there is no server that would resolve an absolute path. The only browser this ever runs in is
// the WebView2 that ships with the application, which is why the target can be current and there are no
// polyfills, no legacy build and no browser matrix.
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    target: 'es2022',
    outDir: 'dist',
    emptyOutDir: true,
    // no source maps in the shipped bundle: they are four megabytes, they go inside the executable, and the
    // only place they could be read is the developer build, which runs from the dev server anyway
    sourcemap: false,
    // three.js is the large chunk, and it is deliberately one: it loads when a 3D tab is opened, not before
    chunkSizeWarningLimit: 1500,
  },
  server: {
    port: 5173,
    // the application is told to load exactly this address with --ui-url; a silent move to 5174 would leave
    // it looking at nothing
    strictPort: true,
  },
});
