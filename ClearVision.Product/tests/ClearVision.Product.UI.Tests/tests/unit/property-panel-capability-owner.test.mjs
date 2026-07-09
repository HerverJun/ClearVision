import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { createPropertyPanelCapabilityAdapter } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertySidebarController.mjs';

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

function collectFiles(rootUrl, extension) {
  const files = [];
  for (const entry of readdirSync(rootUrl, { withFileTypes: true })) {
    const childUrl = new URL(`${entry.name}${entry.isDirectory() ? '/' : ''}`, rootUrl);
    if (entry.isDirectory()) {
      files.push(...collectFiles(childUrl, extension));
    } else if (entry.name.endsWith(extension)) {
      files.push(childUrl);
    }
  }
  return files;
}

function collectOperatorParams() {
  const operatorRoot = new URL('../../../../src/ClearVision.Product.Infrastructure/Operators/', import.meta.url);
  const records = [];
  for (const fileUrl of collectFiles(operatorRoot, '.cs')) {
    const source = readFileSync(fileUrl, 'utf8');
    const matches = source.matchAll(/\[OperatorParam\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)"/g);
    for (const match of matches) {
      records.push({
        name: match[1],
        label: match[2],
        type: match[3]
      });
    }
  }
  return records;
}

function installFakeDocument() {
  const hadDocument = Object.prototype.hasOwnProperty.call(globalThis, 'document');
  const previousDocument = globalThis.document;
  globalThis.document = {
    createElement() {
      return {
        _text: '',
        innerHTML: '',
        set textContent(value) {
          this._text = value == null ? '' : String(value);
          this.innerHTML = this._text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
        },
        get textContent() {
          return this._text;
        }
      };
    }
  };

  return () => {
    if (hadDocument) {
      globalThis.document = previousDocument;
    } else {
      delete globalThis.document;
    }
  };
}

function installValidationDocument(parameterNames) {
  const hadDocument = Object.prototype.hasOwnProperty.call(globalThis, 'document');
  const previousDocument = globalThis.document;
  const messages = [];
  const groups = new Map();
  const inputs = new Map();
  const createClassList = () => {
    const values = new Set();
    return {
      add(name) {
        values.add(name);
      },
      remove(name) {
        values.delete(name);
      },
      contains(name) {
        return values.has(name);
      },
      toggle(name, enabled) {
        if (enabled) {
          values.add(name);
        } else {
          values.delete(name);
        }
      }
    };
  };

  for (const name of parameterNames) {
    const group = {
      children: [],
      classList: createClassList(),
      appendChild(child) {
        child.parentNode = group;
        group.children.push(child);
        messages.push(child);
      },
      querySelector() {
        return null;
      },
      querySelectorAll() {
        return [];
      }
    };
    const input = {
      name,
      attributes: {},
      setAttribute(attributeName, value) {
        this.attributes[attributeName] = String(value);
      },
      removeAttribute(attributeName) {
        delete this.attributes[attributeName];
      },
      getAttribute(attributeName) {
        return this.attributes[attributeName] ?? null;
      },
      closest(selector) {
        return selector === '.form-group' ? group : null;
      }
    };
    groups.set(name, group);
    inputs.set(name, input);
  }

  globalThis.document = {
    createElement(tagName) {
      return {
        tagName,
        className: '',
        dataset: {},
        parentNode: null,
        textContent: '',
        remove() {
          const messageIndex = messages.indexOf(this);
          if (messageIndex >= 0) {
            messages.splice(messageIndex, 1);
          }
          if (this.parentNode) {
            this.parentNode.children = this.parentNode.children.filter(child => child !== this);
          }
        }
      };
    }
  };

  return {
    container: {
      querySelectorAll(selector) {
        if (selector === '[data-property-parameter="true"]') {
          return Array.from(inputs.values());
        }
        if (selector === '.form-group.invalid') {
          return Array.from(groups.values()).filter(group => group.classList.contains('invalid'));
        }
        if (selector === '[data-property-parameter="true"][aria-invalid="true"]') {
          return Array.from(inputs.values()).filter(input => input.getAttribute('aria-invalid') === 'true');
        }
        if (selector === '[data-validation-error="true"]') {
          return [...messages];
        }
        return [];
      }
    },
    groups,
    inputs,
    messages,
    cleanup() {
      if (hadDocument) {
        globalThis.document = previousDocument;
      } else {
        delete globalThis.document;
      }
    }
  };
}

function countSliderMarkup(html) {
  return (String(html).match(/class="param-slider"/g) || []).length;
}

async function loadPropertyPanelCapabilityOwner() {
  const hadWindow = Object.prototype.hasOwnProperty.call(globalThis, 'window');
  const previousWindow = globalThis.window;
  if (!hadWindow) {
    globalThis.window = {
      chrome: null,
      addEventListener() {},
      removeEventListener() {}
    };
  }

  try {
    const module = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanelCapabilityOwner.mjs');
    return module.PropertyPanelCapabilityOwner;
  } finally {
    if (!hadWindow) {
      delete globalThis.window;
    } else {
      globalThis.window = previousWindow;
    }
  }
}

test('PropertyPanelCapabilityAdapter projects selected node metadata and writes through FlowCanvasAdapter once', () => {
  const writeCalls = [];
  const fakeFlowCanvasAdapter = {
    selectedNode: 'node-1',
    nodes: new Map([
      ['node-1', {
        id: 'node-1',
        type: 'Thresholding',
        title: 'Threshold A',
        parameters: [
          { name: 'Threshold', value: 88, dataType: 'int' },
          { name: 'NodeOnly', value: 'kept', dataType: 'string' }
        ]
      }]
    ]),
    subscribeSelection(listener) {
      listener({ selectedNodeId: 'node-1', reason: 'initial' });
      return () => {};
    },
    subscribeStructureState() {
      return () => {};
    },
    patchNodeParameters(nodeId, values, options) {
      writeCalls.push({ nodeId, values, options });
      return { updated: true, reason: 'updated', missingParameters: [] };
    }
  };
  const propertyAdapter = createPropertyPanelCapabilityAdapter({
    flowCanvasAdapter: fakeFlowCanvasAdapter,
    getOperatorMetadata: () => ({
      displayName: '阈值',
      parameters: [
        { name: 'Threshold', displayName: '阈值', value: 10, dataType: 'int' },
        { name: 'Mode', displayName: '模式', value: 'Binary', dataType: 'string' }
      ]
    })
  });

  let selectedOperator = null;
  propertyAdapter.subscribeSelectedNode(operator => {
    selectedOperator = operator;
  });

  assert.equal(selectedOperator.id, 'node-1');
  assert.equal(selectedOperator.displayName, '阈值');
  assert.deepEqual(
    selectedOperator.parameters.map(parameter => [parameter.name, parameter.value]),
    [
      ['Threshold', 88],
      ['Mode', 'Binary'],
      ['NodeOnly', 'kept']
    ]
  );

  const result = propertyAdapter.writeParameters('node-1', { Threshold: 90 });

  assert.equal(result.updated, true);
  assert.equal(writeCalls.length, 1);
  assert.equal(writeCalls[0].nodeId, 'node-1');
  assert.deepEqual(writeCalls[0].values, { Threshold: 90 });
  assert.equal(writeCalls[0].options.allowCreateParameters, true);
  assert.equal(writeCalls[0].options.parameterDefinitions.length, 3);
});

test('PropertyPanelCapabilityAdapter creates and reuses RectangleRegion for Caliper SearchRegion', () => {
  const connections = [];
  const addNodeCalls = [];
  const patchCalls = [];
  const structureReasons = [];
  const nodes = new Map([
    ['caliper-1', {
      id: 'caliper-1',
      type: 'CaliperTool',
      title: 'Caliper A',
      x: 400,
      y: 120,
      inputs: [
        { name: 'Image', dataType: 'Image' },
        { name: 'SearchRegion', dataType: 'Rectangle' }
      ],
      outputs: [],
      parameters: []
    }]
  ]);
  const canvas = {
    connections,
    addConnection(source, sourcePort, target, targetPort) {
      const connection = {
        id: `conn-${connections.length + 1}`,
        source,
        sourcePort,
        target,
        targetPort
      };
      connections.push(connection);
      return connection;
    }
  };
  const fakeFlowCanvasAdapter = {
    selectedNode: 'caliper-1',
    nodes,
    raw: canvas,
    addNode(type, x, y, config) {
      const node = {
        id: `region-${addNodeCalls.length + 1}`,
        type,
        title: config.title,
        x,
        y,
        inputs: config.inputs,
        outputs: config.outputs,
        parameters: config.parameters
      };
      nodes.set(node.id, node);
      addNodeCalls.push({ type, x, y, config, node });
      return node;
    },
    patchNodeParameters(nodeId, values, options) {
      const node = nodes.get(nodeId);
      patchCalls.push({ nodeId, values, options });
      for (const [name, value] of Object.entries(values)) {
        const parameter = node.parameters.find(item => item.name === name);
        if (parameter) {
          parameter.value = value;
        } else {
          node.parameters.push({ name, value, dataType: 'int' });
        }
      }
      return { updated: true, reason: 'updated', missingParameters: [] };
    },
    markFlowStructureChanged(reason) {
      structureReasons.push(reason);
    }
  };
  const propertyAdapter = createPropertyPanelCapabilityAdapter({
    flowCanvasAdapter: fakeFlowCanvasAdapter,
    getOperatorMetadata(type) {
      if (type === 'RectangleRegion') {
        return {
          displayName: 'Rectangle Region',
          parameters: [
            { name: 'X', dataType: 'int', value: 0 },
            { name: 'Y', dataType: 'int', value: 0 },
            { name: 'Width', dataType: 'int', value: 1 },
            { name: 'Height', dataType: 'int', value: 1 }
          ],
          inputPorts: [],
          outputPorts: [
            { name: 'Rectangle', dataType: 'Rectangle' }
          ]
        };
      }

      return null;
    }
  });

  const created = propertyAdapter.upsertCaliperSearchRegion('caliper-1', {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  });

  assert.equal(created.updated, true);
  assert.equal(created.reason, 'created');
  assert.equal(addNodeCalls.length, 1);
  assert.equal(addNodeCalls[0].type, 'RectangleRegion');
  assert.equal(addNodeCalls[0].x, 140);
  assert.equal(addNodeCalls[0].y, 120);
  assert.deepEqual(connections[0], {
    id: 'conn-1',
    source: 'region-1',
    sourcePort: 0,
    target: 'caliper-1',
    targetPort: 1
  });
  assert.deepEqual(
    created.operator.parameters.map(parameter => [parameter.name, parameter.value]),
    [
      ['X', 10],
      ['Y', 12],
      ['Width', 30],
      ['Height', 16]
    ]
  );
  assert.deepEqual(structureReasons, ['caliper-search-region-upsert']);

  const reused = propertyAdapter.upsertCaliperSearchRegion('caliper-1', {
    X: 11,
    Y: 13,
    Width: 31,
    Height: 17
  });

  assert.equal(reused.updated, true);
  assert.equal(addNodeCalls.length, 1);
  assert.equal(patchCalls.length, 1);
  assert.equal(patchCalls[0].nodeId, 'region-1');
  assert.deepEqual(patchCalls[0].values, {
    X: 11,
    Y: 13,
    Width: 31,
    Height: 17
  });
  assert.equal(propertyAdapter.getCaliperSearchRegionBinding('caliper-1').sourceNode.id, 'region-1');
});

test('PropertyPanelCapabilityAdapter rolls back created RectangleRegion when Caliper connection fails', () => {
  const nodes = new Map([
    ['caliper-1', {
      id: 'caliper-1',
      type: 'CaliperTool',
      title: 'Caliper A',
      x: 400,
      y: 120,
      inputs: [
        { name: 'Image', dataType: 'Image' },
        { name: 'SearchRegion', dataType: 'Rectangle' }
      ],
      outputs: [],
      parameters: []
    }]
  ]);
  const removedNodes = [];
  const structureReasons = [];
  const canvas = {
    connections: [],
    addConnection() {
      return null;
    },
    removeNode(nodeId) {
      removedNodes.push(nodeId);
      return nodes.delete(nodeId);
    }
  };
  const propertyAdapter = createPropertyPanelCapabilityAdapter({
    flowCanvasAdapter: {
      selectedNode: 'caliper-1',
      nodes,
      raw: canvas,
      addNode(type, x, y, config) {
        const node = {
          id: 'region-1',
          type,
          title: config.title,
          x,
          y,
          inputs: config.inputs,
          outputs: config.outputs,
          parameters: config.parameters
        };
        nodes.set(node.id, node);
        return node;
      },
      patchNodeParameters() {
        throw new Error('patchNodeParameters should not run when connection creation fails');
      },
      markFlowStructureChanged(reason) {
        structureReasons.push(reason);
      }
    },
    getOperatorMetadata(type) {
      if (type === 'RectangleRegion') {
        return {
          displayName: 'Rectangle Region',
          parameters: [
            { name: 'X', dataType: 'int', value: 0 },
            { name: 'Y', dataType: 'int', value: 0 },
            { name: 'Width', dataType: 'int', value: 1 },
            { name: 'Height', dataType: 'int', value: 1 }
          ],
          inputPorts: [],
          outputPorts: [
            { name: 'Rectangle', dataType: 'Rectangle' }
          ]
        };
      }

      return null;
    }
  });

  const result = propertyAdapter.upsertCaliperSearchRegion('caliper-1', {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  });

  assert.equal(result.updated, false);
  assert.equal(result.reason, 'search_region_connection_failed');
  assert.equal(result.rolledBack, true);
  assert.deepEqual(removedNodes, ['region-1']);
  assert.equal(nodes.has('region-1'), false);
  assert.deepEqual(structureReasons, ['caliper-search-region-rollback']);
});

test('Caliper SearchRegion connected to non RectangleRegion returns explicit reason and owner status', async () => {
  const nodes = new Map([
    ['caliper-1', {
      id: 'caliper-1',
      type: 'CaliperTool',
      inputs: [
        { name: 'Image', dataType: 'Image' },
        { name: 'SearchRegion', dataType: 'Rectangle' }
      ],
      outputs: [],
      parameters: []
    }],
    ['shape-1', {
      id: 'shape-1',
      type: 'ShapeMatching',
      inputs: [],
      outputs: [
        { name: 'Result', dataType: 'Any' }
      ],
      parameters: []
    }]
  ]);
  const propertyAdapter = createPropertyPanelCapabilityAdapter({
    flowCanvasAdapter: {
      selectedNode: 'caliper-1',
      nodes,
      raw: {
        connections: [
          {
            id: 'conn-1',
            source: 'shape-1',
            sourcePort: 0,
            target: 'caliper-1',
            targetPort: 1
          }
        ]
      },
      patchNodeParameters() {
        throw new Error('patchNodeParameters should not run for non RectangleRegion SearchRegion binding');
      }
    }
  });

  const result = propertyAdapter.upsertCaliperSearchRegion('caliper-1', {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  });

  assert.equal(result.updated, false);
  assert.equal(result.reason, 'search_region_connected_to_non_rectangle_region');

  let statusMessage = null;
  const owner = {
    currentNodeId: 'caliper-1',
    currentOperator: { id: 'caliper-1', type: 'CaliperTool' },
    disposed: false,
    propertyAdapter: {
      upsertCaliperSearchRegion() {
        return result;
      }
    },
    roiEditorPanel: {
      refreshFromOperator() {
        throw new Error('refreshFromOperator should not run after failed Caliper commit');
      }
    },
    buildGeometryWriteValues(values) {
      return values;
    },
    applyValuesToForm() {},
    isCaliperToolOperator() {
      return true;
    },
    updateStatus() {
      statusMessage = this.statusMessage;
    }
  };

  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  owner.getCaliperSearchRegionStatus = PropertyPanelCapabilityOwner.prototype.getCaliperSearchRegionStatus;
  PropertyPanelCapabilityOwner.prototype.handleGeometryChanged.call(owner, {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  }, 'commit');

  assert.equal(owner.dirty, false);
  assert.match(statusMessage, /SearchRegion/);
  assert.match(statusMessage, /非 RectangleRegion/);
});

test('PropertyPanelCapabilityOwner treats missing Caliper SearchRegion write result as failure', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let statusUpdated = false;
  let appliedValues = null;
  const owner = {
    currentNodeId: 'caliper-empty-result',
    currentOperator: { id: 'caliper-empty-result', type: 'CaliperTool' },
    disposed: false,
    dirty: true,
    propertyAdapter: {},
    roiEditorPanel: {
      refreshFromOperator() {
        throw new Error('refreshFromOperator should not run without a Caliper write result');
      }
    },
    buildGeometryWriteValues(values) {
      return values;
    },
    applyValuesToForm(values) {
      appliedValues = values;
    },
    isCaliperToolOperator() {
      return true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  };

  owner.getCaliperSearchRegionStatus = PropertyPanelCapabilityOwner.prototype.getCaliperSearchRegionStatus;
  PropertyPanelCapabilityOwner.prototype.handleGeometryChanged.call(owner, {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  }, 'commit');

  assert.deepEqual(appliedValues, {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  });
  assert.equal(owner.dirty, false);
  assert.match(owner.statusMessage, /写入失败/);
  assert.match(owner.statusMessage, /未收到写入结果/);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner marks Caliper SearchRegion write failures as status errors', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const toggles = [];
  const status = {
    textContent: '',
    classList: {
      toggle(name, value) {
        toggles.push({ name, value });
      }
    }
  };
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    statusMessage: 'CaliperTool.SearchRegion 写入失败：未收到写入结果。',
    container: {
      querySelector(selector) {
        return selector === '[data-property-capability-status]' ? status : null;
      }
    }
  });

  owner.updateStatus();

  assert.equal(status.textContent, 'CaliperTool.SearchRegion 写入失败：未收到写入结果。');
  assert.deepEqual(toggles, [
    { name: 'is-error', value: true },
    { name: 'is-success', value: false }
  ]);
});

test('PropertyPanelCapabilityOwner catches thrown geometry parameter writes', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let validationRendered = false;
  let statusUpdated = false;
  let appliedValues = null;
  const owner = {
    currentNodeId: 'roi-1',
    currentOperator: { id: 'roi-1', type: 'Thresholding' },
    disposed: false,
    validationErrors: [{ name: 'RoiX', message: 'old' }],
    dirty: true,
    propertyAdapter: {
      writeParameters() {
        throw new Error('geometry adapter unavailable');
      }
    },
    buildGeometryWriteValues(values) {
      return values;
    },
    applyValuesToForm(values) {
      appliedValues = values;
    },
    isCaliperToolOperator() {
      return false;
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  };

  assert.doesNotThrow(() => PropertyPanelCapabilityOwner.prototype.handleGeometryChanged.call(owner, {
    RoiX: 10,
    RoiY: 12,
    RoiWidth: 30,
    RoiHeight: 16
  }, 'commit'));
  assert.deepEqual(appliedValues, {
    RoiX: 10,
    RoiY: 12,
    RoiWidth: 30,
    RoiHeight: 16
  });
  assert.equal(owner.dirty, false);
  assert.deepEqual(owner.validationErrors, []);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /geometry adapter unavailable/);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner reports failed geometry writes without mutating operator state', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let validationRendered = false;
  let statusUpdated = false;
  let appliedValues = null;
  let parameterUpdated = false;
  const owner = {
    currentNodeId: 'roi-failed',
    currentOperator: {
      id: 'roi-failed',
      type: 'Thresholding',
      parameters: [
        { name: 'RoiX', label: 'ROI X', value: 2 },
        { name: 'RoiY', label: 'ROI Y', value: 4 },
        { name: 'RoiWidth', label: 'ROI 宽度', value: 20 },
        { name: 'RoiHeight', label: 'ROI 高度', value: 10 }
      ]
    },
    disposed: false,
    validationErrors: [{ name: 'RoiX', message: 'old' }],
    dirty: true,
    propertyAdapter: {
      writeParameters() {
        return {
          updated: false,
          reason: 'parameter_not_found',
          missingParameters: ['RoiWidth']
        };
      }
    },
    buildGeometryWriteValues(values) {
      return values;
    },
    applyValuesToForm(values) {
      appliedValues = values;
    },
    updateCurrentOperatorParameterValue() {
      parameterUpdated = true;
    },
    isCaliperToolOperator() {
      return false;
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  };

  PropertyPanelCapabilityOwner.prototype.handleGeometryChanged.call(owner, {
    RoiX: 10,
    RoiY: 12,
    RoiWidth: 30,
    RoiHeight: 16
  }, 'commit');

  assert.deepEqual(appliedValues, {
    RoiX: 10,
    RoiY: 12,
    RoiWidth: 30,
    RoiHeight: 16
  });
  assert.equal(parameterUpdated, false);
  assert.equal(owner.currentOperator.parameters[2].value, 20);
  assert.equal(owner.dirty, false);
  assert.deepEqual(owner.validationErrors, []);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /ROI 宽度/);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner catches thrown Caliper SearchRegion upserts', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let validationRendered = false;
  let statusUpdated = false;
  const owner = {
    currentNodeId: 'caliper-throw',
    currentOperator: { id: 'caliper-throw', type: 'CaliperTool' },
    disposed: false,
    validationErrors: [{ name: 'SearchRegion', message: 'old' }],
    dirty: true,
    propertyAdapter: {
      upsertCaliperSearchRegion() {
        throw new Error('caliper search region unavailable');
      }
    },
    roiEditorPanel: {
      refreshFromOperator() {
        throw new Error('refreshFromOperator should not run after thrown Caliper upsert');
      }
    },
    buildGeometryWriteValues(values) {
      return values;
    },
    applyValuesToForm() {},
    isCaliperToolOperator() {
      return true;
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  };

  assert.doesNotThrow(() => PropertyPanelCapabilityOwner.prototype.handleGeometryChanged.call(owner, {
    X: 10,
    Y: 12,
    Width: 30,
    Height: 16
  }, 'commit'));
  assert.equal(owner.dirty, false);
  assert.deepEqual(owner.validationErrors, []);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /caliper search region unavailable/);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner catches thrown ROI clear writes', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let appliedValues = null;
  let parameterUpdated = false;
  let geometrySynced = false;
  let validationRendered = false;
  let statusUpdated = false;
  const owner = {
    currentNodeId: 'roi-clear-throw',
    currentOperator: {
      id: 'roi-clear-throw',
      type: 'Thresholding',
      parameters: [
        { name: 'RoiX', value: 10 },
        { name: 'RoiY', value: 12 },
        { name: 'RoiWidth', value: 30 },
        { name: 'RoiHeight', value: 16 }
      ]
    },
    validationErrors: [{ name: 'RoiX', message: 'old' }],
    dirty: true,
    getGeometryConfig() {
      return {
        clearValues: {
          RoiX: 0,
          RoiY: 0,
          RoiWidth: 0,
          RoiHeight: 0
        }
      };
    },
    propertyAdapter: {
      writeParameters() {
        throw new Error('clear roi unavailable');
      }
    },
    applyValuesToForm(values) {
      appliedValues = values;
    },
    updateCurrentOperatorParameterValue() {
      parameterUpdated = true;
    },
    syncGeometryEditorFromParams() {
      geometrySynced = true;
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  };

  assert.doesNotThrow(() => PropertyPanelCapabilityOwner.prototype.handleGeometryClearRoi.call(owner));
  assert.equal(appliedValues, null);
  assert.equal(parameterUpdated, false);
  assert.equal(geometrySynced, false);
  assert.equal(owner.dirty, false);
  assert.deepEqual(owner.validationErrors, []);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /clear roi unavailable/);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner filters dependency-disabled values from full apply writes', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const writes = [];
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'template-1',
    currentOperator: {
      id: 'template-1',
      type: 'TemplateMatching',
      parameters: [
        { name: 'TemplatePath', value: '', dataType: 'string' },
        { name: 'TemplateId', value: 'tpl-01', dataType: 'string' }
      ]
    },
    collectFormValues() {
      return { TemplatePath: '', TemplateId: 'tpl-01' };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    validateValues() {
      return [];
    },
    propertyAdapter: {
      writeParameters(nodeId, values) {
        writes.push({ nodeId, values });
        return { updated: true };
      }
    },
    syncParameterDependencyControls() {},
    renderValidationErrors() {},
    updateStatus() {}
  });

  assert.equal(owner.applyChanges({ showToast: false }), true);
  assert.deepEqual(writes, [
    { nodeId: 'template-1', values: { TemplateId: 'tpl-01' } }
  ]);

  owner.currentOperator.parameters[0].value = 'C:\\Templates\\old.tpl';
  assert.deepEqual(
    owner.buildEffectiveWriteValues({ TemplatePath: '', TemplateId: 'tpl-01' }),
    { TemplatePath: '', TemplateId: 'tpl-01' }
  );
  const conflictErrors = owner.validateOperatorModel(owner.currentOperator, {
    values: { TemplatePath: 'C:\\Templates\\old.tpl', TemplateId: 'tpl-02' }
  });
  assert.equal(conflictErrors.length, 1);
  assert.equal(conflictErrors[0].kind, 'mutuallyExclusive');
  assert.deepEqual(
    [...conflictErrors[0].parameterNames].sort(),
    ['TemplateId', 'TemplatePath'].sort()
  );
  assert.match(conflictErrors[0].message, /TemplatePath/);
  assert.match(conflictErrors[0].message, /TemplateId/);
  assert.deepEqual(
    owner.buildWriteValues('TemplateId', { TemplatePath: 'C:\\Templates\\old.tpl', TemplateId: '' }),
    { TemplateId: '' }
  );
});

test('PropertyPanelCapabilityOwner skips full apply when dependency filtering leaves no writable values', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const toasts = [];
  let dependencySynced = false;
  let validationRendered = false;
  let statusUpdated = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'deep-learning-empty-write',
    currentOperator: {
      id: 'deep-learning-empty-write',
      type: 'DeepLearning',
      parameters: [
        { name: 'UseGpu', value: false, dataType: 'bool' },
        { name: 'GpuDeviceId', value: '', dataType: 'string' }
      ]
    },
    validationErrors: [{ name: 'GpuDeviceId', message: 'old' }],
    dirty: true,
    showToast(message, level) {
      toasts.push({ message, level });
    },
    collectFormValues() {
      return { GpuDeviceId: '' };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    validateValues() {
      return [];
    },
    propertyAdapter: {
      writeParameters() {
        throw new Error('writeParameters should not run for an empty dependency-filtered write');
      }
    },
    syncParameterDependencyControls() {
      dependencySynced = true;
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  });

  assert.equal(owner.applyChanges({ showToast: true }), true);
  assert.equal(owner.statusMessage, '参数未变更');
  assert.equal(owner.dirty, false);
  assert.deepEqual(owner.validationErrors, []);
  assert.deepEqual(toasts, []);
  assert.equal(dependencySynced, true);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner ignores stale camera binding loads after rerender', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let resolveFirst;
  let resolveSecond;
  const firstLoad = new Promise(resolve => {
    resolveFirst = resolve;
  });
  const secondLoad = new Promise(resolve => {
    resolveSecond = resolve;
  });
  const loads = [firstLoad, secondLoad];
  const populated = [];
  const firstSelect = { id: 'first-select' };
  const secondSelect = { id: 'second-select' };
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    disposed: false,
    cameraBindingsLoadToken: 0,
    currentSelects: [firstSelect],
    container: {
      querySelectorAll: selector => selector === 'select[data-camera-binding-select="true"]'
        ? owner.currentSelects
        : []
    },
    fetchCameraBindings() {
      return loads.shift();
    },
    populateCameraBindingSelects(selects, bindings) {
      populated.push({
        selectIds: Array.from(selects).map(select => select.id),
        bindingIds: bindings.map(binding => binding.id)
      });
    }
  });

  const first = owner.loadCameraBindingsForSelects();
  owner.currentSelects = [secondSelect];
  const second = owner.loadCameraBindingsForSelects();

  resolveFirst([{ id: 'stale-camera' }]);
  await first;
  assert.deepEqual(populated, []);

  resolveSecond([{ id: 'current-camera' }]);
  await second;
  assert.deepEqual(populated, [{
    selectIds: ['second-select'],
    bindingIds: ['current-camera']
  }]);
});

test('PropertyPanelCapabilityOwner invalidates camera binding loads when rerender has no camera selects', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let resolveLoad;
  const pendingLoad = new Promise(resolve => {
    resolveLoad = resolve;
  });
  const populated = [];
  const firstSelect = { id: 'camera-select' };
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    disposed: false,
    cameraBindingsLoadToken: 0,
    currentSelects: [firstSelect],
    container: {
      querySelectorAll: selector => selector === 'select[data-camera-binding-select="true"]'
        ? owner.currentSelects
        : []
    },
    fetchCameraBindings() {
      return pendingLoad;
    },
    populateCameraBindingSelects(selects, bindings) {
      populated.push({
        selectIds: Array.from(selects).map(select => select.id),
        bindingIds: bindings.map(binding => binding.id)
      });
    }
  });

  const first = owner.loadCameraBindingsForSelects();
  owner.currentSelects = [];
  await owner.loadCameraBindingsForSelects();

  resolveLoad([{ id: 'stale-camera' }]);
  await first;

  assert.deepEqual(populated, []);
});

test('PropertyPanelCapabilityOwner does not clear disabled file paths during passive dependency sync', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const fileRuleHint = { textContent: '', remove() {} };
  const createGroup = ({ pickerButton = null, ruleHint = null } = {}) => ({
    classList: {
      toggle() {}
    },
    querySelector(selector) {
      if (selector === '.btn-pick-file') {
        return pickerButton;
      }
      if (selector === '[data-parameter-rule-hint="true"]') {
        return ruleHint;
      }
      if (selector === '.form-label') {
        return {
          querySelector() {
            return null;
          },
          insertAdjacentHTML() {}
        };
      }
      return null;
    },
    querySelectorAll() {
      return [];
    },
    setAttribute() {}
  });
  const pickerButton = {
    disabled: false,
    setAttribute(name, value) {
      this[name] = value;
    }
  };
  const sourceGroup = createGroup();
  const fileGroup = createGroup({ pickerButton, ruleHint: fileRuleHint });
  const sourceInput = {
    name: 'SourceType',
    value: 'Camera',
    disabled: false,
    setAttribute(name, value) {
      this[name] = value;
    },
    closest() {
      return sourceGroup;
    }
  };
  const fileInput = {
    name: 'FilePath',
    value: 'C:\\Data\\stale.png',
    disabled: false,
    setAttribute(name, value) {
      this[name] = value;
    },
    closest() {
      return fileGroup;
    }
  };
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentOperator: {
      id: 'acq-passive-sync',
      type: 'ImageAcquisition',
      parameters: [
        { name: 'SourceType', value: 'Camera', dataType: 'enum' },
        { name: 'FilePath', value: 'C:\\Data\\stale.png', dataType: 'file' }
      ]
    },
    container: {
      querySelectorAll(selector) {
        return selector === '[data-property-parameter="true"]'
          ? [sourceInput, fileInput]
          : [];
      }
    },
    collectFormValues() {
      return {
        SourceType: 'Camera',
        FilePath: 'C:\\Data\\stale.png'
      };
    }
  });

  owner.syncParameterDependencyControls();

  assert.equal(fileInput.value, 'C:\\Data\\stale.png');
  assert.equal(owner.currentOperator.parameters[1].value, 'C:\\Data\\stale.png');
  assert.equal(fileInput.disabled, true);
  assert.equal(fileInput['aria-disabled'], 'true');
  assert.equal(pickerButton.disabled, true);
  assert.equal(pickerButton['aria-disabled'], 'true');
  assert.match(fileRuleHint.textContent, /相机模式/);
});

test('PropertyPanelCapabilityOwner validates EdgeDetection model sources only for OnnxEdge', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const dom = installValidationDocument([
    'Method',
    'EdgeModelPath',
    'EdgeModelId',
    'ModelCatalogPath',
    'EdgeBinarizationThreshold'
  ]);
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  owner.container = dom.container;
  const createOperator = method => ({
    id: 'edge-detection-1',
    type: 'EdgeDetection',
    parameters: [
      { name: 'Method', value: method, dataType: 'enum' },
      { name: 'EdgeModelPath', displayName: '模型路径', value: '', dataType: 'file', isRequired: true },
      { name: 'EdgeModelId', displayName: '模型 ID', value: '', dataType: 'string', isRequired: true },
      { name: 'ModelCatalogPath', displayName: '模型目录', value: '', dataType: 'file', isRequired: true },
      { name: 'EdgeBinarizationThreshold', displayName: '边缘二值化阈值', value: 0.5, dataType: 'double', isRequired: true }
    ]
  });

  try {
    owner.currentOperator = createOperator('Canny');
    owner.validationErrors = owner.validateOperatorModel(owner.currentOperator);
    owner.renderValidationErrors();

    assert.deepEqual(owner.validationErrors, []);
    assert.equal(dom.messages.length, 0);
    assert.equal(dom.groups.get('EdgeModelPath').classList.contains('invalid'), false);
    assert.equal(dom.inputs.get('EdgeModelPath').getAttribute('aria-invalid'), null);

    owner.currentOperator = createOperator('OnnxEdge');
    owner.validationErrors = owner.validateOperatorModel(owner.currentOperator);
    owner.renderValidationErrors();

    assert.equal(owner.validationErrors.length, 1);
    assert.equal(owner.validationErrors[0].kind, 'atLeastOneOf');
    assert.equal(owner.validationErrors[0].message, 'ONNX 边缘检测需要选择模型路径、模型 ID 或模型目录之一');
    for (const name of ['EdgeModelPath', 'EdgeModelId', 'ModelCatalogPath']) {
      assert.equal(dom.groups.get(name).classList.contains('invalid'), true, `${name} invalid group`);
      assert.equal(dom.inputs.get(name).getAttribute('aria-invalid'), 'true', `${name} aria-invalid`);
    }
    assert.equal(dom.messages.length, 3);
    assert.equal(dom.messages.every(message => message.textContent === owner.validationErrors[0].message), true);

    owner.currentOperator = createOperator('Canny');
    owner.validationErrors = owner.validateOperatorModel(owner.currentOperator);
    owner.renderValidationErrors();

    assert.deepEqual(owner.validationErrors, []);
    assert.equal(dom.messages.length, 0);
    for (const name of ['EdgeModelPath', 'EdgeModelId', 'ModelCatalogPath']) {
      assert.equal(dom.groups.get(name).classList.contains('invalid'), false, `${name} invalid group cleared`);
      assert.equal(dom.inputs.get(name).getAttribute('aria-invalid'), null, `${name} aria-invalid cleared`);
    }
  } finally {
    dom.cleanup();
  }
});

test('PropertyPanelCapabilityOwner clears stale selection when full apply target node is missing', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let rendered = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'deleted-node',
    currentOperator: {
      id: 'deleted-node',
      type: 'Thresholding',
      parameters: [
        { name: 'Threshold', value: 128, dataType: 'int', min: 0, max: 255 }
      ]
    },
    validationErrors: [{ name: 'Threshold', message: 'old error' }],
    statusMessage: '旧状态',
    collectFormValues() {
      return { Threshold: 128 };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    propertyAdapter: {
      writeParameters() {
        return { updated: false, reason: 'node_not_found' };
      }
    },
    render() {
      rendered = true;
    },
    renderValidationErrors() {
      throw new Error('renderValidationErrors should not run after node_not_found');
    },
    updateStatus() {
      throw new Error('updateStatus should not run after node_not_found');
    }
  });

  assert.equal(owner.applyChanges({ showToast: false }), false);
  assert.equal(owner.currentNodeId, null);
  assert.equal(owner.currentOperator, null);
  assert.deepEqual(owner.validationErrors, []);
  assert.equal(owner.statusMessage, '');
  assert.equal(rendered, true);
});

test('PropertyPanelCapabilityOwner reports write failures without mutating local parameter state', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const toasts = [];
  let validationRendered = false;
  let statusUpdated = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'threshold-1',
    currentOperator: {
      id: 'threshold-1',
      type: 'Thresholding',
      parameters: [
        { name: 'Threshold', label: '阈值', value: 128, dataType: 'int', min: 0, max: 255 }
      ]
    },
    validationErrors: [],
    dirty: true,
    showToast(message, level) {
      toasts.push({ message, level });
    },
    collectFormValues() {
      return { Threshold: 180 };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    propertyAdapter: {
      writeParameters() {
        return {
          updated: false,
          reason: 'parameter_not_found',
          missingParameters: ['Threshold']
        };
      }
    },
    syncParameterDependencyControls() {
      throw new Error('syncParameterDependencyControls should not run after failed write');
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  });

  assert.equal(owner.applyChanges({ showToast: true }), false);
  assert.equal(owner.currentOperator.parameters[0].value, 128);
  assert.equal(owner.dirty, false);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /阈值/);
  assert.deepEqual(toasts, [{ message: owner.statusMessage, level: 'error' }]);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner treats missing write results as failed writes', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const toasts = [];
  let validationRendered = false;
  let statusUpdated = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'threshold-missing-result',
    currentOperator: {
      id: 'threshold-missing-result',
      type: 'Thresholding',
      parameters: [
        { name: 'Threshold', value: 128, dataType: 'int', min: 0, max: 255 }
      ]
    },
    validationErrors: [],
    dirty: true,
    showToast(message, level) {
      toasts.push({ message, level });
    },
    collectFormValues() {
      return { Threshold: 180 };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    propertyAdapter: {
      writeParameters() {
        return undefined;
      }
    },
    syncParameterDependencyControls() {
      throw new Error('syncParameterDependencyControls should not run without a write result');
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  });

  assert.equal(owner.applyChanges({ showToast: true }), false);
  assert.equal(owner.currentOperator.parameters[0].value, 128);
  assert.equal(owner.dirty, false);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /未收到写入结果/);
  assert.deepEqual(toasts, [{ message: owner.statusMessage, level: 'error' }]);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner catches thrown full apply writes without mutating local parameter state', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const toasts = [];
  let validationRendered = false;
  let statusUpdated = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'threshold-throw',
    currentOperator: {
      id: 'threshold-throw',
      type: 'Thresholding',
      parameters: [
        { name: 'Threshold', value: 128, dataType: 'int', min: 0, max: 255 }
      ]
    },
    validationErrors: [],
    dirty: true,
    showToast(message, level) {
      toasts.push({ message, level });
    },
    collectFormValues() {
      return { Threshold: 180 };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    propertyAdapter: {
      writeParameters() {
        throw new Error('adapter unavailable');
      }
    },
    syncParameterDependencyControls() {
      throw new Error('syncParameterDependencyControls should not run after thrown write');
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  });

  assert.equal(owner.applyChanges({ showToast: true }), false);
  assert.equal(owner.currentOperator.parameters[0].value, 128);
  assert.equal(owner.dirty, false);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /adapter unavailable/);
  assert.deepEqual(toasts, [{ message: owner.statusMessage, level: 'error' }]);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner catches thrown incremental writes without mutating local parameter state', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let validationRendered = false;
  let statusUpdated = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'threshold-change-throw',
    currentOperator: {
      id: 'threshold-change-throw',
      type: 'Thresholding',
      parameters: [
        { name: 'Threshold', value: 128, dataType: 'int', min: 0, max: 255 }
      ]
    },
    validationErrors: [],
    dirty: true,
    disposed: false,
    collectFormValues() {
      return { Threshold: 180 };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    validateValues() {
      return [];
    },
    propertyAdapter: {
      writeParameters() {
        throw new Error('incremental adapter unavailable');
      }
    },
    syncParameterDependencyControls() {
      throw new Error('syncParameterDependencyControls should not run after thrown write');
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  });
  const input = {
    name: 'Threshold',
    closest(selector) {
      return selector === '[data-property-parameter="true"]' ? input : null;
    }
  };

  owner.handleContainerChange({ target: input });

  assert.equal(owner.currentOperator.parameters[0].value, 128);
  assert.equal(owner.dirty, false);
  assert.match(owner.statusMessage, /参数写入失败/);
  assert.match(owner.statusMessage, /incremental adapter unavailable/);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner clears stale dirty state when incremental write is filtered empty', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let dependencySynced = false;
  let validationRendered = false;
  let statusUpdated = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'empty-incremental-write',
    currentOperator: {
      id: 'empty-incremental-write',
      type: 'DeepLearning',
      parameters: [
        { name: 'UseGpu', value: false, dataType: 'bool' },
        { name: 'GpuDeviceId', value: '', dataType: 'string' }
      ]
    },
    validationErrors: [],
    dirty: true,
    lastChangedParameterName: 'PreviousParameter',
    disposed: false,
    collectNormalizedFormValues() {
      return { UseGpu: false, GpuDeviceId: '' };
    },
    validateValues() {
      return [];
    },
    buildWriteValues() {
      return {};
    },
    propertyAdapter: {
      writeParameters() {
        throw new Error('writeParameters should not run for an empty incremental write');
      }
    },
    syncParameterDependencyControls() {
      dependencySynced = true;
    },
    renderValidationErrors() {
      validationRendered = true;
    },
    updateStatus() {
      statusUpdated = true;
    }
  });
  const input = {
    name: 'GpuDeviceId',
    disabled: false,
    closest(selector) {
      return selector === '[data-property-parameter="true"]' ? input : null;
    },
    getAttribute() {
      return null;
    }
  };

  owner.handleContainerChange({ target: input });

  assert.equal(owner.statusMessage, '参数未变更');
  assert.equal(owner.dirty, false);
  assert.equal(owner.lastChangedParameterName, null);
  assert.deepEqual(owner.validationErrors, []);
  assert.equal(dependencySynced, true);
  assert.equal(validationRendered, true);
  assert.equal(statusUpdated, true);
});

test('PropertyPanelCapabilityOwner ignores disabled parameter change events', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let touchedWritePath = false;
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'disabled-change',
    currentOperator: { id: 'disabled-change', type: 'Thresholding', parameters: [] },
    disposed: false,
    collectNormalizedFormValues() {
      touchedWritePath = true;
      return {};
    },
    validateValues() {
      touchedWritePath = true;
      return [];
    },
    propertyAdapter: {
      writeParameters() {
        touchedWritePath = true;
        return { updated: true };
      }
    }
  });
  const input = {
    name: 'Threshold',
    disabled: false,
    closest(selector) {
      return selector === '[data-property-parameter="true"]' ? input : null;
    },
    getAttribute(name) {
      return name === 'aria-disabled' ? 'true' : null;
    }
  };

  owner.handleContainerChange({ target: input });

  assert.equal(touchedWritePath, false);
});

test('PropertyPanelCapabilityOwner ignores late file picker results for disabled fields', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  let parameterUpdated = false;
  let changeDispatched = false;
  let applyCalled = false;
  const input = {
    name: 'FilePath',
    disabled: true,
    getAttribute(name) {
      return name === 'aria-disabled' ? 'true' : null;
    },
    dispatchEvent() {
      changeDispatched = true;
    }
  };
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'file-disabled',
    disposed: false,
    findParamInput() {
      return input;
    },
    updateCurrentOperatorParameterValue() {
      parameterUpdated = true;
    },
    applyChanges() {
      applyCalled = true;
    },
    syncParameterDependencyControls() {
      throw new Error('syncParameterDependencyControls should not run for a disabled file input');
    }
  });

  owner.handleFilePickedEvent({
    payload: {
      ParameterName: 'FilePath',
      FilePath: 'C:\\Data\\late.png'
    }
  });

  assert.equal(input.value, undefined);
  assert.equal(parameterUpdated, false);
  assert.equal(changeDispatched, false);
  assert.equal(applyCalled, false);
});

test('PropertyPanelCapabilityOwner writes picked acquisition files once through incremental change path', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const writes = [];
  let dispatchCalled = false;
  let applyCalled = false;
  let dependencySynced = false;
  const fileInput = {
    name: 'FilePath',
    value: '',
    disabled: false,
    getAttribute() {
      return null;
    },
    closest(selector) {
      return selector === '[data-property-parameter="true"]' ? fileInput : null;
    },
    dispatchEvent() {
      dispatchCalled = true;
    }
  };
  const sourceInput = {
    name: 'SourceType',
    value: 'Camera',
    options: [{ value: 'File' }, { value: 'Camera' }]
  };
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'acquisition-file',
    currentOperator: {
      id: 'acquisition-file',
      type: 'ImageAcquisition',
      parameters: [
        { name: 'SourceType', value: 'Camera', dataType: 'enum' },
        { name: 'FilePath', value: '', dataType: 'file' }
      ]
    },
    disposed: false,
    validationErrors: [],
    dirty: false,
    findParamInput(...names) {
      const normalized = names.map(name => String(name).toLowerCase());
      if (normalized.includes('filepath')) {
        return fileInput;
      }
      if (normalized.includes('sourcetype')) {
        return sourceInput;
      }
      return null;
    },
    collectFormValues() {
      return {
        SourceType: sourceInput.value,
        FilePath: fileInput.value
      };
    },
    validateValues() {
      return [];
    },
    propertyAdapter: {
      writeParameters(nodeId, values) {
        writes.push({ nodeId, values });
        return { updated: true };
      }
    },
    applyChanges() {
      applyCalled = true;
    },
    syncParameterDependencyControls(options) {
      dependencySynced = options?.clearFilePathWhenCamera === false;
    },
    renderValidationErrors() {},
    updateStatus() {}
  });

  owner.handleFilePickedEvent({
    payload: {
      ParameterName: 'FilePath',
      FilePath: 'C:\\Data\\sample.png'
    }
  });

  assert.equal(fileInput.value, 'C:\\Data\\sample.png');
  assert.equal(sourceInput.value, 'File');
  assert.deepEqual(writes, [{
    nodeId: 'acquisition-file',
    values: {
      FilePath: 'C:\\Data\\sample.png',
      SourceType: 'File'
    }
  }]);
  assert.equal(owner.currentOperator.parameters[0].value, 'File');
  assert.equal(owner.currentOperator.parameters[1].value, 'C:\\Data\\sample.png');
  assert.equal(owner.dirty, true);
  assert.equal(dependencySynced, true);
  assert.equal(dispatchCalled, false);
  assert.equal(applyCalled, false);
});

test('PropertyPanelCapabilityOwner treats no-change writes as stable without success toast', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const toasts = [];
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentNodeId: 'threshold-1',
    currentOperator: {
      id: 'threshold-1',
      type: 'Thresholding',
      parameters: [
        { name: 'Threshold', value: 128, dataType: 'int', min: 0, max: 255 }
      ]
    },
    validationErrors: [],
    showToast(message, level) {
      toasts.push({ message, level });
    },
    collectFormValues() {
      return { Threshold: 128 };
    },
    normalizeImageAcquisitionValues(values) {
      return values;
    },
    propertyAdapter: {
      writeParameters() {
        return { updated: false, reason: 'no_change', missingParameters: [] };
      }
    },
    syncParameterDependencyControls() {},
    renderValidationErrors() {},
    updateStatus() {}
  });

  assert.equal(owner.applyChanges({ showToast: true }), true);
  assert.equal(owner.currentOperator.parameters[0].value, 128);
  assert.equal(owner.dirty, false);
  assert.equal(owner.statusMessage, '参数未变更');
  assert.deepEqual(toasts, []);
});

test('PropertyPanelCapabilityOwner validates normalized current form values', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  let validatedValues = null;
  Object.assign(owner, {
    currentOperator: { id: 'acq-1', type: 'ImageAcquisition', parameters: [] },
    validationErrors: [],
    collectFormValues() {
      return { SourceType: 'Camera', FilePath: 'C:\\Data\\stale.png', CameraBindingId: 'line-camera-01' };
    },
    normalizeImageAcquisitionValues(values, changedName) {
      return PropertyPanelCapabilityOwner.prototype.normalizeImageAcquisitionValues.call(this, values, changedName);
    },
    validateValues(values) {
      validatedValues = values;
      return [];
    },
    renderValidationErrors() {},
    updateStatus() {}
  });

  assert.equal(owner.validateCurrentOperator({ showToast: false }), true);
  assert.deepEqual(validatedValues, {
    SourceType: 'Camera',
    FilePath: 'C:\\Data\\stale.png',
    CameraBindingId: 'line-camera-01'
  });
});

test('PropertyPanelCapabilityOwner clears acquisition file path only for explicit SourceType camera changes', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  Object.assign(owner, {
    currentOperator: {
      id: 'acq-clear-boundary',
      type: 'ImageAcquisition',
      parameters: []
    }
  });
  const values = {
    SourceType: 'Camera',
    FilePath: 'C:\\Data\\stale.png',
    CameraBindingId: 'line-camera-01'
  };

  assert.deepEqual(owner.normalizeImageAcquisitionValues(values), values);
  assert.deepEqual(owner.normalizeImageAcquisitionValues(values, 'CameraBindingId'), values);
  assert.deepEqual(owner.normalizeImageAcquisitionValues(values, 'SourceType'), {
    SourceType: 'Camera',
    FilePath: '',
    CameraBindingId: 'line-camera-01'
  });
});

test('PropertyPanelCapabilityOwner does not render implicit sliders for ranged Caliper numeric parameters', async () => {
  const cleanupDocument = installFakeDocument();
  try {
    const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
    const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
    owner.currentOperator = {
      id: 'caliper-1',
      type: 'CaliperTool',
      parameters: []
    };
    const params = [
      { name: 'Angle', displayName: 'Angle', dataType: 'double', value: 0, min: -180, max: 180 },
      { name: 'EdgeThreshold', displayName: 'Edge threshold', dataType: 'float', value: 10, Min: 0, Max: 255 },
      { name: 'ExpectedCount', displayName: 'Expected count', dataType: 'int', value: 1, minValue: 1, maxValue: 10 }
    ];

    const html = params.map(param => owner.renderParameterField(param)).join('\n');

    assert.equal(countSliderMarkup(html), 0);
    assert.equal((html.match(/type="number"/g) || []).length, 3);
    assert.match(html, /name="Angle"[\s\S]*min="-180"[\s\S]*max="180"/);
    assert.match(html, /name="EdgeThreshold"[\s\S]*min="0"[\s\S]*max="255"/);
    assert.match(html, /name="ExpectedCount"[\s\S]*min="1"[\s\S]*max="10"/);
    for (const param of params) {
      assert.equal(owner.shouldRenderParameterSlider(param), false);
    }
  } finally {
    cleanupDocument();
  }
});

test('PropertyPanelCapabilityOwner renders sliders only for explicit slider metadata', async () => {
  const cleanupDocument = installFakeDocument();
  try {
    const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
    const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
    owner.currentOperator = {
      id: 'slider-fixture',
      type: 'SyntheticTool',
      parameters: []
    };
    const explicitParams = [
      { name: 'ShowSlider', dataType: 'double', value: 3, min: 0, max: 10, showSlider: true },
      { name: 'PascalShowSlider', dataType: 'double', value: 3, min: 0, max: 10, ShowSlider: true },
      { name: 'UiControlSlider', dataType: 'double', value: 3, min: 0, max: 10, UIControl: 'Slider' },
      { name: 'ControlSlider', dataType: 'double', value: 3, min: 0, max: 10, control: 'slider' },
      { name: 'EditorSlider', dataType: 'double', value: 3, min: 0, max: 10, editor: 'SLIDER' }
    ];

    for (const param of explicitParams) {
      const html = owner.renderParameterField(param);
      assert.equal(countSliderMarkup(html), 1);
      assert.match(html, new RegExp(`type="range"[\\s\\S]*name="${param.name}"`));
      assert.equal(owner.shouldRenderParameterSlider(param), true);
    }

    const inferredNameHtml = owner.renderParameterField({
      name: 'ThresholdWithRange',
      dataType: 'double',
      value: 3,
      min: 0,
      max: 10
    });
    assert.equal(countSliderMarkup(inferredNameHtml), 0);
  } finally {
    cleanupDocument();
  }
});

test('PropertyPanelCapabilityOwner keeps required marker separate from slider rendering', async () => {
  const cleanupDocument = installFakeDocument();
  try {
    const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
    const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
    owner.currentOperator = {
      id: 'required-fixture',
      type: 'SyntheticTool',
      parameters: []
    };

    const html = owner.renderParameterField({
      name: 'RequiredCount',
      displayName: 'Required count',
      dataType: 'int',
      value: 1,
      min: 0,
      max: 10,
      isRequired: true
    });

    assert.match(html, /<span class="required">\*<\/span>/);
    assert.equal(countSliderMarkup(html), 0);
  } finally {
    cleanupDocument();
  }
});

test('PropertyPanelCapabilityOwner synchronizes explicit sliders with number inputs without input write storms', async () => {
  const PropertyPanelCapabilityOwner = await loadPropertyPanelCapabilityOwner();
  const owner = Object.create(PropertyPanelCapabilityOwner.prototype);
  const writes = [];
  let collectedChangedName = null;
  let numberInput;
  let slider;
  const wrapper = {
    querySelectorAll(selector) {
      if (selector === 'input[type="number"][data-property-parameter="true"]') {
        return [numberInput];
      }
      if (selector === '.param-slider') {
        return [slider];
      }
      return [];
    },
    querySelector(selector) {
      return this.querySelectorAll(selector)[0] || null;
    }
  };
  numberInput = {
    name: 'Gain',
    type: 'number',
    value: '4',
    disabled: false,
    readOnly: false,
    dataset: { type: 'double' },
    parentElement: wrapper,
    closest(selector) {
      return selector === '[data-property-parameter="true"]' ||
        selector === 'input[type="number"][data-property-parameter="true"]'
        ? numberInput
        : null;
    },
    getAttribute() {
      return null;
    }
  };
  slider = {
    name: 'Gain',
    value: '7',
    disabled: false,
    readOnly: false,
    parentElement: wrapper,
    closest(selector) {
      return selector === '.param-slider' ? slider : null;
    },
    getAttribute(name) {
      return name === 'name' ? 'Gain' : null;
    },
    dispatchEvent() {
      throw new Error('slider sync should not dispatch nested events');
    }
  };
  Object.assign(owner, {
    currentNodeId: 'slider-fixture',
    currentOperator: {
      id: 'slider-fixture',
      type: 'SyntheticTool',
      parameters: [
        { name: 'Gain', value: 4, dataType: 'double', min: 0, max: 10, showSlider: true }
      ]
    },
    validationErrors: [],
    dirty: false,
    disposed: false,
    collectNormalizedFormValues(changedName) {
      collectedChangedName = changedName;
      return { Gain: Number(numberInput.value) };
    },
    validateValues() {
      return [];
    },
    buildWriteValues(parameterName, values) {
      return { [parameterName]: values[parameterName] };
    },
    propertyAdapter: {
      writeParameters(nodeId, values) {
        writes.push({ nodeId, values });
        return { updated: true };
      }
    },
    syncParameterDependencyControls() {},
    renderValidationErrors() {},
    updateStatus() {}
  });

  owner.handleContainerInput({ target: slider });
  assert.equal(numberInput.value, '7');
  assert.deepEqual(writes, []);

  owner.handleContainerChange({ target: slider });
  assert.equal(collectedChangedName, 'Gain');
  assert.deepEqual(writes, [
    { nodeId: 'slider-fixture', values: { Gain: 7 } }
  ]);
  assert.equal(owner.currentOperator.parameters[0].value, 7);

  numberInput.value = '5';
  owner.handleContainerInput({ target: numberInput });
  assert.equal(slider.value, '5');
});

test('Studio2 Inspector keeps the full legacy PropertyPanel capability surface', () => {
  const panelSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');

  for (const requiredText of [
    'FilePickedEvent',
    'PickFileCommand',
    'btn-pick-file',
    'data-camera-binding-select="true"',
    'gv-binding-select',
    'param-slider',
    'form-color-hidden',
    'btn-recommend',
    'btn-reset',
    'roi-editor-container',
    'calibration-draft-workbench-container',
    'setConnection(connection)'
  ]) {
    assert.match(panelSource, new RegExp(requiredText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }

  assert.match(panelSource, /previewPanelEnabled/);
  assert.match(panelSource, /auxiliaryWorkbenchesEnabled/);
});

test('PropertyPanelCapabilityOwner keeps migrated file and camera controls', () => {
  const ownerSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanelCapabilityOwner.mjs');

  for (const requiredText of [
    'FilePickedEvent',
    'PickFileCommand',
    'btn-pick-file',
    'data-camera-binding-select="true"',
    'resolveParameterControlType',
    'isPathLikeParameter',
    'normalizeAcquisitionSourceType',
    'isElementEffectivelyDisabled',
    'getLocalizedDisabledReason',
    'collectNormalizedFormValues',
    'buildEffectiveWriteValues',
    'shouldWriteParameterValue',
    'syncParameterDependencyControls',
    'syncImageAcquisitionSourceControls',
    'data-parameter-rule-hint="true"',
    "httpClient.get('/cameras/bindings')",
    'param-slider',
    'form-color-hidden',
    'RoiEditorPanel',
    'getOperatorRoiConfig',
    'previewCoordinator',
    'previewResourcesEnabled',
    'onOpenPreviewImage',
    'data-property-geometry-editor-container',
    'property-geometry-section',
    'upsertCaliperSearchRegion'
  ]) {
    assert.match(ownerSource, new RegExp(requiredText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }

  assert.match(ownerSource, /controlType === 'file'[\s\S]*btn-pick-file/);
  assert.match(ownerSource, /btn-pick-file[\s\S]*aria-disabled="\$\{isDisabled \? 'true' : 'false'\}"/);
  assert.match(ownerSource, /controlType === 'cameraBinding'[\s\S]*data-camera-binding-select="true"/);
  assert.match(ownerSource, /webMessageBridge\.sendMessage\('PickFileCommand'/);
  assert.match(ownerSource, /webMessageBridge\.on\('FilePickedEvent'/);
  assert.match(ownerSource, /normalizeParameterName\(parameterName\) === 'filepath'/);
  assert.match(ownerSource, /this\.propertyAdapter\.writeParameters\(this\.currentNodeId, writeValues\)/);
  assert.match(ownerSource, /this\.propertyAdapter\.upsertCaliperSearchRegion\?\.\(this\.currentNodeId, writeValues\)/);
});

test('all backend operator parameter types are covered by migrated Inspector controls', () => {
  const panelSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
  const params = collectOperatorParams();
  const knownTypes = new Set(['string', 'int', 'double', 'float', 'number', 'bool', 'boolean', 'enum', 'select', 'file', 'cameraBinding']);
  const unknownTypes = params.filter(param => !knownTypes.has(param.type));
  const fileParams = params.filter(param => param.type === 'file');
  const cameraBindingParams = params.filter(param => param.type === 'cameraBinding');

  assert.equal(unknownTypes.length, 0, `未覆盖参数类型: ${unknownTypes.map(param => `${param.name}:${param.type}`).join(', ')}`);
  assert.ok(fileParams.length > 0, '应至少扫描到 file 参数');
  assert.ok(cameraBindingParams.length > 0, '应至少扫描到 cameraBinding 参数');
  assert.match(panelSource, /case 'file':[\s\S]*btn-pick-file[\s\S]*PickFileCommand/);
  assert.match(panelSource, /case 'cameraBinding':[\s\S]*data-camera-binding-select="true"/);
});

test('app composition root uses PropertyPanelCapabilityOwner with legacy PropertyPanel fallback', () => {
  const appSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');

  assert.match(appSource, /const PROPERTY_PANEL_CAPABILITY_FLAG_KEY = 'Studio2\.PropertyPanel'/);
  assert.match(appSource, /const PROPERTY_PANEL_CAPABILITY_ENABLED = readPropertyPanelCapabilityFlagOnce\(\);/);
  assert.match(appSource, /function createPropertyPanelCapabilityOwner\(\)/);
  assert.match(appSource, /isPropertyPanelCapabilityEnabled\(\)[\s\S]*\? createPropertyPanelCapabilityOwner\(\)/);
  assert.match(appSource, /if \(!propertyPanelOwner\) \{[\s\S]*propertyPanelOwner = await createLegacyPropertyPanelOwner\(\);[\s\S]*\}/);
  assert.equal((appSource.match(/new PropertyPanel\('property-panel'/g) || []).length, 1);
  assert.equal((appSource.match(/new PropertyPanelCapabilityOwner\(/g) || []).length, 1);
  assert.match(appSource, /propertyPanelCapabilityOwner\.mjs/);
  assert.match(appSource, /serviceRegistry\.register\('propertyPanelCapabilityOwner'/);
  assert.match(appSource, /createPropertyPanelCapabilityAdapter/);
  assert.match(appSource, /previewPanelEnabled:\s*ownsPreviewSidebar/);
  assert.match(appSource, /previewCoordinator:\s*nodePreviewCoordinator/);
  assert.match(appSource, /previewResourcesEnabled:\s*!isPreviewPanelCapabilityEnabled\(\)/);
  assert.match(appSource, /onOpenPreviewImage:\s*openImageViewerFromPreview/);
  assert.match(appSource, /circleSearchV2ToolEnabled:\s*readStartupFeatureFlagOnce\('Studio:CircleSearchV2ToolEnabled'\)/);
  assert.match(appSource, /nPointCalibrationWorkbenchEnabled:\s*readStartupFeatureFlagOnce\('Studio:NPointCalibrationWorkbenchEnabled'\)/);
  assert.match(appSource, /auxiliaryWorkbenchesEnabled/);
  assert.match(appSource, /panel\.setConnection/);
  assert.doesNotMatch(appSource, /import\s+\{\s*PropertyPanel\s*\}\s+from\s+'\.\/features\/flow-editor\/propertyPanel\.js'/);
  assert.match(appSource, /legacyPropertyPanelModulePromise = import\('\.\/features\/flow-editor\/propertyPanel\.js'\)/);
  assert.doesNotMatch(appSource, /trackedSubscribe\(subscribeSelectedOperator/);
  assert.match(appSource, /disposePropertyPanelOwner\(\);/);
});
