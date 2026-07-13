export type StudioHostAdapterKind = 'desktop-webview2' | 'browser-fake';

export type HostDiagnosticChannel = 'webview2-web-message' | 'browser-fake';

export type HostMessageHandler = (message: unknown) => void;

export type HostUnsubscribe = () => void;

export interface HostDiagnostics {
  readonly kind: StudioHostAdapterKind;
  readonly channel: HostDiagnosticChannel;
  readonly isWebView2: boolean;
  readonly disposed: boolean;
  readonly activeSubscriptionCount: number;
}

export interface StudioHostAdapter {
  readonly kind: StudioHostAdapterKind;
  postMessage(message: unknown): void;
  subscribe(handler: HostMessageHandler): HostUnsubscribe;
  getDiagnostics(): HostDiagnostics;
  dispose(): void;
}

export class HostAdapterDisposedError extends Error {
  constructor() {
    super('The Studio host adapter has been disposed.');
    this.name = 'HostAdapterDisposedError';
  }
}

export class WebView2UnavailableError extends Error {
  constructor() {
    super('The WebView2 web-message channel is unavailable.');
    this.name = 'WebView2UnavailableError';
  }
}
