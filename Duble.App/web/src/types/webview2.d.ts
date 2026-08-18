// types/webview2.d.ts — the part of WebView2 the interface uses.
//
// window.chrome.webview only exists inside the application. It is optional here on purpose: opening the built
// page in a plain browser has to fail with a clear "not running inside Duble" rather than a TypeError.

interface WebView2HostMessageEvent {
  readonly data: unknown;
}

interface WebView2Host {
  postMessage(message: unknown): void;

  /**
   * Posts a message together with objects WebView2 marshals for the host — File objects become
   * CoreWebView2File on the C# side, which is the only way to learn the PATH of a dropped file.
   */
  postMessageWithAdditionalObjects(message: unknown, objects: readonly unknown[]): void;

  addEventListener(type: 'message', listener: (event: WebView2HostMessageEvent) => void): void;
  removeEventListener(type: 'message', listener: (event: WebView2HostMessageEvent) => void): void;
}

interface Window {
  readonly chrome?: { readonly webview?: WebView2Host };
}
