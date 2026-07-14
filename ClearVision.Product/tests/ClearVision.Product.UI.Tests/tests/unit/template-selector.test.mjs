import test from 'node:test';
import assert from 'node:assert/strict';

const previousSetTimeout = global.setTimeout;
global.setTimeout = (fn, ms, ...args) => {
  if (ms === 100 || ms === 3000) {
    return 0;
  }

  return previousSetTimeout(fn, ms, ...args);
};

function createElement(tag) {
  return {
    tagName: tag.toUpperCase(),
    id: '',
    className: '',
    style: {},
    parentNode: null,
    children: [],
    _innerHTML: '',
    get innerHTML() { return this._innerHTML; },
    set innerHTML(value) { this._innerHTML = value; },
    appendChild(child) {
      child.parentNode = this;
      this.children.push(child);
    },
    remove() {},
    addEventListener() {},
    querySelector() {
      return { addEventListener() {} };
    },
    querySelectorAll() {
      return [];
    }
  };
}

function installMinimalDom() {
  const elementsById = new Map();
  const body = createElement('body');
  body.appendChild = (child) => {
    child.parentNode = body;
    body.children.push(child);
    if (child.id) {
      elementsById.set(child.id, child);
    }
  };

  global.document = {
    body,
    createElement,
    getElementById(id) {
      return elementsById.get(id) || null;
    },
    querySelector() {
      return null;
    },
    addEventListener() {}
  };

  global.window = {
    crypto: {
      randomUUID() {
        return `00000000-0000-4000-8000-${String(++installMinimalDom.id).padStart(12, '0')}`;
      }
    }
  };
}
installMinimalDom.id = 0;

test('TemplateSelector invokes onApplied with serialized canvas flow', async () => {
  installMinimalDom();
  const { TemplateSelector } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/templateSelector.js'
  );

  const applied = [];
  const flowCanvas = {
    selectedNode: null,
    deserializedFlow: null,
    onNodeSelected(node) {
      this.lastSelectedNode = node;
    },
    deserialize(flow) {
      this.deserializedFlow = flow;
    },
    serialize() {
      return {
        operators: this.deserializedFlow?.operators || [],
        connections: this.deserializedFlow?.connections || []
      };
    }
  };

  const selector = new TemplateSelector(flowCanvas, {
    onApplied: (payload) => applied.push(payload)
  });
  selector.operatorMetadata.set('imageacquisition', {
    displayName: 'Image Acquisition',
    inputPorts: [],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
    parameters: []
  });
  selector.templates = [
    {
      id: 'template-1',
      name: 'Template One',
      flowJson: JSON.stringify({
        operators: [
          {
            tempId: 'op_1',
            operatorType: 'ImageAcquisition',
            displayName: 'Camera',
            parameters: {}
          }
        ],
        connections: []
      })
    }
  ];

  await selector._applyTemplate('template-1');

  assert.equal(flowCanvas.deserializedFlow.operators.length, 1);
  assert.equal(flowCanvas.lastSelectedNode, null);
  assert.equal(applied.length, 1);
  assert.equal(applied[0].template.id, 'template-1');
  assert.equal(applied[0].serializedFlow.operators.length, 1);
});

test('TemplateSelector shares in-flight template data loading', async (t) => {
  installMinimalDom();
  const { TemplateSelector } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/templateSelector.js'
  );
  const { default: httpClient } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js'
  );
  const originalGet = httpClient.get;
  const calls = [];
  let resolveTemplates;
  let resolveOperators;

  t.after(() => {
    httpClient.get = originalGet;
  });

  httpClient.get = (url) => {
    calls.push(url);
    if (url === '/templates') {
      return new Promise(resolve => {
        resolveTemplates = resolve;
      });
    }

    if (url === '/operators/library') {
      return new Promise(resolve => {
        resolveOperators = resolve;
      });
    }

    throw new Error(`Unexpected URL: ${url}`);
  };

  const selector = new TemplateSelector({}, {});
  const firstLoad = selector._ensureDataLoaded();
  const secondLoad = selector._ensureDataLoaded();

  assert.deepEqual(calls.sort(), ['/operators/library', '/templates']);

  resolveTemplates([{ id: 'template-1', flowJson: { operators: [{ tempId: 'op-1' }] } }]);
  resolveOperators([{ type: 'ImageAcquisition', parameters: [] }]);
  await Promise.all([firstLoad, secondLoad]);

  assert.equal(selector.templates.length, 1);
  assert.equal(selector.operatorMetadata.size, 1);
  assert.equal(selector.isLoading, false);
});

test('TemplateSelector destroy releases overlay, listeners, and cached template data', async () => {
  installMinimalDom();
  const { TemplateSelector } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/templateSelector.js'
  );
  const listeners = new Map();
  let overlayRemoved = false;
  const target = {
    addEventListener(type, handler) {
      if (!listeners.has(type)) {
        listeners.set(type, new Set());
      }

      listeners.get(type).add(handler);
    },
    removeEventListener(type, handler) {
      listeners.get(type)?.delete(handler);
    }
  };
  const selector = new TemplateSelector({ id: 'canvas' }, {});

  selector.templates = [{ id: 'template-large', flowJson: { operators: new Array(100).fill({}) } }];
  selector.operatorMetadata.set('largeoperator', { type: 'LargeOperator' });
  selector._applyingTemplateIds.add('template-large');
  selector.overlay = {
    remove() {
      overlayRemoved = true;
    }
  };
  selector.dialog = {};
  selector._addEventListener(target, 'click', () => {});

  assert.equal(listeners.get('click')?.size, 1);

  selector.destroy();

  assert.equal(listeners.get('click')?.size, 0);
  assert.equal(overlayRemoved, true);
  assert.equal(selector.templates.length, 0);
  assert.equal(selector.operatorMetadata.size, 0);
  assert.equal(selector._applyingTemplateIds.size, 0);
  assert.equal(selector.overlay, null);
  assert.equal(selector.dialog, null);
  assert.equal(selector.flowCanvas, null);
});

test('FlowEditorInteraction syncs applied template flow into project manager', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  const saved = [];
  const updates = [];
  const serializedFlow = { operators: [{ id: 'from-template' }], connections: [] };
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.history = [];
  interaction.historyIndex = -1;
  interaction.maxHistorySize = 50;
  interaction.canvas = {
    nodes: new Map([['from-template', { id: 'from-template', type: 'ImageAcquisition' }]]),
    connections: [],
    serialize() {
      return serializedFlow;
    }
  };
  interaction.projectManager = {
    updateFlow(flow) {
      updates.push(flow);
    }
  };
  const originalSaveState = interaction.saveState.bind(interaction);
  interaction.saveState = () => {
    saved.push(true);
    originalSaveState();
  };

  interaction.handleTemplateApplied({ serializedFlow });

  assert.equal(saved.length, 1);
  assert.equal(updates.length, 1);
  assert.equal(updates[0], serializedFlow);
});

test('FlowEditorInteraction creates and saves a project when applying a template without current project', async () => {
  installMinimalDom();
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  const created = [];
  const updates = [];
  const saves = [];
  const serializedFlow = { operators: [{ id: 'from-template' }], connections: [] };
  let currentProject = null;
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.history = [];
  interaction.historyIndex = -1;
  interaction.maxHistorySize = 50;
  interaction.canvas = {
    nodes: new Map([['from-template', { id: 'from-template', type: 'ImageAcquisition' }]]),
    connections: [],
    serialize() {
      return serializedFlow;
    }
  };
  interaction.projectManager = {
    getCurrentProject() {
      return currentProject;
    },
    async createProject(name, description) {
      currentProject = { id: 'project-from-template', name, description };
      created.push(currentProject);
      return currentProject;
    },
    updateFlow(flow) {
      if (!currentProject) {
        return;
      }

      currentProject.flow = flow;
      updates.push(flow);
    },
    async saveProject(project) {
      saves.push(project);
    }
  };

  await interaction.handleTemplateApplied({
    template: {
      id: 'template-1',
      name: '视觉检测模板',
      description: '用于快速生成检测流程'
    },
    serializedFlow
  });

  assert.equal(created.length, 1);
  assert.equal(created[0].name, '视觉检测模板 工程');
  assert.match(created[0].description, /从模板/);
  assert.equal(updates.length, 1);
  assert.equal(updates[0], serializedFlow);
  assert.equal(saves.length, 1);
  assert.equal(saves[0].flow, serializedFlow);
});

test('FlowEditorInteraction undo/redo restore syncs project flow and rebuilds connection index', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  const updates = [];
  let rebuilt = false;
  const structureReasons = [];
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.projectManager = {
    updateFlow(flow) {
      updates.push(flow);
    }
  };
  interaction.canvas = {
    nodes: new Map(),
    connections: [],
    _rebuildConnectionIndex() {
      rebuilt = true;
    },
    markFlowStructureChanged(reason) {
      structureReasons.push(reason);
    },
    render() {},
    serialize() {
      return {
        operators: [...this.nodes.keys()].map(id => ({ id })),
        connections: this.connections
      };
    }
  };
  interaction.history = [
    JSON.stringify({
      nodes: [['node-1', { id: 'node-1', type: 'ImageAcquisition' }]],
      connections: [{ id: 'conn-1', source: 'node-1', target: 'node-2' }]
    })
  ];
  interaction.historyIndex = 0;

  interaction.restoreState();

  assert.equal(rebuilt, true);
  assert.deepEqual(structureReasons, ['history-restore']);
  assert.equal(updates.length, 1);
  assert.deepEqual(updates[0].operators, [{ id: 'node-1' }]);
});

test('FlowEditorInteraction restore prunes stale selection state', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  const selectedPayloads = [];
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.projectManager = null;
  interaction.multiSelectedNodes = new Set(['node-1', 'removed-node']);
  interaction.canvas = {
    nodes: new Map([['removed-node', { id: 'removed-node' }]]),
    connections: [{ id: 'removed-conn' }],
    selectedNode: 'removed-node',
    selectedConnection: { id: 'removed-conn' },
    _rebuildConnectionIndex() {},
    markFlowStructureChanged() {},
    render() {},
    serialize() {
      return {
        operators: [...this.nodes.keys()].map(id => ({ id })),
        connections: this.connections
      };
    },
    onNodeSelected(node) {
      selectedPayloads.push(node);
    }
  };
  interaction.history = [
    JSON.stringify({
      nodes: [['node-1', { id: 'node-1', type: 'ImageAcquisition' }]],
      connections: [{ id: 'conn-1', source: 'node-1', target: 'node-2' }]
    })
  ];
  interaction.historyIndex = 0;

  interaction.restoreState();

  assert.equal(interaction.canvas.selectedNode, null);
  assert.equal(interaction.canvas.selectedConnection, null);
  assert.deepEqual([...interaction.multiSelectedNodes], ['node-1']);
  assert.deepEqual(selectedPayloads, [null]);
});

test('FlowEditorInteraction restore clears transient drag and connection state', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  let selectionBoxRemoved = false;
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.projectManager = null;
  interaction.multiSelectedNodes = new Set();
  interaction.isConnecting = true;
  interaction.connectionStart = { nodeId: 'old-source' };
  interaction.connectionEnd = { x: 10, y: 20 };
  interaction.connectionAnchor = { x: 5, y: 5 };
  interaction.connectionDidDrag = true;
  interaction.isDraggingNodes = true;
  interaction.dragStartPos = { x: 1, y: 2 };
  interaction.dragInitialPositions = new Map([['old-node', { x: 10, y: 10 }]]);
  interaction.hasNodeDragMoved = true;
  interaction.isPanning = true;
  interaction.panStart = { x: 1, y: 1 };
  interaction.panStartOffset = { x: 2, y: 2 };
  interaction.isSelecting = true;
  interaction.selectionStart = { x: 0, y: 0 };
  interaction.selectionBox = {
    remove() {
      selectionBoxRemoved = true;
    }
  };
  interaction.canvas = {
    nodes: new Map(),
    connections: [],
    selectedNode: null,
    selectedConnection: null,
    draggedNode: 'old-node',
    isConnecting: true,
    connectingFrom: { nodeId: 'old-source' },
    hoveredPort: { nodeId: 'old-target' },
    canvas: { style: { cursor: 'grabbing' } },
    _rebuildConnectionIndex() {},
    markFlowStructureChanged() {},
    render() {},
    serialize() {
      return {
        operators: [...this.nodes.keys()].map(id => ({ id })),
        connections: this.connections
      };
    }
  };
  interaction.history = [
    JSON.stringify({
      nodes: [['node-1', { id: 'node-1', type: 'ImageAcquisition' }]],
      connections: []
    })
  ];
  interaction.historyIndex = 0;

  interaction.restoreState();

  assert.equal(interaction.isConnecting, false);
  assert.equal(interaction.connectionStart, null);
  assert.equal(interaction.connectionEnd, null);
  assert.equal(interaction.connectionAnchor, null);
  assert.equal(interaction.connectionDidDrag, false);
  assert.equal(interaction.isDraggingNodes, false);
  assert.equal(interaction.dragStartPos, null);
  assert.equal(interaction.dragInitialPositions.size, 0);
  assert.equal(interaction.hasNodeDragMoved, false);
  assert.equal(interaction.isPanning, false);
  assert.equal(interaction.panStart, null);
  assert.equal(interaction.panStartOffset, null);
  assert.equal(interaction.isSelecting, false);
  assert.equal(interaction.selectionStart, null);
  assert.equal(interaction.selectionBox, null);
  assert.equal(selectionBoxRemoved, true);
  assert.equal(interaction.canvas.draggedNode, null);
  assert.equal(interaction.canvas.isConnecting, false);
  assert.equal(interaction.canvas.connectingFrom, null);
  assert.equal(interaction.canvas.hoveredPort, null);
  assert.equal(interaction.canvas.canvas.style.cursor, 'default');
});

test('FlowEditorInteraction deleteSelectedItems records selected connection deletion', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  installMinimalDom();
  const updates = [];
  const connection = { id: 'conn-1', source: 'node-1', target: 'node-2' };
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.history = [];
  interaction.historyIndex = -1;
  interaction.maxHistorySize = 50;
  interaction.multiSelectedNodes = new Set();
  interaction.projectManager = {
    updateFlow(flow) {
      updates.push(flow);
    }
  };
  interaction.canvas = {
    nodes: new Map([['node-1', { id: 'node-1' }], ['node-2', { id: 'node-2' }]]),
    connections: [connection],
    selectedNode: null,
    selectedConnection: connection,
    removeConnection(connectionId) {
      const before = this.connections.length;
      this.connections = this.connections.filter(item => item.id !== connectionId);
      if (this.selectedConnection?.id === connectionId) {
        this.selectedConnection = null;
      }
      return this.connections.length < before;
    },
    serialize() {
      return {
        operators: [...this.nodes.keys()].map(id => ({ id })),
        connections: this.connections
      };
    }
  };

  const handled = interaction.deleteSelectedItems();

  assert.equal(handled, true);
  assert.equal(interaction.canvas.connections.length, 0);
  assert.equal(interaction.canvas.selectedConnection, null);
  assert.equal(interaction.history.length, 1);
  assert.equal(interaction.historyIndex, 0);
  assert.equal(updates.length, 1);
  assert.deepEqual(updates[0].connections, []);
});

test('FlowEditorInteraction bridges canvas delete requests into history', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  installMinimalDom();
  const updates = [];
  const previousHandler = () => false;
  const node = { id: 'node-1' };
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.history = [];
  interaction.historyIndex = -1;
  interaction.maxHistorySize = 50;
  interaction.multiSelectedNodes = new Set();
  interaction.cleanup = [];
  interaction.projectManager = {
    updateFlow(flow) {
      updates.push(flow);
    }
  };
  interaction.canvas = {
    nodes: new Map([[node.id, node]]),
    connections: [],
    selectedNode: node.id,
    selectedConnection: null,
    onSelectionDeleteRequested: previousHandler,
    removeNode(nodeId) {
      if (!this.nodes.has(nodeId)) {
        return false;
      }
      this.nodes.delete(nodeId);
      this.selectedNode = null;
      return true;
    },
    serialize() {
      return {
        operators: [...this.nodes.keys()].map(id => ({ id })),
        connections: this.connections
      };
    }
  };

  interaction.installCanvasDeletionBridge();

  assert.notEqual(interaction.canvas.onSelectionDeleteRequested, previousHandler);
  assert.equal(interaction.canvas.onSelectionDeleteRequested({ reason: 'context-menu-node' }), true);
  assert.equal(interaction.canvas.nodes.has(node.id), false);
  assert.equal(interaction.history.length, 1);
  assert.equal(updates.length, 1);
  assert.deepEqual(updates[0].operators, []);

  interaction.cleanup.splice(0).forEach(dispose => dispose());
  assert.equal(interaction.canvas.onSelectionDeleteRequested, previousHandler);
});

test('FlowEditorInteraction bridges canvas duplicate requests into history', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  installMinimalDom();
  const updates = [];
  const selectedPayloads = [];
  const previousHandler = () => false;
  const sourceNode = { id: 'node-1', type: 'ImageAcquisition', x: 10, y: 20 };
  const duplicatedNode = { ...sourceNode, id: 'node-2', x: 40, y: 50 };
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.history = [];
  interaction.historyIndex = -1;
  interaction.maxHistorySize = 50;
  interaction.multiSelectedNodes = new Set();
  interaction.cleanup = [];
  interaction.projectManager = {
    updateFlow(flow) {
      updates.push(flow);
    }
  };
  interaction.canvas = {
    nodes: new Map([[sourceNode.id, sourceNode]]),
    connections: [],
    selectedNode: sourceNode.id,
    selectedConnection: null,
    onNodeDuplicateRequested: previousHandler,
    duplicateNode(nodeId) {
      if (!this.nodes.has(nodeId)) {
        return null;
      }
      this.nodes.set(duplicatedNode.id, duplicatedNode);
      this.selectedNode = duplicatedNode.id;
      return duplicatedNode;
    },
    invalidate() {},
    serialize() {
      return {
        operators: [...this.nodes.keys()].map(id => ({ id })),
        connections: this.connections
      };
    },
    onNodeSelected(node) {
      selectedPayloads.push(node);
    }
  };

  interaction.installCanvasDuplicationBridge();

  assert.notEqual(interaction.canvas.onNodeDuplicateRequested, previousHandler);
  assert.equal(interaction.canvas.onNodeDuplicateRequested({ reason: 'context-menu-node', nodeId: sourceNode.id }), true);
  assert.equal(interaction.canvas.nodes.has(duplicatedNode.id), true);
  assert.deepEqual([...interaction.multiSelectedNodes], [duplicatedNode.id]);
  assert.equal(interaction.canvas.selectedNode, duplicatedNode.id);
  assert.equal(interaction.history.length, 1);
  assert.equal(updates.length, 1);
  assert.deepEqual(updates[0].operators, [{ id: sourceNode.id }, { id: duplicatedNode.id }]);
  assert.deepEqual(selectedPayloads, [duplicatedNode]);

  interaction.cleanup.splice(0).forEach(dispose => dispose());
  assert.equal(interaction.canvas.onNodeDuplicateRequested, previousHandler);
});

test('FlowEditorInteraction bridges canvas disabled toggle requests into history', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  installMinimalDom();
  const updates = [];
  const selectedPayloads = [];
  const previousHandler = () => false;
  const node = { id: 'node-1', type: 'ImageAcquisition', disabled: false };
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.history = [];
  interaction.historyIndex = -1;
  interaction.maxHistorySize = 50;
  interaction.multiSelectedNodes = new Set();
  interaction.cleanup = [];
  interaction.projectManager = {
    updateFlow(flow) {
      updates.push(flow);
    }
  };
  interaction.canvas = {
    nodes: new Map([[node.id, node]]),
    connections: [],
    selectedNode: node.id,
    selectedConnection: null,
    onNodeDisabledToggleRequested: previousHandler,
    toggleNodeDisabled(nodeId) {
      const target = this.nodes.get(nodeId);
      if (!target) {
        return false;
      }
      target.disabled = !target.disabled;
      return true;
    },
    serialize() {
      return {
        operators: [...this.nodes.values()].map(value => ({
          id: value.id,
          isEnabled: value.disabled !== true
        })),
        connections: this.connections
      };
    },
    onNodeSelected(selectedNode) {
      selectedPayloads.push(selectedNode);
    }
  };

  interaction.installCanvasDisabledToggleBridge();

  assert.notEqual(interaction.canvas.onNodeDisabledToggleRequested, previousHandler);
  assert.equal(interaction.canvas.onNodeDisabledToggleRequested({ reason: 'context-menu-node', nodeId: node.id }), true);
  assert.equal(node.disabled, true);
  assert.equal(interaction.history.length, 1);
  assert.equal(updates.length, 1);
  assert.deepEqual(updates[0].operators, [{ id: node.id, isEnabled: false }]);
  assert.deepEqual(selectedPayloads, [node]);

  interaction.cleanup.splice(0).forEach(dispose => dispose());
  assert.equal(interaction.canvas.onNodeDisabledToggleRequested, previousHandler);
});

test('FlowEditorInteraction deleteSelectedItems ignores refused node deletion', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  installMinimalDom();
  const selectedPayloads = [];
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.history = [];
  interaction.historyIndex = -1;
  interaction.maxHistorySize = 50;
  interaction.multiSelectedNodes = new Set();
  interaction.projectManager = {
    updateFlow() {
      throw new Error('Project flow should not update when nothing is deleted');
    }
  };
  interaction.canvas = {
    nodes: new Map([['system-node', { id: 'system-node', _systemNode: true }]]),
    connections: [],
    selectedNode: 'system-node',
    selectedConnection: null,
    removeNode(nodeId) {
      const node = this.nodes.get(nodeId);
      if (!node || node._systemNode) {
        return false;
      }
      this.nodes.delete(nodeId);
      return true;
    },
    serialize() {
      return {
        operators: [...this.nodes.keys()].map(id => ({ id })),
        connections: this.connections
      };
    },
    onNodeSelected(node) {
      selectedPayloads.push(node);
    }
  };

  const handled = interaction.deleteSelectedItems();

  assert.equal(handled, false);
  assert.equal(interaction.canvas.nodes.has('system-node'), true);
  assert.equal(interaction.canvas.selectedNode, 'system-node');
  assert.equal(interaction.history.length, 0);
  assert.deepEqual(selectedPayloads, []);
});

test('FlowEditorInteraction enhanced drag advances the canonical flow revision once', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
  );

  const reasons = [];
  let saved = 0;
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.isDraggingNodes = true;
  interaction.dragStartPos = { x: 1, y: 2 };
  interaction.dragInitialPositions = new Map([['node-a', { x: 10, y: 20 }]]);
  interaction.hasNodeDragMoved = true;
  interaction.canvas = {
    draggedNode: 'node-a',
    canvas: { style: {} },
    markFlowStructureChanged(reason) {
      reasons.push(reason);
    },
    notifyViewStateChanged() {},
    invalidate() {},
    _markNodesBoundsDirty() {}
  };
  interaction.syncCursorToPointer = () => {};
  interaction.saveState = () => {
    saved += 1;
  };

  interaction.endNodeDrag();

  assert.deepEqual(reasons, ['moveNode']);
  assert.equal(saved, 1);
  assert.equal(interaction.canvas.draggedNode, null);
  assert.equal(interaction.isDraggingNodes, false);
});
