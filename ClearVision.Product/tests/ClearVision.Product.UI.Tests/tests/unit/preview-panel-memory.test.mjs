import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  buildPreviewParameterSnapshot
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';
import {
  PreviewPanel
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanel.js';
import {
  PreviewPanelCapabilityOwner,
  createPreviewPanelCapabilityAdapter
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanelCapabilityOwner.mjs';
import {
  MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS,
  buildOperatorResultViewModel,
  buildSafeJsonPreview
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/operatorResultViewModel.mjs';

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

function identity(overrides = {}) {
  return {
    projectId: 'project-1',
    targetNodeId: 'node-1',
    debugSessionId: 'debug-1',
    clientRequestSequence: 7,
    flowRevision: 3,
    runId: null,
    ...overrides
  };
}

function node(kind, fields = {}) {
  return {
    kind,
    name: fields.name ?? kind,
    displayValue: fields.displayValue ?? `${kind} value`,
    originalType: fields.originalType ?? null,
    pathHint: fields.pathHint ?? `$["${fields.name ?? kind}"]`,
    resultPathVersion: fields.resultPathVersion ?? null,
    resultPath: fields.resultPath ?? null,
    artifact: fields.artifact ?? null,
    children: fields.children ?? []
  };
}

function observation(overrides = {}) {
  return {
    schemaVersion: 'execution-observation.v1',
    observedAtUtc: '2026-07-04T06:00:00Z',
    identity: identity(overrides.identity),
    outcome: {
      success: true,
      executionTimeMs: 12,
      executedOperatorCount: 1
    },
    detail: node('dictionary', {
      pathHint: '$',
      children: [
        node('number', {
          name: 'Score',
          displayValue: '0.98',
          resultPathVersion: 1,
          resultPath: '$["Score"]'
        }),
        node('array', {
          name: 'Detections',
          displayValue: '2 items',
          resultPathVersion: 1,
          resultPath: '$["Detections"]',
          children: [
            node('detection', {
              name: '0',
              displayValue: 'circle candidate',
              resultPathVersion: 1,
              resultPath: '$["Detections"][0]'
            })
          ]
        }),
        node('circle', {
          name: 'Circle',
          displayValue: 'center=(10, 20) radius=5',
          resultPathVersion: 1,
          resultPath: '$["Circle"]'
        }),
        node('image', {
          name: 'Mask',
          displayValue: 'mask artifact',
          artifact: {
            artifactId: 'mask-artifact',
            kind: 'image',
            role: 'mask',
            contentType: 'image/png',
            length: 10
          }
        })
      ]
    }),
    visualScene: {
      coordinateSpace: 'image.pixel',
      imageWidth: 320,
      imageHeight: 240,
      primitives: [
        {
          primitiveId: 'circle:primary',
          kind: 'circle',
          layer: 'measurement',
          label: 'Circle',
          resultPathVersion: 1,
          resultPath: '$["Circle"]'
        }
      ],
      diagnostics: []
    },
    diagnostics: [
      { code: 'low-contrast', message: 'contrast warning', pathHint: '$["Score"]' }
    ],
    ...overrides
  };
}

function successState(overrides = {}) {
  const parameters = overrides.parameters ?? [{ name: 'Threshold', value: 128 }];
  return {
    activeNodeId: 'node-1',
    nodeType: 'Thresholding',
    title: 'Threshold',
    status: 'success',
    executionTimeMs: 12,
    errorMessage: null,
    request: {
      projectId: 'project-1',
      nodeId: 'node-1',
      flowRevision: 3,
      parameterSnapshot: buildPreviewParameterSnapshot(parameters),
      requestKey: 'request-1'
    },
    outputData: {
      Score: 0.98,
      LocalPath: 'C:\\Users\\A\\secret.png',
      ApiToken: 'secret-value'
    },
    observation: observation(),
    artifacts: [
      {
        artifactId: 'mask-artifact',
        kind: 'image',
        role: 'mask',
        contentType: 'image/png',
        length: 10,
        createdAtUtc: '2026-07-04T06:00:00Z'
      },
      {
        artifactId: 'json-artifact',
        kind: 'profile',
        role: 'profile',
        contentType: 'application/json',
        length: 20
      }
    ],
    presenter: {
      statusText: '预览完成',
      inputImageSrc: null,
      outputImageSrc: null
    },
    ...overrides
  };
}

function createCoordinatorHarness(initialState, options = {}) {
  const listeners = new Set();
  let state = initialState;
  const coordinator = {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      listener(state);
      return () => listeners.delete(listener);
    },
    emit(nextState) {
      state = nextState;
      listeners.forEach(listener => listener(state));
    },
    listenerCount: () => listeners.size,
    readArtifactForCurrentState: async (...args) => {
      if (typeof options.readArtifactForCurrentState === 'function') {
        return options.readArtifactForCurrentState(...args);
      }
      throw Object.assign(new Error('HTTP 404'), { status: 404 });
    }
  };

  return coordinator;
}

class PreviewPanelFakeElement {
  constructor(id = '') {
    this.id = id;
    this._innerHTML = '';
    this.textContent = '';
    this.style = {};
    this.disabled = false;
    this.attributes = new Map();
    this.listeners = new Map();
  }

  set innerHTML(value) {
    this._innerHTML = String(value ?? '');
  }

  get innerHTML() {
    return this._innerHTML;
  }

  set src(value) {
    this.setAttribute('src', value);
  }

  get src() {
    return this.getAttribute('src') || '';
  }

  setAttribute(name, value) {
    this.attributes.set(String(name), String(value));
  }

  getAttribute(name) {
    return this.attributes.get(String(name)) ?? null;
  }

  removeAttribute(name) {
    this.attributes.delete(String(name));
  }

  addEventListener(type, listener) {
    if (!this.listeners.has(type)) {
      this.listeners.set(type, []);
    }
    this.listeners.get(type).push(listener);
  }

  querySelectorAll() {
    return [];
  }
}

class PreviewPanelFakeContainer extends PreviewPanelFakeElement {
  constructor() {
    super('preview-root');
    this.elementsById = new Map();
    this.elementsByRole = new Map();
  }

  set innerHTML(value) {
    this._innerHTML = String(value ?? '');
    this.elementsById = new Map();
    this.elementsByRole = new Map();

    const ids = Array.from(this._innerHTML.matchAll(/id="([^"]+)"/g)).map(match => match[1]);
    ids.forEach(id => {
      const element = new PreviewPanelFakeElement(id);
      if (id === 'btn-preview-open-output') {
        element.disabled = true;
      }
      this.elementsById.set(id, element);
    });

    const outputImage = this.elementsById.get('preview-output-image');
    if (outputImage) {
      this.elementsByRole.set('preview-output-image', outputImage);
    }
    const imageContainer = new PreviewPanelFakeElement('preview-main-image-container');
    this.elementsByRole.set('preview-main-image-container', imageContainer);
  }

  get innerHTML() {
    return this._innerHTML;
  }

  querySelector(selector) {
    const idMatch = String(selector).match(/^#(.+)$/);
    if (idMatch) {
      return this.elementsById.get(idMatch[1]) || null;
    }

    const roleMatch = String(selector).match(/^\[data-role="([^"]+)"\]$/);
    if (roleMatch) {
      return this.elementsByRole.get(roleMatch[1]) || null;
    }

    return null;
  }

  querySelectorAll() {
    return [];
  }
}

function createPreviewCapabilityHarness(options = {}) {
  const listeners = {
    selection: new Set(),
    structure: new Set(),
    preview: new Set()
  };
  const node = options.node ?? {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }],
    outputs: [{ type: 'image' }]
  };
  const nodes = new Map([[node.id, node]]);
  const flowCanvasAdapter = {
    selectedNode: options.selectedNodeId ?? node.id,
    nodes,
    getFlowRevision: () => options.flowRevision ?? 3,
    subscribeSelection(listener) {
      listeners.selection.add(listener);
      listener({
        selectedNodeId: this.selectedNode,
        reason: 'initial',
        flowRevision: options.flowRevision ?? 3
      });
      return () => listeners.selection.delete(listener);
    },
    subscribeStructureState(listener) {
      listeners.structure.add(listener);
      return () => listeners.structure.delete(listener);
    },
    selectNode(nodeId) {
      if (!nodes.has(nodeId)) {
        return false;
      }
      this.selectedNode = nodeId;
      listeners.selection.forEach(listener => listener({
        selectedNodeId: nodeId,
        reason: 'selectNode',
        flowRevision: options.flowRevision ?? 3
      }));
      return true;
    }
  };
  const calls = {
    setActiveNode: [],
    requestPreview: [],
    cancelPreview: 0,
    artifactReads: []
  };
  let previewState = options.previewState ?? {
    ...successState({ activeNodeId: null }),
    status: 'idle',
    activeNodeId: null,
    presenter: {
      statusText: '等待预览',
      inputImageSrc: null,
      outputImageSrc: null
    }
  };
  const previewCoordinator = {
    getState: () => previewState,
    subscribe(listener) {
      listeners.preview.add(listener);
      listener(previewState);
      return () => listeners.preview.delete(listener);
    },
    setActiveNode(activeNode, setOptions = {}) {
      calls.setActiveNode.push({ nodeId: activeNode?.id || null, options: setOptions });
      previewState = {
        ...previewState,
        activeNodeId: activeNode?.id || null,
        nodeType: activeNode?.type || null,
        title: activeNode?.title || '',
        presenter: {
          statusText: activeNode ? '等待预览' : '请选择一个算子',
          inputImageSrc: null,
          outputImageSrc: null
        }
      };
      listeners.preview.forEach(listener => listener(previewState));
    },
    requestActivePreview(requestOptions) {
      calls.requestPreview.push(requestOptions);
      previewState = {
        ...successState(),
        status: 'loading',
        presenter: {
          statusText: '预览中',
          inputImageSrc: null,
          outputImageSrc: null
        }
      };
      listeners.preview.forEach(listener => listener(previewState));
    },
    cancelPreview() {
      calls.cancelPreview += 1;
      previewState = {
        ...previewState,
        status: 'canceled',
        presenter: {
          statusText: '预览已取消',
          inputImageSrc: null,
          outputImageSrc: null
        }
      };
      listeners.preview.forEach(listener => listener(previewState));
    },
    readArtifactForCurrentState: async (artifactId, expectedIdentity, readOptions) => {
      calls.artifactReads.push({ artifactId, expectedIdentity, readOptions });
      if (typeof options.readArtifactForCurrentState === 'function') {
        return options.readArtifactForCurrentState(artifactId, expectedIdentity, readOptions);
      }
      return {
        artifact: {
          artifactId,
          kind: 'profile',
          role: 'profile',
          contentType: 'application/json',
          length: 20
        },
        blob: {
          size: 20,
          slice(start, end) {
            return {
              async text() {
                return JSON.stringify({ score: 1, token: 'secret-token', range: [start, end] });
              }
            };
          }
        }
      };
    }
  };
  const adapter = createPreviewPanelCapabilityAdapter({
    flowCanvasAdapter,
    previewCoordinator,
    getOperatorMetadata: () => ({
      displayName: '阈值',
      parameters: [{ name: 'Threshold', value: 10, dataType: 'int' }]
    })
  });
  const container = {
    innerHTML: '',
    dataset: {},
    listeners: new Map(),
    addEventListener(type, listener) {
      this.listeners.set(type, listener);
    },
    removeEventListener(type, listener) {
      if (this.listeners.get(type) === listener) {
        this.listeners.delete(type);
      }
    }
  };

  return {
    adapter,
    container,
    calls,
    listeners,
    nodes,
    flowCanvasAdapter,
    emitPreview(nextState) {
      previewState = nextState;
      listeners.preview.forEach(listener => listener(previewState));
    },
    emitStructure() {
      listeners.structure.forEach(listener => listener({
        flowRevision: options.flowRevision ?? 3,
        reason: 'test'
      }));
    }
  };
}

test('PreviewPanel analysis results avoid retaining input images and oversized previews', () => {
  const panel = new PreviewPanel(null, {
    maxAnalysisImageBase64Chars: 8
  });

  const normalized = panel._normalizeAnalysisResult({
    targetNodeId: 'node-1',
    success: true,
    inputImageBase64: 'INPUT_IMAGE',
    previewImageBase64: 'PREVIEW',
    outputs: { Count: 1 }
  });

  assert.equal(normalized.inputImageSrc, null);
  assert.equal(normalized.previewImageSrc, 'data:image/png;base64,PREVIEW');
  assert.deepEqual(normalized.outputs, { Count: 1 });

  const oversized = panel._normalizeAnalysisResult({
    targetNodeId: 'node-1',
    success: true,
    previewImageBase64: 'TOO_LARGE_PREVIEW'
  });

  assert.equal(oversized.previewImageSrc, null);

  panel.analysisResult = normalized;
  panel.destroy();
  assert.equal(panel.analysisResult, null);
});

test('PreviewPanel renders one output image surface and keeps output summary visible', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const coordinator = createCoordinatorHarness(successState({
    outputData: {
      Score: 0.98,
      Image: 'data:image/png;base64,SHOULD_BE_SKIPPED'
    },
    presenter: {
      statusText: '预览完成',
      inputImageSrc: 'input-image-src',
      outputImageSrc: 'output-image-src'
    }
  }));
  const container = new PreviewPanelFakeContainer();

  const panel = new PreviewPanel(container, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    getFlowRevision: () => 3
  });

  assert.equal((container.innerHTML.match(/data-role="preview-output-image"/g) || []).length, 1);
  assert.doesNotMatch(container.innerHTML, /preview-before|preview-after|输入图像预览/);
  assert.equal(container.querySelector('#preview-output-image').getAttribute('src'), 'output-image-src');
  assert.equal(container.querySelector('#preview-output-placeholder').style.display, 'none');
  assert.equal(container.querySelector('#btn-preview-open-output').disabled, false);
  assert.match(container.querySelector('#preview-output-list').innerHTML, /Score/);
  assert.doesNotMatch(container.querySelector('#preview-output-list').innerHTML, /SHOULD_BE_SKIPPED/);
  assert.equal(coordinator.listenerCount(), 1);

  panel.destroy();
  assert.equal(coordinator.listenerCount(), 0);
});

test('PreviewPanel does not fall back to a persistent input image when output is missing', () => {
  const operator = {
    id: 'node-1',
    type: 'ImageAcquisition',
    title: '图像采集',
    parameters: []
  };
  const coordinator = createCoordinatorHarness(successState({
    presenter: {
      statusText: '预览完成',
      inputImageSrc: 'input-image-src',
      outputImageSrc: null
    }
  }));
  const container = new PreviewPanelFakeContainer();

  const panel = new PreviewPanel(container, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    getFlowRevision: () => 3
  });

  assert.equal(container.querySelector('#preview-output-image').getAttribute('src'), null);
  assert.equal(container.querySelector('#preview-output-placeholder').style.display, 'flex');
  assert.match(container.querySelector('#preview-output-placeholder').textContent, /暂无输出图像/);
  assert.equal(container.querySelector('#btn-preview-open-output').disabled, true);

  panel.destroy();
});

test('OperatorResultViewModel renders no-selection, no-preview, loading, error, stale, and disabled states', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }]
  };

  assert.equal(
    buildOperatorResultViewModel(null, { status: 'idle' }).stateMessage,
    '请选择一个算子节点查看模块结果'
  );

  assert.equal(
    buildOperatorResultViewModel(operator, { status: 'idle', activeNodeId: null }).stateMessage,
    '该算子暂无预览结果'
  );

  const loading = buildOperatorResultViewModel(operator, {
    ...successState(),
    status: 'loading',
    outputData: { Score: 0.99 }
  }, {
    flowRevision: 3
  });
  assert.equal(loading.status, 'loading');
  assert.equal(loading.outputSections.length, 0);

  const error = buildOperatorResultViewModel(operator, {
    ...successState(),
    status: 'error',
    errorMessage: 'preview failed'
  }, {
    flowRevision: 3
  });
  assert.equal(error.status, 'error');
  assert.match(error.stateMessage, /preview failed/);

  const stale = buildOperatorResultViewModel(operator, successState({
    request: {
      projectId: 'project-1',
      nodeId: 'node-1',
      flowRevision: 2,
      parameterSnapshot: buildPreviewParameterSnapshot([{ name: 'Threshold', value: 99 }]),
      requestKey: 'old-request'
    }
  }), {
    flowRevision: 3
  });
  assert.equal(stale.status, 'stale');
  assert.equal(stale.stateMessage, '结果已过期，请重新预览');
  assert.deepEqual(stale.staleReasons.sort(), ['flowRevision', 'parameters']);

  const disabled = buildOperatorResultViewModel(operator, successState(), {
    liveNode: { id: 'node-1', type: 'Thresholding', disabled: true, parameters: operator.parameters },
    flowRevision: 3
  });
  assert.equal(disabled.status, 'disabled');
  assert.match(disabled.stateMessage, /禁用/);
});

test('OperatorResultViewModel summarizes observation outputs, artifacts, scene, diagnostics, and node list', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const model = buildOperatorResultViewModel(operator, successState(), {
    flowRevision: 3,
    nodes: [
      { id: 'node-1', type: 'Thresholding', title: 'Threshold' },
      { id: 'node-2', type: 'BlobAnalysis', title: 'Blob', disabled: true }
    ]
  });

  assert.equal(model.status, 'success');
  assert.ok(model.overviewItems.some(([label, value]) => label === 'ResultPath' && value === '$["Score"]'));
  assert.ok(model.outputSections.some(section => section.kind === 'scalar'));
  assert.ok(model.outputSections.some(section => section.kind === 'table'));
  assert.ok(model.outputSections.some(section => section.kind === 'geometry'));
  assert.ok(model.outputSections.some(section => section.kind === 'artifact'));
  assert.equal(model.artifacts.length, 2);
  assert.equal(model.sceneSummary.available, true);
  assert.equal(model.sceneSummary.primitiveCount, 1);
  assert.ok(model.diagnostics.some(item => item.code === 'low-contrast'));
  assert.deepEqual(model.nodeResults.map(item => item.statusKind), ['success', 'disabled']);
});

test('OperatorResultViewModel fails soft when scene is missing', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: []
  };
  const state = successState({
    request: {
      projectId: 'project-1',
      nodeId: 'node-1',
      flowRevision: 3,
      parameterSnapshot: buildPreviewParameterSnapshot([]),
      requestKey: 'scene-missing'
    },
    observation: {
      ...observation(),
      visualScene: null
    }
  });
  const model = buildOperatorResultViewModel(operator, state, { flowRevision: 3 });

  assert.equal(model.sceneSummary.available, false);
  assert.equal(model.sceneSummary.message, '该算子暂无可视化叠加');
});

test('OperatorResult raw JSON is truncated and redacts secret-like fields and local absolute paths', () => {
  const preview = buildSafeJsonPreview({
    password: 'open-sesame',
    apiKey: 'key-1',
    outputPath: 'C:\\Users\\A\\Desktop\\ClearVision\\secret.png',
    nested: {
      token: 'token-1',
      text: 'x'.repeat(800)
    }
  }, {
    maxChars: 260
  });

  assert.ok(preview.truncated);
  assert.ok(!preview.text.includes('open-sesame'));
  assert.ok(!preview.text.includes('key-1'));
  assert.ok(!preview.text.includes('token-1'));
  assert.ok(!preview.text.includes('C:\\Users\\A'));
  assert.ok(preview.text.includes('[redacted-secret]'));
  assert.ok(preview.text.includes('[redacted-path]'));
});

test('PreviewPanel artifact preview fails soft for missing artifacts and clears reads on node switch', async () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const coordinator = createCoordinatorHarness(successState());
  const panel = new PreviewPanel(null, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    getFlowRevision: () => 3
  });

  await panel.readArtifactPreview('json-artifact');

  assert.equal(panel.artifactReadState.get('json-artifact').status, 'error');
  assert.equal(panel.artifactReadState.get('json-artifact').text, '资源已过期或不可用');

  coordinator.emit({
    ...successState(),
    activeNodeId: 'node-2',
    observation: observation({ identity: identity({ targetNodeId: 'node-2' }) })
  });

  assert.equal(panel.artifactReadState.size, 0);
  panel.destroy();
});

test('PreviewPanel reads artifact text only through bounded Blob slices and redacts content', async () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const sliceCalls = [];
  const rawJson = JSON.stringify({
    token: 'secret-token',
    path: 'C:\\Users\\A\\Desktop\\ClearVision\\artifact.json',
    text: 'ok'
  });
  const blob = {
    size: rawJson.length,
    text() {
      throw new Error('full blob text must not be called');
    },
    slice(start, end) {
      sliceCalls.push([start, end]);
      return {
        async text() {
          return rawJson;
        }
      };
    }
  };
  const coordinator = createCoordinatorHarness(successState(), {
    readArtifactForCurrentState: async artifactId => ({
      artifact: {
        artifactId,
        kind: 'profile',
        role: 'profile',
        contentType: 'application/json',
        length: rawJson.length
      },
      blob
    })
  });
  const panel = new PreviewPanel(null, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    getFlowRevision: () => 3
  });

  await panel.readArtifactPreview('json-artifact');

  const readState = panel.artifactReadState.get('json-artifact');
  assert.equal(readState.status, 'success');
  assert.deepEqual(sliceCalls, [[0, 64 * 1024]]);
  assert.ok(!readState.text.includes('secret-token'));
  assert.ok(!readState.text.includes('C:\\Users\\A'));
  assert.ok(readState.text.includes('[redacted-secret]'));
  assert.ok(readState.text.includes('[redacted-path]'));
  panel.destroy();
});

test('PreviewPanel avoids fetching declared-oversized artifacts and bounds displayed text', async () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  let readCount = 0;
  const state = successState({
    artifacts: [
      {
        artifactId: 'large-artifact',
        kind: 'profile',
        role: 'profile',
        contentType: 'application/json',
        length: 64 * 1024 + 1
      }
    ]
  });
  const coordinator = createCoordinatorHarness(state, {
    readArtifactForCurrentState: async () => {
      readCount += 1;
      return null;
    }
  });
  const panel = new PreviewPanel(null, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    getFlowRevision: () => 3
  });

  await panel.readArtifactPreview('large-artifact');

  assert.equal(readCount, 0);
  assert.match(panel.artifactReadState.get('large-artifact').text, /内容过大/);
  panel.destroy();
});

test('PreviewPanel truncates actual artifact text preview even when metadata is smaller', async () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const longText = 'x'.repeat(MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS + 256);
  const blob = {
    size: 64 * 1024 + 10,
    slice() {
      return {
        async text() {
          return longText;
        }
      };
    }
  };
  const coordinator = createCoordinatorHarness(successState(), {
    readArtifactForCurrentState: async artifactId => ({
      artifact: {
        artifactId,
        kind: 'profile',
        role: 'profile',
        contentType: 'text/plain',
        length: 12
      },
      blob
    })
  });
  const panel = new PreviewPanel(null, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    getFlowRevision: () => 3
  });

  await panel.readArtifactPreview('json-artifact');

  const text = panel.artifactReadState.get('json-artifact').text;
  assert.match(text, /已截断/);
  assert.ok(text.length < MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS + 32);
  panel.destroy();
});

test('PreviewPanel destroy unsubscribes preview and structure listeners', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: []
  };
  const coordinator = createCoordinatorHarness(successState());
  const structureListeners = new Set();
  const panel = new PreviewPanel(null, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    getFlowRevision: () => 3,
    subscribeStructureState: listener => {
      structureListeners.add(listener);
      return () => structureListeners.delete(listener);
    }
  });

  assert.equal(coordinator.listenerCount(), 1);
  assert.equal(structureListeners.size, 1);

  panel.destroy();

  assert.equal(coordinator.listenerCount(), 0);
  assert.equal(structureListeners.size, 0);
});

test('PreviewPanelCapabilityAdapter projects selected node and routes preview requests through coordinator once', () => {
  const harness = createPreviewCapabilityHarness();
  let selectedOperator = null;
  const unsubscribe = harness.adapter.subscribeSelectedNode(operator => {
    selectedOperator = operator;
  });

  assert.equal(selectedOperator.id, 'node-1');
  assert.equal(selectedOperator.displayName, '阈值');
  assert.deepEqual(
    selectedOperator.parameters.map(parameter => [parameter.name, parameter.value]),
    [['Threshold', 128]]
  );

  harness.adapter.setActiveNode('node-1', { autoPreview: false });
  harness.adapter.requestPreview({ immediate: true, force: true, trigger: 'manual' });
  harness.adapter.cancelPreview();

  assert.deepEqual(harness.calls.setActiveNode.at(-1), {
    nodeId: 'node-1',
    options: { autoPreview: false }
  });
  assert.deepEqual(harness.calls.requestPreview, [{ immediate: true, force: true, trigger: 'manual' }]);
  assert.equal(harness.calls.cancelPreview, 1);

  unsubscribe();
});

test('PreviewPanelCapabilityOwner renders required states and uses one active preview request entry', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  assert.match(harness.container.innerHTML, /预览工作台/);
  assert.match(harness.container.innerHTML, /当前算子/);
  assert.match(harness.container.innerHTML, /手动预览/);
  assert.match(harness.container.innerHTML, /自动预览/);
  assert.match(harness.container.innerHTML, /取消预览/);
  assert.equal(harness.calls.setActiveNode.length, 1);
  assert.deepEqual(harness.calls.setActiveNode[0], {
    nodeId: 'node-1',
    options: { autoPreview: true }
  });

  owner.requestManualPreview();
  assert.deepEqual(harness.calls.requestPreview.at(-1), {
    immediate: true,
    force: true,
    trigger: 'manual'
  });
  assert.match(harness.container.innerHTML, /预览中/);

  harness.emitPreview({
    ...successState(),
    presenter: {
      statusText: '预览完成',
      inputImageSrc: null,
      outputImageSrc: null
    }
  });
  assert.match(harness.container.innerHTML, /预览完成/);
  assert.match(harness.container.innerHTML, /预览结果/);
  assert.match(harness.container.innerHTML, /中间结果/);

  harness.emitPreview({
    ...successState(),
    status: 'error',
    errorMessage: 'boom',
    presenter: {
      statusText: '预览失败',
      inputImageSrc: null,
      outputImageSrc: null
    }
  });
  assert.match(harness.container.innerHTML, /预览失败/);

  harness.adapter.cancelPreview();
  assert.match(harness.container.innerHTML, /预览已取消/);

  owner.dispose();
  assert.equal(harness.listeners.preview.size, 0);
  assert.equal(harness.listeners.selection.size, 0);
  assert.equal(harness.listeners.structure.size, 0);
  assert.equal(harness.container.innerHTML, '');
});

test('PreviewPanelCapabilityOwner honors auto toggle and clears UI after node deletion', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  owner.handleChange({
    target: {
      checked: false,
      dataset: { previewAuto: 'true' }
    }
  });
  harness.flowCanvasAdapter.selectNode('node-1');

  assert.deepEqual(harness.calls.setActiveNode.at(-1), {
    nodeId: 'node-1',
    options: { autoPreview: false }
  });

  harness.nodes.delete('node-1');
  harness.emitStructure();

  assert.match(harness.container.innerHTML, /节点已删除/);
  assert.deepEqual(harness.calls.setActiveNode.at(-1), {
    nodeId: null,
    options: { autoPreview: false }
  });

  owner.dispose();
});

test('PreviewPanelCapabilityOwner reads artifacts only through coordinator with bounded slices', async () => {
  const sliceCalls = [];
  const harness = createPreviewCapabilityHarness({
    readArtifactForCurrentState: async (artifactId, expectedIdentity) => ({
      artifact: {
        artifactId,
        kind: 'profile',
        role: 'profile',
        contentType: 'application/json',
        length: 20
      },
      blob: {
        size: 20,
        text() {
          throw new Error('full blob text must not be called');
        },
        slice(start, end) {
          sliceCalls.push([start, end, expectedIdentity.targetNodeId]);
          return {
            async text() {
              return JSON.stringify({
                token: 'secret-token',
                path: 'C:\\Users\\A\\Desktop\\ClearVision\\artifact.json',
                score: 1
              });
            }
          };
        }
      }
    })
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  harness.emitPreview({
    ...successState(),
    presenter: {
      statusText: '预览完成',
      inputImageSrc: null,
      outputImageSrc: null
    }
  });

  await owner.readArtifactPreview('json-artifact');

  assert.equal(harness.calls.artifactReads.length, 1);
  assert.equal(harness.calls.artifactReads[0].artifactId, 'json-artifact');
  assert.deepEqual(sliceCalls, [[0, 64 * 1024, 'node-1']]);
  const readState = owner.artifactReadState.get('json-artifact');
  assert.equal(readState.status, 'success');
  assert.ok(!readState.text.includes('secret-token'));
  assert.ok(!readState.text.includes('C:\\Users\\A'));
  assert.ok(readState.text.includes('[redacted-secret]'));
  assert.ok(readState.text.includes('[redacted-path]'));

  owner.dispose();
});

test('Preview Panel capability source and app composition keep legacy resources behind active owner flag', () => {
  const appSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');
  const ownerSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanelCapabilityOwner.mjs');
  const propertyPanelSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
  const indexSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/index.html');

  assert.match(appSource, /const PREVIEW_PANEL_CAPABILITY_FLAG_KEY = 'Studio2\.PreviewPanel'/);
  assert.match(appSource, /const PREVIEW_PANEL_CAPABILITY_ENABLED = readPreviewPanelCapabilityFlagOnce\(\);/);
  assert.match(appSource, /if \(isPreviewPanelCapabilityEnabled\(\)\) \{[\s\S]*disposeLegacyNodePreviewSurfaces\(\);[\s\S]*return;/);
  assert.match(appSource, /new PreviewPanelCapabilityOwner\(/);
  assert.match(appSource, /function shouldPreviewPanelCapabilityOwnSidebarPreview\(\)/);
  assert.match(appSource, /function shouldLegacyPropertyPanelOwnSidebarPreview\(\)/);
  assert.match(appSource, /function shouldHideUnownedSidebarPreviewHost\(\)/);
  assert.match(appSource, /if \(shouldHideUnownedSidebarPreviewHost\(\)\) \{[\s\S]*hostPanel\?\.classList\.add\('hidden'\)/);
  assert.match(appSource, /if \(!shouldPreviewPanelCapabilityOwnSidebarPreview\(\)\) \{[\s\S]*hostPanel\?\.classList\.remove\('hidden'\)/);
  assert.match(appSource, /previewResourcesEnabled: ownsPreviewSidebar/);
  assert.equal((appSource.match(/new PreviewPanelCapabilityOwner\(/g) || []).length, 1);
  assert.equal((appSource.match(/new NodePreviewCoordinator\(/g) || []).length, 1);
  assert.doesNotMatch(appSource, /import\s+NodePreviewOverlay\s+from\s+'\.\/features\/flow-editor\/nodePreviewOverlay\.js'/);
  assert.doesNotMatch(appSource, /import\s+NodePreviewInspector\s+from\s+'\.\/features\/flow-editor\/nodePreviewInspector\.js'/);
  assert.match(appSource, /nodePreviewOverlayModulePromise = import\('\.\/features\/flow-editor\/nodePreviewOverlay\.js'\)/);
  assert.match(appSource, /nodePreviewInspectorModulePromise = import\('\.\/features\/flow-editor\/nodePreviewInspector\.js'\)/);
  assert.ok(appSource.indexOf('if (isPreviewPanelCapabilityEnabled())') < appSource.indexOf('new NodePreviewOverlay('));
  assert.ok(appSource.indexOf('if (!isPreviewPanelCapabilityEnabled())') < appSource.indexOf('nodePreviewCoordinator?.setActiveNode(node);'));

  assert.match(indexSource, /id="preview-panel"/);
  assert.match(ownerSource, /PreviewPanelCapabilityAdapter/);
  assert.match(ownerSource, /requestPreview/);
  assert.match(ownerSource, /readArtifactForCurrentState/);
  assert.match(ownerSource, /buildOperatorResultViewModel/);
  assert.match(ownerSource, /预览工作台/);
  assert.match(ownerSource, /请选择一个算子/);
  assert.match(ownerSource, /当前算子/);
  assert.match(ownerSource, /手动预览/);
  assert.match(ownerSource, /自动预览/);
  assert.match(ownerSource, /取消预览/);
  assert.match(ownerSource, /预览中/);
  assert.match(ownerSource, /预览完成/);
  assert.match(ownerSource, /预览失败/);
  assert.match(ownerSource, /预览已取消/);
  assert.match(ownerSource, /节点已删除/);
  assert.match(ownerSource, /预览结果/);
  assert.match(ownerSource, /中间结果/);
  assert.match(ownerSource, /端口与耗时/);
  assert.doesNotMatch(ownerSource, /\bfetch\s*\(/);
  assert.doesNotMatch(ownerSource, /httpClient/);
  assert.doesNotMatch(ownerSource, /localStorage|IndexedDB|InspectionHistory|Evidence/);
  assert.doesNotMatch(ownerSource, /new ImageCanvas|createElement\('canvas'|document\.createElement\('canvas'/);
  assert.match(propertyPanelSource, /previewResourcesEnabled/);
});
