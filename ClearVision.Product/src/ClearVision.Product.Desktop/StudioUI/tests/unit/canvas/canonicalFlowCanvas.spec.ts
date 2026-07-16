import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const adapterFactory = vi.hoisted(() => vi.fn());
const disposalEvents = vi.hoisted(() => [] as string[]);

vi.mock('@clearvision/canonical-flow-canvas', () => ({
  createHostedFlowCanvasAdapter: adapterFactory
}));

vi.mock('@clearvision/canonical-flow-interaction', () => ({
  FlowEditorInteraction: class FakeInteraction {
    disposed = false;
    isConnecting = false;
    isDraggingNodes = false;
    isPanning = false;
    isSelecting = false;
    multiSelectedNodes = new Set<string>();
    cleanup = [1, 2, 3];
    viewStateNotifyRaf = null;
    history = ['baseline'];
    historyIndex = 0;
    private readonly canvas: FakeCanvas;
    private readonly options: Readonly<Record<string, unknown>>;

    constructor(canvas: FakeCanvas, options: Readonly<Record<string, unknown>>) {
      this.canvas = canvas;
      this.options = options;
    }

    addOperatorNode(type: string, x: number, y: number, data: Readonly<Record<string, unknown>>) {
      const id = `node-${this.canvas.nodes.size + 1}`;
      const node = {
        id,
        type,
        title: String(data.displayName ?? data.name ?? type),
        x,
        y,
        inputs: data.inputPorts ?? [],
        outputs: data.outputPorts ?? [],
        parameters: data.parameters ?? [],
        disabled: false
      };
      this.canvas.nodes.set(id, node);
      emitStructure();
      return node;
    }

    clearSelection() {
      this.multiSelectedNodes.clear();
      this.canvas.selectedNode = null;
      emitSelection();
    }

    selectNode(nodeId: string) {
      this.multiSelectedNodes.add(nodeId);
      this.canvas.selectedNode = nodeId;
      emitSelection();
    }

    selectAll() {
      this.multiSelectedNodes = new Set(this.canvas.nodes.keys());
      this.canvas.selectedNode = [...this.multiSelectedNodes].at(-1) ?? null;
      emitSelection();
    }

    copySelectedNodes() {}
    pasteNodes() { return false; }
    deleteSelectedItems() { return false; }
    duplicateNodeFromCanvasRequest() { return false; }
    undo() { return false; }
    redo() { return false; }
    resetTransientInteractionAfterRestore() {}
    resetHistory() { this.history = ['baseline']; this.historyIndex = 0; }
    getHistoryState() { return { canUndo: this.historyIndex > 0, canRedo: false }; }
    saveState() {
      this.history.push('commit');
      this.historyIndex += 1;
      const callback = this.options.onDraftCommitted as (() => void) | undefined;
      callback?.();
    }
    destroy() {
      if (this.disposed) return;
      this.disposed = true;
      this.cleanup = [];
      disposalEvents.push('interaction');
    }
  }
}));

import {
  CanonicalFlowCanvasOwnerConflictError,
  createCanonicalFlowCanvasHost,
  type CanonicalFlowCanvasHost,
  type CanonicalOperatorDefinition
} from '@/platform/canvas';

interface FakeCanvas {
  canvas: HTMLCanvasElement;
  nodes: Map<string, Record<string, unknown>>;
  connections: Array<Record<string, unknown>>;
  selectedNode: string | null;
  selectedConnection: Record<string, unknown> | null;
  scale: number;
  offset: { x: number; y: number };
  _dpr: number;
  _logicalWidth: number;
  _logicalHeight: number;
  _isDestroyed: boolean;
  _resizeObserver: object | null;
  _themeObserver: object | null;
  _animationFrameId: number | null;
  _resizeRafId: number | null;
  _contextMenuOpenTimer: number | null;
  structureStateListeners: Set<unknown>;
  viewStateListeners: Set<unknown>;
  selectionStateListeners: Set<unknown>;
  nodeRunEnabled: boolean;
  nodeHelpEnabled: boolean;
  getSelectionState(): Record<string, unknown>;
  getNodeScreenRect(nodeId: string): Record<string, number> | null;
  getPortPosition(nodeId: string, portIndex: number, isOutput: boolean): Record<string, number> | null;
  getConnectionValidationError(): string | null;
  addConnection(sourceId: string, sourcePort: number, targetId: string, targetPort: number): object;
  removeConnection(connectionId: string): boolean;
  toggleNodeDisabled(nodeId: string): boolean;
  markSelectionChanged(): void;
  notifyViewStateChanged(): void;
  invalidate(): void;
}

let raw: FakeCanvas;
let structureListeners: Set<() => void>;
let selectionListeners: Set<() => void>;
let viewListeners: Set<() => void>;
let mountedHost: CanonicalFlowCanvasHost | undefined;

function emitStructure(): void { structureListeners.forEach(listener => listener()); }
function emitSelection(): void { selectionListeners.forEach(listener => listener()); }
function emitView(): void { viewListeners.forEach(listener => listener()); }

function createAdapter() {
  const canvas = document.createElement('canvas');
  canvas.id = 'canonical-unit-canvas';
  raw = {
    canvas,
    nodes: new Map(),
    connections: [],
    selectedNode: null,
    selectedConnection: null,
    scale: 1,
    offset: { x: 0, y: 0 },
    _dpr: 1,
    _logicalWidth: 800,
    _logicalHeight: 600,
    _isDestroyed: false,
    _resizeObserver: {},
    _themeObserver: {},
    _animationFrameId: null,
    _resizeRafId: null,
    _contextMenuOpenTimer: null,
    structureStateListeners: new Set(),
    viewStateListeners: new Set(),
    selectionStateListeners: new Set(),
    nodeRunEnabled: true,
    nodeHelpEnabled: true,
    getSelectionState: () => ({
      selectedNodeId: raw.selectedNode,
      selectedConnectionId: raw.selectedConnection?.id ?? null,
      selectionRevision: 0
    }),
    getNodeScreenRect: nodeId => raw.nodes.has(nodeId) ? { x: 10, y: 20, width: 180, height: 88 } : null,
    getPortPosition: () => ({ x: 20, y: 30 }),
    getConnectionValidationError: () => null,
    addConnection: (sourceId, sourcePort, targetId, targetPort) => {
      const connection = { id: `connection-${raw.connections.length + 1}`, sourceId, sourcePort, targetId, targetPort };
      raw.connections.push(connection);
      emitStructure();
      return connection;
    },
    removeConnection: connectionId => {
      const before = raw.connections.length;
      raw.connections = raw.connections.filter(connection => connection.id !== connectionId);
      if (raw.connections.length !== before) emitStructure();
      return raw.connections.length !== before;
    },
    toggleNodeDisabled: nodeId => {
      const node = raw.nodes.get(nodeId);
      if (!node) return false;
      node.disabled = node.disabled !== true;
      emitStructure();
      return true;
    },
    markSelectionChanged: emitSelection,
    notifyViewStateChanged: emitView,
    invalidate: vi.fn()
  };
  structureListeners = new Set();
  selectionListeners = new Set();
  viewListeners = new Set();

  return {
    raw,
    disposed: false,
    serialize: () => ({
      operators: [...raw.nodes.values()],
      connections: raw.connections,
      decisionConfiguration: null
    }),
    replaceFlow: (flow: Readonly<Record<string, unknown>>) => {
      const operators = Array.isArray(flow.operators) ? flow.operators : [];
      raw.nodes = new Map(operators.map((operator, index) => {
        const item = operator as Record<string, unknown>;
        const id = String(item.id ?? `baseline-${index}`);
        return [id, { ...item, id, inputs: item.inputPorts ?? [], outputs: item.outputPorts ?? [] }];
      }));
      raw.connections = Array.isArray(flow.connections)
        ? flow.connections as Array<Record<string, unknown>>
        : [];
    },
    resize: emitView,
    subscribeStructureState: (listener: () => void) => {
      structureListeners.add(listener);
      raw.structureStateListeners.add(listener);
      return () => { structureListeners.delete(listener); raw.structureStateListeners.delete(listener); };
    },
    subscribeViewState: (listener: () => void) => {
      viewListeners.add(listener);
      raw.viewStateListeners.add(listener);
      return () => { viewListeners.delete(listener); raw.viewStateListeners.delete(listener); };
    },
    subscribeSelection: (listener: () => void) => {
      selectionListeners.add(listener);
      raw.selectionStateListeners.add(listener);
      return () => { selectionListeners.delete(listener); raw.selectionStateListeners.delete(listener); };
    },
    dispose() {
      raw._isDestroyed = true;
      raw._resizeObserver = null;
      raw._themeObserver = null;
      disposalEvents.push('adapter');
      this.disposed = true;
    }
  };
}

const thresholdOperator: CanonicalOperatorDefinition = Object.freeze({
  operatorType: 'Threshold',
  displayName: '全局阈值处理',
  category: '图像预处理',
  iconName: null,
  inputPorts: Object.freeze([{ name: 'Image', displayName: '图像', dataType: 'Image', isRequired: true }]),
  outputPorts: Object.freeze([{ name: 'Binary', displayName: '二值图', dataType: 'Image', isRequired: false }]),
  parameters: Object.freeze([])
});

beforeEach(() => {
  disposalEvents.splice(0);
  adapterFactory.mockReset();
  adapterFactory.mockImplementation(createAdapter);
});

afterEach(() => {
  mountedHost?.disposeInteraction();
  mountedHost?.disposeAdapter();
  mountedHost = undefined;
});

describe('production canonical FlowCanvas facade', () => {
  it('keeps hydrate at local revision zero and increments once per atomic draft command', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-1', name: '流程', operators: [], connections: []
    });

    expect(mountedHost.getProjection().runtime.flowRevision).toBe(0);
    const result = mountedHost.addOperator(thresholdOperator);
    expect(result).toMatchObject({ ok: true, flowRevision: 1 });
    expect(mountedHost.getProjection().draft.operators).toHaveLength(1);

    emitSelection();
    emitView();
    expect(mountedHost.getProjection().runtime.flowRevision).toBe(1);
  });

  it('rejects readonly and running mutations without changing the draft', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-1', name: '流程', operators: [], connections: []
    });
    mountedHost.setMutationGate('readonly');
    expect(mountedHost.addOperator(thresholdOperator)).toMatchObject({ ok: false, code: 'readonly' });
    mountedHost.setMutationGate('running');
    expect(mountedHost.addOperator(thresholdOperator)).toMatchObject({ ok: false, code: 'running' });
    expect(mountedHost.getProjection().draft.operators).toHaveLength(0);
    expect(mountedHost.getProjection().runtime.flowRevision).toBe(0);
  });

  it('returns stable connection rejection reasons', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-1', name: '流程', operators: [], connections: []
    });
    raw.getConnectionValidationError = () => 'cycle';

    const result = mountedHost.connect({
      sourceNodeId: 'source', sourcePortId: 'output', targetNodeId: 'target', targetPortId: 'input'
    });

    expect(result).toMatchObject({ ok: false, code: 'missing-port' });
  });

  it('enforces one global owner and rejects commands after ordered disposal', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-1', name: '流程', operators: [], connections: []
    });
    expect(() => createCanonicalFlowCanvasHost('second-canvas', {}))
      .toThrow(CanonicalFlowCanvasOwnerConflictError);

    mountedHost.disposeInteraction();
    mountedHost.disposeAdapter();
    expect(disposalEvents).toEqual(['interaction', 'adapter']);
    expect(() => mountedHost!.addOperator(thresholdOperator)).toThrow(/disposed/i);
  });
});
