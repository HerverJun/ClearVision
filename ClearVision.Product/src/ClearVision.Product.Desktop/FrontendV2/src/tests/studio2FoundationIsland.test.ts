import { afterEach, describe, expect, it } from 'vitest';
import {
  disposeStudio2FoundationIsland,
  getActiveStudio2FoundationIsland,
  mountStudio2FoundationIsland
} from '@/foundation/studio2FoundationIsland';
import type {
  HostedFlowCanvasAdapter,
  LegacyFrontendServices
} from '@/adapters/legacyModules';
import {
  FLOW_EDITOR_PORT_SERVICE_KEY,
  WORKSPACE_SHELL_SERVICE_KEY
} from '@/workspace/workspaceShellRuntime';

const startup = {
  workspaceV2Enabled: true,
  apiBaseUrl: 'http://localhost:5000/api',
  hostKind: 'desktop-webview2',
  frontendV2BasePath: '/v2'
};

describe('mountStudio2FoundationIsland', () => {
  afterEach(() => {
    disposeStudio2FoundationIsland();
  });

  it('returns the same pending mount promise and exposes active handle only after initialization', async () => {
    let created = 0;
    const services = createLegacyServices();
    const deferred = createDeferred<LegacyFrontendServices>();

    const first = mountStudio2FoundationIsland({
      startup,
      container: {} as Element,
      loadServices: () => deferred.promise,
      createApplication: () => {
        created += 1;
        return { unmount: () => {} };
      }
    });
    const second = mountStudio2FoundationIsland({
      startup,
      container: {} as Element,
      loadServices: () => Promise.resolve(services.services),
      createApplication: () => {
        created += 1;
        return { unmount: () => {} };
      }
    });

    expect(second).toBe(first);
    expect(getActiveStudio2FoundationIsland()).toBeNull();

    deferred.resolve(services.services);
    const [firstHandle, secondHandle] = await Promise.all([first, second]);

    expect(secondHandle).toBe(firstHandle);
    expect(getActiveStudio2FoundationIsland()).toBe(firstHandle);
    expect(created).toBe(1);
    expect(firstHandle.model.goal).toBe('G04A');
  });

  it('disposes a pending mount deterministically without mounting the application', async () => {
    let created = 0;
    const services = createLegacyServices();
    const deferred = createDeferred<LegacyFrontendServices>();

    const mountPromise = mountStudio2FoundationIsland({
      startup,
      container: {} as Element,
      loadServices: () => deferred.promise,
      createApplication: () => {
        created += 1;
        return { unmount: () => {} };
      }
    });

    disposeStudio2FoundationIsland();
    deferred.resolve(services.services);

    await expect(mountPromise).rejects.toThrow('disposed before completion');
    expect(created).toBe(0);
    expect(getActiveStudio2FoundationIsland()).toBeNull();
    expect(services.messageHandlers.size).toBe(0);
    expect(services.eventHandlers.size).toBe(0);
    expect(services.registry.size).toBe(0);
  });

  it('reuses legacy modules and keeps a single active shell instance', async () => {
    let created = 0;
    let unmounted = 0;
    const services = createLegacyServices();

    const handle = await mountStudio2FoundationIsland({
      startup,
      container: {} as Element,
      loadServices: () => Promise.resolve(services.services),
      createApplication: () => {
        created += 1;
        return { unmount: () => { unmounted += 1; } };
      }
    });
    const second = await mountStudio2FoundationIsland({
      startup,
      container: {} as Element,
      loadServices: () => Promise.resolve(services.services),
      createApplication: () => {
        created += 1;
        return { unmount: () => { unmounted += 1; } };
      }
    });

    expect(second).toBe(handle);
    expect(getActiveStudio2FoundationIsland()).toBe(handle);
    expect(created).toBe(1);
    expect(handle.model.httpClientStatus).toBe('ready');
    expect(handle.model.hostBridgeStatus).toBe('ready');
    expect(handle.model.eventBusStatus).toBe('ready');
    expect(handle.model.serviceRegistryStatus).toBe('ready');
    expect(handle.scope.getTelemetry().appInstanceCount).toBe(1);

    handle.dispose();
    handle.dispose();

    expect(unmounted).toBe(1);
    expect(services.unregisterCount).toBe(1);
    expect(getActiveStudio2FoundationIsland()).toBeNull();
    expect(handle.scope.getTelemetry()).toEqual(emptyTelemetry());
  });

  it('cleans real shell-owned handlers, registry entries, abort controllers and adapters across 20 cycles', async () => {
    let totalUnmounted = 0;

    for (let index = 0; index < 20; index += 1) {
      const services = createLegacyServices();
      let unmounted = 0;

      const handle = await mountStudio2FoundationIsland({
        startup,
        container: {} as Element,
        loadServices: () => Promise.resolve(services.services),
        createApplication: () => ({
          unmount: () => {
            unmounted += 1;
            totalUnmounted += 1;
          }
        })
      });
      const port = handle.model.workspaceRuntime.mountFlowCanvas(`studio2-flow-canvas-${String(index)}`);

      expect(services.messageHandlers.size).toBe(1);
      expect(services.eventHandlers.size).toBe(1);
      expect(services.registry.get(WORKSPACE_SHELL_SERVICE_KEY)).toBeTruthy();
      expect(services.registry.get(FLOW_EDITOR_PORT_SERVICE_KEY)).toBe(port);
      expect(services.adapterCreateCount).toBe(1);
      expect(handle.scope.getTelemetry().abortControllerCount).toBe(1);

      handle.dispose();
      await Promise.resolve();

      expect(unmounted).toBe(1);
      expect(services.messageHandlers.size).toBe(0);
      expect(services.eventHandlers.size).toBe(0);
      expect(services.registry.size).toBe(0);
      expect(services.abortCount).toBe(1);
      expect(services.adapterDisposeCount).toBe(1);
      expect(handle.scope.getTelemetry()).toEqual(emptyTelemetry());
      expect(getActiveStudio2FoundationIsland()).toBeNull();
    }

    expect(totalUnmounted).toBe(20);
  });
});

interface LegacyServicesFixture {
  services: LegacyFrontendServices;
  readonly messageHandlers: Set<(payload: unknown) => void>;
  readonly eventHandlers: Set<(payload: unknown) => void>;
  readonly registry: Map<string, unknown>;
  unregisterCount: number;
  adapterCreateCount: number;
  adapterDisposeCount: number;
  abortCount: number;
}

function createLegacyServices(): LegacyServicesFixture {
  const messageHandlers = new Set<(payload: unknown) => void>();
  const eventHandlers = new Set<(payload: unknown) => void>();
  const registry = new Map<string, unknown>();
  const fixture: LegacyServicesFixture = {
    services: null as unknown as LegacyFrontendServices,
    messageHandlers,
    eventHandlers,
    registry,
    unregisterCount: 0,
    adapterCreateCount: 0,
    adapterDisposeCount: 0,
    abortCount: 0
  };

  const services: LegacyFrontendServices = {
    httpClient: {
      getRoot: <T,>(
        _url: string,
        _params?: Record<string, string> | null,
        options?: { readonly signal?: AbortSignal }
      ) => new Promise<T>((resolve, reject) => {
        const signal = options?.signal;
        if (!signal) {
          resolve({ status: 'Healthy', port: 5000 } as T);
          return;
        }

        signal.addEventListener('abort', () => {
          fixture.abortCount += 1;
          reject(new Error('aborted'));
        }, { once: true });
      })
    },
    webMessageBridge: {
      on(_type: string, handler: (payload: unknown) => void) {
        messageHandlers.add(handler);
        return () => {
          messageHandlers.delete(handler);
        };
      },
      sendMessage: () => Promise.resolve({ ok: true })
    },
    eventBus: {
      on(_eventName: string, handler: (payload: unknown) => void) {
        eventHandlers.add(handler);
        return () => {
          eventHandlers.delete(handler);
        };
      },
      emit(_eventName: string, payload?: unknown) {
        for (const handler of eventHandlers) {
          handler(payload);
        }
      }
    },
    serviceRegistry: {
      register(key: string, service: unknown) {
        registry.set(key, service);
        return service;
      },
      unregister(key: string, expectedService?: unknown) {
        if (expectedService !== undefined && registry.get(key) !== expectedService) {
          return false;
        }

        const deleted = registry.delete(key);
        if (deleted) {
          fixture.unregisterCount += 1;
        }
        return deleted;
      }
    },
    flowCanvasAdapterModule: {
      createHostedFlowCanvasAdapter() {
        fixture.adapterCreateCount += 1;
        return createHostedAdapterFixture(fixture);
      }
    }
  };
  fixture.services = services;

  return fixture;
}

function createHostedAdapterFixture(fixture: LegacyServicesFixture): HostedFlowCanvasAdapter {
  return {
    resize: () => undefined,
    render: () => undefined,
    dispose: () => {
      fixture.adapterDisposeCount += 1;
    },
    getViewState: () => ({
      selectedNode: null,
      selectedConnection: null,
      scale: 1,
      offset: { x: 0, y: 0 },
      nodeCount: 0,
      connectionCount: 0
    }),
    getSnapshot: () => ({
      flowRevision: 0,
      selectionRevision: 0,
      selectedNodeId: null,
      flow: { operators: [], connections: [] },
      selectedNode: null
    }),
    replaceFlow: () => undefined,
    selectNode: () => true,
    patchNodeParameters: () => ({
      updated: true,
      reason: 'updated',
      missingParameters: []
    }),
    subscribeStructure: (listener) => {
      listener({});
      return () => {};
    },
    subscribeSelection: (listener) => {
      listener({});
      return () => {};
    }
  };
}

function createDeferred<T>(): {
  readonly promise: Promise<T>;
  resolve(value: T): void;
  reject(error: unknown): void;
} {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

function emptyTelemetry() {
  return {
    appInstanceCount: 0,
    listenerCount: 0,
    timerCount: 0,
    observerCount: 0,
    abortControllerCount: 0,
    registryRegistrationCount: 0,
    blobUrlCount: 0,
    pendingRequestCount: 0
  };
}
