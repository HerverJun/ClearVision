import { createApp, reactive } from 'vue';
import { createPinia } from 'pinia';
import FoundationIsland from '@/components/FoundationIsland.vue';
import type { ClearVisionStartupConfig } from '@/startup/startupConfig';
import {
  loadLegacyFrontendServices,
  type LegacyFrontendServices
} from '@/adapters/legacyModules';
import { createHealthApi } from '@/api/healthApi';
import { createHostBridge } from '@/host/hostBridge';
import {
  Studio2LifecycleScope,
  type MountedStudio2App,
  type Studio2LifecycleTelemetry
} from '@/foundation/studio2Lifecycle';

const SERVICE_KEY = 'studio2.foundationIsland';

export interface Studio2FoundationIslandViewModel {
  readonly goal: 'G02B';
  workspaceV2Enabled: boolean;
  apiBaseUrl: string;
  hostKind: string;
  frontendV2BasePath: string;
  httpClientStatus: 'pending' | 'ready' | 'error';
  hostBridgeStatus: 'pending' | 'ready' | 'error';
  eventBusStatus: 'pending' | 'ready' | 'error';
  serviceRegistryStatus: 'pending' | 'ready' | 'error';
  healthStatus: 'pending' | 'healthy' | 'cancelled' | 'error' | 'not-requested';
  healthDetail: string;
  lifecycle: Studio2LifecycleTelemetry;
}

export interface Studio2FoundationIslandHandle {
  readonly model: Studio2FoundationIslandViewModel;
  readonly scope: Studio2LifecycleScope;
  dispose(): void;
}

export interface Studio2FoundationIslandOptions {
  readonly startup: ClearVisionStartupConfig;
  readonly container: Element;
  readonly loadServices?: () => Promise<LegacyFrontendServices>;
  readonly createApplication?: (model: Studio2FoundationIslandViewModel) => MountedStudio2App;
}

let activeHandle: Studio2FoundationIslandHandle | null = null;
let activeMountPromise: Promise<Studio2FoundationIslandHandle> | null = null;

export async function mountStudio2FoundationIsland(
  options: Studio2FoundationIslandOptions
): Promise<Studio2FoundationIslandHandle> {
  if (activeHandle) {
    return activeHandle;
  }

  if (activeMountPromise) {
    return activeMountPromise;
  }

  activeMountPromise = createFoundationIsland(options).finally(() => {
    activeMountPromise = null;
  });

  return activeMountPromise;
}

export function disposeStudio2FoundationIsland(): void {
  activeHandle?.dispose();
}

export function getActiveStudio2FoundationIsland(): Studio2FoundationIslandHandle | null {
  return activeHandle;
}

function createViewModel(startup: ClearVisionStartupConfig): Studio2FoundationIslandViewModel {
  return reactive<Studio2FoundationIslandViewModel>({
    goal: 'G02B',
    workspaceV2Enabled: startup.workspaceV2Enabled,
    apiBaseUrl: startup.apiBaseUrl,
    hostKind: startup.hostKind,
    frontendV2BasePath: startup.frontendV2BasePath,
    httpClientStatus: 'pending',
    hostBridgeStatus: 'pending',
    eventBusStatus: 'pending',
    serviceRegistryStatus: 'pending',
    healthStatus: 'pending',
    healthDetail: 'checking',
    lifecycle: emptyTelemetry()
  });
}

async function createFoundationIsland(
  options: Studio2FoundationIslandOptions
): Promise<Studio2FoundationIslandHandle> {
  const scope = new Studio2LifecycleScope();
  const model = createViewModel(options.startup);
  const createApplication = options.createApplication ?? ((viewModel) => {
    const app = createApp(FoundationIsland, { model: viewModel });
    app.use(createPinia());
    app.mount(options.container);
    return {
      unmount() {
        app.unmount();
      }
    };
  });

  const handle: Studio2FoundationIslandHandle = {
    model,
    scope,
    dispose() {
      scope.dispose();
      model.lifecycle = scope.getTelemetry();
      if (activeHandle === handle) {
        activeHandle = null;
      }
    }
  };

  activeHandle = handle;

  try {
    const services = await (options.loadServices ?? loadLegacyFrontendServices)();
    model.httpClientStatus = 'ready';
    model.eventBusStatus = 'ready';
    model.serviceRegistryStatus = 'ready';

    const hostBridge = createHostBridge(services.webMessageBridge);
    model.hostBridgeStatus = 'ready';
    scope.trackListener(hostBridge.onHostMessage('studio2.foundation.dispose', () => {
      handle.dispose();
    }));
    scope.trackListener(() => {
      hostBridge.dispose();
    });

    const serviceRegistration = {
      goal: 'G02B',
      dispose: () => {
        handle.dispose();
      }
    };
    services.serviceRegistry.register(SERVICE_KEY, serviceRegistration);
    scope.trackRegistryRegistration({
      unregister() {
        services.serviceRegistry.unregister(SERVICE_KEY, serviceRegistration);
      }
    });

    const eventSubscription = services.eventBus.on('studio2:foundation:dispose', () => {
      handle.dispose();
    });
    scope.trackListener(eventSubscription);

    const healthController = scope.createAbortController();
    const healthRequest = createHealthApi(services.httpClient)
      .getHealth(healthController.signal)
      .then((health) => {
        model.healthStatus = 'healthy';
        model.healthDetail = health.status ?? 'Healthy';
      })
      .catch((error: unknown) => {
        if (healthController.signal.aborted) {
          model.healthStatus = 'cancelled';
          model.healthDetail = 'cancelled';
          return;
        }

        model.healthStatus = 'error';
        model.healthDetail = error instanceof Error ? error.message : String(error);
      });
    void scope.trackPendingRequest(healthRequest);

    scope.mountApp(() => createApplication(model));
    model.lifecycle = scope.getTelemetry();
    return handle;
  } catch (error) {
    handle.dispose();
    model.httpClientStatus = model.httpClientStatus === 'pending' ? 'error' : model.httpClientStatus;
    model.hostBridgeStatus = model.hostBridgeStatus === 'pending' ? 'error' : model.hostBridgeStatus;
    model.eventBusStatus = model.eventBusStatus === 'pending' ? 'error' : model.eventBusStatus;
    model.serviceRegistryStatus = model.serviceRegistryStatus === 'pending' ? 'error' : model.serviceRegistryStatus;
    model.healthStatus = 'error';
    model.healthDetail = error instanceof Error ? error.message : String(error);
    throw error;
  }
}

function emptyTelemetry(): Studio2LifecycleTelemetry {
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
