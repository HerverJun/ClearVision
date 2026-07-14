import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const canonicalHostFactory = vi.hoisted(() => vi.fn());
const lifecycleOwnerCountReporter = vi.hoisted(() => vi.fn());

vi.mock('@/labs/canvas/canonicalFlowCanvas', () => ({
  createCanonicalFlowCanvasHost: canonicalHostFactory
}));
vi.mock('@/platform/diagnostics/studioUiLifecycleDiagnostics', () => ({
  reportCanvasOwnerCountForDiagnostics: lifecycleOwnerCountReporter
}));

import {
  CanvasLabOwnerConflictError,
  getCanvasLabDiagnostics,
  mountCanvasLab,
  type CanvasLabController
} from '@/labs/canvas/canvasLabOwner';
import type {
  CanonicalCanvasRuntimeSnapshot,
  CanonicalFlowCanvasHost
} from '@/labs/canvas/canonicalFlowCanvas';
import {
  CANONICAL_OPERATOR_FLOW_FIXTURE,
  type OperatorFlowDto
} from '@/labs/canvas/operatorFlowFixtures';

interface FakeCanonicalHost extends CanonicalFlowCanvasHost {
  readonly disposalEvents: string[];
}

const mountedControllers: CanvasLabController[] = [];
let disposalEvents: string[];

function cloneFlow(flow: OperatorFlowDto): OperatorFlowDto {
  return structuredClone(flow);
}

function createFakeHost(initialFlow: unknown): FakeCanonicalHost {
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
      interactionCleanupCount: interactionDisposed ? 0 : 6
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
      listeners.forEach(listener => listener());
    }),
    resize: vi.fn(() => {
      listeners.forEach(listener => listener());
    }),
    validateConnection: vi.fn(() => validationResponses[validationIndex++] ?? null),
    subscribe: vi.fn(listener => {
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
    }),
    disposeAdapter: vi.fn(() => {
      if (adapterDisposed) {
        return;
      }
      adapterDisposed = true;
      listeners.clear();
      disposalEvents.push('adapter');
    })
  };
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
      interactionCleanupCount: 0
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

  it('loads all fixture sizes through the same owner generation', () => {
    const controller = mountTrackedController();
    const generation = controller.generation;

    controller.loadFixture('benchmark-100');
    expect(controller.getDiagnostics()).toMatchObject({
      generation,
      fixtureId: 'benchmark-100',
      runtime: { nodeCount: 100, connectionCount: 150 }
    });

    controller.loadFixture('stress-300');
    expect(controller.getDiagnostics()).toMatchObject({
      generation,
      fixtureId: 'stress-300',
      runtime: { nodeCount: 300, connectionCount: 450 }
    });
    expect(canonicalHostFactory).toHaveBeenCalledTimes(1);
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
