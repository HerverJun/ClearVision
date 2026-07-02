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
    addEventListener() {},
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
  assert.equal(selected.pathHint, '$["Score"]');
  assert.equal(store.getSelection().artifact.artifactId, 'artifact-1');

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
  assert.match(appSource, /if \(inspectorEnabled\) \{[\s\S]*createNodePreviewSelectionStore\(\)[\s\S]*new NodePreviewInspector/);
});
