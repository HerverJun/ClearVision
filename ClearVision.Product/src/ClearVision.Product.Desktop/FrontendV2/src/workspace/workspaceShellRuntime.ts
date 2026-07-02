import type {
  HostedFlowCanvasViewState,
  LegacyFlowCanvasAdapter,
  LegacyFrontendServices
} from '@/adapters/legacyModules';
import {
  createStudioFlowEditorPort,
  type StudioFlowEditorPort,
  type StudioFlowEditorSnapshot
} from '@/flowEditor/studioFlowEditorPort';
import {
  createStudioProjectPersistencePort,
  type StudioProjectPersistencePort,
  type StudioProjectPersistenceSnapshot
} from '@/project/studioProjectPersistencePort';
import type { Studio2LifecycleScope } from '@/foundation/studio2Lifecycle';
import type { WorkspaceShellMode } from '@/workspace/workspaceShellStore';

export const WORKSPACE_SHELL_SERVICE_KEY = 'studio2.workspaceShell';
export const FLOW_EDITOR_PORT_SERVICE_KEY = 'studio2.flowEditorPort';
export const PROJECT_PERSISTENCE_PORT_SERVICE_KEY = 'studio2.projectPersistencePort';

export interface Studio2WorkspaceShellRuntimeState {
  currentMode: WorkspaceShellMode;
  flowCanvasStatus: 'pending' | 'ready' | 'disposed' | 'error';
  flowCanvasInstanceCount: number;
  resizeCount: number;
  renderCount: number;
  lastResizeReason: string;
  lastError: string;
  flowEditorStatus: 'pending' | 'ready' | 'disposed' | 'error';
  flowEditorSnapshot: StudioFlowEditorSnapshot | null;
  lastFlowEditorDisposition: string;
  projectPersistenceStatus: 'pending' | 'ready' | 'disposed' | 'error';
  projectPersistenceSnapshot: StudioProjectPersistenceSnapshot;
}

export function createWorkspaceShellRuntimeState(): Studio2WorkspaceShellRuntimeState {
  return {
    currentMode: 'flow',
    flowCanvasStatus: 'pending',
    flowCanvasInstanceCount: 0,
    resizeCount: 0,
    renderCount: 0,
    lastResizeReason: 'none',
    lastError: '',
    flowEditorStatus: 'pending',
    flowEditorSnapshot: null,
    lastFlowEditorDisposition: '',
    projectPersistenceStatus: 'pending',
    projectPersistenceSnapshot: {
      projectId: null,
      project: null,
      name: '',
      description: null,
      persistenceRevision: null,
      globalVariables: {
        schemaVersion: '1.0',
        variables: [],
        sourceBindings: [],
        targetBindings: []
      },
      status: 'empty',
      loaded: false,
      saving: false,
      dirty: false,
      error: '',
      lastDisposition: 'idle'
    }
  };
}

export interface Studio2WorkspaceShellRuntimeHandle {
  readonly state: Studio2WorkspaceShellRuntimeState;
  mountFlowCanvas(canvasId: string): StudioFlowEditorPort;
  setMode(mode: WorkspaceShellMode): void;
  resizeFlowCanvas(reason: string): boolean;
  getFlowCanvasViewState(): HostedFlowCanvasViewState | null;
  getFlowEditorPort(): StudioFlowEditorPort | null;
  getProjectPersistencePort(): StudioProjectPersistencePort | null;
  dispose(): void;
}

export class Studio2WorkspaceShellRuntime implements Studio2WorkspaceShellRuntimeHandle {
  private flowCanvasAdapter: LegacyFlowCanvasAdapter | null = null;
  private flowEditorPort: StudioFlowEditorPort | null = null;
  private projectPersistencePort: StudioProjectPersistencePort | null = null;
  private readonly flowEditorSubscriptions = new Set<() => void>();
  private readonly projectPersistenceSubscriptions = new Set<() => void>();
  private flowCanvasId: string | null = null;
  private disposed = false;

  constructor(
    private readonly services: LegacyFrontendServices,
    private readonly scope: Studio2LifecycleScope,
    public readonly state: Studio2WorkspaceShellRuntimeState
  ) {
  }

  mountFlowCanvas(canvasId: string): StudioFlowEditorPort {
    if (this.disposed || this.scope.isDisposed) {
      throw new Error('Studio2 workspace shell has been disposed.');
    }

    if (this.flowCanvasAdapter && this.flowEditorPort) {
      if (this.flowCanvasId !== canvasId) {
        throw new Error('Studio2 workspace shell already owns a FlowCanvas adapter.');
      }

      return this.flowEditorPort;
    }

    try {
      const adapter = this.services.flowCanvasAdapterModule.createHostedFlowCanvasAdapter(canvasId, {
        eventBus: this.services.eventBus
      });
      const port = createStudioFlowEditorPort(adapter);
      const projectPersistencePort = createStudioProjectPersistencePort(this.services.httpClient, port);

      this.flowCanvasAdapter = adapter;
      this.flowEditorPort = port;
      this.projectPersistencePort = projectPersistencePort;
      this.flowCanvasId = canvasId;
      this.state.flowCanvasStatus = 'ready';
      this.state.flowEditorStatus = 'ready';
      this.state.projectPersistenceStatus = 'ready';
      this.state.flowCanvasInstanceCount = 1;
      this.state.lastError = '';
      this.syncFlowEditorSnapshot();
      this.syncProjectPersistenceSnapshot();

      this.services.serviceRegistry.register(FLOW_EDITOR_PORT_SERVICE_KEY, port);
      this.scope.trackRegistryRegistration({
        unregister: () => {
          this.services.serviceRegistry.unregister(FLOW_EDITOR_PORT_SERVICE_KEY, port);
        }
      });
      this.services.serviceRegistry.register(PROJECT_PERSISTENCE_PORT_SERVICE_KEY, projectPersistencePort);
      this.scope.trackRegistryRegistration({
        unregister: () => {
          this.services.serviceRegistry.unregister(PROJECT_PERSISTENCE_PORT_SERVICE_KEY, projectPersistencePort);
        }
      });
      this.trackFlowEditorSubscription(port.subscribeStructure((snapshot) => {
        this.state.flowEditorSnapshot = snapshot;
      }));
      this.trackFlowEditorSubscription(port.subscribeSelection((snapshot) => {
        this.state.flowEditorSnapshot = snapshot;
      }));
      this.trackProjectPersistenceSubscription(projectPersistencePort.subscribe((snapshot) => {
        this.state.projectPersistenceSnapshot = snapshot;
      }));
      this.scope.trackListener(() => {
        this.disposeFlowCanvas();
      });

      this.resizeFlowCanvas('mount');
      return port;
    } catch (error) {
      this.state.flowCanvasStatus = 'error';
      this.state.flowEditorStatus = 'error';
      this.state.projectPersistenceStatus = 'error';
      this.state.lastError = error instanceof Error ? error.message : String(error);
      throw error;
    }
  }

  setMode(mode: WorkspaceShellMode): void {
    this.state.currentMode = mode;
    if (mode === 'flow') {
      this.resizeFlowCanvas('mode-flow');
    }
  }

  resizeFlowCanvas(reason: string): boolean {
    if (!this.flowCanvasAdapter || this.state.flowCanvasStatus !== 'ready') {
      return false;
    }

    this.flowCanvasAdapter.resize();
    this.flowCanvasAdapter.render();
    this.state.resizeCount += 1;
    this.state.renderCount += 1;
    this.state.lastResizeReason = reason;
    return true;
  }

  getFlowCanvasViewState(): HostedFlowCanvasViewState | null {
    return this.flowCanvasAdapter?.getViewState() ?? null;
  }

  getFlowEditorPort(): StudioFlowEditorPort | null {
    return this.flowEditorPort;
  }

  getProjectPersistencePort(): StudioProjectPersistencePort | null {
    return this.projectPersistencePort;
  }

  dispose(): void {
    this.disposed = true;
    this.disposeFlowCanvas();
  }

  private disposeFlowCanvas(): void {
    if (!this.flowCanvasAdapter && !this.flowEditorPort) {
      return;
    }

    for (const unsubscribe of [...this.flowEditorSubscriptions]) {
      unsubscribe();
    }
    this.flowEditorSubscriptions.clear();
    for (const unsubscribe of [...this.projectPersistenceSubscriptions]) {
      unsubscribe();
    }
    this.projectPersistenceSubscriptions.clear();

    this.projectPersistencePort?.dispose();
    this.flowEditorPort?.dispose();
    this.flowCanvasAdapter?.dispose();
    this.projectPersistencePort = null;
    this.flowEditorPort = null;
    this.flowCanvasAdapter = null;
    this.flowCanvasId = null;
    this.state.flowCanvasStatus = 'disposed';
    this.state.flowEditorStatus = 'disposed';
    this.state.projectPersistenceStatus = 'disposed';
    this.state.flowEditorSnapshot = null;
    this.state.flowCanvasInstanceCount = 0;
  }

  private syncFlowEditorSnapshot(): void {
    this.state.flowEditorSnapshot = this.flowEditorPort?.getSnapshot() ?? null;
  }

  private syncProjectPersistenceSnapshot(): void {
    this.state.projectPersistenceSnapshot = this.projectPersistencePort?.getSnapshot() ?? this.state.projectPersistenceSnapshot;
  }

  private trackFlowEditorSubscription(unsubscribe: () => void): void {
    this.flowEditorSubscriptions.add(unsubscribe);
  }

  private trackProjectPersistenceSubscription(unsubscribe: () => void): void {
    this.projectPersistenceSubscriptions.add(unsubscribe);
  }
}
