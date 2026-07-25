import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { AuthLifecycleOwner, AuthLifecycleProjection } from '@/app/auth';
import { createStudioRouter, installAuthRouteGuard, resolveSafeReturnRoute } from '@/app/router';
import type { StudioStartupConfigV1 } from '@/platform/startup';

function startup(featureFlags: Readonly<Record<string, boolean>> = {}, hostKind: StudioStartupConfigV1['hostKind'] = 'desktop-webview2'): StudioStartupConfigV1 {
  return Object.freeze({
    schemaVersion: 1,
    uiKind: 'studio-ui',
    hostKind,
    apiBaseUrl: 'http://localhost:5000/api',
    studioUiBasePath: '/studio/',
    featureFlags: Object.freeze({ ...featureFlags })
  });
}

function auth(phase: AuthLifecycleProjection['phase'], role: string | null = null) {
  const projection = reactive({
    phase,
    user: role ? { userId: 'u-1', username: 'user', role } : null,
    setupPolicy: null,
    sessionGeneration: 1,
    message: '',
    errorCode: null,
    updatedAt: 1
  }) as unknown as AuthLifecycleProjection;
  const owner = {
    projection,
    start: vi.fn(async () => undefined),
    prepareChangePasswordRoute: vi.fn(async () => true)
  } as unknown as AuthLifecycleOwner;
  return { projection: projection as AuthLifecycleProjection & { phase: AuthLifecycleProjection['phase'] }, owner };
}

describe('G2 route guard', () => {
  it('accepts only allowlisted internal safe return routes', () => {
    expect(resolveSafeReturnRoute('/projects/a?tab=flow')).toBe('/projects/a?tab=flow');
    expect(resolveSafeReturnRoute('/stations')).toBe('/stations');
    expect(resolveSafeReturnRoute('/stations/station-1?tab=health')).toBe('/stations/station-1?tab=health');
    for (const attack of [
      'https://evil.example', '//evil.example', '/labs/design', '/login', '/unknown',
      '/projects/%2f%2fevil', '/projects\\evil', '/projects/../login',
      '/stations/%2f%2fevil', '/stations\\evil', '/stations/../login', '%2F%2Fevil.example'
    ]) {
      expect(resolveSafeReturnRoute(attack), attack).toBeNull();
    }
  });

  it('forces setup-only and redirects protected routes to login with safe return', async () => {
    const setupAuth = auth('setup-required');
    const setupRouter = createStudioRouter(createMemoryHistory());
    installAuthRouteGuard(setupRouter, setupAuth.owner, startup());
    await setupRouter.push('/projects');
    expect(setupRouter.currentRoute.value.path).toBe('/setup');

    const signedOut = auth('unauthenticated');
    const router = createStudioRouter(createMemoryHistory());
    installAuthRouteGuard(router, signedOut.owner, startup());
    await router.push('/projects/abc');
    expect(router.currentRoute.value.path).toBe('/login');
    expect(router.currentRoute.value.query.returnTo).toBe('/projects/abc');

    await router.push('/stations/station-1');
    expect(router.currentRoute.value.path).toBe('/login');
    expect(router.currentRoute.value.query.returnTo).toBe('/stations/station-1');
  });

  it('enforces role, product profile and internal route decisions on direct URLs', async () => {
    const operator = auth('authenticated', 'Operator');
    const router = createStudioRouter(createMemoryHistory());
    installAuthRouteGuard(router, operator.owner, startup());
    await router.push('/projects/00000000-0000-0000-0000-000000000001/workspace');
    expect(router.currentRoute.value.path).toBe('/forbidden');
    await router.push('/diagnostics');
    expect(router.currentRoute.value.path).toBe('/forbidden');
    await router.push('/stations');
    expect(router.currentRoute.value.path).toBe('/forbidden');

    const engineer = auth('authenticated', 'Engineer');
    const browserRouter = createStudioRouter(createMemoryHistory());
    installAuthRouteGuard(browserRouter, engineer.owner, startup({ 'Studio2.StationsRead': true }, 'browser-test'));
    await browserRouter.push('/stations');
    expect(browserRouter.currentRoute.value.path).toBe('/stations');
    await browserRouter.push('/labs/design');
    expect(browserRouter.currentRoute.value.path).toBe('/labs/design');
  });

  it('re-evaluates browser navigation after logout and never restores protected routes', async () => {
    const session = auth('authenticated', 'Engineer');
    const router = createStudioRouter(createMemoryHistory());
    installAuthRouteGuard(router, session.owner, startup());
    await router.push('/overview');
    session.projection.phase = 'unauthenticated';
    await router.push('/projects');
    expect(router.currentRoute.value.path).toBe('/login');
    await router.back();
    await Promise.resolve();
    expect(router.currentRoute.value.path).toBe('/login');
  });

  it('leaves change-password protection to the single product leave guard', async () => {
    const session = auth('authenticated', 'Engineer');
    vi.mocked(session.owner.prepareChangePasswordRoute).mockResolvedValue(false);
    const router = createStudioRouter(createMemoryHistory());
    installAuthRouteGuard(router, session.owner, startup());
    await router.push('/overview');
    await router.push('/change-password');

    expect(router.currentRoute.value.path).toBe('/change-password');
    expect(session.owner.prepareChangePasswordRoute).not.toHaveBeenCalled();
  });
});
