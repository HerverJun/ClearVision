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
    addEventListener() {},
    removeEventListener() {}
  };
}

function createMockWindow(canvas) {
  return {
    devicePixelRatio: 1,
    addEventListener() {},
    removeEventListener() {},
    requestAnimationFrame(fn) {
      return setTimeout(fn, 16);
    },
    cancelAnimationFrame(id) {
      clearTimeout(id);
    },
    ResizeObserver: class MockResizeObserver {
      observe() {}
      disconnect() {}
    }
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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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
  fc.removeConnection(conn.id);
  assert.equal(fc.getConnectionsAtPort(n1.id, 0, true).length, 0);
  assert.equal(fc.getConnectionAtPort(n2.id, 0, false), null);

  fc.destroy();
});

test('FlowCanvas removeNode preserves _systemNode and clears selectedConnection', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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
  fc.deleteNode(n1.id);
  assert.ok(fc.nodes.has(n1.id), 'system node should not be removed');

  // removeNode on non-system node should also clear selectedConnection if affected
  fc.removeNode(n2.id);
  assert.equal(fc.selectedConnection, null, 'selectedConnection should be cleared when its target is removed');

  fc.destroy();
});

test('FlowCanvas addConnection rejects occupied inputs and cycles', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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
    inputs: [{ id: 'i2', name: 'in', type: 'Image' }],
    outputs: []
  });
  fc.addConnection(n1.id, 0, n2.id, 0);

  const result = fc.serialize();
  assert.ok(Array.isArray(result.operators));
  assert.equal(result.operators.length, 2);
  assert.ok(Array.isArray(result.connections));
  assert.equal(result.connections.length, 1);
  assert.equal(result.connections[0].sourceOperatorId, n1.id);

  fc.destroy();
});

test('FlowCanvas deserialize rebuilds connection index', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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

test('FlowCanvas persists disabled node state through serialize and deserialize', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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

test('FlowCanvas expands node height for multi-port operators', async () => {
  const { FlowCanvas } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js'
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

// =============================================================================
// lintPanel XSS-safe rendering
// =============================================================================

test('lintPanel render uses textContent for issue fields', async () => {
  const { default: LintPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/lintPanel.js'
  );

  const container = document.createElement('div');
  global.document = createMockDocument(container);

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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js'
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

// =============================================================================
// ImageCanvas blob URL cleanup
// =============================================================================

test('ImageCanvas revokes blob URL on destroy', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
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

test('ImageCanvas blank click clears selection and schedules redraw', async () => {
  const { ImageCanvas } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
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
    '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js'
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
    const ic = new ImageCanvas('canvas');
    const blob = new Blob(['fake-image']);
    await ic.loadImage(blob);
    const trackedUrl = ic._imageUrlToRevoke;

    ic.overlays = [{ id: 'roi-1', x: 10, y: 10, width: 20, height: 20, editable: true }];
    ic.selectedOverlay = 'roi-1';
    ic.activeOverlayId = 'roi-1';
    ic.interactionState = { type: 'draw', overlayId: 'roi-1', startPoint: { x: 10, y: 10 } };
    ic.activeHandle = 'se';
    ic._pendingResetView = true;

    clearPendingFrame(ic, rafSpy);
    ic.clear();

    assert.equal(ic.image, null, 'clear should remove the current image');
    assert.equal(ic.overlays.length, 0, 'clear should remove overlays');
    assert.equal(ic.selectedOverlay, null, 'clear should reset selected overlay');
    assert.equal(ic.activeOverlayId, null, 'clear should reset active overlay');
    assert.equal(ic.interactionState, null, 'clear should cancel active ROI interaction');
    assert.equal(ic.activeHandle, null, 'clear should reset active handle');
    assert.equal(ic._pendingResetView, false, 'clear should not leave a pending reset for a removed image');
    assert.equal(global.URL._lastRevoked, trackedUrl, 'clear should revoke the tracked blob URL');
    assert.equal(rafSpy.count, 1, 'clear should schedule a redraw for the empty canvas');

    ic.destroy();
  } finally {
    rafSpy.restore();
  }
});
