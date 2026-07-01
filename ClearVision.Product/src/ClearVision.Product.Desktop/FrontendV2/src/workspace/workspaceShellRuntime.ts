import type {
  HostedFlowCanvasAdapter,
  HostedFlowCanvasViewState,
  LegacyFrontendServices
} from '@/adapters/legacyModules';
import type { Studio2LifecycleScope } from '@/foundation/studio2Lifecycle';
import type { WorkspaceShellMode } from '@/workspace/workspaceShellStore';

export const WORKSPACE_SHELL_SERVICE_KEY = 'studio2.workspaceShell';
export const FLOW_CANVAS_ADAPTER_SERVICE_KEY = 'studio2.flowCanvasAdapter';

export interface Studio2WorkspaceShellRuntimeState {
  currentMode: WorkspaceShellMode;
  flowCanvasStatus: 'pending' | 'ready' | 'disposed' | 'error';
  flowCanvasInstanceCount: number;
  resizeCount: number;
  renderCount: number;
  lastResizeReason: string;
  lastError: string;
}

export function createWorkspaceShellRuntimeState(): Studio2WorkspaceShellRuntimeState {
  return {
    currentMode: 'flow',
    flowCanvasStatus: 'pending',
    flowCanvasInstanceCount: 0,
    resizeCount: 0,
    renderCount: 0,
    lastResizeReason: 'none',
    lastError: ''
  };
}

export interface Studio2WorkspaceShellRuntimeHandle {
  readonly state: Studio2WorkspaceShellRuntimeState;
  mountFlowCanvas(canvasId: string): HostedFlowCanvasAdapter;
  setMode(mode: WorkspaceShellMode): void;
  resizeFlowCanvas(reason: string): boolean;
  getFlowCanvasViewState(): HostedFlowCanvasViewState | null;
  dispose(): void;
}

export class Studio2WorkspaceShellRuntime implements Studio2WorkspaceShellRuntimeHandle {
  private flowCanvasAdapter: HostedFlowCanvasAdapter | null = null;
  private flowCanvasId: string | null = null;
  private disposed = false;

  constructor(
    private readonly services: LegacyFrontendServices,
    private readonly scope: Studio2LifecycleScope,
    public readonly state: Studio2WorkspaceShellRuntimeState
  ) {
  }

  mountFlowCanvas(canvasId: string): HostedFlowCanvasAdapter {
    if (this.disposed || this.scope.isDisposed) {
      throw new Error('Studio2 workspace shell has been disposed.');
    }

    if (this.flowCanvasAdapter) {
      if (this.flowCanvasId !== canvasId) {
        throw new Error('Studio2 workspace shell already owns a FlowCanvas adapter.');
      }

      return this.flowCanvasAdapter;
    }

    try {
      const adapter = this.services.flowCanvasAdapterModule.createHostedFlowCanvasAdapter(canvasId, {
        eventBus: this.services.eventBus
      });

      this.flowCanvasAdapter = adapter;
      this.flowCanvasId = canvasId;
      this.state.flowCanvasStatus = 'ready';
      this.state.flowCanvasInstanceCount = 1;
      this.state.lastError = '';

      this.services.serviceRegistry.register(FLOW_CANVAS_ADAPTER_SERVICE_KEY, adapter);
      this.scope.trackRegistryRegistration({
        unregister: () => {
          this.services.serviceRegistry.unregister(FLOW_CANVAS_ADAPTER_SERVICE_KEY, adapter);
        }
      });
      this.scope.trackListener(() => {
        this.disposeFlowCanvas();
      });

      this.resizeFlowCanvas('mount');
      return adapter;
    } catch (error) {
      this.state.flowCanvasStatus = 'error';
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

  dispose(): void {
    this.disposed = true;
    this.disposeFlowCanvas();
  }

  private disposeFlowCanvas(): void {
    if (!this.flowCanvasAdapter) {
      return;
    }

    this.flowCanvasAdapter.dispose();
    this.flowCanvasAdapter = null;
    this.flowCanvasId = null;
    this.state.flowCanvasStatus = 'disposed';
    this.state.flowCanvasInstanceCount = 0;
  }
}
