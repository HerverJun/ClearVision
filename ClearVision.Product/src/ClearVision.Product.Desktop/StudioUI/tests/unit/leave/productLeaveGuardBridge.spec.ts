import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import { installProductLeaveGuardBridge } from '@/app/leave';
import type { AuthLifecycleRoot } from '@/app/auth';
import type { ProductLeaveGuardOwner } from '@/app/leave';

function router() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/overview', component: { template: '<div />' } },
      { path: '/projects/:id/workspace', component: { template: '<div />' } },
      { path: '/change-password', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } }
    ]
  });
}

describe('product leave guard bridge', () => {
  it('installs the one route/host adapter and removes it on app unmount', async () => {
    const request = vi.fn(async () => true);
    const hasProtection = vi.fn(() => true);
    const guard = { request, hasProtection } as unknown as ProductLeaveGuardOwner;
    const authRoot = {
      auth: { projection: { phase: 'authenticated' } },
      getProductLeaveGuard: vi.fn(() => guard)
    } as unknown as AuthLifecycleRoot;
    const testRouter = router();
    const dispose = installProductLeaveGuardBridge(testRouter, authRoot);

    await testRouter.push('/projects/11111111-1111-4111-8111-111111111111/workspace');
    await testRouter.push('/projects/22222222-2222-4222-8222-222222222222/workspace');
    expect(request).toHaveBeenLastCalledWith('project-switch');

    const flush = (window as Window & {
      __clearVisionFlushProjectWorkspace?: () => Promise<boolean>;
    }).__clearVisionFlushProjectWorkspace;
    await expect(flush?.()).resolves.toBe(true);
    expect(request).toHaveBeenLastCalledWith('host-close');

    const beforeUnload = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(beforeUnload);
    expect(hasProtection).toHaveBeenCalled();
    expect(beforeUnload.defaultPrevented).toBe(true);

    dispose();
    expect((window as Window & { __clearVisionFlushProjectWorkspace?: unknown })
      .__clearVisionFlushProjectWorkspace).toBeUndefined();
  });

  it('rejects a second mounted bridge instead of replacing the Host-close authority', () => {
    const guard = {
      request: vi.fn(async () => true),
      hasProtection: vi.fn(() => false)
    } as unknown as ProductLeaveGuardOwner;
    const authRoot = {
      auth: { projection: { phase: 'authenticated' } },
      getProductLeaveGuard: vi.fn(() => guard)
    } as unknown as AuthLifecycleRoot;
    const testRouter = router();
    const dispose = installProductLeaveGuardBridge(testRouter, authRoot);

    expect(() => installProductLeaveGuardBridge(testRouter, authRoot))
      .toThrow('already installed');

    dispose();
  });
});
