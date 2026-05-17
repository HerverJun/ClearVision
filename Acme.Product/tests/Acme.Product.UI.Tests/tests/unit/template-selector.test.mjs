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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/flow-editor/templateSelector.js'
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

test('FlowEditorInteraction syncs applied template flow into project manager', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
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

test('FlowEditorInteraction undo/redo restore syncs project flow and rebuilds connection index', async () => {
  const { FlowEditorInteraction } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/flow-editor/flowEditorInteraction.js'
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
