export type StudioUiActiveRoot = 'studio-ui' | null;

export interface StudioUiLifecycleDiagnostics {
  readonly ready: boolean;
  readonly mountCount: number;
  readonly activeRoot: StudioUiActiveRoot;
  readonly hostKind: 'desktop-webview2' | 'browser-test' | null;
  readonly canvasOwnerCount: number;
  readonly unhandledErrorCount: number;
  readonly lastBootstrapError: string | null;
}

export interface StudioUiLifecycleDiagnosticsWindow extends EventTarget {
  readonly __STUDIO_UI_READY__?: boolean;
  readonly __STUDIO_UI_DIAGNOSTICS__?: StudioUiLifecycleDiagnostics;
}

export interface StudioUiLifecycleDiagnosticsOwner {
  readonly diagnostics: StudioUiLifecycleDiagnostics;
  markMounted(hostKind: 'desktop-webview2' | 'browser-test'): void;
  markBootstrapFailed(error: unknown): void;
  dispose(): void;
}

let activeOwner: {
  setCanvasOwnerCount(count: number): void;
} | undefined;

function summarizeError(error: unknown): string {
  if (error instanceof Error && error.message.trim()) {
    return error.message.trim().slice(0, 240);
  }

  return String(error ?? 'Unknown bootstrap error').slice(0, 240);
}

export function reportCanvasOwnerCountForDiagnostics(count: number): void {
  if (!Number.isInteger(count) || count < 0 || count > 1) {
    throw new RangeError('StudioUI Canvas owner count must be either 0 or 1.');
  }

  activeOwner?.setCanvasOwnerCount(count);
}

export function createStudioUiLifecycleDiagnosticsOwner(
  runtimeWindow: StudioUiLifecycleDiagnosticsWindow =
    window as unknown as StudioUiLifecycleDiagnosticsWindow
): StudioUiLifecycleDiagnosticsOwner {
  if (activeOwner) {
    throw new Error('StudioUI lifecycle diagnostics already has an active owner.');
  }

  const state = {
    ready: false,
    mountCount: 0,
    activeRoot: null as StudioUiActiveRoot,
    hostKind: null as 'desktop-webview2' | 'browser-test' | null,
    canvasOwnerCount: 0,
    unhandledErrorCount: 0,
    lastBootstrapError: null as string | null,
    disposed: false
  };
  const diagnostics: StudioUiLifecycleDiagnostics = Object.freeze({
    get ready() {
      return state.ready;
    },
    get mountCount() {
      return state.mountCount;
    },
    get activeRoot() {
      return state.activeRoot;
    },
    get hostKind() {
      return state.hostKind;
    },
    get canvasOwnerCount() {
      return state.canvasOwnerCount;
    },
    get unhandledErrorCount() {
      return state.unhandledErrorCount;
    },
    get lastBootstrapError() {
      return state.lastBootstrapError;
    }
  });
  const countUnhandledError = (): void => {
    if (!state.disposed) {
      state.unhandledErrorCount += 1;
    }
  };

  runtimeWindow.addEventListener('error', countUnhandledError);
  runtimeWindow.addEventListener('unhandledrejection', countUnhandledError);

  Object.defineProperty(runtimeWindow, '__STUDIO_UI_READY__', {
    get: () => state.ready,
    configurable: false,
    enumerable: true
  });
  Object.defineProperty(runtimeWindow, '__STUDIO_UI_DIAGNOSTICS__', {
    value: diagnostics,
    writable: false,
    configurable: false,
    enumerable: true
  });

  const owner = {
    setCanvasOwnerCount(count: number): void {
      state.canvasOwnerCount = count;
    }
  };
  activeOwner = owner;

  return Object.freeze({
    diagnostics,
    markMounted(hostKind: 'desktop-webview2' | 'browser-test'): void {
      if (state.disposed) {
        return;
      }

      state.mountCount += 1;
      state.ready = true;
      state.activeRoot = 'studio-ui';
      state.hostKind = hostKind;
      state.lastBootstrapError = null;
    },
    markBootstrapFailed(error: unknown): void {
      if (state.disposed) {
        return;
      }

      state.ready = false;
      state.activeRoot = null;
      state.lastBootstrapError = summarizeError(error);
    },
    dispose(): void {
      if (state.disposed) {
        return;
      }

      state.disposed = true;
      state.ready = false;
      state.activeRoot = null;
      state.canvasOwnerCount = 0;
      runtimeWindow.removeEventListener('error', countUnhandledError);
      runtimeWindow.removeEventListener('unhandledrejection', countUnhandledError);
      if (activeOwner === owner) {
        activeOwner = undefined;
      }
    }
  });
}
