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
  const interaction = Object.create(FlowEditorInteraction.prototype);
  interaction.canvas = {
    serialize() {
      return { operators: [{ id: 'from-canvas' }], connections: [] };
    }
  };
  interaction.projectManager = {
    updateFlow(flow) {
      updates.push(flow);
    }
  };
  interaction.saveState = () => saved.push(true);

  const serializedFlow = { operators: [{ id: 'from-template' }], connections: [] };
  interaction.handleTemplateApplied({ serializedFlow });

  assert.equal(saved.length, 1);
  assert.equal(updates.length, 1);
  assert.equal(updates[0], serializedFlow);
});
