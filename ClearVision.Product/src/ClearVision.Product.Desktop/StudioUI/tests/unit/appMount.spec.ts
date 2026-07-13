import { nextTick } from 'vue';
import { afterEach, describe, expect, it } from 'vitest';
import { mountStudioApp, type MountedStudioApp } from '@/app/createStudioApp';
import { createStudioPlatform, type StudioPlatform } from '@/app/studioPlatform';
import type { ApiTransport } from '@/platform/api';
import { createBrowserHostFake } from '@/platform/host';
import { readBrowserTestStudioStartupConfig } from '@/platform/startup';
import { createTestRouter } from '@/test-support/createTestRouter';

let mountedApp: MountedStudioApp | undefined;

afterEach(() => {
  mountedApp?.unmount();
  mountedApp = undefined;
  document.body.innerHTML = '';
});

function createTestPlatform(): StudioPlatform {
  const startup = readBrowserTestStudioStartupConfig({
    schemaVersion: 1,
    uiKind: 'studio-ui',
    hostKind: 'browser-test',
    apiBaseUrl: 'http://localhost:5000/api',
    studioUiBasePath: '/studio/',
    featureFlags: {}
  }, { pageOrigin: 'http://localhost:5000' });
  const api: ApiTransport = Object.freeze({
    apiBaseUrl: startup.apiBaseUrl,
    async get<T>(path: string): Promise<T | undefined> {
      const payload = path === '/health'
        ? { status: 'Healthy', port: 5000 }
        : { requiresInitialAdminSetup: false };
      return payload as T;
    }
  });

  return createStudioPlatform({
    startup,
    host: createBrowserHostFake(),
    api,
    tokenProvider: () => 'test-token'
  });
}

describe('mountStudioApp', () => {
  it('mounts the diagnostics route once and disposes idempotently', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    const platform = createTestPlatform();

    mountedApp = await mountStudioApp('#app', { router, platform });

    expect(router.currentRoute.value.path).toBe('/diagnostics');
    expect(document.querySelector('[data-studio-page="diagnostics"]')).not.toBeNull();
    expect(document.body.textContent).toContain('browser-test');
    expect(document.body.textContent).toContain('browser-fake');
    expect(document.body.textContent).toContain('Token present');

    mountedApp.unmount();
    mountedApp.unmount();
    expect(document.querySelector('#app')?.innerHTML).toBe('');
    expect(platform.host.getDiagnostics().disposed).toBe(true);
  });

  it('renders the reserved design and canvas routes without mounting business capabilities', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router,
      platform: createTestPlatform()
    });

    await router.push('/labs/design');
    await nextTick();
    expect(document.querySelector('[data-studio-page="design-placeholder"]')).not.toBeNull();

    await router.push('/labs/canvas');
    await nextTick();
    expect(document.querySelector('[data-studio-page="canvas-placeholder"]')).not.toBeNull();
  });

  it('disposes the platform when the mount target is missing', async () => {
    const platform = createTestPlatform();

    await expect(mountStudioApp('#missing', {
      router: createTestRouter(),
      platform
    })).rejects.toThrow('mount target was not found');

    expect(platform.host.getDiagnostics().disposed).toBe(true);
  });
});
