import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ApiTransport, ApiUnauthorizedHandler } from '@/platform/api';
import { createMemoryTokenPort } from '@/platform/auth';
import type { ProductRuntime } from '@/app/productRuntime';
import type { StudioPlatform } from '@/app/studioPlatform';

const mocks = vi.hoisted(() => ({
  createProductRuntime: vi.fn()
}));

vi.mock('@/app/productRuntime', async importOriginal => ({
  ...(await importOriginal()),
  createProductRuntime: mocks.createProductRuntime
}));

import { createAuthLifecycleRoot } from '@/app/auth';

function fakeRuntime(requiresPreservation = true): ProductRuntime {
  return {
    api: {} as ProductRuntime['api'],
    featureFlags: Object.freeze({}),
    queries: {} as ProductRuntime['queries'],
    session: {} as ProductRuntime['session'],
    systemStatus: {} as ProductRuntime['systemStatus'],
    preferences: {} as ProductRuntime['preferences'],
    projectLifecycle: {} as ProductRuntime['projectLifecycle'],
    leaveGuard: {} as ProductRuntime['leaveGuard'],
    workspace: {} as ProductRuntime['workspace'],
    prepareForProtectedTransition: vi.fn(async () => true),
    quarantineForSessionExpiration: vi.fn(() => Object.freeze({
      requiresPreservation,
      activeWorkspaceOwnerCount: requiresPreservation ? 1 : 0,
      runIdentities: Object.freeze([])
    })),
    reconcileAfterReauthentication: vi.fn(async () => true),
    dispose: vi.fn()
  };
}

function platform(runtime: ProductRuntime) {
  let unauthorized: ApiUnauthorizedHandler | null = null;
  let generation = () => 0;
  const tokenPort = createMemoryTokenPort('token-1');
  const api: ApiTransport = Object.freeze({
    apiBaseUrl: 'http://localhost:5000/api',
    setUnauthorizedHandler(handler: ApiUnauthorizedHandler | null, provider: () => number = () => 0) {
      unauthorized = handler;
      generation = provider;
    },
    async get<T>(path: string): Promise<T | undefined> {
      const payload = path === 'auth/setup-status'
        ? {
            requiresInitialAdminSetup: false,
            usernameMinLength: 3,
            passwordMinLength: 6,
            requiresUppercase: false,
            requiresLowercase: false,
            requiresDigit: false
          }
        : { userId: 'user-1', username: 'engineer', role: 'Engineer' };
      return payload as T;
    },
    async post<T>(): Promise<T | undefined> {
      return { token: 'token-2' } as T;
    }
  });
  const value = {
    startup: {
      schemaVersion: 1,
      uiKind: 'studio-ui',
      hostKind: 'browser-test',
      apiBaseUrl: 'http://localhost:5000/api',
      studioUiBasePath: '/studio/',
      startupProfile: 'NEXT_DEFAULT',
      profileAllowedRoles: ['Admin', 'Engineer', 'Operator'],
      featureFlags: Object.freeze({})
    },
    host: { dispose: vi.fn() },
    api,
    tokenPort,
    hasToken: () => Boolean(tokenPort.readToken()),
    dispose: vi.fn()
  } as unknown as StudioPlatform;
  mocks.createProductRuntime.mockReturnValue(runtime);
  return {
    value,
    expire: () => unauthorized?.({
      method: 'GET',
      path: 'projects',
      url: 'http://localhost:5000/api/projects',
      sessionGeneration: generation()
    })
  };
}

describe('AuthLifecycleRoot ProductRuntime ledger', () => {
  beforeEach(() => mocks.createProductRuntime.mockReset());

  it('mounts once after auth, unmounts on 401, reconciles and restores the quarantined runtime', async () => {
    const runtime = fakeRuntime(true);
    const h = platform(runtime);
    const root = createAuthLifecycleRoot(h.value);

    await root.start();
    expect(root.productRuntime.value?.dispose).toBe(runtime.dispose);
    expect(mocks.createProductRuntime).toHaveBeenCalledTimes(1);
    await root.auth.refreshSession();
    expect(mocks.createProductRuntime).toHaveBeenCalledTimes(1);
    expect(runtime.dispose).not.toHaveBeenCalled();

    await h.expire();
    expect(root.productRuntime.value).toBeNull();
    expect(runtime.quarantineForSessionExpiration).toHaveBeenCalledTimes(1);
    expect(runtime.dispose).not.toHaveBeenCalled();

    await root.auth.login({ username: 'engineer', password: 'secret' });
    expect(runtime.reconcileAfterReauthentication).toHaveBeenCalledTimes(1);
    expect(root.productRuntime.value?.dispose).toBe(runtime.dispose);
    expect(mocks.createProductRuntime).toHaveBeenCalledTimes(1);
    root.dispose();
  });

  it('disposes an empty runtime on expiration and creates a fresh owner set after login', async () => {
    const first = fakeRuntime(false);
    const second = fakeRuntime(false);
    const h = platform(first);
    mocks.createProductRuntime.mockReturnValueOnce(first).mockReturnValueOnce(second);
    const root = createAuthLifecycleRoot(h.value);

    await root.start();
    await h.expire();
    expect(first.dispose).toHaveBeenCalledTimes(1);
    await root.auth.login({ username: 'engineer', password: 'secret' });
    expect(root.productRuntime.value?.dispose).toBe(second.dispose);
    expect(mocks.createProductRuntime).toHaveBeenCalledTimes(2);
    root.dispose();
  });
});
