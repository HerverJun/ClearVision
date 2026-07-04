import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {
  MAX_ARTIFACT_TEXT_DISPLAY_CHARS,
  MAX_ARTIFACT_TEXT_PREVIEW_BYTES,
  NodePreviewInspector,
  buildVisibleObservationRows,
  nodePreviewRendererRegistry,
  searchObservationRows
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewInspector.js';
import {
  createNodePreviewSelectionStore,
  getNodePreviewIdentitySignature
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewSelectionStore.js';

function identity(overrides = {}) {
  return {
    projectId: 'project-1',
    targetNodeId: 'node-1',
    debugSessionId: 'debug-1',
    clientRequestSequence: 7,
    flowRevision: 11,
    ...overrides
  };
}

function node(kind, fields = {}) {
  return {
    kind,
    displayValue: fields.displayValue ?? `${kind} value`,
    originalType: fields.originalType ?? null,
    name: fields.name ?? kind,
    pathHint: fields.pathHint ?? `$["${fields.name ?? kind}"]`,
    addressable: fields.addressable ?? true,
    locatable: fields.locatable ?? false,
    truncated: fields.truncated ?? false,
    outputPortId: fields.outputPortId ?? null,
    outputPortName: fields.outputPortName ?? null,
    resultPathVersion: fields.resultPathVersion ?? null,
    resultPath: fields.resultPath ?? null,
    bindableVariableTypes: fields.bindableVariableTypes ?? [],
    children: fields.children ?? [],
    artifact: fields.artifact ?? null
  };
}

function createFakeElement(tagName) {
  return {
    tagName,
    children: [],
    dataset: {},
    listeners: {},
    style: {
      setProperty() {}
    },
    classList: {
      add() {},
      remove() {}
    },
    appendChild(child) {
      this.children.push(child);
      return child;
    },
    replaceChildren(...children) {
      this.children = children;
    },
    remove() {
      this.removed = true;
    },
    addEventListener(type, handler) {
      this.listeners[type] = handler;
    },
    click() {
      this.listeners.click?.({
        target: this,
        preventDefault() {},
        stopPropagation() {}
      });
    },
    setAttribute() {},
    querySelector() {
      return null;
    }
  };
}

function installFakeDocument() {
  const originalDocument = globalThis.document;
  const fakeBody = createFakeElement('body');
  globalThis.document = {
    body: fakeBody,
    createElement: createFakeElement,
    execCommand() {
      return false;
    }
  };

  return () => {
    if (originalDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = originalDocument;
    }
  };
}

function createInspectorHarness(options = {}) {
  const state = options.state ?? {
    activeNodeId: 'node-1',
    nodeType: 'Thresholding',
    title: 'Threshold',
    status: 'success',
    executionTimeMs: 5,
    observation: {
      identity: identity(),
      detail: node('dictionary', {
        pathHint: '$',
        addressable: false,
        children: []
      })
    },
    artifacts: options.artifacts ?? []
  };
  const calls = [];
  const coordinator = {
    getState: () => options.getState?.() ?? state,
    subscribe: () => () => {},
    readArtifactForCurrentState: async (artifactId, expectedIdentity, readOptions) => {
      calls.push({ artifactId, expectedIdentity, readOptions });
      if (typeof options.readArtifactForCurrentState === 'function') {
        return options.readArtifactForCurrentState(artifactId, expectedIdentity, readOptions);
      }
      return options.readResult;
    }
  };
  const inspector = new NodePreviewInspector(
    createFakeElement('div'),
    {
      subscribeViewState: () => () => {},
      getNodeScreenRect: () => null
    },
    coordinator,
    {
      selectionStore: options.selectionStore ?? null,
      onBindGlobalVariable: options.onBindGlobalVariable ?? undefined
    }
  );

  return { inspector, calls, state };
}

function collectText(element) {
  if (!element) {
    return '';
  }

  const ownText = typeof element.textContent === 'string' ? element.textContent : '';
  return [ownText, ...(element.children || []).map(collectText)].join(' ');
}

function findElement(element, predicate) {
  if (!element) {
    return null;
  }

  if (predicate(element)) {
    return element;
  }

  for (const child of element.children || []) {
    const found = findElement(child, predicate);
    if (found) {
      return found;
    }
  }

  return null;
}

test('NodePreviewInspector renderer registry covers bounded Observation DTO kinds', () => {
  assert.deepEqual(
    nodePreviewRendererRegistry.coverage(),
    [
      'scalar',
      'point',
      'circle',
      'line',
      'rectangle',
      'resource',
      'detectionList',
      'detection',
      'calibrationQuality',
      'container',
      'bounded',
      'unknown'
    ]
  );

  assert.equal(nodePreviewRendererRegistry.render(node('number')).renderer, 'scalar');
  assert.equal(nodePreviewRendererRegistry.render(node('guid')).renderer, 'scalar');
  assert.equal(nodePreviewRendererRegistry.render(node('dateTime')).renderer, 'scalar');
  assert.equal(nodePreviewRendererRegistry.render(node('duration')).renderer, 'scalar');
  assert.equal(nodePreviewRendererRegistry.render(node('nonFiniteNumber')).renderer, 'scalar');
  assert.equal(nodePreviewRendererRegistry.render(node('object')).renderer, 'container');
  assert.equal(nodePreviewRendererRegistry.render(node('detectionList')).renderer, 'detectionList');
  assert.equal(nodePreviewRendererRegistry.render(node('image')).renderer, 'resource');
  assert.equal(nodePreviewRendererRegistry.render(node('pointSet')).renderer, 'resource');
  assert.equal(nodePreviewRendererRegistry.render(node('profile')).renderer, 'resource');
  assert.equal(nodePreviewRendererRegistry.render(node('binary')).renderer, 'resource');
  assert.equal(nodePreviewRendererRegistry.render(node('unknownKind')).renderer, 'unknown');
  assert.equal(
    nodePreviewRendererRegistry.render(node('object', { originalType: 'OpenCvSharp.Point2f' })).label,
    'Point'
  );
  assert.equal(
    nodePreviewRendererRegistry.render(node('object', { originalType: 'CalibrationQuality' })).label,
    'Calibration Quality'
  );
});

test('NodePreviewInspector renderer priority avoids broad substring matches', () => {
  assert.equal(nodePreviewRendererRegistry.render(node('pointSet')).renderer, 'resource');
  assert.equal(nodePreviewRendererRegistry.render(node('DetectionList')).renderer, 'detectionList');
  assert.equal(nodePreviewRendererRegistry.render(node('Detection')).renderer, 'detection');
  assert.equal(nodePreviewRendererRegistry.render(node('object', { originalType: 'PointSet' })).renderer, 'container');
  assert.equal(nodePreviewRendererRegistry.render(node('object', { originalType: 'System.Drawing.RectangleF' })).label, 'Rectangle');
});

test('NodePreviewInspector tree rendering is row-limited and searches only bounded DTO fields', () => {
  const children = Array.from({ length: 240 }, (_, index) => node('number', {
    name: `Field${index}`,
    displayValue: `Value${index}`,
    pathHint: `$["Field${index}"]`
  }));
  const root = node('dictionary', {
    name: null,
    pathHint: '$',
    addressable: false,
    children
  });
  root.outputData = 'SECRET_OUTSIDE_DTO';

  const rows = buildVisibleObservationRows(root, {
    expandedKeys: new Set(),
    limit: 50
  });

  assert.equal(rows.rows.length, 50);
  assert.equal(rows.hasMore, true);
  assert.ok(rows.rows.every(row => row.normalized.pathHint.startsWith('$')));

  const matched = searchObservationRows(root, 'Field120', 10);
  assert.equal(matched.rows.length, 1);
  assert.equal(matched.rows[0].normalized.name, 'Field120');

  const outside = searchObservationRows(root, 'SECRET_OUTSIDE_DTO', 10);
  assert.equal(outside.rows.length, 0);
});

test('NodePreviewInspector preserves malicious display strings as text data and does not use HTML injection APIs', () => {
  const malicious = '<img src=x onerror=alert(1)><script>alert(2)</script>';
  const root = node('string', {
    name: 'Unsafe',
    displayValue: malicious,
    pathHint: '$["Unsafe"]'
  });
  const rows = buildVisibleObservationRows(root, { limit: 5 });

  assert.equal(rows.rows[0].normalized.displayValue, malicious);
  assert.equal(rows.rows[0].rendered.value, malicious);

  const sourcePath = path.resolve(
    process.cwd(),
    '../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewInspector.js'
  );
  const source = fs.readFileSync(sourcePath, 'utf8');
  assert.equal(source.includes('.innerHTML'), false);
  assert.equal(/\bfetch\s*\(/.test(source), false);
});

test('nodePreviewSelectionStore stores complete identity and clears on identity or status boundaries', () => {
  const store = createNodePreviewSelectionStore();
  const selected = store.select({
    identity: identity(),
    nodeName: 'Threshold',
    nodeKind: 'Thresholding',
    displayValue: '0.98',
    originalType: 'System.Double',
    pathHint: '$["Score"]',
    outputPortId: 'out-score',
    outputPortName: 'Score',
    resultPathVersion: 1,
    resultPath: '$["Score"]',
    bindableVariableTypes: ['Double'],
    addressable: false,
    artifact: {
      artifactId: 'artifact-1',
      kind: 'image',
      role: 'outputImage',
      pathHint: '$["Image"]',
      contentType: 'image/png',
      length: 4,
      sha256: 'abc'
    }
  });

  assert.equal(selected.identitySignature, getNodePreviewIdentitySignature(identity()));
  assert.equal(selected.addressable, false);
  assert.equal(selected.outputPortId, 'out-score');
  assert.equal(selected.resultPathVersion, 1);
  assert.equal(selected.resultPath, '$["Score"]');
  assert.equal(selected.pathHint, '$["Score"]');
  assert.equal(store.getSelection().artifact.artifactId, 'artifact-1');

  store.clearIfBindingContextChanged({
    identity: identity(),
    outputPortId: 'out-score',
    resultPathVersion: 1,
    resultPath: '$["Score"]'
  });
  assert.notEqual(store.getSelection(), null);

  store.clearIfBindingContextChanged({
    identity: identity(),
    outputPortId: 'out-score',
    resultPathVersion: 1,
    resultPath: '$["Other"]'
  });
  assert.equal(store.getSelection(), null);

  store.select({
    identity: identity(),
    outputPortId: 'out-score',
    resultPathVersion: 1,
    resultPath: '$["Score"]',
    displayValue: '0.98',
    pathHint: '$["Score"]'
  });
  store.clearIfIdentityChanged(identity({ flowRevision: 12 }));
  assert.equal(store.getSelection(), null);

  store.select({ identity: identity(), displayValue: 'x', pathHint: '$["x"]' });
  store.clear();
  assert.equal(store.getSelection(), null);
});

test('NodePreviewInspector exposes global variable binding only for current canonical scalar metadata', () => {
  const restoreDocument = installFakeDocument();
  try {
    const store = createNodePreviewSelectionStore();
    const bindableScalar = node('number', {
      name: 'Score',
      displayValue: '7',
      originalType: 'System.Int64',
      pathHint: '$["Payload"]["Score"]',
      outputPortId: 'out-payload',
      outputPortName: 'Payload',
      resultPathVersion: 1,
      resultPath: '$["Score"]',
      bindableVariableTypes: ['String', 'Int64', 'Double']
    });
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'Thresholding',
      title: 'Threshold',
      status: 'success',
      executionTimeMs: 5,
      observation: {
        identity: identity(),
        detail: node('dictionary', {
          pathHint: '$',
          addressable: false,
          children: [bindableScalar]
        })
      },
      artifacts: []
    };
    const { inspector } = createInspectorHarness({ state, selectionStore: store });
    const row = buildVisibleObservationRows(state.observation.detail, { limit: 5 }).rows[1];

    assert.equal(inspector.canBindGlobalVariable(row), true);
    const rowElement = inspector.renderDetailRow(row);
    assert.ok(rowElement.children.at(-1).children.some(child => String(child.className).includes('bind-global-variable')));

    const descriptor = inspector.selectDetailRow(row);
    assert.equal(descriptor.outputPortId, 'out-payload');
    assert.equal(descriptor.outputPortName, 'Payload');
    assert.equal(descriptor.resultPathVersion, 1);
    assert.equal(descriptor.resultPath, '$["Score"]');
    assert.deepEqual(descriptor.bindableVariableTypes, ['String', 'Int64', 'Double']);
    assert.equal(store.getSelection().resultPath, '$["Score"]');
    assert.equal(store.getSelection().truncated, false);

    const pathHintOnly = buildVisibleObservationRows(node('dictionary', {
      pathHint: '$',
      addressable: false,
      children: [node('number', {
        name: 'PathHintOnly',
        pathHint: '$["PathHintOnly"]'
      })]
    }), { limit: 5 }).rows[1];
    assert.equal(inspector.canBindGlobalVariable(pathHintOnly), false);

    const resourceRow = buildVisibleObservationRows(node('dictionary', {
      pathHint: '$',
      addressable: false,
      children: [node('image', {
        name: 'Image',
        outputPortId: 'out-image',
        outputPortName: 'Image',
        resultPathVersion: 1,
        resultPath: '$',
        bindableVariableTypes: ['String']
      })]
    }), { limit: 5 }).rows[1];
    assert.equal(inspector.canBindGlobalVariable(resourceRow), false);

    const truncatedRow = buildVisibleObservationRows(node('dictionary', {
      pathHint: '$',
      addressable: false,
      children: [node('number', {
        name: 'Truncated',
        truncated: true,
        outputPortId: 'out',
        outputPortName: 'Out',
        resultPathVersion: 1,
        resultPath: '$',
        bindableVariableTypes: ['Int64']
      })]
    }), { limit: 5 }).rows[1];
    assert.equal(inspector.canBindGlobalVariable(truncatedRow), false);

    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector disables field binding when coordinator identity becomes stale', () => {
  const restoreDocument = installFakeDocument();
  try {
    let currentState;
    const bindableScalar = node('number', {
      name: 'Score',
      outputPortId: 'out-payload',
      outputPortName: 'Payload',
      resultPathVersion: 1,
      resultPath: '$["Score"]',
      bindableVariableTypes: ['Int64']
    });
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'Thresholding',
      title: 'Threshold',
      status: 'success',
      observation: {
        identity: identity(),
        detail: node('dictionary', {
          pathHint: '$',
          addressable: false,
          children: [bindableScalar]
        })
      },
      artifacts: []
    };
    currentState = state;
    const { inspector } = createInspectorHarness({
      state,
      getState: () => currentState
    });
    const row = buildVisibleObservationRows(state.observation.detail, { limit: 5 }).rows[1];
    assert.equal(inspector.canBindGlobalVariable(row), true);

    currentState = {
      ...state,
      observation: {
        ...state.observation,
        identity: identity({ flowRevision: 12 })
      }
    };
    assert.equal(inspector.canBindGlobalVariable(row), false);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector links detail fields and scene primitives by canonical ResultPath metadata', () => {
  const restoreDocument = installFakeDocument();
  try {
    const store = createNodePreviewSelectionStore();
    const radiusNode = node('number', {
      name: 'Radius',
      displayValue: '12.5',
      outputPortId: 'radius-port',
      outputPortName: 'Radius',
      resultPathVersion: 1,
      resultPath: '$',
      bindableVariableTypes: ['Double']
    });
    const scenePrimitive = {
      primitiveId: 'circle:primary',
      kind: 'circle',
      layer: 'measurement',
      zOrder: 10,
      visible: true,
      selectable: true,
      label: 'Circle',
      geometry: { centerX: 50, centerY: 60, radius: 12.5 },
      style: { stroke: '#16a34a', strokeWidth: 2 },
      outputPortId: 'radius-port',
      resultPathVersion: 1,
      resultPath: '$'
    };
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'CircleMeasurement',
      title: 'Circle',
      status: 'success',
      observation: {
        identity: identity(),
        detail: node('dictionary', {
          pathHint: '$',
          addressable: false,
          children: [radiusNode]
        }),
        visualScene: {
          schemaVersion: 'visual-scene.v1',
          coordinateSpace: 'image.pixel',
          imageWidth: 320,
          imageHeight: 240,
          primitives: [scenePrimitive],
          diagnostics: [],
          truncated: false
        }
      },
      artifacts: []
    };

    const { inspector } = createInspectorHarness({ state, selectionStore: store });
    const row = buildVisibleObservationRows(state.observation.detail, { limit: 5 }).rows[1];
    const descriptor = inspector.selectDetailRow(row);

    assert.equal(descriptor.resultPath, '$');
    assert.equal(inspector.activeScenePrimitiveId, 'circle:primary');

    store.clear();
    inspector.activeScenePrimitiveId = null;
    const sceneDescriptor = inspector.selectScenePrimitive(scenePrimitive);
    assert.equal(sceneDescriptor.outputPortId, 'radius-port');
    assert.equal(store.getSelection().resultPath, '$');
    assert.equal(inspector.activeScenePrimitiveId, 'circle:primary');
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector links locatable list items without enabling global variable binding', () => {
  const restoreDocument = installFakeDocument();
  try {
    const store = createNodePreviewSelectionStore();
    const circleItem = node('objectDescriptor', {
      name: '0',
      displayValue: 'Unsupported object; content omitted.',
      addressable: false,
      locatable: true,
      outputPortId: 'circles-port',
      outputPortName: 'CircleDataList',
      resultPathVersion: 1,
      resultPath: '$[0]',
      bindableVariableTypes: null
    });
    const circleList = node('array', {
      name: 'CircleDataList',
      addressable: false,
      locatable: true,
      outputPortId: 'circles-port',
      outputPortName: 'CircleDataList',
      resultPathVersion: 1,
      resultPath: '$',
      children: [circleItem]
    });
    const primitive = {
      primitiveId: 'circle:data-list:0',
      kind: 'circle',
      layer: 'measurement',
      zOrder: 20,
      visible: true,
      selectable: true,
      label: 'Circle 1',
      geometry: { centerX: 10, centerY: 20, radius: 5 },
      style: { stroke: '#16a34a' },
      outputPortId: 'circles-port',
      resultPathVersion: 1,
      resultPath: '$[0]'
    };
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'CircleMeasurement',
      title: 'Circle',
      status: 'success',
      observation: {
        identity: identity(),
        detail: node('dictionary', {
          pathHint: '$',
          addressable: false,
          children: [circleList]
        }),
        visualScene: {
          schemaVersion: 'visual-scene.v1',
          coordinateSpace: 'image.pixel',
          imageWidth: 320,
          imageHeight: 240,
          primitives: [primitive],
          diagnostics: [],
          truncated: false
        }
      },
      artifacts: []
    };

    const { inspector } = createInspectorHarness({ state, selectionStore: store });
    const rows = buildVisibleObservationRows(state.observation.detail, { limit: 10, searchQuery: '0' }).rows;
    const itemRow = rows.find(row => row.normalized.name === '0');

    assert.equal(inspector.canBindGlobalVariable(itemRow), false);
    const detailDescriptor = inspector.selectDetailRow(itemRow);
    assert.equal(detailDescriptor.resultPath, '$[0]');
    assert.equal(detailDescriptor.addressable, false);
    assert.equal(detailDescriptor.locatable, true);
    assert.equal(inspector.activeScenePrimitiveId, 'circle:data-list:0');

    store.clear();
    inspector.activeScenePrimitiveId = null;
    const sceneDescriptor = inspector.selectScenePrimitive(primitive);
    assert.equal(sceneDescriptor.resultPath, '$[0]');
    assert.equal(store.getSelection().locatable, true);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector fails closed when scene locator has duplicate or missing detail nodes', () => {
  const restoreDocument = installFakeDocument();
  try {
    const store = createNodePreviewSelectionStore();
    const duplicateA = node('objectDescriptor', {
      name: '0',
      addressable: false,
      locatable: true,
      outputPortId: 'circles-port',
      outputPortName: 'CircleDataList',
      resultPathVersion: 1,
      resultPath: '$[0]'
    });
    const duplicateB = node('objectDescriptor', {
      name: 'duplicate',
      addressable: false,
      locatable: true,
      outputPortId: 'circles-port',
      outputPortName: 'CircleDataList',
      resultPathVersion: 1,
      resultPath: '$[0]'
    });
    const primitive = {
      primitiveId: 'circle:data-list:0',
      kind: 'circle',
      selectable: true,
      outputPortId: 'circles-port',
      resultPathVersion: 1,
      resultPath: '$[0]',
      geometry: {},
      style: {}
    };
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'CircleMeasurement',
      title: 'Circle',
      status: 'success',
      observation: {
        identity: identity(),
        detail: node('dictionary', {
          pathHint: '$',
          addressable: false,
          children: [duplicateA, duplicateB]
        }),
        visualScene: {
          imageWidth: 320,
          imageHeight: 240,
          primitives: [primitive],
          diagnostics: []
        }
      },
      artifacts: []
    };

    const { inspector } = createInspectorHarness({ state, selectionStore: store });
    assert.equal(inspector.selectScenePrimitive(primitive), null);
    assert.equal(store.getSelection(), null);
    assert.equal(inspector.activeScenePrimitiveId, null);

    state.observation.detail.children = [];
    const panel = inspector.renderScene(state.observation);
    const rowButton = findElement(panel, item => item.dataset?.primitiveId === 'circle:data-list:0')
      ?.children?.[0];
    assert.equal(rowButton?.disabled, true);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector shows controlled state instead of blank canvas when scene size is unavailable', () => {
  const restoreDocument = installFakeDocument();
  try {
    const primitive = {
      primitiveId: 'npoint:point:1',
      kind: 'point',
      selectable: false,
      geometry: { x: 10, y: 20 },
      style: {}
    };
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'NPointCalibration',
      title: 'Calibration',
      status: 'success',
      observation: {
        identity: identity(),
        detail: node('dictionary', { pathHint: '$', addressable: false }),
        visualScene: {
          imageWidth: 0,
          imageHeight: 0,
          primitives: [primitive],
          diagnostics: []
        }
      },
      artifacts: []
    };

    const { inspector } = createInspectorHarness({ state });
    const panel = inspector.renderScene(state.observation);
    assert.match(collectText(panel), /Scene 坐标尺寸不可用，无法安全叠加显示/);
    assert.equal(inspector.pendingSceneRender, null);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector forces neutral plane for World2D scene even when image dimensions match', () => {
  const restoreDocument = installFakeDocument();
  try {
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'PixelToWorldTransform',
      title: 'PixelToWorld',
      status: 'success',
      inputImageBase64: 'data:image/png;base64,MATCHING_512_IMAGE',
      outputImageBase64: 'data:image/png;base64,MATCHING_512_OUTPUT',
      observation: {
        identity: identity(),
        detail: node('dictionary', { pathHint: '$', addressable: false }),
        visualScene: {
          coordinateSpace: 'world.2d.neutral-plane',
          frameId: 'world.2d',
          frameKind: 'World2D',
          unit: 'cm',
          worldMinX: 1,
          worldMinY: 2,
          worldMaxX: 3,
          worldMaxY: 4,
          worldToSceneScale: 216,
          imageWidth: 512,
          imageHeight: 512,
          primitives: [{
            primitiveId: 'ptw:point:0',
            kind: 'point',
            selectable: false,
            geometry: { x: 256, y: 256 },
            style: {}
          }],
          diagnostics: []
        }
      },
      artifacts: []
    };

    const { inspector } = createInspectorHarness({ state });
    const panel = inspector.renderScene(state.observation);

    assert.match(collectText(panel), /FrameId world\.2d/);
    assert.match(collectText(panel), /Unit cm/);
    assert.match(collectText(panel), /World bounds/);
    assert.match(collectText(panel), /WorldToSceneScale/);
    assert.equal(inspector.pendingSceneRender.requiresNeutralPlane, true);
    assert.deepEqual(inspector.pendingSceneRender.imageCandidates, []);

    inspector.sceneMode = 'annotated';
    const annotatedPanel = inspector.renderScene(state.observation);
    assert.match(collectText(annotatedPanel), /not the World2D Scene base/);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector does not invoke binding callback for a stale descriptor click', () => {
  const restoreDocument = installFakeDocument();
  try {
    let currentState;
    let callbackCount = 0;
    const store = createNodePreviewSelectionStore();
    const bindableScalar = node('number', {
      name: 'Score',
      outputPortId: 'out-payload',
      outputPortName: 'Payload',
      resultPathVersion: 1,
      resultPath: '$["Score"]',
      bindableVariableTypes: ['Int64']
    });
    const state = {
      activeNodeId: 'node-1',
      nodeType: 'Thresholding',
      title: 'Threshold',
      status: 'success',
      observation: {
        identity: identity(),
        detail: node('dictionary', {
          pathHint: '$',
          addressable: false,
          children: [bindableScalar]
        })
      },
      artifacts: []
    };
    currentState = state;
    const { inspector } = createInspectorHarness({
      state,
      selectionStore: store,
      getState: () => currentState,
      onBindGlobalVariable: () => {
        callbackCount += 1;
      }
    });
    const row = buildVisibleObservationRows(state.observation.detail, { limit: 5 }).rows[1];
    const rowElement = inspector.renderDetailRow(row);
    const bindButton = rowElement.children.at(-1).children.find(child => String(child.className).includes('bind-global-variable'));
    assert.ok(bindButton);

    currentState = {
      ...state,
      observation: {
        ...state.observation,
        identity: identity({ flowRevision: 12 })
      }
    };
    bindButton.click();

    assert.equal(callbackCount, 0);
    assert.equal(store.getSelection(), null);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector does not fetch declared-oversized text artifacts', async () => {
  const restoreDocument = installFakeDocument();
  try {
    const oversizedArtifact = {
      artifactId: 'large-json',
      kind: 'profile',
      role: 'profile',
      contentType: 'application/json',
      length: MAX_ARTIFACT_TEXT_PREVIEW_BYTES + 1,
      sha256: 'sha-large',
      expiresAtUtc: '2026-07-02T09:00:00Z'
    };
    const { inspector, calls } = createInspectorHarness({ artifacts: [oversizedArtifact] });

    await inspector.startArtifactRead(oversizedArtifact, 'text');

    assert.equal(calls.length, 0);
    const readState = inspector.artifactReadState.get('large-json');
    assert.equal(readState.status, 'success');
    assert.match(readState.text, /内容过大，仅展示元数据/);
    assert.match(readState.text, /sha-large/);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector decodes text only from bounded Blob slices', async () => {
  const restoreDocument = installFakeDocument();
  try {
    let originalTextCalls = 0;
    let sliceTextCalls = 0;
    const sliceCalls = [];
    const blob = {
      size: 5,
      text() {
        originalTextCalls += 1;
        throw new Error('full blob text must not be called');
      },
      slice(start, end) {
        sliceCalls.push([start, end]);
        return {
          async text() {
            sliceTextCalls += 1;
            return 'hello';
          }
        };
      }
    };
    const artifact = {
      artifactId: 'small-text',
      kind: 'profile',
      contentType: 'text/plain',
      length: 5
    };
    const { inspector, calls } = createInspectorHarness({
      artifacts: [artifact],
      readResult: { blob, artifact }
    });

    await inspector.startArtifactRead(artifact, 'text');

    assert.equal(calls.length, 1);
    assert.deepEqual(sliceCalls, [[0, MAX_ARTIFACT_TEXT_PREVIEW_BYTES]]);
    assert.equal(originalTextCalls, 0);
    assert.equal(sliceTextCalls, 1);
    assert.equal(inspector.artifactReadState.get('small-text').text, 'hello');
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector truncates when actual Blob size exceeds text limit despite smaller declaration', async () => {
  const restoreDocument = installFakeDocument();
  try {
    const artifact = {
      artifactId: 'mismatch-text',
      kind: 'profile',
      contentType: 'application/json',
      length: 12
    };
    const blob = new Blob(['x'.repeat(MAX_ARTIFACT_TEXT_DISPLAY_CHARS + 256)], {
      type: 'application/json'
    });
    Object.defineProperty(blob, 'size', { value: MAX_ARTIFACT_TEXT_PREVIEW_BYTES + 10 });
    const { inspector } = createInspectorHarness({
      artifacts: [artifact],
      readResult: { blob, artifact }
    });

    await inspector.startArtifactRead(artifact, 'text');

    const text = inspector.artifactReadState.get('mismatch-text').text;
    assert.match(text, /已截断/);
    assert.ok(text.length < MAX_ARTIFACT_TEXT_DISPLAY_CHARS + 64);
    inspector.destroy();
  } finally {
    restoreDocument();
  }
});

test('NodePreviewInspector ignores stale text completion after identity change or destroy', async () => {
  const restoreDocument = installFakeDocument();
  try {
    const artifact = {
      artifactId: 'late-text',
      kind: 'profile',
      contentType: 'text/plain',
      length: 16
    };
    let releaseText;
    let textStartedResolve;
    const textStarted = new Promise(resolve => {
      textStartedResolve = resolve;
    });
    const blob = {
      size: 16,
      slice() {
        textStartedResolve();
        return {
          text: () => new Promise(resolveText => {
            releaseText = () => resolveText('late text');
          })
        };
      }
    };
    const { inspector } = createInspectorHarness({
      artifacts: [artifact],
      readResult: { blob, artifact }
    });

    const staleRead = inspector.startArtifactRead(artifact, 'text');
    await textStarted;
    inspector.state = {
      ...inspector.state,
      observation: {
        ...inspector.state.observation,
        identity: identity({ flowRevision: 99 })
      }
    };
    releaseText();
    await staleRead;
    assert.notEqual(inspector.artifactReadState.get('late-text')?.status, 'success');

    let destroyReleaseText;
    let destroyTextStartedResolve;
    const destroyTextStarted = new Promise(resolve => {
      destroyTextStartedResolve = resolve;
    });
    const destroyBlob = {
      size: 16,
      slice() {
        destroyTextStartedResolve();
        return {
          text: () => new Promise(resolveText => {
            destroyReleaseText = () => resolveText('destroyed text');
          })
        };
      }
    };
    inspector.state = {
      ...inspector.state,
      observation: {
        ...inspector.state.observation,
        identity: identity()
      }
    };
    inspector.artifactReadState.clear();
    inspector.previewCoordinator.readArtifactForCurrentState = async () => ({ blob: destroyBlob, artifact });
    const destroyRead = inspector.startArtifactRead(artifact, 'text');
    await destroyTextStarted;
    inspector.destroy();
    destroyReleaseText();
    await destroyRead;
    assert.equal(inspector.destroyed, true);
    assert.notEqual(inspector.artifactReadState.get('late-text')?.status, 'success');
  } finally {
    restoreDocument();
  }
});

test('app node preview cutover creates selectionStore only in inspector-enabled branch', () => {
  const appSourcePath = path.resolve(
    process.cwd(),
    '../../src/ClearVision.Product.Desktop/wwwroot/src/app.js'
  );
  const appSource = fs.readFileSync(appSourcePath, 'utf8');

  assert.equal((appSource.match(/createNodePreviewSelectionStore\(\)/g) || []).length, 1);
  assert.match(appSource, /const NODE_PREVIEW_INSPECTOR_ENABLED = readNodePreviewInspectorFlagOnce\(\);/);
  assert.match(appSource, /featureFlags\?\.\[NODE_PREVIEW_INSPECTOR_FLAG_KEY\] === true/);
  assert.equal(appSource.includes('startup.nodePreviewInspectorEnabled === true'), false);
  assert.doesNotMatch(appSource, /import\s+\{\s*createNodePreviewSelectionStore\s*\}\s+from\s+'\.\/features\/flow-editor\/nodePreviewSelectionStore\.js'/);
  assert.match(appSource, /nodePreviewSelectionStoreModulePromise = import\('\.\/features\/flow-editor\/nodePreviewSelectionStore\.js'\)/);
  assert.match(appSource, /if \(inspectorEnabled\) \{[\s\S]*createNodePreviewSelectionStore\(\)[\s\S]*new NodePreviewInspector/);
});
