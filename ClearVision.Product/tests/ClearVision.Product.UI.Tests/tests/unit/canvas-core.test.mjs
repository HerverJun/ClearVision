import test from 'node:test';
import assert from 'node:assert/strict';

// =============================================================================
// Minimal DOM / Canvas mocks for Node environment
// =============================================================================

global.requestAnimationFrame = global.requestAnimationFrame || ((fn) => setTimeout(fn, 16));
global.cancelAnimationFrame = global.cancelAnimationFrame || ((id) => clearTimeout(id));

global.Image = class MockImage {
  constructor() {
    this.onload = null;
    this.onerror = null;
    this.src = '';
    this.width = 100;
    this.height = 100;
  }
  set src(value) {
    this._src = value;
    // 同步触发加载成功，避免测试挂起
    if (this.onload) this.onload();
  }
  get src() { return this._src; }
};

function createMockCanvas() {
  const events = {};
  return {
    width: 800,
    height: 600,
    style: {},
    getContext() {
      return {
        clearRect() {},
        fillRect() {},
        beginPath() {},
        closePath() {},
        moveTo() {},
        lineTo() {},
        bezierCurveTo() {},
        quadraticCurveTo() {},
        arc() {},
        fill() {},
        stroke() {},
        fillText() {},
        measureText(text) { return { width: String(text || '').length * 6 }; },
        save() {},
        restore() {},
        translate() {},
        scale() {},
        setTransform() {},
        setLineDash() {},
        createLinearGradient() {
          return { addColorStop() {} };
        },
        createRadialGradient() {
          return { addColorStop() {} };
        }
      };
    },
    addEventListener(type, fn) {
      (events[type] ||= []).push(fn);
    },
    removeEventListener(type, fn) {
      if (events[type]) {
        events[type] = events[type].filter(f => f !== fn);
      }
    },
    getBoundingClientRect() {
      return { left: 0, top: 0, width: 800, height: 600 };
    },
    parentElement: {
      clientWidth: 800,
      clientHeight: 600,
      appendChild() {},
      removeChild() {}
    },
    _events: events
  };
}

function createMockDocument(canvas) {
  const events = {};
  return {
    getElementById(id) {
      return canvas;
    },
    createElement(tag) {
      const el = {
        tagName: tag.toUpperCase(),
        style: {},
        classList: { add() {}, remove() {} },
        className: '',
        children: [],
        _innerHTML: '',
        _textContent: '',
        get innerHTML() { return this._innerHTML; },
        set innerHTML(v) { this._innerHTML = v; },
        get textContent() { return this._textContent; },
        set textContent(v) { this._textContent = v; },
        appendChild(child) { this.children.push(child); },
        remove() {},
        addEventListener() {},
        removeEventListener() {},
        setAttribute() {},
        getContext() {
          return canvas.getContext();
        },
        getBoundingClientRect() {
          return { left: 0, top: 0, width: 200, height: 150 };
        },
        querySelector(sel) {
          function matches(node, s) {
            if (!s || !node) return false;
            if (s.startsWith('.')) return (node.className || '').split(' ').includes(s.slice(1));
            if (s.startsWith('#')) return (node.id || '') === s.slice(1);
            return node.tagName === s.toUpperCase();
          }
          function search(nodes) {
            for (const n of nodes) {
              if (matches(n, sel)) return n;
              if (n.children) {
                const found = search(n.children);
                if (found) return found;
              }
            }
            return null;
          }
          return matches(this, sel) ? this : search(this.children || []);
        },
        querySelectorAll(sel) {
          const results = [];
          function matches(node, s) {
            if (!s || !node) return false;
            if (s.startsWith('.')) return (node.className || '').split(' ').includes(s.slice(1));
            return false;
          }
          function collect(nodes) {
            for (const n of nodes) {
              if (matches(n, sel)) results.push(n);
              if (n.children) collect(n.children);
            }
          }
          if (matches(this, sel)) results.push(this);
          collect(this.children || []);
          return results;
        }
      };
      return el;
    },
    body: {
      appendChild() {},
      removeChild() {}
    },
    head: {
      appendChild() {}
    },
    addEventListener(type, fn) {
      (events[type] ||= []).push(fn);
    },
    removeEventListener(type, fn) {
      if (events[type]) {
        events[type] = events[type].filter(candidate => candidate !== fn);
      }
    },
    _events: events
  };
}

function createMockWindow(canvas) {
  const events = {};
  return {
    devicePixelRatio: 1,
    addEventListener(type, fn) {
      (events[type] ||= []).push(fn);
    },
    removeEventListener(type, fn) {
      if (events[type]) {
        events[type] = events[type].filter(candidate => candidate !== fn);
      }
    },
    requestAnimationFrame(fn) {
      return setTimeout(fn, 16);
    },
    cancelAnimationFrame(id) {
      clearTimeout(id);
    },
    ResizeObserver: class MockResizeObserver {
      observe() {}
      disconnect() {}
    },
    _events: events
  };
}

function installRafSpy() {
  const previousRaf = global.requestAnimationFrame;
  const previousCancel = global.cancelAnimationFrame;
  const scheduled = [];
  let nextId = 1;

  global.requestAnimationFrame = (fn) => {
    const id = nextId++;
    scheduled.push({ id, fn });
    return id;
  };
  global.cancelAnimationFrame = (id) => {
    const index = scheduled.findIndex(item => item.id === id);
    if (index !== -1) scheduled.splice(index, 1);
  };

  return {
    get count() {
      return scheduled.length;
    },
    reset() {
      scheduled.length = 0;
    },
    restore() {
      global.requestAnimationFrame = previousRaf;
      global.cancelAnimationFrame = previousCancel;
    }
  };
}

function clearPendingFrame(instance, rafSpy) {
  if (instance._animationFrameId !== null && instance._animationFrameId !== undefined) {
    cancelAnimationFrame(instance._animationFrameId);
    instance._animationFrameId = null;
  }
  rafSpy.reset();
}

// =============================================================================
// FlowCanvas tests (module-level helpers only, no full instantiation in Node)
// =============================================================================

test('FlowCanvas portKey helper produces stable identifiers', async () => {
  // portKey is module-level, but we can't easily import it since it's not exported.
  // Instead we test the connection indexing behavior via a lightweight instantiation.
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');

  // Add two nodes
  const n1 = fc.addNode('ImageAcquisition', 0, 0, {
    inputs: [{ id: 'i1', name: 'in', type: 'Image' }],
    outputs: [{ id: 'o1', name: 'out', type: 'Image' }]
  });
  const n2 = fc.addNode('Filtering', 200, 0, {
    inputs: [{ id: 'i2', name: 'in', type: 'Image' }],
    outputs: [{ id: 'o2', name: 'out', type: 'Image' }]
  });

  // Add connection
  const conn = fc.addConnection(n1.id, 0, n2.id, 0);
  assert.ok(conn, 'connection should be created');

  // O(1) lookup by output port
  const foundOutput = fc.getConnectionsAtPort(n1.id, 0, true);
  assert.equal(foundOutput.length, 1);
  assert.equal(foundOutput[0].id, conn.id);

  // O(1) lookup by input port
  const foundInput = fc.getConnectionAtPort(n2.id, 0, false);
  assert.ok(foundInput);
  assert.equal(foundInput.id, conn.id);

  // Remove connection should clean indices
  assert.equal(fc.removeConnection(conn.id), true);
  assert.equal(fc.getConnectionsAtPort(n1.id, 0, true).length, 0);
  assert.equal(fc.getConnectionAtPort(n2.id, 0, false), null);
  assert.equal(fc.removeConnection(conn.id), false);

  fc.destroy();
});

test('FlowCanvas truncates long node titles without compressing or overflowing the header', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  assert.equal(fc.fitTextWithEllipsis('短标题', 100), '短标题');
  assert.equal(fc.fitTextWithEllipsis('上料工位瓶盖外观与密封完整性综合检测节点', 42), '上料工位瓶盖…');
  assert.equal(fc.fitTextWithEllipsis('标题', 4), '');
  fc.destroy();
});

test('FlowCanvas removeNode preserves _systemNode and clears selectedConnection', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');

  const n1 = fc.addNode('ImageAcquisition', 0, 0, {
    inputs: [],
    outputs: [{ id: 'o1', name: 'out', type: 'Image' }]
  });
  const n2 = fc.addNode('ResultOutput', 200, 0, {
    inputs: [{ id: 'i2', name: 'in', type: 'Image' }],
    outputs: []
  });

  const conn = fc.addConnection(n1.id, 0, n2.id, 0);
  fc.selectedConnection = conn;

  // Simulate _systemNode flag
  n1._systemNode = true;

  // deleteNode should delegate to removeNode and protect system nodes
  assert.equal(fc.deleteNode(n1.id), false);
  assert.ok(fc.nodes.has(n1.id), 'system node should not be removed');

  // removeNode on non-system node should also clear selectedConnection if affected
  assert.equal(fc.removeNode(n2.id), true);
  assert.equal(fc.selectedConnection, null, 'selectedConnection should be cleared when its target is removed');
  assert.equal(fc.removeNode(n2.id), false);

  fc.destroy();
});

test('FlowCanvas requestSelectionDelete delegates selection deletion when provided', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const node = fc.addNode('ImageAcquisition', 0, 0, {
    inputs: [],
    outputs: [{ id: 'o1', name: 'out', type: 'Image' }]
  });
  const payloads = [];

  fc.selectedNode = node.id;
  fc.onSelectionDeleteRequested = payload => {
    payloads.push(payload);
    return true;
  };

  assert.equal(fc.requestSelectionDelete('context-menu-node'), true);
  assert.equal(payloads.length, 1);
  assert.equal(payloads[0].reason, 'context-menu-node');
  assert.equal(payloads[0].selectedNode, node.id);
  assert.equal(fc.nodes.has(node.id), true, 'delegate owns the actual deletion');

  fc.destroy();
});

test('FlowCanvas requestNodeDuplicate delegates node duplication when provided', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const node = fc.addNode('ImageAcquisition', 0, 0, {
    inputs: [],
    outputs: [{ id: 'o1', name: 'out', type: 'Image' }]
  });
  const payloads = [];

  fc.onNodeDuplicateRequested = payload => {
    payloads.push(payload);
    return true;
  };

  assert.equal(fc.requestNodeDuplicate(node.id, 'context-menu-node'), true);
  assert.equal(payloads.length, 1);
  assert.equal(payloads[0].reason, 'context-menu-node');
  assert.equal(payloads[0].nodeId, node.id);
  assert.equal(fc.nodes.size, 1, 'delegate owns the actual duplication');

  fc.destroy();
});

test('FlowCanvas requestNodeDisabledToggle delegates disabled toggle when provided', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const node = fc.addNode('ImageAcquisition', 0, 0, {
    inputs: [],
    outputs: [{ id: 'o1', name: 'out', type: 'Image' }]
  });
  const payloads = [];

  fc.onNodeDisabledToggleRequested = payload => {
    payloads.push(payload);
    return true;
  };
  const disabledBefore = node.disabled;

  assert.equal(fc.requestNodeDisabledToggle(node.id, 'context-menu-node'), true);
  assert.equal(payloads.length, 1);
  assert.equal(payloads[0].reason, 'context-menu-node');
  assert.equal(payloads[0].nodeId, node.id);
  assert.equal(node.disabled, disabledBefore, 'delegate owns the actual disabled-state change');

  fc.destroy();
});

test('FlowCanvas addConnection rejects occupied inputs and cycles', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');

  const source = fc.addNode('ImageAcquisition', 0, 0, {
    inputs: [{ id: 'source-in', name: 'in', type: 'Image' }],
    outputs: [{ id: 'source-out', name: 'out', type: 'Image' }]
  });
  const target = fc.addNode('Filtering', 200, 0, {
    inputs: [{ id: 'target-in', name: 'in', type: 'Image' }],
    outputs: [{ id: 'target-out', name: 'out', type: 'Image' }]
  });
  const otherSource = fc.addNode('ImageAcquisition', 400, 0, {
    inputs: [],
    outputs: [{ id: 'other-out', name: 'out', type: 'Image' }]
  });

  const first = fc.addConnection(source.id, 0, target.id, 0);
  assert.ok(first);

  assert.equal(
    fc.addConnection(otherSource.id, 0, target.id, 0),
    null,
    'a target input port should accept only one incoming connection'
  );
  assert.equal(
    fc.addConnection(target.id, 0, source.id, 0),
    null,
    'adding a reverse edge should not be allowed to create a cycle'
  );

  assert.equal(fc.connections.length, 1);
  fc.destroy();
});

test('FlowCanvas serialize does not throw when DEBUG_FLOW_CANVAS is false', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const n1 = fc.addNode('ImageAcquisition', 10, 20, {
    inputs: [],
    outputs: [{ id: 'o1', name: 'out', type: 'Image' }]
  });
  const n2 = fc.addNode('ResultOutput', 200, 200, {
    inputs: [{ id: 'i2', name: 'in', type: 'Image', isRequired: true }],
    outputs: []
  });
  fc.addConnection(n1.id, 0, n2.id, 0);

  const result = fc.serialize();
  assert.ok(Array.isArray(result.operators));
  assert.equal(result.operators.length, 2);
  assert.ok(Array.isArray(result.connections));
  assert.equal(result.connections.length, 1);
  assert.equal(result.connections[0].sourceOperatorId, n1.id);
  assert.equal(result.operators[1].inputPorts[0].isRequired, true);
  assert.equal(result.operators[0].outputPorts[0].isRequired, false);

  fc.destroy();
});

test('FlowCanvas deserialize rebuilds connection index', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');

  const data = {
    operators: [
      { id: 'op1', name: 'Src', type: 'ImageAcquisition', x: 0, y: 0, inputPorts: [], outputPorts: [{ id: 'p1', name: 'out', dataType: 'Image' }] },
      { id: 'op2', name: 'Dst', type: 'ResultOutput', x: 200, y: 0, inputPorts: [{ id: 'p2', name: 'in', dataType: 'Image' }], outputPorts: [] }
    ],
    connections: [
      { id: 'c1', sourceOperatorId: 'op1', sourcePortId: 'p1', targetOperatorId: 'op2', targetPortId: 'p2' }
    ]
  };

  fc.deserialize(data);

  // After deserialize, O(1) lookup must work
  const conn = fc.getConnectionAtPort('op2', 0, false);
  assert.ok(conn, 'connection index should be rebuilt after deserialize');
  assert.equal(conn.id, 'c1');

  fc.destroy();
});

test('FlowCanvas preserves agent temp id metadata through title edits', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  fc.deserialize({
    operators: [
      {
        id: 'op_1',
        name: '图像采集',
        type: 'ImageAcquisition',
        x: 0,
        y: 0,
        metadata: { agentTempId: 'op_1' },
        inputPorts: [],
        outputPorts: [{ id: 'p1', name: 'Image', dataType: 'Image' }]
      }
    ],
    connections: []
  });

  const node = fc.nodes.get('op_1');
  assert.equal(node.title, '图像采集');
  assert.equal(node.metadata.agentTempId, 'op_1');

  node.title = '用户自定义名称';
  const serialized = fc.serialize();
  assert.equal(serialized.operators[0].name, '用户自定义名称');
  assert.equal(serialized.operators[0].metadata.agentTempId, 'op_1');
  assert.doesNotMatch(JSON.stringify({ name: serialized.operators[0].name }), /\bop_1\b/);

  fc.destroy();
});

test('FlowCanvas persists disabled node state through serialize and deserialize', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const node = fc.addNode('ImageAcquisition', 0, 0, {
    inputs: [],
    outputs: [{ id: 'out', name: 'Image', type: 'Image' }]
  });

  fc.toggleNodeDisabled(node.id);

  const serialized = fc.serialize();
  assert.equal(serialized.operators[0].isEnabled, false);

  const restored = new FlowCanvas('canvas');
  restored.deserialize(serialized);
  assert.equal(restored.nodes.get(node.id).disabled, true);

  fc.destroy();
  restored.destroy();
});

test('FlowCanvas persists canonical final decision configuration through serialize and deserialize', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);
  const configuration = {
    finalDecisionBinding: {
      sourceOperatorId: 'op-decision',
      sourceOutputPortId: 'port-decision',
      sourceOutputName: 'JudgmentResult',
      dataType: 'String',
      rule: 'StringMap',
      okValue: 'OK',
      ngValue: 'NG'
    },
    missingDecisionPolicy: 'Invalid'
  };

  const fc = new FlowCanvas('canvas');
  fc.deserialize({ operators: [], connections: [], decisionConfiguration: configuration });
  const serialized = fc.serialize();
  assert.deepEqual(serialized.decisionConfiguration, configuration);

  serialized.decisionConfiguration.finalDecisionBinding.okValue = 'PASS';
  assert.equal(fc.decisionConfiguration.finalDecisionBinding.okValue, 'OK', 'serialize should return an isolated DTO copy');

  const restored = new FlowCanvas('canvas');
  restored.deserialize(fc.serialize());
  assert.deepEqual(restored.decisionConfiguration, configuration);

  fc.destroy();
  restored.destroy();
});

test('buildOperatorNodeConfig → addNode → serialize preserves explicit-empty input ports', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );
  const { buildOperatorNodeConfig } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/operatorVisuals.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');

  // RectangleRegion：metadata 明确声明 inputPorts: []，是真正的无输入源头算子。
  const config = buildOperatorNodeConfig('RectangleRegion', {
    type: 'RectangleRegion',
    displayName: '矩形框定义',
    inputPorts: [],
    outputPorts: [{ name: 'Rectangle', displayName: '矩形', dataType: 'Rectangle' }]
  });

  // 画布节点：不得凭空多出输入端口。
  const node = fc.addNode('RectangleRegion', 0, 0, config);
  assert.equal(node.inputs.length, 0, '画布节点不得伪造输入端口');
  assert.equal(node.outputs.length, 1);

  // serialize 后 inputPorts 仍为空。
  const serialized = fc.serialize();
  assert.equal(serialized.operators.length, 1);
  assert.equal(serialized.operators[0].inputPorts.length, 0, 'serialize 后 inputPorts 仍为空');
  assert.equal(serialized.operators[0].outputPorts.length, 1);

  // 反序列化回来仍为空，且不破坏旧流程兼容。
  const restored = new FlowCanvas('canvas');
  restored.deserialize(serialized);
  const restoredNode = restored.nodes.get(node.id);
  assert.equal(restoredNode.inputs.length, 0, '反序列化后 inputPorts 仍为空');
  assert.equal(restoredNode.outputs.length, 1);

  fc.destroy();
  restored.destroy();
});

test('buildOperatorNodeConfig → addNode → serialize keeps ImageAcquisition real ports intact', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );
  const { buildOperatorNodeConfig } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/operatorVisuals.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');

  // ImageAcquisition 的真实后端契约：2 个输入端口（Image/FilePath）+ 1 个图像输出端口。
  const config = buildOperatorNodeConfig('ImageAcquisition', {
    type: 'ImageAcquisition',
    displayName: '图像采集',
    inputPorts: [
      { name: 'Image', displayName: 'Runtime supplied image', dataType: 'Image', isRequired: false },
      { name: 'FilePath', displayName: '文件路径输入', dataType: 'String', isRequired: false }
    ],
    outputPorts: [{ name: 'Image', displayName: '图像', dataType: 'Image' }]
  });

  const node = fc.addNode('ImageAcquisition', 0, 0, config);
  assert.equal(node.inputs.length, 2, 'ImageAcquisition 保留其真实声明的 2 个输入端口');
  assert.deepEqual(node.inputs.map(p => p.name), ['Image', 'FilePath']);

  const serialized = fc.serialize();
  assert.equal(serialized.operators[0].inputPorts.length, 2, 'serialize 后保留 2 个输入端口');
  assert.deepEqual(serialized.operators[0].inputPorts.map(p => p.name), ['Image', 'FilePath']);
  assert.equal(serialized.operators[0].outputPorts.length, 1);

  fc.destroy();
});

test('FlowCanvas handleMouseMove skips redraws when hover state does not change', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const rafSpy = installRafSpy();
  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  try {
    const fc = new FlowCanvas('canvas');
    clearPendingFrame(fc, rafSpy);

    let invalidations = 0;
    fc.invalidate = () => {
      invalidations += 1;
    };

    fc.handleMouseMove({ clientX: 100, clientY: 120 });
    fc.handleMouseMove({ clientX: 100, clientY: 120 });

    assert.equal(invalidations, 1);

    fc.destroy();
  } finally {
    rafSpy.restore();
  }
});

test('FlowCanvas node drag commits one moveNode revision and adapter snapshot stays consistent', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );
  const { FlowCanvasAdapter } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const adapter = new FlowCanvasAdapter(fc);
  const node = fc.addNode('ImageAcquisition', 20, 20, {
    id: 'node-a',
    inputs: [],
    outputs: []
  });
  const structureEvents = [];
  fc.subscribeStructureState(event => {
    structureEvents.push(event);
  });
  structureEvents.length = 0;
  const beforeRevision = fc.getFlowRevision();

  fc.handleMouseDown({
    clientX: node.x + 10,
    clientY: node.y + 10,
    button: 0
  });
  fc.handleMouseMove({
    clientX: node.x + 86,
    clientY: node.y + 53
  });
  fc.handleMouseMove({
    clientX: node.x + 93,
    clientY: node.y + 61
  });
  fc.handleMouseUp();

  const moveEvents = structureEvents.filter(event => event.reason === 'moveNode');
  const snapshot = adapter.getSnapshot();
  const serializedNode = snapshot.flow.operators.find(operator => operator.id === 'node-a');

  assert.equal(fc.getFlowRevision(), beforeRevision + 1);
  assert.equal(moveEvents.length, 1);
  assert.equal(snapshot.flowRevision, fc.getFlowRevision());
  assert.ok(serializedNode, 'dragged node should be present in serialized flow');
  assert.equal(serializedNode.x, node.x);
  assert.equal(serializedNode.y, node.y);
  assert.notEqual(node.x, 20);
  assert.notEqual(node.y, 20);

  fc.destroy();
});

test('FlowCanvas click without node movement does not advance flow revision', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const node = fc.addNode('ImageAcquisition', 20, 20, {
    id: 'node-a',
    inputs: [],
    outputs: []
  });
  const structureEvents = [];
  fc.subscribeStructureState(event => {
    structureEvents.push(event);
  });
  structureEvents.length = 0;
  const beforeRevision = fc.getFlowRevision();

  fc.handleMouseDown({
    clientX: node.x + 10,
    clientY: node.y + 10,
    button: 0
  });
  fc.handleMouseUp();

  assert.equal(fc.getFlowRevision(), beforeRevision);
  assert.equal(structureEvents.some(event => event.reason === 'moveNode'), false);
  assert.equal(node.x, 20);
  assert.equal(node.y, 20);

  fc.destroy();
});

test('FlowCanvas expands node height for multi-port operators', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const ports = Array.from({ length: 5 }, (_, index) => ({
    id: `in-${index}`,
    name: `Input ${index + 1}`,
    type: 'Image'
  }));

  const node = fc.addNode('RegionUnion', 0, 0, {
    inputs: ports,
    outputs: [{ id: 'out-1', name: 'Region', type: 'Region' }]
  });

  assert.ok(node.height > 60, 'multi-port node should be taller than the legacy fixed height');

  const firstPort = fc.getPortPosition(node.id, 0, false);
  const lastPort = fc.getPortPosition(node.id, ports.length - 1, false);
  assert.ok(firstPort.y > 24, 'first port should render below the title bar');
  assert.ok(lastPort.y < node.height, 'last port should stay inside the node body');
  assert.ok(lastPort.y - firstPort.y >= 60, 'ports should have enough vertical spread to avoid collapsing');

  fc.destroy();
});

test('FlowCanvas deserialize expands saved multi-port nodes and keeps port hit testing aligned', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  const inputPorts = Array.from({ length: 4 }, (_, index) => ({
    id: `p-${index}`,
    name: `Input ${index + 1}`,
    dataType: 'Region'
  }));

  fc.deserialize({
    operators: [
      {
        id: 'region-union',
        name: 'RegionUnion',
        type: 'RegionUnion',
        x: 100,
        y: 80,
        height: 60,
        inputPorts,
        outputPorts: [{ id: 'out', name: 'Region', dataType: 'Region' }]
      }
    ],
    connections: []
  });

  const node = fc.nodes.get('region-union');
  assert.ok(node.height > 60, 'deserialize should expand legacy saved height for multi-port nodes');

  const port = fc.getPortPosition(node.id, inputPorts.length - 1, false);
  const hit = fc.getPortAt(node.x, port.y);
  assert.deepEqual(hit, { nodeId: node.id, portIndex: inputPorts.length - 1, isOutput: false });

  fc.destroy();
});

test('FlowCanvas interactive state changes schedule redraws', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const rafSpy = installRafSpy();
  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  try {
    const fc = new FlowCanvas('canvas');
    const n1 = fc.addNode('ImageAcquisition', 0, 0, {
      inputs: [],
      outputs: [{ id: 'o1', name: 'out', type: 'Image' }]
    });
    const n2 = fc.addNode('ResultOutput', 200, 0, {
      inputs: [{ id: 'i2', name: 'in', type: 'Image' }],
      outputs: []
    });

    clearPendingFrame(fc, rafSpy);
    fc.startConnection(n1.id, 0);
    assert.equal(rafSpy.count, 1, 'starting a temp connection should schedule a redraw');

    clearPendingFrame(fc, rafSpy);
    fc.handleMouseMove({ clientX: 320, clientY: 20 });
    assert.equal(rafSpy.count, 1, 'moving a temp connection should schedule a redraw');

    fc.cancelConnection();
    clearPendingFrame(fc, rafSpy);
    fc.draggedNode = n2.id;
    fc.dragOffset = { x: 0, y: 0 };
    fc.handleMouseMove({ clientX: 260, clientY: 40 });
    assert.equal(rafSpy.count, 1, 'dragging a node should schedule a redraw');

    fc.draggedNode = null;
    clearPendingFrame(fc, rafSpy);
    fc.handleWheel({ deltaY: -1, clientX: 100, clientY: 100, preventDefault() {} });
    assert.equal(rafSpy.count, 1, 'zooming the flow canvas should schedule a redraw');

    fc.destroy();
  } finally {
    rafSpy.restore();
  }
});

test('FlowCanvas draws global variable dependency badges from project schema', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  const labels = [];
  const ctx = canvas.getContext();
  ctx.fillText = text => labels.push(String(text));
  canvas.getContext = () => ctx;
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const fc = new FlowCanvas('canvas');
  try {
    fc.setGlobalVariableSchema({
      sourceBindings: [{ operatorId: 'node-1' }],
      targetBindings: [{ operatorId: 'node-1' }, { operatorId: 'node-1' }]
    });

    fc.drawGlobalVariableBadges({ id: 'node-1' }, 0, 0, 140);

    assert.ok(labels.includes('G^1'));
    assert.ok(labels.includes('Gv2'));
  } finally {
    fc.destroy();
  }
});

// =============================================================================
// lintPanel XSS-safe rendering
// =============================================================================

test('lintPanel render uses textContent for issue fields', async () => {
  const { default: LintPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/lintPanel.js'
  );

  global.document = createMockDocument(createMockCanvas());
  const container = document.createElement('div');

  const panel = new LintPanel('lint-panel');
  // Inject container manually since getElementById may have returned a different mock
  panel.container = container;

  panel.update([
    {
      code: 'E001',
      severity: 'Error',
      message: '<script>alert(1)</script>',
      suggestion: 'Use \"quotes\" and \'apostrophes\'',
      operatorName: '<b>evil</b>'
    }
  ]);
  panel.isCollapsed = false;
  panel.render();

  const list = container.querySelector('.lint-panel__list');
  assert.ok(list, 'list should be rendered');
  const items = list.querySelectorAll('.lint-panel__item');
  assert.equal(items.length, 1);

  const html = list.innerHTML;
  assert.ok(!html.includes('<script>'), 'message should not be rendered as HTML');
  assert.ok(!html.includes('<b>evil</b>'), 'operatorName should not be rendered as HTML');
});

// =============================================================================
// flowCanvasAdapter payload slimming
// =============================================================================

test('flowCanvasAdapter emitFlowChanged omits flow snapshot', async () => {
  const { FlowCanvasAdapter } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js'
  );

  const emitted = [];
  const eventBus = {
    emit(name, payload) {
      emitted.push({ name, payload });
    }
  };

  const mockFlowCanvas = {
    serialize() { return { nodes: [], edges: [] }; },
    getFlowRevision() { return 42; }
  };

  const adapter = new FlowCanvasAdapter(mockFlowCanvas, { eventBus });
  adapter.emitFlowChanged('test');

  assert.equal(emitted.length, 1);
  assert.equal(emitted[0].name, 'flow:changed');
  assert.equal(emitted[0].payload.revision, 42);
  assert.equal(emitted[0].payload.reason, 'test');
  assert.equal('flow' in emitted[0].payload, false, 'payload should not contain a flow snapshot');
});

test('flowCanvasAdapter snapshots are deep cloned and parameter patches are atomic', async () => {
  const { FlowCanvasAdapter } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js'
  );

  let flowRevision = 1;
  let selectionRevision = 1;
  let renderCount = 0;
  const node = {
    id: 'node-a',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [
      { name: 'Threshold', displayName: 'Threshold', value: 10, dataType: 'int' },
      { Name: 'Gain', DisplayName: 'Gain', Value: 2, DataType: 'float' }
    ]
  };
  const mockFlowCanvas = {
    nodes: new Map([['node-a', node]]),
    connections: [],
    selectedNode: 'node-a',
    selectedConnection: null,
    serialize() {
      return { operators: [node], connections: [] };
    },
    getFlowRevision() {
      return flowRevision;
    },
    getSelectionState() {
      return {
        flowRevision,
        selectionRevision,
        selectedNodeId: this.selectedNode,
        selectedConnectionId: null
      };
    },
    render() {
      renderCount += 1;
    },
    markFlowStructureChanged() {
      flowRevision += 1;
    },
    markSelectionChanged() {
      selectionRevision += 1;
    }
  };

  const adapter = new FlowCanvasAdapter(mockFlowCanvas);
  const snapshot = adapter.getSnapshot();
  snapshot.flow.operators[0].parameters[0].value = 999;
  snapshot.selectedNode.parameters[0].value = 888;

  assert.equal(node.parameters[0].value, 10, 'snapshot mutation should not touch the real node');

  const rejected = adapter.patchNodeParameters('node-a', {
    Threshold: 20,
    Missing: 30
  });
  assert.deepEqual(rejected, {
    updated: false,
    reason: 'parameter_not_found',
    missingParameters: ['Missing']
  });
  assert.equal(node.parameters[0].value, 10, 'rejected patch should not partially mutate existing parameters');

  const accepted = adapter.patchNodeParameters('node-a', {
    threshold: 21,
    gain: 3
  });
  assert.deepEqual(accepted, {
    updated: true,
    reason: 'updated',
    missingParameters: []
  });
  assert.equal(node.parameters[0].value, 21);
  assert.equal(node.parameters[1].Value, 3);
  assert.equal(flowRevision, 2);
  assert.equal(selectionRevision, 2);
  assert.equal(renderCount, 1);
});

test('createHostedFlowCanvasAdapter owns one FlowCanvas per canvas id and destroys it on dispose', async () => {
  const { createHostedFlowCanvasAdapter } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const first = createHostedFlowCanvasAdapter('studio2-hosted-canvas', {
    eventBus: { emit() {} }
  });
  const second = createHostedFlowCanvasAdapter('studio2-hosted-canvas', {
    eventBus: { emit() {} }
  });

  assert.equal(second, first, 'same canvas id should reuse the hosted adapter');
  assert.equal(first.getViewState().scale, 1);
  assert.equal(canvas._events.mousedown.length, 1);

  first.dispose();

  assert.equal(canvas._events.mousedown.length, 0, 'dispose should call FlowCanvas.destroy');

  const third = createHostedFlowCanvasAdapter('studio2-hosted-canvas', {
    eventBus: { emit() {} }
  });

  assert.notEqual(third, first, 'new workspace lifecycle can create a fresh adapter after dispose');

  third.dispose();
});

test('createHostedFlowCanvasAdapter ignores stale dispose from an old hosted instance', async () => {
  const { createHostedFlowCanvasAdapter } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const first = createHostedFlowCanvasAdapter('studio2-stale-dispose-canvas', {
    eventBus: { emit() {} }
  });
  first.dispose();

  const second = createHostedFlowCanvasAdapter('studio2-stale-dispose-canvas', {
    eventBus: { emit() {} }
  });
  assert.notEqual(second, first, 'disposed hosted instance should allow a new adapter');
  assert.equal(canvas._events.mousedown.length, 1);

  first.dispose();
  const third = createHostedFlowCanvasAdapter('studio2-stale-dispose-canvas', {
    eventBus: { emit() {} }
  });

  assert.equal(third, second, 'stale dispose from the first instance must not evict the current adapter');
  assert.equal(canvas._events.mousedown.length, 1, 'stale dispose must not create a second FlowCanvas');

  second.dispose();
});

test('FlowCanvas destroy cancels deferred menu listeners and global pointer release', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
  );

  const canvas = createMockCanvas();
  const documentMock = createMockDocument(canvas);
  const windowMock = createMockWindow(canvas);
  global.document = documentMock;
  global.window = windowMock;

  const fc = new FlowCanvas('canvas');
  const node = fc.addNode('Thresholding', 20, 20, {
    inputs: [{ id: 'in-image', name: 'Image', type: 'Image' }],
    outputs: [{ id: 'out-image', name: 'Image', type: 'Image' }]
  });
  fc.showNodeContextMenu(20, 20, node.id);
  fc.destroy();
  fc.destroy();

  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(documentMock._events.click?.length ?? 0, 0);
  assert.equal(windowMock._events.mouseup?.length ?? 0, 0);
});

// =============================================================================
// ImageCanvas blob URL cleanup
// =============================================================================

test('ImageCanvas revokes blob URL on destroy', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);
  global.URL = {
    createObjectURL(blob) {
      return 'blob:mock-url/' + Math.random().toString(36).slice(2);
    },
    revokeObjectURL(url) {
      this._lastRevoked = url;
    },
    _lastRevoked: null
  };

  const ic = new ImageCanvas('canvas');

  const blob = new Blob(['fake-image']);
  await ic.loadImage(blob);

  const trackedUrl = ic._imageUrlToRevoke;
  assert.ok(trackedUrl, 'blob URL should be tracked after load');
  assert.equal(global.URL._lastRevoked, null, 'URL should not be revoked before destroy');

  ic.destroy();
  assert.equal(global.URL._lastRevoked, trackedUrl, 'blob URL should be revoked on destroy');
});

test('ImageCanvas pan, zoom, and ROI edits schedule redraws', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const rafSpy = installRafSpy();
  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  try {
    const ic = new ImageCanvas('canvas');
    ic.image = { width: 400, height: 300 };

    clearPendingFrame(ic, rafSpy);
    ic.isDragging = true;
    ic.lastMouse = { x: 10, y: 10 };
    ic.handleMouseMove({ clientX: 30, clientY: 40 });
    assert.equal(rafSpy.count, 1, 'panning the image canvas should schedule a redraw');

    clearPendingFrame(ic, rafSpy);
    ic.handleWheel({ deltaY: -1, clientX: 100, clientY: 100, preventDefault() {} });
    assert.equal(rafSpy.count, 1, 'zooming the image canvas should schedule a redraw');

    clearPendingFrame(ic, rafSpy);
    ic.interactionMode = 'roi-rect';
    ic.interactionState = {
      type: 'draw',
      overlayId: 'roi-1',
      startPoint: { x: 10, y: 10 }
    };
    ic.overlays = [{ id: 'roi-1', x: 10, y: 10, width: 20, height: 20, editable: true }];
    ic.handleRoiMouseMove({ clientX: 80, clientY: 90 });
    assert.equal(rafSpy.count, 1, 'editing an ROI should schedule a redraw');

    clearPendingFrame(ic, rafSpy);
    ic.interactionState = {
      type: 'pan',
      startCanvasPoint: { x: 10, y: 10 },
      startOffset: { x: 0, y: 0 }
    };
    ic.handleRoiMouseMove({ clientX: 40, clientY: 45 });
    assert.equal(rafSpy.count, 1, 'right-button ROI pan should schedule a redraw');

    ic.destroy();
  } finally {
    rafSpy.restore();
  }
});

test('ImageCanvas refits host resizes only while the view remains in fit mode', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const rafSpy = installRafSpy();
  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  try {
    const viewChanges = [];
    const ic = new ImageCanvas('canvas', { onViewChanged: view => viewChanges.push(view) });
    ic.image = { width: 100, height: 100 };
    ic.resetView();

    canvas.parentElement.clientWidth = 317;
    canvas.parentElement.clientHeight = 231;
    ic.resize();

    assert.equal(ic._viewMode, 'fit');
    assert.ok(Math.abs(ic.scale - 2.079) < 1e-9, 'fit scale should follow the resized logical host');
    assert.ok(Math.abs(ic.offset.x - 54.55) < 1e-9);
    assert.ok(Math.abs(ic.offset.y - 11.55) < 1e-9);
    assert.deepEqual(viewChanges.at(-1), ic.getViewState(), 'fit resize should publish the canonical view');

    ic.setViewState({ scale: 1.5, offset: { x: 7, y: 9 } });
    canvas.parentElement.clientWidth = 500;
    canvas.parentElement.clientHeight = 400;
    ic.resize();

    assert.equal(ic._viewMode, 'custom');
    assert.deepEqual(ic.getViewState(), { scale: 1.5, offset: { x: 7, y: 9 } });
    assert.deepEqual(viewChanges.at(-1), ic.getViewState(), 'explicit view changes should be published');

    ic.fitToScreen();
    assert.equal(ic._viewMode, 'fit');
    assert.equal(ic.scale, 3.6);

    ic.actualSize();
    const actualView = ic.getViewState();
    canvas.parentElement.clientWidth = 640;
    canvas.parentElement.clientHeight = 480;
    ic.resize();

    assert.equal(ic._viewMode, 'actual');
    assert.deepEqual(ic.getViewState(), actualView, 'actual-size view should retain its explicit position');

    ic.destroy();
  } finally {
    rafSpy.restore();
  }
});

test('ImageCanvas isolates synchronous and asynchronous view observer failures', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const rafSpy = installRafSpy();
  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);
  const originalConsoleError = console.error;
  const errors = [];
  console.error = (...args) => errors.push(args);

  try {
    const syncError = new Error('sync observer failure');
    const syncCanvas = new ImageCanvas('canvas', { onViewChanged: () => { throw syncError; } });
    assert.doesNotThrow(() => syncCanvas.setViewState({ scale: 1.25, offset: { x: 4, y: 6 } }));
    assert.deepEqual(syncCanvas.getViewState(), { scale: 1.25, offset: { x: 4, y: 6 } });
    assert.equal(errors.length, 1);
    assert.equal(errors[0]?.[1], syncError);
    syncCanvas.destroy();

    const asyncError = new Error('async observer failure');
    const asyncCanvas = new ImageCanvas('canvas', {
      onViewChanged: () => Promise.reject(asyncError)
    });
    assert.doesNotThrow(() => asyncCanvas.setViewState({ scale: 1.5, offset: { x: 7, y: 9 } }));
    await Promise.resolve();
    await Promise.resolve();
    assert.equal(errors.length, 2);
    assert.equal(errors[1]?.[1], asyncError);

    asyncCanvas.destroy();
    assert.doesNotThrow(() => asyncCanvas.notifyViewChanged());
    assert.equal(errors.length, 2, 'destroyed canvases must not notify observers');
  } finally {
    console.error = originalConsoleError;
    rafSpy.restore();
  }
});

test('ImageCanvas blank click clears selection and schedules redraw', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const rafSpy = installRafSpy();
  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  try {
    const ic = new ImageCanvas('canvas');
    ic.image = { width: 400, height: 300 };
    ic.overlays = [{ id: 'roi-1', x: 10, y: 10, width: 20, height: 20, editable: true }];
    ic.selectedOverlay = 'roi-1';

    clearPendingFrame(ic, rafSpy);
    ic.handleMouseDown({ button: 0, clientX: 200, clientY: 200 });

    assert.equal(ic.selectedOverlay, null, 'blank click should clear selected overlay');
    assert.equal(rafSpy.count, 1, 'blank click selection clear should schedule a redraw');

    ic.destroy();
  } finally {
    rafSpy.restore();
  }
});

test('ImageCanvas clear releases image resources and redraws empty canvas', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const rafSpy = installRafSpy();
  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);
  global.URL = {
    createObjectURL() {
      return 'blob:mock-url/clear-test';
    },
    revokeObjectURL(url) {
      this._lastRevoked = url;
    },
    _lastRevoked: null
  };

  try {
    const viewChanges = [];
    const ic = new ImageCanvas('canvas', { onViewChanged: view => viewChanges.push(view) });
    const blob = new Blob(['fake-image']);
    await ic.loadImage(blob);
    const trackedUrl = ic._imageUrlToRevoke;

    ic.overlays = [{ id: 'roi-1', x: 10, y: 10, width: 20, height: 20, editable: true }];
    ic.selectedOverlay = 'roi-1';
    ic.activeOverlayId = 'roi-1';
    ic.interactionState = { type: 'draw', overlayId: 'roi-1', startPoint: { x: 10, y: 10 } };
    ic.activeHandle = 'se';
    ic._pendingResetView = true;
    ic.setViewState({ scale: 2, offset: { x: 30, y: 40 } });

    clearPendingFrame(ic, rafSpy);
    ic.clear();

    assert.equal(ic.image, null, 'clear should remove the current image');
    assert.equal(ic.overlays.length, 0, 'clear should remove overlays');
    assert.equal(ic.selectedOverlay, null, 'clear should reset selected overlay');
    assert.equal(ic.activeOverlayId, null, 'clear should reset active overlay');
    assert.equal(ic.interactionState, null, 'clear should cancel active ROI interaction');
    assert.equal(ic.activeHandle, null, 'clear should reset active handle');
    assert.equal(ic._pendingResetView, false, 'clear should not leave a pending reset for a removed image');
    assert.deepEqual(ic.getViewState(), { scale: 1, offset: { x: 0, y: 0 } }, 'clear should reset the empty view');
    assert.deepEqual(viewChanges.at(-1), ic.getViewState(), 'clear should publish the reset view');
    assert.equal(global.URL._lastRevoked, trackedUrl, 'clear should revoke the tracked blob URL');
    assert.equal(rafSpy.count, 1, 'clear should schedule a redraw for the empty canvas');

    ic.destroy();
  } finally {
    rafSpy.restore();
  }
});

test('ImageCanvas cancelAndReleaseActiveInteraction restores ROI geometry without commit', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const canvas = createMockCanvas();
  let releasedPointerId = null;
  canvas.hasPointerCapture = pointerId => pointerId === 42;
  canvas.releasePointerCapture = pointerId => {
    releasedPointerId = pointerId;
  };
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);

  const phases = [];
  const ic = new ImageCanvas('canvas', {
    interactionMode: 'roi-rect',
    onOverlayChanged: (_geometry, phase) => phases.push(phase)
  });
  ic.image = { width: 400, height: 300 };
  ic.overlays = [{ id: 'roi-1', type: 'rectangle', x: 50, y: 60, width: 70, height: 80, editable: true }];
  ic.activeOverlayId = 'roi-1';
  ic.selectedOverlay = 'roi-1';
  ic.resetGeometryDraft({ kind: 'rectangle', x: 50, y: 60, width: 70, height: 80 });
  ic.interactionState = {
    type: 'move',
    overlayId: 'roi-1',
    originalGeometry: { kind: 'rectangle', x: 50, y: 60, width: 70, height: 80 },
    dragAnchor: { x: 50, y: 60 }
  };
  Object.assign(ic.overlays[0], { x: 90, y: 100, width: 70, height: 80 });
  ic.activeHandle = 'center';
  ic.activePointerId = 42;

  ic.clear();

  assert.deepEqual(phases, ['cancel'], 'clear during drag must emit cancel only');
  assert.equal(releasedPointerId, 42, 'active pointer capture should be released');
  assert.equal(ic.activePointerId, null);
  assert.equal(ic.interactionState, null);
  assert.equal(ic.activeHandle, null);
  ic.destroy();
});

test('ImageCanvas ignores late image load completions from stale generations', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
  );

  const originalImage = global.Image;
  const createdImages = [];
  class DeferredImage {
    constructor() {
      this.onload = null;
      this.onerror = null;
      this.width = 100;
      this.height = 100;
      createdImages.push(this);
    }
    set src(value) {
      this._src = value;
    }
    get src() {
      return this._src;
    }
  }

  const canvas = createMockCanvas();
  global.document = createMockDocument(canvas);
  global.window = createMockWindow(canvas);
  global.Image = DeferredImage;

  try {
    const ic = new ImageCanvas('canvas');
    const first = ic.loadImage('first.png');
    const second = ic.loadImage('second.png');

    createdImages[1].width = 222;
    createdImages[1].height = 111;
    createdImages[1].onload();
    await second;
    assert.equal(ic.image, createdImages[1], 'newer image should become authoritative');

    createdImages[0].width = 10;
    createdImages[0].height = 10;
    createdImages[0].onload();
    await first;
    assert.equal(ic.image, createdImages[1], 'late older image should not overwrite the current image');

    ic.destroy();
  } finally {
    global.Image = originalImage;
  }
});
