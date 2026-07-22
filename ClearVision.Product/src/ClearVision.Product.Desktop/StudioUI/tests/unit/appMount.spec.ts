import { nextTick } from 'vue';
import { afterEach, describe, expect, it, vi } from 'vitest';
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
        : path === 'auth/setup-status'
          ? {
              requiresInitialAdminSetup: false,
              usernameMinLength: 3,
              passwordMinLength: 6,
              requiresUppercase: false,
              requiresLowercase: false,
              requiresDigit: false
            }
        : path === 'auth/me'
          ? { userId: 'user-1', username: 'tester', role: 'Engineer' }
          : [];
      return payload as T;
    },
    async post<T>(): Promise<T | undefined> { return undefined; },
    async put<T>(): Promise<T | undefined> { return undefined; }
  });

  return createStudioPlatform({
    startup,
    host: createBrowserHostFake(),
    api,
    tokenProvider: () => 'test-token'
  });
}

describe('mountStudioApp', () => {
  it('mounts the projects product shell once and disposes idempotently', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    const platform = createTestPlatform();

    mountedApp = await mountStudioApp('#app', { router, platform });

    expect(router.currentRoute.value.path).toBe('/projects');
    await vi.waitFor(() => {
      expect(document.querySelector('[data-product-shell="ready"]')).not.toBeNull();
      expect(document.querySelector('[data-capability="projects-read"]')).not.toBeNull();
    });
    expect(document.body.textContent).toContain('ClearVision');
    expect(document.body.textContent).toContain('tester');
    expect(document.querySelectorAll('main')).toHaveLength(1);
    expect(document.querySelector('main')?.id).toBe('product-main');
    expect(document.querySelector('.product-layout__skip-link')?.textContent).toContain('跳到主要内容');
    expect(document.querySelector('[data-design-pattern="brand"]')).not.toBeNull();
    expect(document.querySelector('.product-layout__sidebar')).toBeNull();
    expect(document.querySelector('[aria-label="产品主导航"]')).not.toBeNull();
    expect(document.querySelector('[data-product-nav="/projects"]')).not.toBeNull();
    expect(document.querySelector('[data-product-nav="/results"]')).not.toBeNull();

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
    expect(document.querySelector('[data-product-shell]')).toBeNull();
    expect(document.querySelector('[data-internal-lab-layout="ready"]')).not.toBeNull();
    expect(document.querySelector('[data-studio-page="design-placeholder"]')).not.toBeNull();

    await router.push('/labs/canvas');
    await nextTick();
    expect(document.querySelector('[data-studio-page="canvas-placeholder"]')).not.toBeNull();
  });

  it('renders formal diagnostics, about and 404 inside the single product shell', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router,
      platform: createTestPlatform()
    });

    await router.push('/diagnostics');
    await vi.waitFor(() => {
      expect(document.querySelector('[data-studio-page="diagnostics"]')).not.toBeNull();
      expect(document.querySelectorAll('[data-probe-state="ok"]')).toHaveLength(2);
    });
    expect(document.querySelectorAll('[data-product-shell]')).toHaveLength(1);
    expect(document.querySelectorAll('main')).toHaveLength(1);

    await router.push('/about');
    await nextTick();
    expect(document.querySelector('[data-studio-page="about"]')).not.toBeNull();
    expect(document.body.textContent).toContain('authenticated');

    await router.push('/missing-page');
    await nextTick();
    expect(document.querySelector('[data-studio-page="not-found"]')).not.toBeNull();
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
