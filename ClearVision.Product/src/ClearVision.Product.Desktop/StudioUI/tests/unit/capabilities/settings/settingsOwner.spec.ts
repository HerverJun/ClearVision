import { describe, expect, it, vi } from 'vitest';
import type { ProductRuntime } from '@/app/productRuntime';
import { ApiConflictError, ApiForbiddenError, type ApiTransport } from '@/platform/api';
import {
  createSettingsOwner,
  createSettingsWriteCoordinator,
  getSettingsOwnerActiveCount,
  type SettingsWriteResult
} from '@/capabilities/settings';

function settingsPayload(safeSubset = false) {
  return safeSubset
    ? {
        safeSubset: true,
        revision: 4,
        general: { softwareTitle: 'ClearVision', theme: 'dark' }
      }
    : {
        revision: 4,
        general: { softwareTitle: 'ClearVision', theme: 'dark', autoStart: false },
        storage: { imageSavePath: 'D:/VisionData', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
        runtime: {
          autoRun: false, stopOnConsecutiveNg: 3, missingMaterialTimeoutSeconds: 120,
          applyProtectionRules: true, runtimePreviewPilot: { mode: 'metadata_only' }
        },
        security: { passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 },
        communication: {}, tcpCommunication: {}, features: {}, cameras: [], activeCameraId: ''
      };
}

type FakeGet = (path: string, options?: { readonly signal?: AbortSignal }) => Promise<unknown>;

function runtime(get: FakeGet): Pick<ProductRuntime, 'api'> {
  return {
    api: { apiBaseUrl: 'http://localhost:5000/api', get: get as ApiTransport['get'] } as ApiTransport
  };
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

describe('F07 G1 Settings owner lifecycle', () => {
  it('uses ProductRuntime.api and exposes a ready public projection without a route', async () => {
    const get = vi.fn(async () => settingsPayload());
    const owner = createSettingsOwner({ runtime: runtime(get), role: 'Admin' });

    await owner.start();

    expect(get).toHaveBeenCalledWith('settings', expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(owner.projection.phase).toBe('ready');
    expect(owner.projection.settings?.sections.general.softwareTitle).toBe('ClearVision');
    expect(owner.diagnostics().inFlightReadCount).toBe(0);
    owner.dispose();
  });

  it('allows Engineer safe projection but fails closed for Operator without a request', async () => {
    const engineerGet = vi.fn(async () => settingsPayload(true));
    const engineer = createSettingsOwner({ runtime: runtime(engineerGet), role: 'Engineer' });
    await engineer.start();
    expect(engineer.projection.phase).toBe('ready');
    expect(engineer.projection.settings?.safeSubset).toBe(true);
    engineer.dispose();

    const operatorGet = vi.fn(async () => settingsPayload());
    const operator = createSettingsOwner({ runtime: runtime(operatorGet), role: 'Operator' });
    await operator.start();
    expect(operator.projection.phase).toBe('forbidden');
    expect(operatorGet).not.toHaveBeenCalled();
    const write = await operator.enqueueSectionWrite('general', async () => 'must-not-run');
    expect(write.status).toBe('forbidden');
    operator.dispose();
  });

  it('projects backend 403 and 409 without exposing raw error payload fields', async () => {
    const forbidden = createSettingsOwner({
      runtime: runtime(vi.fn(async () => {
        throw new ApiForbiddenError({
          url: 'http://localhost:5000/api/settings',
          status: 403,
          statusText: 'Forbidden',
          payload: { code: 'AdminRequired', policy: 'RequireAdmin', token: 'raw-token' },
          responseBody: '{"code":"AdminRequired"}'
        });
      })),
      role: 'Admin'
    });
    await forbidden.refresh();
    expect(forbidden.projection.phase).toBe('forbidden');
    expect(forbidden.projection.error).toMatchObject({ code: 'forbidden', policy: null });
    expect(forbidden.projection.message).not.toContain('raw-token');
    forbidden.dispose();

    const conflict = createSettingsOwner({
      runtime: runtime(vi.fn(async () => {
        throw new ApiConflictError({
          url: 'http://localhost:5000/api/cameras/bindings',
          status: 409,
          statusText: 'Conflict',
          payload: { code: 'conflict', message: 'active stream', secret: 'raw-secret' },
          responseBody: '{"code":"conflict"}'
        });
      })),
      role: 'Admin'
    });
    await conflict.refresh();
    expect(conflict.projection.phase).toBe('error');
    expect(conflict.projection.error).toMatchObject({ code: 'conflict', policy: null });
    expect(conflict.projection.message).not.toContain('raw-secret');
    conflict.dispose();
  });

  it('enforces one mounted Settings owner at a time', () => {
    const first = createSettingsOwner({ runtime: runtime(vi.fn()), role: 'Admin' });
    expect(getSettingsOwnerActiveCount()).toBe(1);
    expect(() => createSettingsOwner({ runtime: runtime(vi.fn()), role: 'Engineer' })).toThrow('Only one mounted');
    first.dispose();
    expect(getSettingsOwnerActiveCount()).toBe(0);
  });

  it('rejects a late refresh projection after a newer refresh supersedes it', async () => {
    const first = deferred<unknown>();
    let call = 0;
    const get = vi.fn((_path: string, options?: { signal?: AbortSignal }) => {
      call += 1;
      if (call === 1) {
        options?.signal?.addEventListener('abort', () => undefined, { once: true });
        return first.promise;
      }
      return Promise.resolve(settingsPayload());
    });
    const owner = createSettingsOwner({ runtime: runtime(get), role: 'Admin' });

    const firstRefresh = owner.refresh();
    await Promise.resolve();
    const secondRefresh = owner.refresh();
    expect(await secondRefresh).toBe(true);
    first.resolve({ ...settingsPayload(), revision: 1 });
    expect(await firstRefresh).toBe(false);
    expect(owner.projection.settings?.revision).toBe(4);
    owner.dispose();
  });

  it('aborts and disposes an in-flight read without allowing a late write', async () => {
    const pending = deferred<unknown>();
    let signal: AbortSignal | undefined;
    const get = vi.fn((_path: string, options?: { signal?: AbortSignal }) => {
      signal = options?.signal;
      return pending.promise;
    });
    const owner = createSettingsOwner({ runtime: runtime(get), role: 'Admin' });
    const refresh = owner.refresh();
    await Promise.resolve();
    owner.dispose('test-dispose');
    expect(signal?.aborted).toBe(true);
    pending.resolve(settingsPayload());
    expect(await refresh).toBe(false);
    expect(owner.projection.phase).toBe('disposed');
    expect(owner.diagnostics().activeAbortControllerCount).toBe(0);
  });
});

describe('F07 G1 Settings section write coordinator skeleton', () => {
  it('serializes writes within one section and preserves independent section queues', async () => {
    const coordinator = createSettingsWriteCoordinator();
    const first = deferred<string>();
    const calls: string[] = [];
    const firstWrite = coordinator.enqueue('general', async () => {
      calls.push('general-1');
      return first.promise;
    });
    const secondWrite = coordinator.enqueue('general', async () => {
      calls.push('general-2');
      return 'second';
    });
    const storageWrite = coordinator.enqueue('storage', async () => {
      calls.push('storage-1');
      return 'storage';
    });

    await Promise.resolve();
    expect(calls).toEqual(['general-1', 'storage-1']);
    first.resolve('first');
    expect((await firstWrite).status).toBe('completed');
    expect((await secondWrite).status).toBe('completed');
    expect((await storageWrite).status).toBe('completed');
    expect(calls).toEqual(['general-1', 'storage-1', 'general-2']);
    coordinator.dispose();
  });

  it('marks invalidated in-flight work stale and queued work cancelled', async () => {
    const coordinator = createSettingsWriteCoordinator();
    const pending = deferred<string>();
    let signal: AbortSignal | undefined;
    const active = coordinator.enqueue('runtime', async context => {
      signal = context.signal;
      return pending.promise;
    });
    const queued = coordinator.enqueue('runtime', async () => 'queued');
    await Promise.resolve();
    coordinator.invalidate('new-settings-generation');
    expect(signal?.aborted).toBe(true);
    pending.resolve('late');
    expect((await active).status).toBe('stale');
    expect((await queued).status).toBe('stale');
    coordinator.dispose();
  });

  it('settles active and queued writes as disposed and exposes zero resources', async () => {
    const coordinator = createSettingsWriteCoordinator();
    const pending = deferred<string>();
    const active = coordinator.enqueue('security', async () => pending.promise);
    const queued = coordinator.enqueue('security', async () => 'never');
    await Promise.resolve();
    coordinator.dispose('owner-disposed');
    pending.resolve('late');
    const results: SettingsWriteResult<string>[] = await Promise.all([active, queued]);
    expect(results.map(result => result.status)).toEqual(['disposed', 'disposed']);
    expect(coordinator.diagnostics()).toMatchObject({
      activeSectionCount: 0, activeAbortControllerCount: 0, queuedTaskCount: 0, disposed: true
    });
  });

  it('returns a typed failed result for a writer error without hiding the error', async () => {
    const coordinator = createSettingsWriteCoordinator();
    const failure = new Error('backend contract gap');
    const result = await coordinator.enqueue('ai-model', async () => { throw failure; });
    expect(result).toMatchObject({ status: 'failed', section: 'ai-model', message: 'backend contract gap' });
    if (result.status === 'failed') expect(result.error).toBe(failure);
    coordinator.dispose();
  });
});
