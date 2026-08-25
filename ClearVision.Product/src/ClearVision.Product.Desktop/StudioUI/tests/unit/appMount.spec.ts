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

interface TestPlatformOptions {
  readonly role?: 'Admin' | 'Engineer' | 'Operator';
  readonly startupProfile?: string;
  readonly profileAllowedRoles?: readonly ('Admin' | 'Engineer' | 'Operator')[];
  readonly featureFlags?: Readonly<Record<string, boolean>>;
  readonly requestPaths?: string[];
}

function createTestPlatform(options: TestPlatformOptions = {}): StudioPlatform {
  const startup = readBrowserTestStudioStartupConfig({
    schemaVersion: 1,
    uiKind: 'studio-ui',
    hostKind: 'browser-test',
    apiBaseUrl: 'http://localhost:5000/api',
    studioUiBasePath: '/studio/',
    startupProfile: options.startupProfile ?? 'NEXT_DEFAULT',
    profileAllowedRoles: options.profileAllowedRoles ?? ['Admin', 'Engineer', 'Operator'],
    featureFlags: {
      'Studio2.Settings': true,
      'Studio2.AiWorkbench': true,
      ...options.featureFlags
    }
  }, { pageOrigin: 'http://localhost:5000' });
  const api: ApiTransport = Object.freeze({
    apiBaseUrl: startup.apiBaseUrl,
    async get<T>(path: string): Promise<T | undefined> {
      options.requestPaths?.push(path);
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
          ? { userId: 'user-1', username: 'tester', role: options.role ?? 'Engineer' }
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
    expect(document.querySelector('[data-product-nav="/settings"]')).not.toBeNull();
    expect(document.querySelector('[data-product-more]')).not.toBeNull();
    document.querySelector<HTMLButtonElement>('[data-product-more] button[aria-haspopup="menu"]')?.click();
    await nextTick();
    expect(document.querySelector('[data-product-nav="/overview"]')).not.toBeNull();
    expect(document.querySelector('[data-product-nav="/operators"]')).not.toBeNull();
    expect(document.querySelector('[data-product-nav="/diagnostics"]')).not.toBeNull();
    expect(document.querySelector('[data-product-nav="/about"]')).not.toBeNull();
    expect(document.querySelector('[data-product-nav="/stations"]')).toBeNull();

    mountedApp.unmount();
    mountedApp.unmount();
    expect(document.querySelector('#app')?.innerHTML).toBe('');
    expect(platform.host.getDiagnostics().disposed).toBe(true);
  });

  it('composes standard, workspace and product-rail shells with one main landmark', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router,
      platform: createTestPlatform()
    });

    for (const expectation of [
      { path: '/projects', shellMode: 'standard', workspace: 'false', railTone: null },
      {
        path: '/projects/11111111-1111-4111-8111-111111111111/workspace',
        shellMode: 'workspace',
        workspace: 'true',
        railTone: 'light'
      },
      { path: '/ai', shellMode: 'product-rail', workspace: 'false', railTone: 'dark' },
      { path: '/settings', shellMode: 'product-rail', workspace: 'false', railTone: 'dark' }
    ] as const) {
      await router.push(expectation.path);
      await vi.waitFor(() => {
        expect(document.querySelector('[data-product-shell]')?.getAttribute('data-shell-mode'))
          .toBe(expectation.shellMode);
      });
      expect(document.querySelectorAll('[data-product-shell="ready"]')).toHaveLength(1);
      expect(document.querySelector('[data-product-shell]')?.getAttribute('data-workspace-mode'))
        .toBe(expectation.workspace);
      expect(document.querySelectorAll('main')).toHaveLength(1);
      expect(document.querySelector('main')?.id).toBe('product-main');
      const rail = document.querySelector('[data-product-rail-tone]');
      if (expectation.railTone) {
        expect(rail?.getAttribute('data-product-rail-tone')).toBe(expectation.railTone);
      } else {
        expect(rail).toBeNull();
      }
    }
  });

  it('closes shell menus and restores main focus for query-only and hash-only routes', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router,
      platform: createTestPlatform()
    });
    const main = document.querySelector<HTMLElement>('#product-main')!;
    const moreTrigger = document.querySelector<HTMLButtonElement>(
      '[data-product-more] button[aria-haspopup="menu"]'
    )!;
    const appearanceTrigger = document.querySelector<HTMLButtonElement>(
      '[data-product-appearance] button[aria-haspopup="menu"]'
    )!;

    moreTrigger.click();
    await nextTick();
    expect(moreTrigger.getAttribute('aria-expanded')).toBe('true');
    await router.push({ path: '/projects', query: { view: 'recent' } });
    await vi.waitFor(() => {
      expect(moreTrigger.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(main);
    });

    appearanceTrigger.click();
    await nextTick();
    expect(appearanceTrigger.getAttribute('aria-expanded')).toBe('true');
    await router.push({ path: '/projects', query: { view: 'recent' }, hash: '#project-list' });
    await vi.waitFor(() => {
      expect(appearanceTrigger.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(main);
    });
  });

  it('rejects profile, role and flag admission before the target capability mounts or requests', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const profileRequests: string[] = [];
    const router = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router,
      platform: createTestPlatform({
        startupProfile: 'NEXT_INTERNAL_PILOT',
        profileAllowedRoles: ['Admin'],
        requestPaths: profileRequests
      })
    });
    await vi.waitFor(() => {
      expect(router.currentRoute.value.path).toBe('/forbidden');
      expect(document.querySelector('[data-studio-page="forbidden"]')).not.toBeNull();
    });
    expect(document.querySelector('[data-product-shell]')).toBeNull();
    expect(document.querySelector('[data-capability]')).toBeNull();
    expect(profileRequests.filter(path => !['/health', 'auth/setup-status', 'auth/me'].includes(path)))
      .toEqual([]);

    mountedApp.unmount();
    mountedApp = undefined;
    document.body.innerHTML = '<div id="app"></div>';
    const roleRequests: string[] = [];
    const roleRouter = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router: roleRouter,
      platform: createTestPlatform({ role: 'Operator', requestPaths: roleRequests })
    });
    await vi.waitFor(() => expect(roleRouter.currentRoute.value.path).toBe('/projects'));
    roleRequests.length = 0;
    await roleRouter.push('/projects/11111111-1111-4111-8111-111111111111/workspace');
    await vi.waitFor(() => expect(roleRouter.currentRoute.value.path).toBe('/forbidden'));
    expect(document.querySelector('.workspace-page')).toBeNull();
    expect(roleRequests).toEqual([]);

    mountedApp.unmount();
    mountedApp = undefined;
    document.body.innerHTML = '<div id="app"></div>';
    const flagRequests: string[] = [];
    const flagRouter = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router: flagRouter,
      platform: createTestPlatform({
        featureFlags: { 'Studio2.AiWorkbench': false },
        requestPaths: flagRequests
      })
    });
    await vi.waitFor(() => expect(flagRouter.currentRoute.value.path).toBe('/projects'));
    flagRequests.length = 0;
    await flagRouter.push('/ai');
    await vi.waitFor(() => expect(flagRouter.currentRoute.value.path).toBe('/forbidden'));
    expect(document.querySelector('.ai-workbench-page')).toBeNull();
    expect(flagRequests).toEqual([]);
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

  it('closes lifecycle-owned top-bar menus mutually, on outside pointer input, and returns focus on Escape', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    mountedApp = await mountStudioApp('#app', {
      router,
      platform: createTestPlatform()
    });
    await nextTick();

    const appearanceTrigger = document.querySelector<HTMLButtonElement>('[data-product-appearance] button[aria-haspopup="menu"]')!;
    const moreTrigger = document.querySelector<HTMLButtonElement>('[data-product-more] button[aria-haspopup="menu"]')!;

    appearanceTrigger.click();
    await nextTick();
    expect(appearanceTrigger.getAttribute('aria-expanded')).toBe('true');

    moreTrigger.click();
    await nextTick();
    expect(appearanceTrigger.getAttribute('aria-expanded')).toBe('false');
    expect(moreTrigger.getAttribute('aria-expanded')).toBe('true');

    document.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    await nextTick();
    expect(moreTrigger.getAttribute('aria-expanded')).toBe('false');

    appearanceTrigger.click();
    await nextTick();
    const appearanceControl = document.querySelector<HTMLElement>('[role="menu"][aria-label="外观设置"] [role="menuitemcheckbox"]')!;
    appearanceControl.focus();
    appearanceControl.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'Escape',
      bubbles: true,
      cancelable: true
    }));
    await nextTick();

    expect(appearanceTrigger.getAttribute('aria-expanded')).toBe('false');
    await vi.waitFor(() => expect(document.activeElement).toBe(appearanceTrigger));
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
    expect(document.body.textContent).toContain('账号验证由本地服务统一管理');

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
