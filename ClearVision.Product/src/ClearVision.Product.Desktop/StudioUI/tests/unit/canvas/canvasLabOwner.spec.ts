import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const canonicalHostFactory = vi.hoisted(() => vi.fn());
const lifecycleOwnerCountReporter = vi.hoisted(() => vi.fn());

vi.mock('@/platform/canvas', () => ({
  createCanonicalFlowCanvasHost: canonicalHostFactory
}));
vi.mock('@/platform/diagnostics/studioUiLifecycleDiagnostics', () => ({
  reportCanvasOwnerCountForDiagnostics: lifecycleOwnerCountReporter
}));

import {
  CanvasLabOwnerConflictError,
  getCanvasLabDiagnostics,
  mountCanvasLab,
  type CanvasLabController,
  type CanvasLabDiagnostics
} from '@/labs/canvas/canvasLabOwner';
import type {
  CanonicalCanvasRuntimeSnapshot,
  CanonicalFlowCanvasHost
} from '@/platform/canvas';
import {
  CANVAS_FIXTURE_IDS,
  CANONICAL_OPERATOR_FLOW_FIXTURE,
  type OperatorFlowDto
} from '@/labs/canvas/operatorFlowFixtures';

interface FakeCanonicalHost extends CanonicalFlowCanvasHost {
  readonly disposalEvents: string[];
}

interface FakeHostOptions {
  readonly subscribeError?: Error;
  readonly interactionDisposeError?: Error;
  readonly adapterDisposeError?: Error;
}

const mountedControllers: CanvasLabController[] = [];
let disposalEvents: string[];

function cloneFlow(flow: OperatorFlowDto): OperatorFlowDto {
  return structuredClone(flow);
}

function createFakeHost(initialFlow: unknown, options: FakeHostOptions = {}): FakeCanonicalHost {
  let flow = cloneFlow(initialFlow as OperatorFlowDto);
  let interactionDisposed = false;
  let adapterDisposed = false;
  const listeners = new Set<() => void>();
  const validationResponses = [
    'duplicate-connection',
    'input-port-occupied',
    'self-connection',
    'incompatible-port-type',
    'cycle'
  ];
  let validationIndex = 0;

  const runtimeSnapshot = (): CanonicalCanvasRuntimeSnapshot => Object.freeze({
    nodeCount: adapterDisposed ? 0 : flow.operators.length,
    connectionCount: adapterDisposed ? 0 : flow.connections.length,
    flowRevision: 1,
    selectionRevision: 0,
    selectedNodeId: null,
    selectedNodeIds: Object.freeze([]),
    selectedConnectionId: null,
    multiSelectionCount: 0,
    scale: 1,
    offsetX: 0,
    offsetY: 0,
    logicalWidth: 960,
    logicalHeight: 540,
    backingWidth: 960,
    backingHeight: 540,
    dpr: 1,
    isConnecting: false,
    isDraggingNodes: false,
    isPanning: false,
    isSelecting: false,
    canUndo: false,
    canRedo: false,
    mutationGate: 'editable',
    nodes: Object.freeze([]),
    resources: Object.freeze({
      adapterDisposed,
      canvasDestroyed: adapterDisposed,
      interactionDisposed,
      resizeObserverActive: !adapterDisposed,
      themeObserverActive: !adapterDisposed,
      drawFramePending: false,
      resizeFramePending: false,
      interactionFramePending: false,
      contextMenuTimerActive: false,
      structureListenerCount: listeners.size > 0 ? 1 : 0,
      viewListenerCount: listeners.size > 0 ? 1 : 0,
      selectionListenerCount: listeners.size > 0 ? 1 : 0,
      interactionCleanupCount: interactionDisposed ? 0 : 6,
      facadeListenerCount: listeners.size
    })
  });

  return {
    disposalEvents,
    serialize: vi.fn(() => ({
      operators: structuredClone(flow.operators),
      connections: structuredClone(flow.connections),
      decisionConfiguration: structuredClone(flow.decisionConfiguration)
    })),
    replaceFlow: vi.fn(nextFlow => {
      flow = cloneFlow(nextFlow as OperatorFlowDto);
      validationIndex = 0;
      listeners.forEach(listener => listener());
    }),
    resize: vi.fn(() => {
      listeners.forEach(listener => listener());
    }),
    validateConnection: vi.fn(() => validationResponses[validationIndex++] ?? null),
    subscribe: vi.fn(listener => {
      if (options.subscribeError) {
        throw options.subscribeError;
      }
      listeners.add(listener);
      let subscribed = true;
      return () => {
        if (!subscribed) {
          return;
        }
        subscribed = false;
        disposalEvents.push('unsubscribe');
        listeners.delete(listener);
      };
    }),
    getRuntimeSnapshot: vi.fn(runtimeSnapshot),
    disposeInteraction: vi.fn(() => {
      if (interactionDisposed) {
        return;
      }
      interactionDisposed = true;
      disposalEvents.push('interaction');
      if (options.interactionDisposeError) {
        throw options.interactionDisposeError;
      }
    }),
    disposeAdapter: vi.fn(() => {
      if (adapterDisposed) {
        return;
      }
      adapterDisposed = true;
      listeners.clear();
      disposalEvents.push('adapter');
      if (options.adapterDisposeError) {
        throw options.adapterDisposeError;
      }
    })
  } as unknown as FakeCanonicalHost;
}

function mountTrackedController(): CanvasLabController {
  const controller = mountCanvasLab({
    canvasId: 'unit-canvas',
    initialFixtureId: 'canonical'
  });
  mountedControllers.push(controller);
  return controller;
}

beforeEach(() => {
  disposalEvents = [];
  canonicalHostFactory.mockReset();
  lifecycleOwnerCountReporter.mockReset();
  canonicalHostFactory.mockImplementation((_canvasId: string, initialFlow: unknown) =>
    createFakeHost(initialFlow));
});

afterEach(() => {
  for (const controller of mountedControllers.splice(0).reverse()) {
    controller.dispose();
  }
});

describe('CanvasLab capability owner', () => {
  it('enforces one mounted owner and allows a new generation only after disposal', () => {
    const first = mountTrackedController();

    expect(first.getDiagnostics().ownerCount).toBe(1);
    expect(() => mountCanvasLab({ canvasId: 'second-canvas' }))
      .toThrow(CanvasLabOwnerConflictError);
    expect(canonicalHostFactory).toHaveBeenCalledTimes(1);

    first.dispose();
    const second = mountTrackedController();
    expect(second.generation).toBe(first.generation + 1);
    expect(second.getDiagnostics().ownerCount).toBe(1);
    expect(canonicalHostFactory).toHaveBeenCalledTimes(2);
  });

  it('disposes unsubscribe, interaction, then adapter exactly once', () => {
    const controller = mountTrackedController();
    const mountsBeforeDispose = controller.getDiagnostics().totalMounts;

    controller.dispose();
    controller.dispose();

    expect(disposalEvents).toEqual(['unsubscribe', 'interaction', 'adapter']);
    const disposed = getCanvasLabDiagnostics();
    expect(disposed.ownerCount).toBe(0);
    expect(disposed.totalMounts).toBe(mountsBeforeDispose);
    expect(disposed.runtime?.resources).toMatchObject({
      adapterDisposed: true,
      canvasDestroyed: true,
      interactionDisposed: true,
      resizeObserverActive: false,
      themeObserverActive: false,
      structureListenerCount: 0,
      viewListenerCount: 0,
      selectionListenerCount: 0,
      interactionCleanupCount: 0,
      facadeListenerCount: 0
    });
  });

  it('projects the mounted owner count into the shared lifecycle diagnostics', () => {
    const controller = mountTrackedController();

    expect(lifecycleOwnerCountReporter).toHaveBeenLastCalledWith(1);

    controller.dispose();

    expect(lifecycleOwnerCountReporter).toHaveBeenLastCalledWith(0);
  });

  it('round-trips the canonical DTO without changing its identity fingerprint', () => {
    const controller = mountTrackedController();
    const result = controller.runIdentityRoundTrip();

    expect(result.state).toBe('pass');
    expect(result.beforeFingerprint).toMatch(/^[0-9a-f]{8}$/);
    expect(result.afterFingerprint).toBe(result.beforeFingerprint);
    expect(controller.getDiagnostics().identity).toEqual(result);
    expect(controller.getDiagnostics().validation).toHaveLength(5);
    expect(controller.getDiagnostics().validation.every(item => item.passed)).toBe(true);
  });

  it('validates the canonical rejection matrix with the frozen node and port identities', () => {
    mountTrackedController();
    const host = canonicalHostFactory.mock.results[0]?.value as FakeCanonicalHost;
    const ids = CANVAS_FIXTURE_IDS;

    expect(vi.mocked(host.validateConnection).mock.calls).toEqual([
      [ids.acquisition.operator, 0, ids.threshold.operator, 0],
      [ids.acquisition.operator, 0, ids.blob.operator, 0],
      [ids.acquisition.operator, 0, ids.acquisition.operator, 0],
      [ids.threshold.operator, 0, ids.regionErosion.operator, 0],
      [ids.blob.operator, 0, ids.acquisition.operator, 0]
    ]);
  });

  it('loads all fixture sizes through the same owner generation', () => {
    const controller = mountTrackedController();
    const generation = controller.generation;

    controller.loadFixture('benchmark-100');
    expect(controller.getDiagnostics()).toMatchObject({
      generation,
      fixtureId: 'benchmark-100',
      validation: [],
      runtime: { nodeCount: 100, connectionCount: 150 }
    });

    controller.loadFixture('stress-300');
    expect(controller.getDiagnostics()).toMatchObject({
      generation,
      fixtureId: 'stress-300',
      validation: [],
      runtime: { nodeCount: 300, connectionCount: 450 }
    });
    expect(canonicalHostFactory).toHaveBeenCalledTimes(1);
  });

  it('publishes one fixture-consistent diagnostic snapshot per switch', () => {
    const snapshots: CanvasLabDiagnostics[] = [];
    const controller = mountCanvasLab({
      canvasId: 'transaction-canvas',
      initialFixtureId: 'canonical',
      onDiagnostics: diagnostics => snapshots.push(diagnostics)
    });
    mountedControllers.push(controller);
    snapshots.splice(0);

    controller.loadFixture('benchmark-100');
    expect(snapshots).toHaveLength(1);
    expect(snapshots[0]).toMatchObject({
      fixtureId: 'benchmark-100',
      validation: [],
      runtime: { nodeCount: 100, connectionCount: 150 }
    });

    controller.loadFixture('canonical');
    expect(snapshots).toHaveLength(2);
    expect(snapshots[1]).toMatchObject({
      fixtureId: 'canonical',
      runtime: { nodeCount: 5, connectionCount: 3 }
    });
    expect(snapshots[1]?.validation).toHaveLength(5);
    expect(snapshots[1]?.validation.every(item => item.passed)).toBe(true);
  });

  it('rejects commands from a disposed controller', () => {
    const controller = mountTrackedController();
    controller.dispose();

    expect(() => controller.loadFixture('canonical')).toThrow('no longer active');
    expect(() => controller.runIdentityRoundTrip()).toThrow('no longer active');
    expect(() => controller.resize()).toThrow('no longer active');
    expect(() => controller.getDiagnostics()).toThrow('no longer active');
  });

  it('cleans the host and owner projection when mount setup fails', () => {
    canonicalHostFactory.mockImplementationOnce((_canvasId: string, initialFlow: unknown) =>
      createFakeHost(initialFlow, { subscribeError: new Error('subscribe failed') }));

    expect(() => mountCanvasLab({ canvasId: 'failing-canvas' })).toThrow('subscribe failed');
    expect(disposalEvents).toEqual(['interaction', 'adapter']);
    expect(getCanvasLabDiagnostics()).toMatchObject({ status: 'error', ownerCount: 0 });
    expect(lifecycleOwnerCountReporter).toHaveBeenLastCalledWith(0);
  });

  it('unsubscribes when the initial diagnostics callback fails', () => {
    let diagnosticsCalls = 0;

    expect(() => mountCanvasLab({
      canvasId: 'diagnostics-failing-canvas',
      onDiagnostics: () => {
        diagnosticsCalls += 1;
        if (diagnosticsCalls === 1) throw new Error('diagnostics failed');
      }
    })).toThrow('diagnostics failed');

    expect(diagnosticsCalls).toBe(2);
    expect(disposalEvents).toEqual(['unsubscribe', 'interaction', 'adapter']);
    expect(getCanvasLabDiagnostics()).toMatchObject({ status: 'error', ownerCount: 0 });
    expect(lifecycleOwnerCountReporter).toHaveBeenLastCalledWith(0);
  });

  it('continues cleanup when host disposal methods throw', () => {
    canonicalHostFactory.mockImplementationOnce((_canvasId: string, initialFlow: unknown) =>
      createFakeHost(initialFlow, {
        interactionDisposeError: new Error('interaction cleanup failed'),
        adapterDisposeError: new Error('adapter cleanup failed')
      }));
    const controller = mountTrackedController();

    expect(() => controller.dispose()).toThrow('interaction cleanup failed');
    expect(disposalEvents).toEqual(['unsubscribe', 'interaction', 'adapter']);
    expect(getCanvasLabDiagnostics()).toMatchObject({
      status: 'error',
      ownerCount: 0,
      lastError: 'interaction cleanup failed'
    });
    expect(lifecycleOwnerCountReporter).toHaveBeenLastCalledWith(0);
  });

  it('survives twenty mount/unmount cycles without overlapping owners', () => {
    const before = getCanvasLabDiagnostics();

    for (let cycle = 0; cycle < 20; cycle += 1) {
      const controller = mountTrackedController();
      expect(controller.getDiagnostics().ownerCount).toBe(1);
      expect(window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.ownerCount).toBe(1);
      controller.dispose();
      expect(getCanvasLabDiagnostics().ownerCount).toBe(0);
    }

    const after = getCanvasLabDiagnostics();
    expect(after.totalMounts - before.totalMounts).toBe(20);
    expect(after.totalDisposals - before.totalDisposals).toBe(20);
    expect(disposalEvents).toHaveLength(60);
    expect(disposalEvents.filter(event => event === 'unsubscribe')).toHaveLength(20);
    expect(disposalEvents.filter(event => event === 'interaction')).toHaveLength(20);
    expect(disposalEvents.filter(event => event === 'adapter')).toHaveLength(20);
  });

  it('preserves the real canonical flow passed to the host factory', () => {
    mountTrackedController();

    expect(canonicalHostFactory).toHaveBeenCalledWith(
      'unit-canvas',
      CANONICAL_OPERATOR_FLOW_FIXTURE
    );
  });
});
