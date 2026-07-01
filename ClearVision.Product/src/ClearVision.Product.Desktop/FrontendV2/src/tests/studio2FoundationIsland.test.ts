import { afterEach, describe, expect, it } from 'vitest';
import {
  disposeStudio2FoundationIsland,
  getActiveStudio2FoundationIsland,
  mountStudio2FoundationIsland
} from '@/foundation/studio2FoundationIsland';
import type { LegacyFrontendServices } from '@/adapters/legacyModules';

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

  it('reuses legacy modules and keeps a single active island', async () => {
    let created = 0;
    let unmounted = 0;
    let registryUnregistered = 0;
    const services = createLegacyServices(() => {
      registryUnregistered += 1;
    });

    const handle = await mountStudio2FoundationIsland({
      startup,
      container: {} as Element,
      loadServices: () => Promise.resolve(services),
      createApplication: () => {
        created += 1;
        return { unmount: () => { unmounted += 1; } };
      }
    });
    const second = await mountStudio2FoundationIsland({
      startup,
      container: {} as Element,
      loadServices: () => Promise.resolve(services),
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
    expect(registryUnregistered).toBe(1);
    expect(getActiveStudio2FoundationIsland()).toBeNull();
    expect(handle.scope.getTelemetry()).toEqual({
      appInstanceCount: 0,
      listenerCount: 0,
      timerCount: 0,
      observerCount: 0,
      abortControllerCount: 0,
      registryRegistrationCount: 0,
      blobUrlCount: 0,
      pendingRequestCount: 0
    });
  });
});

function createLegacyServices(onUnregister: () => void): LegacyFrontendServices {
  const messageHandlers = new Set<(payload: unknown) => void>();
  const eventHandlers = new Set<(payload: unknown) => void>();
  let registeredService: unknown = null;

  return {
    httpClient: {
      getRoot: <T,>() => Promise.resolve({ status: 'Healthy', port: 5000 } as T)
    },
    webMessageBridge: {
      on(_type, handler) {
        messageHandlers.add(handler);
        return () => {
          messageHandlers.delete(handler);
        };
      },
      sendMessage: () => Promise.resolve({ ok: true })
    },
    eventBus: {
      on(_eventName, handler) {
        eventHandlers.add(handler);
        return () => {
          eventHandlers.delete(handler);
        };
      },
      emit(_eventName, payload) {
        for (const handler of eventHandlers) {
          handler(payload);
        }
      }
    },
    serviceRegistry: {
      register(_key, service) {
        registeredService = service;
        return service;
      },
      unregister(_key, expectedService) {
        if (expectedService !== registeredService) {
          return false;
        }

        registeredService = null;
        onUnregister();
        return true;
      }
    }
  };
}
