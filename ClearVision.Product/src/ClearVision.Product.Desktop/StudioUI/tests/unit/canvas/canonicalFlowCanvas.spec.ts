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
      this.history = [this.snapshot()];
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
    undo() {
      if (this.historyIndex <= 0) return false;
      this.historyIndex -= 1;
      this.restore(this.history[this.historyIndex]!);
      const callback = this.options.onDraftCommitted as (() => void) | undefined;
      callback?.();
      return true;
    }
    redo() {
      if (this.historyIndex >= this.history.length - 1) return false;
      this.historyIndex += 1;
      this.restore(this.history[this.historyIndex]!);
      const callback = this.options.onDraftCommitted as (() => void) | undefined;
      callback?.();
      return true;
    }
    resetTransientInteractionAfterRestore() {}
    resetHistory() { this.history = [this.snapshot()]; this.historyIndex = 0; }
    getHistoryState() { return { canUndo: this.historyIndex > 0, canRedo: this.historyIndex < this.history.length - 1 }; }
    saveState() {
      this.history = this.history.slice(0, this.historyIndex + 1);
      this.history.push(this.snapshot());
      this.historyIndex += 1;
      const callback = this.options.onDraftCommitted as (() => void) | undefined;
      callback?.();
    }
    private snapshot() {
      return JSON.stringify({ nodes: [...this.canvas.nodes.entries()], connections: this.canvas.connections });
    }
    private restore(snapshot: string) {
      const value = JSON.parse(snapshot) as {
        nodes: Array<[string, Record<string, unknown>]>;
        connections: Array<Record<string, unknown>>;
      };
      this.canvas.nodes = new Map(value.nodes);
      this.canvas.connections = value.connections;
      emitStructure();
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
  addConnection(sourceId: string, sourcePort: number, targetId: string, targetPort: number): object | null;
  removeNode(nodeId: string): boolean;
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
      const sourceNode = raw.nodes.get(sourceId);
      const targetNode = raw.nodes.get(targetId);
      const connection = {
        id: `connection-${raw.connections.length + 1}`,
        source: sourceId,
        sourceId,
        sourcePort,
        sourcePortId: (sourceNode?.outputs as Array<Record<string, unknown>> | undefined)?.[sourcePort]?.id,
        target: targetId,
        targetId,
        targetPort,
        targetPortId: (targetNode?.inputs as Array<Record<string, unknown>> | undefined)?.[targetPort]?.id
      };
      raw.connections.push(connection);
      emitStructure();
      return connection;
    },
    removeNode: nodeId => {
      const removed = raw.nodes.delete(nodeId);
      if (removed) {
        raw.connections = raw.connections.filter(connection =>
          connection.source !== nodeId && connection.target !== nodeId);
        emitStructure();
      }
      return removed;
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
      operators: [...raw.nodes.values()].map(node => ({
        id: node.id,
        name: node.title ?? node.name,
        type: node.type,
        x: node.x,
        y: node.y,
        inputPorts: Array.isArray(node.inputs) ? node.inputs.map(port => ({
          id: port.id, name: port.name, direction: 0, dataType: port.dataType, isRequired: port.isRequired
        })) : [],
        outputPorts: Array.isArray(node.outputs) ? node.outputs.map(port => ({
          id: port.id, name: port.name, direction: 1, dataType: port.dataType, isRequired: port.isRequired
        })) : [],
        parameters: Array.isArray(node.parameters) ? node.parameters.map(parameter => ({
          id: parameter.id,
          name: parameter.name,
          displayName: parameter.displayName,
          description: parameter.description,
          dataType: parameter.dataType,
          ...(Object.prototype.hasOwnProperty.call(parameter, 'value') ? { value: parameter.value } : { value: parameter.defaultValue }),
          defaultValue: parameter.defaultValue,
          minValue: parameter.minValue,
          maxValue: parameter.maxValue,
          isRequired: parameter.isRequired,
          options: parameter.options
        })) : [],
        isEnabled: node.disabled !== true
      })),
      connections: raw.connections.map(connection => ({
        id: connection.id,
        sourceOperatorId: connection.sourceOperatorId ?? connection.sourceId,
        sourcePortId: connection.sourcePortId,
        targetOperatorId: connection.targetOperatorId ?? connection.targetId,
        targetPortId: connection.targetPortId
      })),
      decisionConfiguration: null
    }),
    replaceFlow: (flow: Readonly<Record<string, unknown>>) => {
      const operators = Array.isArray(flow.operators) ? flow.operators : [];
      raw.nodes = new Map(operators.map((operator, index) => {
        const item = operator as Record<string, unknown>;
        const id = String(item.id ?? `baseline-${index}`);
        return [id, {
          ...item,
          id,
          title: item.name,
          inputs: item.inputPorts ?? [],
          outputs: item.outputPorts ?? [],
          parameters: item.parameters ?? [],
          disabled: item.isEnabled === false
        }];
      }));
      raw.connections = Array.isArray(flow.connections)
        ? flow.connections as Array<Record<string, unknown>>
        : [];
    },
    resize: emitView,
    selectNode: (nodeId: string | null) => {
      if (nodeId !== null && !raw.nodes.has(nodeId)) return false;
      raw.selectedNode = nodeId;
      raw.selectedConnection = null;
      emitSelection();
      return true;
    },
    patchNodeParameters: (
      nodeId: string,
      patch: Readonly<Record<string, unknown>>
    ) => {
      const node = raw.nodes.get(nodeId);
      if (!node) return { updated: false, reason: 'node_not_found', missingParameters: [] };
      const parameters = Array.isArray(node.parameters) ? node.parameters : [];
      let changed = false;
      const missingParameters: string[] = [];
      for (const [name, value] of Object.entries(patch)) {
        const parameter = parameters.find(item => String(item.name).toLowerCase() === name.toLowerCase());
        if (!parameter) {
          missingParameters.push(name);
          continue;
        }
        if (!Object.is(parameter.value, value)) {
          parameter.value = value;
          changed = true;
        }
      }
      if (missingParameters.length) return { updated: false, reason: 'parameter_not_found', missingParameters };
      if (changed) emitStructure();
      return { updated: changed, reason: changed ? 'updated' : 'no_change', missingParameters: [] };
    },
    patchNodeProperties: (
      nodeId: string,
      patch: Readonly<{ name?: string; isEnabled?: boolean }>
    ) => {
      const node = raw.nodes.get(nodeId);
      if (!node) return { updated: false, reason: 'node_not_found' };
      let changed = false;
      if (Object.prototype.hasOwnProperty.call(patch, 'name') && node.title !== patch.name) {
        node.title = patch.name;
        changed = true;
      }
      if (Object.prototype.hasOwnProperty.call(patch, 'isEnabled') && node.disabled === patch.isEnabled) {
        node.disabled = patch.isEnabled !== true;
        changed = true;
      }
      if (changed) emitStructure();
      return { updated: changed, reason: changed ? 'updated' : 'no_change' };
    },
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

  it('patches parameter and node properties through one history entry and supports undo/redo', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-1',
      name: '流程',
      operators: [{
        id: 'node-1', name: '阈值', type: 20, x: 0, y: 0,
        inputPorts: [], outputPorts: [],
        parameters: [{
          id: 'parameter-1', name: 'Threshold', displayName: '阈值', description: null,
          dataType: 'int', value: 0, defaultValue: 0, minValue: 0, maxValue: 255,
          isRequired: true, options: null
        }],
        isEnabled: true
      }],
      connections: []
    });

    expect(mountedHost.patchNodeParameter({
      nodeId: 'node-1', parameterName: 'Threshold', value: 12
    })).toMatchObject({ ok: true, flowRevision: 1 });
    expect(mountedHost.getProjection().draft.operators[0]?.parameters).toEqual(expect.arrayContaining([
      expect.objectContaining({ name: 'Threshold', value: 12 })
    ]));
    expect(mountedHost.patchNodeProperties({ nodeId: 'node-1', name: '新阈值', isEnabled: false }))
      .toMatchObject({ ok: true, flowRevision: 2 });
    expect(mountedHost.patchNodeParameter({
      nodeId: 'node-1', parameterName: 'Threshold', value: 12
    })).toMatchObject({ ok: false, code: 'no-change', flowRevision: 2 });

    expect(mountedHost.undo()).toMatchObject({ ok: true, flowRevision: 3 });
    expect(mountedHost.getProjection().draft.operators[0]).toMatchObject({ name: '阈值', isEnabled: true });
    expect(mountedHost.undo()).toMatchObject({ ok: true, flowRevision: 4 });
    expect(mountedHost.getProjection().draft.operators[0]?.parameters).toEqual(expect.arrayContaining([
      expect.objectContaining({ value: 0 })
    ]));
    expect(mountedHost.redo()).toMatchObject({ ok: true, flowRevision: 5 });
    expect(mountedHost.getProjection().draft.operators[0]?.parameters).toEqual(expect.arrayContaining([
      expect.objectContaining({ value: 12 })
    ]));
  });

  it('commits a multi-parameter ROI patch as one revision/history entry', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-roi', name: 'ROI flow', operators: [{
        id: 'roi-1', name: 'ROI', type: 'RoiManager', x: 0, y: 0,
        inputPorts: [], outputPorts: [],
        parameters: [
          { id: 'x', name: 'X', displayName: 'X', description: null, dataType: 'int', value: 1, defaultValue: 0, minValue: 0, maxValue: 1000, isRequired: true, options: null },
          { id: 'y', name: 'Y', displayName: 'Y', description: null, dataType: 'int', value: 2, defaultValue: 0, minValue: 0, maxValue: 1000, isRequired: true, options: null },
          { id: 'w', name: 'Width', displayName: 'Width', description: null, dataType: 'int', value: 10, defaultValue: 1, minValue: 1, maxValue: 1000, isRequired: true, options: null },
          { id: 'h', name: 'Height', displayName: 'Height', description: null, dataType: 'int', value: 20, defaultValue: 1, minValue: 1, maxValue: 1000, isRequired: true, options: null }
        ],
        isEnabled: true
      }], connections: []
    });

    expect(mountedHost.patchNodeParameters({
      nodeId: 'roi-1',
      values: { X: 11, Y: 12, Width: 30, Height: 40 }
    })).toMatchObject({ ok: true, flowRevision: 1 });
    expect(mountedHost.getProjection().draft.operators[0]?.parameters).toEqual(expect.arrayContaining([
      expect.objectContaining({ name: 'X', value: 11 }),
      expect.objectContaining({ name: 'Y', value: 12 }),
      expect.objectContaining({ name: 'Width', value: 30 }),
      expect.objectContaining({ name: 'Height', value: 40 })
    ]));
    expect(mountedHost.undo()).toMatchObject({ ok: true, flowRevision: 2 });
    expect(mountedHost.getProjection().draft.operators[0]?.parameters).toEqual(expect.arrayContaining([
      expect.objectContaining({ name: 'X', value: 1 }),
      expect.objectContaining({ name: 'Height', value: 20 })
    ]));
  });

  it('creates Caliper RectangleRegion plus connection atomically and rolls the whole change back on undo', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-caliper', name: 'Caliper flow', operators: [{
        id: 'caliper-1', name: 'Caliper', type: 'CaliperTool', x: 400, y: 120,
        inputPorts: [{ id: 'search-region', name: 'SearchRegion', dataType: 'Rectangle', isRequired: true }],
        outputPorts: [], parameters: [], isEnabled: true
      }], connections: []
    });
    const rectangleRegion: CanonicalOperatorDefinition = Object.freeze({
      operatorType: 'RectangleRegion',
      displayName: 'Rectangle Region',
      category: 'SegmentationAndRegion',
      iconName: null,
      inputPorts: Object.freeze([]),
      outputPorts: Object.freeze([{ name: 'Rectangle', dataType: 'Rectangle', isRequired: false }]),
      parameters: Object.freeze([
        { name: 'X', displayName: 'X', description: null, dataType: 'int', defaultValue: 0, minValue: 0, maxValue: 1000, isRequired: true, options: null },
        { name: 'Y', displayName: 'Y', description: null, dataType: 'int', defaultValue: 0, minValue: 0, maxValue: 1000, isRequired: true, options: null },
        { name: 'Width', displayName: 'Width', description: null, dataType: 'int', defaultValue: 1, minValue: 1, maxValue: 1000, isRequired: true, options: null },
        { name: 'Height', displayName: 'Height', description: null, dataType: 'int', defaultValue: 1, minValue: 1, maxValue: 1000, isRequired: true, options: null }
      ])
    });

    expect(mountedHost.upsertCaliperSearchRegion({
      caliperNodeId: 'caliper-1',
      values: { X: 10, Y: 20, Width: 100, Height: 40 },
      rectangleRegion
    })).toMatchObject({ ok: true, code: 'caliper-search-region-created', flowRevision: 1 });
    expect(mountedHost.getProjection().runtime).toMatchObject({ nodeCount: 2, connectionCount: 1 });
    expect(mountedHost.undo()).toMatchObject({ ok: true, flowRevision: 2 });
    expect(mountedHost.getProjection().runtime).toMatchObject({ nodeCount: 1, connectionCount: 0 });
  });

  it('rolls back a newly created Caliper RectangleRegion when the connection cannot be created', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-caliper', name: 'Caliper flow', operators: [{
        id: 'caliper-1', name: 'Caliper', type: 'CaliperTool', x: 400, y: 120,
        inputPorts: [{ id: 'search-region', name: 'SearchRegion', dataType: 'Rectangle', isRequired: true }],
        outputPorts: [], parameters: [], isEnabled: true
      }], connections: []
    });
    raw.addConnection = () => null;
    const rectangleRegion: CanonicalOperatorDefinition = Object.freeze({
      operatorType: 'RectangleRegion', displayName: 'Rectangle Region', category: 'SegmentationAndRegion', iconName: null,
      inputPorts: Object.freeze([]),
      outputPorts: Object.freeze([{ name: 'Rectangle', dataType: 'Rectangle', isRequired: false }]),
      parameters: Object.freeze([
        { name: 'X', displayName: 'X', description: null, dataType: 'int', defaultValue: 0, minValue: 0, maxValue: 1000, isRequired: true, options: null },
        { name: 'Y', displayName: 'Y', description: null, dataType: 'int', defaultValue: 0, minValue: 0, maxValue: 1000, isRequired: true, options: null },
        { name: 'Width', displayName: 'Width', description: null, dataType: 'int', defaultValue: 1, minValue: 1, maxValue: 1000, isRequired: true, options: null },
        { name: 'Height', displayName: 'Height', description: null, dataType: 'int', defaultValue: 1, minValue: 1, maxValue: 1000, isRequired: true, options: null }
      ])
    });

    expect(mountedHost.upsertCaliperSearchRegion({
      caliperNodeId: 'caliper-1', values: { X: 1, Y: 2, Width: 3, Height: 4 }, rectangleRegion
    })).toMatchObject({ ok: false, code: 'search-region-connection-failed', flowRevision: 0 });
    expect(mountedHost.getProjection().runtime).toMatchObject({ nodeCount: 1, connectionCount: 0 });
  });

  it('preserves flow/operator/port/parameter/connection opaque fields after a normal edit', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-opaque',
      name: 'opaque flow',
      futureFlowField: { version: 2 },
      operators: [{
        id: 'node-1', name: '来源', type: 20, x: 0, y: 0, futureOperatorField: 'keep-operator',
        inputPorts: [],
        outputPorts: [{
          id: 'output-1', name: 'Out', direction: 1, dataType: 1, isRequired: false,
          futurePortField: 'keep-port'
        }],
        parameters: [{
          id: 'parameter-1', name: 'Value', displayName: '值', description: null,
          dataType: 'int', value: 0, defaultValue: 0, minValue: 0, maxValue: 10,
          isRequired: false, options: null, futureParameterField: 'keep-parameter'
        }],
        isEnabled: true
      }, {
        id: 'node-2', name: '目标', type: 20, x: 100, y: 0,
        inputPorts: [{ id: 'input-1', name: 'In', direction: 0, dataType: 1, isRequired: true }],
        outputPorts: [], parameters: [], isEnabled: true
      }],
      connections: [{
        id: 'connection-1', sourceOperatorId: 'node-1', sourcePortId: 'output-1',
        targetOperatorId: 'node-2', targetPortId: 'input-1', futureConnectionField: 'keep-connection'
      }],
      decisionConfiguration: null
    });

    mountedHost.patchNodeParameter({ nodeId: 'node-1', parameterName: 'Value', value: 5 });
    const draft = mountedHost.getProjection().draft;
    expect(draft.opaquePassthrough).toMatchObject({ futureFlowField: { version: 2 } });
    expect(draft.operators[0]).toMatchObject({ futureOperatorField: 'keep-operator' });
    expect(draft.operators[0]?.outputPorts).toEqual(expect.arrayContaining([
      expect.objectContaining({ futurePortField: 'keep-port' })
    ]));
    expect(draft.operators[0]?.parameters).toEqual(expect.arrayContaining([
      expect.objectContaining({ futureParameterField: 'keep-parameter', value: 5 })
    ]));
    expect(draft.connections[0]).toMatchObject({ futureConnectionField: 'keep-connection' });
  });

  it('rejects parameter/property edits in readonly and running modes without revision changes', () => {
    mountedHost = createCanonicalFlowCanvasHost('canonical-unit-canvas', {
      id: 'flow-1', name: '流程', operators: [{
        id: 'node-1', name: '节点', type: 20, x: 0, y: 0, inputPorts: [], outputPorts: [],
        parameters: [{ id: 'p-1', name: 'Flag', displayName: 'Flag', description: null, dataType: 'bool', value: false, defaultValue: false, minValue: null, maxValue: null, isRequired: false, options: null }],
        isEnabled: true
      }], connections: []
    });
    mountedHost.setMutationGate('readonly');
    expect(mountedHost.patchNodeParameter({ nodeId: 'node-1', parameterName: 'Flag', value: true }))
      .toMatchObject({ ok: false, code: 'readonly', flowRevision: 0 });
    mountedHost.setMutationGate('running');
    expect(mountedHost.patchNodeProperties({ nodeId: 'node-1', name: 'blocked' }))
      .toMatchObject({ ok: false, code: 'running', flowRevision: 0 });
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
