// types/dev.d.ts — the handle developer mode puts on window, for the console and for `Duble.exe --exec`.
import type { bridge } from '../bridge/bridge';

declare global {
  interface Window {
    duble?: { bridge: typeof bridge };
  }
}

export {};
