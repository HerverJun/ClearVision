import { createApp, markRaw, reactive } from 'vue';
import { createPinia } from 'pinia';
import WorkspaceShell from '@/components/WorkspaceShell.vue';
import type { ClearVisionStartupConfig } from '@/startup/startupConfig';
import {
  loadLegacyFrontendServices,
  type LegacyFrontendServices
} from '@/adapters/legacyModules';
import { createHealthApi } from '@/api/healthApi';
import { createHostBridge } from '@/host/hostBridge';
import {
  createWorkspaceShellRuntimeState,
  Studio2WorkspaceShellRuntime,
  WORKSPACE_SHELL_SERVICE_KEY,
  type Studio2WorkspaceShellRuntimeHandle,
  type Studio2WorkspaceShellRuntimeState
} from '@/workspace/workspaceShellRuntime';
import {
  Studio2LifecycleScope,
  type MountedStudio2App,
  type Studio2LifecycleTelemetry
} from '@/foundation/studio2Lifecycle';

const MOUNT_DISPOSED_ERROR = 'Studio2 foundation mount was disposed before completion.';

export interface Studio2FoundationIslandViewModel {
  readonly goal: 'G04A';
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
  workspaceState: Studio2WorkspaceShellRuntimeState;
  workspaceRuntime: Studio2WorkspaceShellRuntimeHandle;
  refreshLifecycle(): void;
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
let activeMount: ActiveMountState | null = null;

interface ActiveMountState {
  disposeRequested: boolean;
  promise: Promise<Studio2FoundationIslandHandle>;
}

export function mountStudio2FoundationIsland(
  options: Studio2FoundationIslandOptions
): Promise<Studio2FoundationIslandHandle> {
  if (activeHandle) {
    return Promise.resolve(activeHandle);
  }

  if (activeMount) {
    return activeMount.promise;
  }

  const mountState = {
    disposeRequested: false,
    promise: Promise.resolve(null as unknown as Studio2FoundationIslandHandle)
  };
  mountState.promise = createFoundationIsland(options, mountState)
    .then((handle) => {
      if (mountState.disposeRequested || handle.scope.isDisposed) {
        handle.dispose();
        throw new Error(MOUNT_DISPOSED_ERROR);
      }

      activeHandle = handle;
      return handle;
    })
    .finally(() => {
      if (activeMount === mountState) {
        activeMount = null;
      }
    });
  activeMount = mountState;

  return mountState.promise;
}

export function disposeStudio2FoundationIsland(): void {
  if (activeHandle) {
    activeHandle.dispose();
    return;
  }

  if (activeMount) {
    activeMount.disposeRequested = true;
  }
}

export function getActiveStudio2FoundationIsland(): Studio2FoundationIslandHandle | null {
  return activeHandle;
}

function createViewModel(
  startup: ClearVisionStartupConfig,
  workspaceRuntime: Studio2WorkspaceShellRuntimeHandle,
  workspaceState: Studio2WorkspaceShellRuntimeState,
  scope: Studio2LifecycleScope
): Studio2FoundationIslandViewModel {
  return reactive<Studio2FoundationIslandViewModel>({
    goal: 'G04A',
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
    lifecycle: emptyTelemetry(),
    workspaceState,
    workspaceRuntime: markRaw(workspaceRuntime),
    refreshLifecycle() {
      this.lifecycle = scope.getTelemetry();
    }
  });
}

async function createFoundationIsland(
  options: Studio2FoundationIslandOptions,
  mountState: ActiveMountState
): Promise<Studio2FoundationIslandHandle> {
  const scope = new Studio2LifecycleScope();
  const services = await (options.loadServices ?? loadLegacyFrontendServices)();
  throwIfMountDisposed(scope, mountState);
  const workspaceState = reactive(createWorkspaceShellRuntimeState());
  const workspaceRuntime = new Studio2WorkspaceShellRuntime(services, scope, workspaceState);
  const model = createViewModel(options.startup, workspaceRuntime, workspaceState, scope);
  const createApplication = options.createApplication ?? ((viewModel) => {
    const app = createApp(WorkspaceShell, { model: viewModel });
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
      workspaceRuntime.dispose();
      scope.dispose();
      model.lifecycle = scope.getTelemetry();
      if (activeHandle === handle) {
        activeHandle = null;
      }
    }
  };

  try {
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
      goal: 'G04A',
      dispose: () => {
        handle.dispose();
      }
    };
    services.serviceRegistry.register(WORKSPACE_SHELL_SERVICE_KEY, serviceRegistration);
    scope.trackRegistryRegistration({
      unregister() {
        services.serviceRegistry.unregister(WORKSPACE_SHELL_SERVICE_KEY, serviceRegistration);
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
    void scope.trackPendingRequest(healthRequest, () => {
      healthController.abort();
    });

    throwIfMountDisposed(scope, mountState);
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

function throwIfMountDisposed(scope: Studio2LifecycleScope, mountState: ActiveMountState): void {
  if (!mountState.disposeRequested && !scope.isDisposed) {
    return;
  }

  scope.dispose();
  throw new Error(MOUNT_DISPOSED_ERROR);
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
