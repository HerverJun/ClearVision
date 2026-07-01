import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it } from 'vitest';
import type {
  HostedFlowCanvasAdapter,
  HostedFlowCanvasViewState,
  LegacyFrontendServices
} from '@/adapters/legacyModules';
import { Studio2LifecycleScope } from '@/foundation/studio2Lifecycle';
import {
  FLOW_EDITOR_PORT_SERVICE_KEY,
  Studio2WorkspaceShellRuntime,
  createWorkspaceShellRuntimeState
} from '@/workspace/workspaceShellRuntime';
import {
  MAX_DOCK_WIDTH,
  MIN_DOCK_WIDTH,
  useWorkspaceShellStore
} from '@/workspace/workspaceShellStore';

describe('Studio2WorkspaceShellRuntime', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('creates and registers one hosted FlowCanvas adapter per workspace lifecycle', () => {
    const fixture = createRuntimeFixture();
    const first = fixture.runtime.mountFlowCanvas('studio2-flow-canvas');
    const second = fixture.runtime.mountFlowCanvas('studio2-flow-canvas');

    expect(second).toBe(first);
    expect(fixture.createCount).toBe(1);
    expect(fixture.registry.get(FLOW_EDITOR_PORT_SERVICE_KEY)).toBe(first);
    expect(fixture.state.flowCanvasInstanceCount).toBe(1);

    fixture.runtime.dispose();

    expect(fixture.disposeCount).toBe(1);
    expect(fixture.subscriptionDisposeCount).toBe(2);
    expect(fixture.state.flowCanvasStatus).toBe('disposed');
  });

  it('keeps Port, registry, listeners and adapters bounded across 20 workspace cycles', () => {
    for (let index = 0; index < 20; index += 1) {
      const fixture = createRuntimeFixture();
      const port = fixture.runtime.mountFlowCanvas(`studio2-flow-canvas-${String(index)}`);

      expect(fixture.createCount).toBe(1);
      expect(fixture.registry.get(FLOW_EDITOR_PORT_SERVICE_KEY)).toBe(port);
      expect(fixture.structureSubscribeCount).toBe(1);
      expect(fixture.selectionSubscribeCount).toBe(1);
      expect(fixture.state.flowCanvasInstanceCount).toBe(1);

      fixture.runtime.dispose();
      fixture.scope.dispose();

      expect(fixture.disposeCount).toBe(1);
      expect(fixture.subscriptionDisposeCount).toBe(2);
      expect(fixture.registry.size).toBe(0);
      expect(fixture.state.flowCanvasInstanceCount).toBe(0);
      expect(fixture.state.flowCanvasStatus).toBe('disposed');
      expect(fixture.state.flowEditorStatus).toBe('disposed');
    }
  });

  it('keeps nodes, edges, selection, scale and offset across Flow Tool Review Flow mode switches', () => {
    const fixture = createRuntimeFixture({
      viewState: {
        selectedNode: 'node-1',
        selectedConnection: 'edge-1',
        scale: 1.35,
        offset: { x: 24, y: -12 },
        nodeCount: 2,
        connectionCount: 1
      }
    });

    fixture.runtime.mountFlowCanvas('studio2-flow-canvas');
    const before = fixture.runtime.getFlowCanvasViewState();

    fixture.runtime.setMode('tool');
    fixture.runtime.setMode('review');
    fixture.runtime.setMode('flow');

    expect(fixture.runtime.getFlowCanvasViewState()).toEqual(before);
    expect(fixture.state.currentMode).toBe('flow');
    expect(fixture.resizeCount).toBeGreaterThanOrEqual(2);
    expect(fixture.renderCount).toBeGreaterThanOrEqual(2);
  });

  it('bounds dock widths and resizes the FlowCanvas after dock toggles', () => {
    const fixture = createRuntimeFixture();
    const shellStore = useWorkspaceShellStore();

    fixture.runtime.mountFlowCanvas('studio2-flow-canvas');
    shellStore.setLeftDockWidth(80);
    shellStore.setRightDockWidth(800);
    shellStore.toggleLeftDock();
    fixture.runtime.resizeFlowCanvas('left-dock-toggle');
    shellStore.toggleRightDock();
    fixture.runtime.resizeFlowCanvas('right-dock-toggle');

    expect(shellStore.leftDockWidth).toBe(MIN_DOCK_WIDTH);
    expect(shellStore.rightDockWidth).toBe(MAX_DOCK_WIDTH);
    expect(shellStore.leftDockCollapsed).toBe(true);
    expect(shellStore.rightDockCollapsed).toBe(true);
    expect(fixture.resizeCount).toBeGreaterThanOrEqual(3);
    expect(fixture.renderCount).toBeGreaterThanOrEqual(3);
    expect(fixture.state.lastResizeReason).toBe('right-dock-toggle');
  });
});

interface RuntimeFixture {
  runtime: Studio2WorkspaceShellRuntime;
  readonly state: ReturnType<typeof createWorkspaceShellRuntimeState>;
  readonly scope: Studio2LifecycleScope;
  readonly registry: Map<string, unknown>;
  createCount: number;
  resizeCount: number;
  renderCount: number;
  disposeCount: number;
  structureSubscribeCount: number;
  selectionSubscribeCount: number;
  subscriptionDisposeCount: number;
}

function createRuntimeFixture(options?: { readonly viewState?: HostedFlowCanvasViewState }): RuntimeFixture {
  const scope = new Studio2LifecycleScope();
  const state = createWorkspaceShellRuntimeState();
  const registry = new Map<string, unknown>();
  const fixture: RuntimeFixture = {
    runtime: null as unknown as Studio2WorkspaceShellRuntime,
    state,
    scope,
    registry,
    createCount: 0,
    resizeCount: 0,
    renderCount: 0,
    disposeCount: 0,
    structureSubscribeCount: 0,
    selectionSubscribeCount: 0,
    subscriptionDisposeCount: 0
  };
  const services: LegacyFrontendServices = {
    httpClient: {
      getRoot: <T,>() => Promise.resolve({} as T)
    },
    webMessageBridge: {
      on: () => () => {},
      sendMessage: () => Promise.resolve({})
    },
    eventBus: {
      on: () => () => {},
      emit: () => undefined
    },
    serviceRegistry: {
      register(key, service) {
        registry.set(key, service);
        return service;
      },
      unregister(key, expectedService) {
        if (expectedService !== undefined && registry.get(key) !== expectedService) {
          return false;
        }

        return registry.delete(key);
      }
    },
    flowCanvasAdapterModule: {
      createHostedFlowCanvasAdapter() {
        fixture.createCount += 1;
        return createAdapterFixture(fixture, options?.viewState);
      }
    }
  };

  fixture.runtime = new Studio2WorkspaceShellRuntime(services, scope, state);
  return fixture;
}

function createAdapterFixture(
  fixture: RuntimeFixture,
  viewState?: HostedFlowCanvasViewState
): HostedFlowCanvasAdapter {
  const resolvedViewState = viewState ?? {
    selectedNode: null,
    selectedConnection: null,
    scale: 1,
    offset: { x: 0, y: 0 },
    nodeCount: 0,
    connectionCount: 0
  };

  return {
    resize: () => {
      fixture.resizeCount += 1;
    },
    render: () => {
      fixture.renderCount += 1;
    },
    dispose: () => {
      fixture.disposeCount += 1;
    },
    getViewState: () => resolvedViewState
    ,
    getSnapshot: () => ({
      flowRevision: 1,
      selectionRevision: 1,
      selectedNodeId: resolvedViewState.selectedNode,
      flow: {
        operators: resolvedViewState.selectedNode
          ? [{ id: resolvedViewState.selectedNode, type: 'MockNode', parameters: [] }]
          : [],
        connections: []
      },
      selectedNode: resolvedViewState.selectedNode
        ? { id: resolvedViewState.selectedNode, type: 'MockNode', title: 'Mock', parameters: [] }
        : null
    }),
    replaceFlow: () => undefined,
    selectNode: () => true,
    patchNodeParameters: () => ({
      updated: true,
      reason: 'updated',
      missingParameters: []
    }),
    subscribeStructure: (listener) => {
      fixture.structureSubscribeCount += 1;
      listener({});
      return () => {
        fixture.subscriptionDisposeCount += 1;
      };
    },
    subscribeSelection: (listener) => {
      fixture.selectionSubscribeCount += 1;
      listener({});
      return () => {
        fixture.subscriptionDisposeCount += 1;
      };
    }
  };
}
