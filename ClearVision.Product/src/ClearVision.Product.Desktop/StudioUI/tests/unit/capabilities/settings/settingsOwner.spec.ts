import { describe, expect, it, vi } from 'vitest';
import type { ProductRuntime } from '@/app/productRuntime';
import {
  ApiConflictError,
  ApiDecodeError,
  ApiForbiddenError,
  ApiNetworkError,
  type ApiTransport
} from '@/platform/api';
import {
  createSettingsOwner,
  createSettingsWriteCoordinator,
  getSettingsOwnerActiveCount,
  SettingsContractDecodeError,
  SettingsUnknownOutcomeError,
  settingsOperationResultMessage,
  type SettingsEndpointTask,
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

function diskUsagePayload() {
  return {
    driveName: 'D:', sourcePath: 'D:/VisionData', isAccessible: true, canWrite: true,
    totalBytes: 1000, usedBytes: 400, freeBytes: 600,
    totalGb: 1, usedGb: 0.4, freeGb: 0.6, usedPercent: 40
  };
}

type FakeGet = (path: string, options?: { readonly signal?: AbortSignal }) => Promise<unknown>;

function runtime(get: FakeGet): Pick<ProductRuntime, 'api'> {
  return {
    api: { apiBaseUrl: 'http://localhost:5000/api', get: get as ApiTransport['get'] } as ApiTransport
  };
}

function runtimeWithApi(api: ApiTransport): Pick<ProductRuntime, 'api'> {
  return { api };
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
    const write = await operator.enqueueEndpointOperation('general', 'settings.write', async () => 'must-not-run');
    expect(write.status).toBe('forbidden');
    const diagnostic = await operator.enqueueEndpointOperation('plc', 'plc.test-connection', async () => 'must-not-run');
    expect(diagnostic.status).toBe('forbidden');
    operator.dispose();
  });

  it('allows Engineer diagnostics but denies Admin writes, route-only, unknown and excluded endpoints', async () => {
    const executed: string[] = [];
    const engineer = createSettingsOwner({ runtime: runtime(vi.fn()), role: 'Engineer' });
    const diagnosticTask: SettingsEndpointTask<string> = async context => {
      executed.push(context.endpoint.id);
      return context.endpoint.id;
    };

    expect(await engineer.enqueueEndpointOperation('plc', 'plc.test-connection', diagnosticTask))
      .toMatchObject({ status: 'completed', section: 'plc', value: 'plc.test-connection' });
    expect(await engineer.enqueueEndpointOperation('tcp', 'tcp.runtime', diagnosticTask))
      .toMatchObject({ status: 'completed', section: 'tcp', value: 'tcp.runtime' });
    expect(await engineer.enqueueEndpointOperation('camera', 'camera.trigger-and-preview', diagnosticTask))
      .toMatchObject({ status: 'completed', section: 'camera', value: 'camera.trigger-and-preview' });

    const adminWrite = await engineer.enqueueEndpointOperation('plc', 'plc.settings.write', diagnosticTask);
    expect(adminWrite).toMatchObject({ status: 'forbidden', section: 'plc' });
    const genericWrite = await engineer.enqueueEndpointOperation('general', 'settings.write', diagnosticTask);
    expect(genericWrite).toMatchObject({ status: 'forbidden', section: 'general' });
    const routeOnly = await engineer.enqueueEndpointOperation('ai-model', 'ai.reasoning-support', diagnosticTask);
    expect(routeOnly).toMatchObject({ status: 'forbidden', section: 'ai-model' });
    const unknown = await engineer.enqueueEndpointOperation('plc', 'settings.unknown', diagnosticTask);
    expect(unknown).toMatchObject({ status: 'forbidden', section: 'plc' });
    const excluded = await engineer.enqueueEndpointOperation('general', 'settings/import', diagnosticTask);
    expect(excluded).toMatchObject({ status: 'forbidden', section: 'general' });
    expect(executed).toEqual(['plc.test-connection', 'tcp.runtime', 'camera.trigger-and-preview']);
    engineer.dispose();

    const admin = createSettingsOwner({ runtime: runtime(vi.fn()), role: 'Admin' });
    const saved = await admin.enqueueEndpointOperation('general', 'settings.write', async context => {
      expect(context.endpoint.id).toBe('settings.write');
      return 'admin-write';
    });
    expect(saved).toMatchObject({ status: 'completed', section: 'general', value: 'admin-write' });
    admin.dispose();
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

  it('classifies mutation network and contract decode failures as unknown outcomes', async () => {
    const network = createSettingsOwner({
      runtime: runtimeWithApi({
        ...({} as ApiTransport),
        get: vi.fn(async () => undefined),
        put: vi.fn(async () => {
          throw new ApiNetworkError('http://localhost:5000/api/settings', new Error('offline'));
        })
      }),
      role: 'Admin'
    });
    const networkResult = await network.saveGenericSection('general', { softwareTitle: 'Updated' });
    expect(networkResult.status).toBe('failed');
    if (networkResult.status === 'failed') expect(networkResult.error).toBeInstanceOf(SettingsUnknownOutcomeError);
    expect(settingsOperationResultMessage(networkResult)).toContain('结果未知');
    expect(network.leaveProtection()).toBe('settings-unknown');
    network.dispose();

    const decode = createSettingsOwner({
      runtime: runtimeWithApi({
        ...({} as ApiTransport),
        get: vi.fn(async () => undefined),
        put: vi.fn(async () => {
          throw new SettingsContractDecodeError('$.config', 'object');
        })
      }),
      role: 'Admin'
    });
    const decodeResult = await decode.saveGenericSection('general', { softwareTitle: 'Updated' });
    expect(decodeResult.status).toBe('failed');
    if (decodeResult.status === 'failed') expect(decodeResult.error).toBeInstanceOf(SettingsUnknownOutcomeError);
    decode.dispose();
  });

  it('clears Generic unknown only after a decoded Settings GET, not an unrelated read', async () => {
    const get = vi.fn(async (path: string) => path === 'settings' ? settingsPayload() : diskUsagePayload()) as unknown as ApiTransport['get'];
    const put = vi.fn(async () => {
      throw new ApiNetworkError('http://localhost:5000/api/settings', new Error('offline'));
    }) as NonNullable<ApiTransport['put']>;
    const owner = createSettingsOwner({
      runtime: runtimeWithApi({ ...({} as ApiTransport), get, put }),
      role: 'Admin'
    });

    const failed = await owner.saveGenericSection('general', { softwareTitle: 'Updated' });
    expect(failed.status).toBe('failed');
    expect(owner.projection.unknownOutcomeKeys).toEqual(['generic-settings']);

    const unrelatedRead = await owner.readDiskUsage();
    expect(unrelatedRead.status).toBe('completed');
    expect(owner.projection.unknownOutcomeKeys).toEqual(['generic-settings']);

    const reconciled = await owner.reconcileAuthority('generic-settings');
    expect(reconciled.status).toBe('completed');
    expect(owner.projection.unknownOutcomeKeys).toEqual([]);
    expect(get).toHaveBeenCalledWith('settings', expect.objectContaining({ signal: expect.any(AbortSignal) }));
    owner.dispose();
  });

  it('does not clear Camera bindings unknown after discovery; bindings reread clears it', async () => {
    const get = vi.fn(async (path: string) => path === 'cameras/bindings' ? [] : settingsPayload()) as unknown as ApiTransport['get'];
    const owner = createSettingsOwner({
      runtime: runtimeWithApi({ ...({} as ApiTransport), get }),
      role: 'Admin'
    });

    const failed = await owner.enqueueEndpointOperation(
      'camera',
      'camera.bindings.write',
      async () => { throw new ApiNetworkError('http://localhost:5000/api/cameras/bindings', new Error('offline')); }
    );
    expect(failed.status).toBe('failed');
    expect(owner.projection.unknownOutcomeKeys).toEqual(['camera-bindings']);

    const discovery = await owner.enqueueEndpointOperation(
      'camera',
      'camera.discovery.all',
      async () => 'discovered',
      'read'
    );
    expect(discovery).toMatchObject({ status: 'completed', value: 'discovered' });
    expect(owner.projection.unknownOutcomeKeys).toEqual(['camera-bindings']);

    const reread = await owner.readCameraBindings();
    expect(reread.status).toBe('completed');
    expect(owner.projection.unknownOutcomeKeys).toEqual([]);
    owner.dispose();
  });

  it('keeps read decode failures classified as read/decode failures', async () => {
    const owner = createSettingsOwner({
      runtime: runtime(async () => {
        throw new ApiDecodeError('http://localhost:5000/api/settings', 200, new Error('invalid response'));
      }),
      role: 'Admin'
    });
    expect(await owner.refresh()).toBe(false);
    expect(owner.projection.error).toMatchObject({ code: 'decode' });
    owner.dispose();
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

  it('cancels only the selected section while another section completes', async () => {
    const coordinator = createSettingsWriteCoordinator();
    const generalPending = deferred<string>();
    const storagePending = deferred<string>();
    const general = coordinator.enqueue('general', async () => generalPending.promise, 'write');
    const storage = coordinator.enqueue('storage', async () => storagePending.promise, 'write');
    await Promise.resolve();

    coordinator.cancel('general', 'leave-general');
    generalPending.resolve('late');
    storagePending.resolve('saved');

    expect((await general).status).toBe('cancelled');
    expect((await storage).status).toBe('completed');
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
