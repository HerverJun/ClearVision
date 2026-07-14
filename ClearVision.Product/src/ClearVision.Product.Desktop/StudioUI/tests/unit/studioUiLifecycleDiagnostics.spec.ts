import { afterEach, describe, expect, it } from 'vitest';
import {
  createStudioUiLifecycleDiagnosticsOwner,
  reportCanvasOwnerCountForDiagnostics,
  type StudioUiLifecycleDiagnosticsWindow
} from '@/platform/diagnostics/studioUiLifecycleDiagnostics';

let disposeOwner: (() => void) | undefined;

afterEach(() => {
  disposeOwner?.();
  disposeOwner = undefined;
});

function createRuntimeWindow(): StudioUiLifecycleDiagnosticsWindow {
  return new EventTarget() as StudioUiLifecycleDiagnosticsWindow;
}

describe('StudioUI lifecycle diagnostics', () => {
  it('publishes readonly readiness, mount and Canvas owner facts', () => {
    const runtimeWindow = createRuntimeWindow();
    const owner = createStudioUiLifecycleDiagnosticsOwner(runtimeWindow);
    disposeOwner = () => owner.dispose();

    expect(runtimeWindow.__STUDIO_UI_READY__).toBe(false);
    expect(runtimeWindow.__STUDIO_UI_DIAGNOSTICS__?.mountCount).toBe(0);

    owner.markMounted('browser-test');
    reportCanvasOwnerCountForDiagnostics(1);

    expect(runtimeWindow.__STUDIO_UI_READY__).toBe(true);
    expect(runtimeWindow.__STUDIO_UI_DIAGNOSTICS__).toMatchObject({
      ready: true,
      mountCount: 1,
      activeRoot: 'studio-ui',
      hostKind: 'browser-test',
      canvasOwnerCount: 1,
      unhandledErrorCount: 0
    });
    expect(Object.getOwnPropertyDescriptor(
      runtimeWindow,
      '__STUDIO_UI_DIAGNOSTICS__'
    )).toMatchObject({
      writable: false,
      configurable: false
    });
  });

  it('counts unhandled browser errors and disposes its listeners idempotently', () => {
    const runtimeWindow = createRuntimeWindow();
    const owner = createStudioUiLifecycleDiagnosticsOwner(runtimeWindow);
    disposeOwner = () => owner.dispose();

    runtimeWindow.dispatchEvent(new Event('error'));
    runtimeWindow.dispatchEvent(new Event('unhandledrejection'));
    expect(owner.diagnostics.unhandledErrorCount).toBe(2);

    owner.dispose();
    owner.dispose();
    runtimeWindow.dispatchEvent(new Event('error'));
    expect(owner.diagnostics.unhandledErrorCount).toBe(2);
    expect(owner.diagnostics.ready).toBe(false);
    expect(owner.diagnostics.activeRoot).toBeNull();
  });

  it('rejects an impossible Canvas owner count', () => {
    const runtimeWindow = createRuntimeWindow();
    const owner = createStudioUiLifecycleDiagnosticsOwner(runtimeWindow);
    disposeOwner = () => owner.dispose();

    expect(() => reportCanvasOwnerCountForDiagnostics(2)).toThrow(RangeError);
  });
});
