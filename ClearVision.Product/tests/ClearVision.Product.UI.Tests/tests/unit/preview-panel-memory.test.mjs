import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  NodePreviewCoordinator,
  buildPreviewInputImageHash,
  buildPreviewParameterSnapshot
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';
import {
  PreviewPanel
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanel.js';
import {
  PreviewPanelCapabilityOwner,
  buildRegionInputGuidance,
  createPreviewPanelCapabilityAdapter
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanelCapabilityOwner.mjs';
import {
  PIXEL_PROBE_DEFAULT_MESSAGE
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/imagePixelProbe.mjs';
import {
  MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS,
  STALE_PREVIEW_MESSAGE,
  buildOperatorResultViewModel,
  buildSafeJsonPreview
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/operatorResultViewModel.mjs';

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

function waitFor(assertion, timeoutMs = 1000) {
  const startedAt = Date.now();
  return new Promise((resolve, reject) => {
    const tick = () => {
      try {
        assertion();
        resolve();
      } catch (error) {
        if (Date.now() - startedAt > timeoutMs) {
          reject(error);
          return;
        }
        setTimeout(tick, 0);
      }
    };

    tick();
  });
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
    artifactReads: [],
    openImages: []
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
      if (typeof options.requestActivePreview === 'function') {
        return options.requestActivePreview(requestOptions);
      }

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
    }),
    getProjectId: () => options.projectId ?? 'project-1',
    getInputImageBase64: () => options.inputImageBase64 ?? null,
    onOpenPreviewImage: imageSource => calls.openImages.push(imageSource)
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

function createDeferred() {
  let resolve;
  let reject;
  const promise = new Promise((innerResolve, innerReject) => {
    resolve = innerResolve;
    reject = innerReject;
  });

  return { promise, resolve, reject };
}

function buildRealPreviewResponse(nodeId, options, overrides = {}) {
  return {
    success: true,
    executionTimeMs: 12,
    outputImageBase64: 'OUTPUT_IMAGE',
    outputData: { Score: 0.98 },
    artifacts: [],
    observation: {
      schemaVersion: 'execution-observation.v1',
      identity: {
        projectId: 'project-1',
        targetNodeId: nodeId,
        debugSessionId: options.debugSessionId,
        clientRequestSequence: options.clientRequestSequence,
        flowRevision: options.flowRevision
      },
      outcome: {
        success: true,
        executionTimeMs: 12,
        executedOperatorCount: 1
      },
      summary: [],
      detail: {
        kind: 'dictionary',
        displayValue: '1 field',
        children: []
      },
      diagnostics: []
    },
    ...overrides
  };
}

function createRealPreviewOwnerHarness(options = {}) {
  const listeners = {
    selection: new Set(),
    structure: new Set()
  };
  const defaultNode = options.node ?? {
    id: 'node-1',
    type: 'TemplateMatching',
    title: 'Template',
    parameters: [{ name: 'Threshold', value: 128 }],
    outputs: [{ type: 'image' }]
  };
  const nodes = options.nodes instanceof Map
    ? options.nodes
    : new Map([[defaultNode.id, defaultNode]]);
  let selectedNodeId = options.selectedNodeId ?? defaultNode.id;
  let flowRevision = options.flowRevision ?? 3;
  const executorCalls = [];
  const toasts = [];
  const flowCanvasAdapter = {
    nodes,
    get selectedNode() {
      return selectedNodeId;
    },
    set selectedNode(value) {
      selectedNodeId = value;
    },
    getFlowRevision: () => flowRevision,
    subscribeSelection(listener) {
      listeners.selection.add(listener);
      listener({
        selectedNodeId,
        reason: 'initial',
        flowRevision
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
      selectedNodeId = nodeId;
      listeners.selection.forEach(listener => listener({
        selectedNodeId,
        reason: 'selectNode',
        flowRevision
      }));
      return true;
    }
  };
  const coordinator = new NodePreviewCoordinator({
    getProjectId: () => options.projectId ?? 'project-1',
    getFlowRevision: () => flowRevision,
    getNodeById: nodeId => nodes.get(nodeId) || null,
    getOperatorMetadata: type => ({
      displayName: type,
      outputPorts: [{ dataType: 'image' }]
    }),
    getInputImageBase64: options.getInputImageBase64 ?? (() => options.inputImageBase64 ?? 'INPUT_IMAGE'),
    previewExecutor: async (nodeId, executorOptions) => {
      executorCalls.push({ nodeId, options: executorOptions });
      if (typeof options.previewExecutor === 'function') {
        return options.previewExecutor(nodeId, executorOptions, executorCalls.length);
      }
      return buildRealPreviewResponse(nodeId, executorOptions);
    },
    artifactClient: {
      getPreviewArtifactBlob: async () => ({ blob: { size: 0 } }),
      deletePreviewArtifact: async () => {}
    },
    debounceMs: options.debounceMs ?? 0
  });
  const adapter = createPreviewPanelCapabilityAdapter({
    flowCanvasAdapter,
    previewCoordinator: coordinator,
    getOperatorMetadata: type => ({
      displayName: type,
      outputPorts: [{ dataType: 'image' }]
    }),
    getProjectId: () => options.projectId ?? 'project-1',
    getInputImageBase64: () => options.inputImageHashBase64 ?? 'INPUT_IMAGE'
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
  const owner = new PreviewPanelCapabilityOwner(container, {
    previewAdapter: adapter,
    showToast(message, level) {
      toasts.push({ message, level });
    }
  });

  return {
    owner,
    adapter,
    coordinator,
    container,
    nodes,
    flowCanvasAdapter,
    executorCalls,
    toasts,
    setFlowRevision(value) {
      flowRevision = value;
    },
    destroy() {
      owner.dispose();
      coordinator.destroy();
    }
  };
}

function previewActionEvent(action, dataset = {}) {
  const target = {
    dataset: {
      previewAction: action,
      ...dataset
    },
    closest() {
      return target;
    }
  };

  return {
    target,
    preventDefault() {}
  };
}

test('Region morphology preview guidance requires Region and rejects Image or Contour substitutes', () => {
  const operator = {
    type: 'RegionErosion',
    inputPorts: [
      { name: 'Region', dataType: 'Region', isRequired: true },
      { name: 'Image', dataType: 'Image', isRequired: false }
    ]
  };

  const guidance = buildRegionInputGuidance(operator, { hasRegionInputConnection: false });

  assert.equal(guidance.title, '当前缺少 Region');
  assert.match(guidance.summary, /Image\/Contour 不能直接替代/);
  assert.match(guidance.summary, /BinaryImageToRegion/);
  assert.match(guidance.summary, /可选 Image 输入仅用于参考图和可视化/);
  assert.equal(buildRegionInputGuidance(operator, { hasRegionInputConnection: true }), null);
  assert.equal(buildRegionInputGuidance({ ...operator, type: 'BlobAnalysis' }), null);
});

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

test('PreviewPanel default owner renders and clears missing Region guidance from live connections', () => {
  let connected = false;
  const operator = {
    id: 'region-node',
    type: 'RegionErosion',
    title: '区域腐蚀',
    inputs: [
      { name: 'Region', dataType: 'Region', isRequired: true },
      { name: 'Image', dataType: 'Image', isRequired: false }
    ],
    parameters: []
  };
  const coordinator = createCoordinatorHarness(successState({ activeNodeId: operator.id }));
  const container = new PreviewPanelFakeContainer();
  const panel = new PreviewPanel(container, {
    getOperator: () => operator,
    previewCoordinator: coordinator,
    hasInputConnection: () => connected,
    getFlowRevision: () => 3
  });

  assert.equal(container.querySelector('#preview-status-text').textContent, '当前缺少 Region');
  assert.match(container.querySelector('#preview-region-guidance').innerHTML, /Image\/Contour 不能直接替代/);
  assert.match(container.querySelector('#preview-region-guidance').innerHTML, /BinaryImageToRegion/);
  assert.equal(container.querySelector('#btn-preview-refresh').disabled, true);

  connected = true;
  panel.applyPreviewState();

  assert.equal(container.querySelector('#preview-region-guidance').innerHTML, '');
  assert.equal(container.querySelector('#btn-preview-refresh').disabled, false);
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
  assert.equal(stale.stateMessage, STALE_PREVIEW_MESSAGE);
  assert.deepEqual(stale.staleReasons.sort(), ['flowRevision', 'parameters']);

  const disabled = buildOperatorResultViewModel(operator, successState(), {
    liveNode: { id: 'node-1', type: 'Thresholding', disabled: true, parameters: operator.parameters },
    flowRevision: 3
  });
  assert.equal(disabled.status, 'disabled');
  assert.match(disabled.stateMessage, /禁用/);
});

test('OperatorResultViewModel marks stale project, target node, and input image hash mismatches', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const state = successState({
    request: {
      projectId: 'old-project',
      nodeId: 'node-2',
      flowRevision: 3,
      parameterSnapshot: buildPreviewParameterSnapshot(operator.parameters),
      inputImageHash: buildPreviewInputImageHash('old-input-image'),
      requestKey: 'old-project:node-2:3:params:old'
    },
    observation: observation({
      identity: identity({
        projectId: 'old-project',
        targetNodeId: 'node-2',
        flowRevision: 3
      })
    })
  });

  const model = buildOperatorResultViewModel(operator, state, {
    flowRevision: 3,
    projectId: 'project-1',
    inputImageHash: buildPreviewInputImageHash('new-input-image')
  });

  assert.equal(model.status, 'stale');
  assert.equal(model.stateMessage, STALE_PREVIEW_MESSAGE);
  assert.deepEqual(model.staleReasons.sort(), ['inputImageHash', 'projectId', 'targetNodeId']);
});

test('OperatorResultViewModel normalizes PascalCase diagnostics and redacts local paths', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const diagnosticObservation = observation();
  delete diagnosticObservation.diagnostics;
  diagnosticObservation.Diagnostics = [
    { Code: 'OBS001', Message: 'Camera input missing' }
  ];
  diagnosticObservation.outcome = {
    success: false,
    executionTimeMs: 0,
    ErrorMessage: 'Parameter validation failed',
    FailedOperatorName: '定位算子',
    FailedOperatorType: 'BlobAnalysis'
  };
  const state = successState({
    status: 'error',
    ErrorMessage: 'Preview backend unavailable at C:\\Users\\A\\secret\\runtime.log',
    Diagnostics: [
      { Code: 'D001', Message: '模板 C:\\Users\\A\\templates\\part.ncc 缺失' }
    ],
    MissingResources: [
      { Name: 'Template', PathHint: 'C:\\Users\\A\\templates\\part.ncc' }
    ],
    FailedOperatorName: '定位算子',
    FailedOperatorType: 'BlobAnalysis',
    observation: diagnosticObservation
  });

  const model = buildOperatorResultViewModel(operator, state, { flowRevision: 3 });
  const diagnosticText = model.diagnostics.map(item => `${item.code}:${item.message}`).join('\n');

  assert.match(diagnosticText, /预览超时或服务不可用|后端异常/);
  assert.match(diagnosticText, /缺少资源/);
  assert.match(diagnosticText, /失败算子/);
  assert.match(diagnosticText, /D001/);
  assert.match(diagnosticText, /OBS001/);
  assert.doesNotMatch(diagnosticText, /C:\\Users\\A/);
  assert.match(diagnosticText, /\[redacted-path\]/);
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
  assert.ok(model.executionSummaryItems.some(item => item.label === '节点名称' && item.value === 'Threshold'));
  assert.ok(model.keyOutputs.some(item => item.label === '分数' && item.value === '0.98'));
  assert.ok(model.keyOutputs.some(item => item.label === 'Circle' && item.value.includes('center')));
  assert.ok(model.imageSummaries.some(item => item.label === '掩膜'));
  assert.ok(model.rawDataSections.some(section => section.label === '标量输出'));
  assert.ok(model.advancedDiagnostics.some(item => item.code === 'low-contrast'));
  assert.equal(model.artifacts.length, 2);
  assert.equal(model.sceneSummary.available, true);
  assert.equal(model.sceneSummary.primitiveCount, 1);
  assert.ok(model.diagnostics.some(item => item.code === 'low-contrast'));
  assert.deepEqual(model.nodeResults.map(item => item.statusKind), ['success', 'disabled']);
});

test('OperatorResultViewModel productizes noisy preview observations without losing raw data', () => {
  const operator = {
    id: 'node-1',
    type: 'RegionClosing',
    title: 'Region Closing',
    parameters: []
  };
  const noisyObservation = observation({
    diagnostics: [
      'Observation detail omitted because depth-limit was reached.',
      'Observation detail omitted because depth-limit was reached.',
      'Observation output key does not match a declared output port; canonical ResultPath metadata omitted.'
    ]
  });
  noisyObservation.detail = node('dictionary', {
    pathHint: '$',
    children: [
      node('object', {
        name: 'spatialContext',
        displayValue: 'System.Text.Json.JsonElement',
        originalType: 'System.Text.Json.JsonElement',
        children: [node('string', { name: 'FrameId', displayValue: 'frame-1' })]
      }),
      node('number', {
        name: 'Area',
        displayValue: '42',
        outputPortId: 'area-port',
        outputPortName: 'Area',
        resultPathVersion: 1,
        resultPath: '$["Area"]'
      }),
      node('string', {
        name: 'depth-limit',
        displayValue: 'Observation detail omitted because depth-limit was reached.'
      }),
      node('image', {
        name: 'outputImage',
        displayValue: 'image artifact; content omitted.',
        artifact: {
          artifactId: 'image-artifact',
          kind: 'image',
          role: 'outputImage',
          contentType: 'image/png',
          length: 4_608_000,
          width: 1280,
          height: 960
        }
      })
    ]
  });

  const model = buildOperatorResultViewModel(operator, successState({
    observation: noisyObservation,
    outputData: {
      outputImage: 'data:image/png;base64,IMAGE',
      Area: 42,
      spatialContext: { frameId: 'frame-1', matrix: [1, 0, 0] },
      diagnostics: ['resource-descriptor']
    },
    artifacts: [{
      artifactId: 'image-artifact',
      kind: 'image',
      role: 'outputImage',
      contentType: 'image/png',
      length: 4_608_000,
      width: 1280,
      height: 960
    }]
  }), { flowRevision: 3 });

  assert.ok(model.keyOutputs.some(item => item.label === '面积' && item.value === '42'));
  assert.ok(model.keyOutputs.some(item => item.label === '空间上下文' && item.value === '1 个字段'));
  const outputImageSummary = model.imageSummaries.find(item => item.label === '输出图像');
  assert.match(outputImageSummary?.summary || '', /4\.4 MB/);
  assert.match(outputImageSummary?.summary || '', /图像内容已省略/);
  assert.doesNotMatch(outputImageSummary?.summary || '', /image artifact; content omitted/i);
  assert.ok(model.advancedDiagnostics.some(item => /详情过深，已自动折叠/.test(item.message)));
  assert.ok(model.advancedDiagnostics.some(item => /输出键不属于声明端口/.test(item.message)));
  assert.ok(model.rawDataSections.some(section => section.items.some(item => item.meta === '类型：JSON 对象')));
  assert.doesNotMatch(model.keyOutputs.map(item => `${item.label}:${item.value}:${item.meta}`).join('\n'), /System\.Text\.Json\.JsonElement|depth-limit|resource-descriptor/);
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

  owner.requestManualPreview();
  assert.equal(harness.calls.requestPreview.length, 1);

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
  assert.match(harness.container.innerHTML, /模块结果/);

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

test('PreviewPanelCapabilityOwner keeps Blob semantics outside the pixel probe status', () => {
  const blobNode = {
    id: 'node-1',
    type: 'BlobAnalysis',
    title: 'Blob 分析',
    parameters: [{ name: 'MaxArea', value: 1000 }],
    outputs: [
      { name: 'Image', type: 'Image' },
      { name: 'Blobs', type: 'BlobList' },
      { name: 'BlobCount', type: 'Integer' }
    ]
  };
  const harness = createPreviewCapabilityHarness({ node: blobNode });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  harness.emitPreview({
    ...successState({
      nodeType: 'BlobAnalysis',
      parameters: blobNode.parameters,
      outputData: {
        BlobCount: 2,
        Blobs: [{ Id: 1 }, { Id: 2 }]
      },
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: 'data:image/png;base64:BLOB'
      }
    })
  });

  assert.match(harness.container.innerHTML, /blob-preview-semantics/);
  assert.match(harness.container.innerHTML, /BlobCount 为过滤后数量/);
  assert.match(harness.container.innerHTML, /底图保留原始目标，未标记不表示通过/);
  const pixelProbeStatus = harness.container.innerHTML.match(/data-role="pixel-probe-status"[^>]*>([^<]*)<\/div>/)?.[1] || '';
  assert.doesNotMatch(pixelProbeStatus, /BlobCount|底图保留/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner marks stale Blob output and clears it after a fresh preview', () => {
  const blobNode = {
    id: 'node-1',
    type: 'BlobAnalysis',
    title: 'Blob 分析',
    parameters: [{ name: 'MaxArea', value: 200 }],
    outputs: [
      { name: 'Image', type: 'Image' },
      { name: 'Blobs', type: 'BlobList' },
      { name: 'BlobCount', type: 'Integer' }
    ]
  };
  const harness = createPreviewCapabilityHarness({ node: blobNode });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  harness.emitPreview({
    ...successState({
      nodeType: 'BlobAnalysis',
      parameters: [{ name: 'MaxArea', value: 1000 }],
      outputData: {
        BlobCount: 2,
        Blobs: [{ Id: 1 }, { Id: 2 }]
      },
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: 'data:image/png;base64:OLD_BLOB'
      }
    })
  });

  assert.match(harness.container.innerHTML, /data-status="stale"/);
  assert.match(harness.container.innerHTML, new RegExp(STALE_PREVIEW_MESSAGE));
  assert.match(harness.container.innerHTML, /旧输出摘要/);
  assert.match(harness.container.innerHTML, /Blob数量（过滤后）/);
  assert.match(harness.container.innerHTML, /data-stale="true"/);
  assert.doesNotMatch(harness.container.innerHTML, /preview-capability-blob-semantics/);

  harness.emitPreview({
    ...successState({
      nodeType: 'BlobAnalysis',
      parameters: blobNode.parameters,
      outputData: {
        BlobCount: 1,
        Blobs: [{ Id: 1 }]
      },
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: 'data:image/png;base64:NEW_BLOB'
      }
    })
  });

  assert.match(harness.container.innerHTML, /data-status="success"/);
  assert.match(harness.container.innerHTML, /preview-capability-blob-semantics/);
  assert.doesNotMatch(harness.container.innerHTML, /data-stale="true"/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner renders ImageSave safe preview as completed summary', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  harness.emitPreview({
    ...successState({
      nodeType: 'ImageSave',
      title: '图像保存',
      inputImageBase64: 'INPUT_IMAGE',
      outputImageBase64: 'INPUT_IMAGE',
      outputData: {
        Directory: 'D:\\CV\\Preview',
        FileNameTemplate: 'edge_{timestamp}.jpg',
        EstimatedFileName: 'edge_20260709_120000.jpg',
        Format: 'jpg',
        Quality: 88,
        Message: '预览模式不会写入磁盘；点击运行流程后才会保存图像。',
        WillWriteToDisk: false,
        PreviewMode: 'ImageSaveDryRun',
        PreviewBlocked: false
      },
      presenter: {
        statusText: '预览完成',
        inputImageSrc: 'data:image/png;base64,INPUT_IMAGE',
        outputImageSrc: 'data:image/png;base64,INPUT_IMAGE'
      }
    })
  });

  assert.match(harness.container.innerHTML, /预览完成/);
  assert.match(harness.container.innerHTML, /保存目录/);
  assert.match(harness.container.innerHTML, /命名规则/);
  assert.match(harness.container.innerHTML, /预计文件名/);
  assert.match(harness.container.innerHTML, /预览模式不会写入磁盘；点击运行流程后才会保存图像。/);
  assert.doesNotMatch(harness.container.innerHTML, /预览失败/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner renders side-effect preview block as warning state', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  harness.emitPreview({
    ...successState(),
    status: 'blocked',
    errorMessage: '节点预览已安全拦截副作用算子“TcpCommunication”：预览不会执行外部动作，正式运行流程时才会执行。',
    outputData: null,
    outputImageBase64: null,
    presenter: {
      statusText: '预览已安全拦截',
      inputImageSrc: null,
      outputImageSrc: null
    }
  });

  assert.match(harness.container.innerHTML, /安全拦截/);
  assert.match(harness.container.innerHTML, /正式运行流程时才会执行/);
  assert.match(harness.container.innerHTML, /preview-capability-empty warning/);
  assert.doesNotMatch(harness.container.innerHTML, /预览失败/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner restores manual preview controls when request throws', () => {
  const toasts = [];
  const harness = createPreviewCapabilityHarness({
    requestActivePreview() {
      throw new Error('executor offline');
    }
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter,
    showToast(message, level) {
      toasts.push({ message, level });
    }
  });

  owner.requestManualPreview();

  assert.equal(harness.calls.requestPreview.length, 1);
  assert.equal(owner.manualPreviewPending, false);
  assert.match(harness.container.innerHTML, /data-preview-action="manual-preview" aria-disabled="false"/);
  assert.deepEqual(toasts, [{ message: '预览请求失败：executor offline', level: 'error' }]);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner restores manual preview controls when request promise rejects', async () => {
  const toasts = [];
  let rejectPreview;
  const requestPromise = new Promise((_, reject) => {
    rejectPreview = reject;
  });
  const harness = createPreviewCapabilityHarness({
    requestActivePreview() {
      return requestPromise;
    }
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter,
    showToast(message, level) {
      toasts.push({ message, level });
    }
  });

  owner.requestManualPreview();

  assert.equal(harness.calls.requestPreview.length, 1);
  assert.equal(owner.manualPreviewPending, true);
  assert.match(harness.container.innerHTML, /data-preview-action="manual-preview" disabled aria-disabled="true"/);

  rejectPreview(new Error('queued executor offline'));
  await requestPromise.catch(() => {});
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(owner.manualPreviewPending, false);
  assert.match(harness.container.innerHTML, /data-preview-action="manual-preview" aria-disabled="false"/);
  assert.deepEqual(toasts, [{ message: '预览请求失败：queued executor offline', level: 'error' }]);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner allows canceling a pending manual preview before loading state', async () => {
  const toasts = [];
  let rejectPreview;
  const requestPromise = new Promise((_, reject) => {
    rejectPreview = reject;
  });
  const harness = createPreviewCapabilityHarness({
    requestActivePreview() {
      return requestPromise;
    }
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter,
    showToast(message, level) {
      toasts.push({ message, level });
    }
  });

  owner.requestManualPreview();

  assert.equal(owner.manualPreviewPending, true);
  assert.match(
    harness.container.innerHTML,
    /data-preview-action="cancel-preview" aria-disabled="false"/
  );

  owner.handleClick(previewActionEvent('cancel-preview'));

  assert.equal(harness.calls.cancelPreview, 1);
  assert.equal(owner.manualPreviewPending, false);
  assert.match(harness.container.innerHTML, /data-preview-action="manual-preview" aria-disabled="false"/);

  rejectPreview(new Error('canceled executor offline'));
  await requestPromise.catch(() => {});
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.deepEqual(toasts, []);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner ignores stale manual preview request rejections after node switch', async () => {
  const toasts = [];
  let rejectFirst;
  let resolveSecond;
  const firstRequest = new Promise((_, reject) => {
    rejectFirst = reject;
  });
  const secondRequest = new Promise(resolve => {
    resolveSecond = resolve;
  });
  const requests = [firstRequest, secondRequest];
  const harness = createPreviewCapabilityHarness({
    requestActivePreview() {
      return requests.shift();
    }
  });
  harness.nodes.set('node-2', {
    id: 'node-2',
    type: 'Thresholding',
    title: 'Threshold B',
    parameters: [{ name: 'Threshold', value: 64 }],
    outputs: [{ type: 'image' }]
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter,
    showToast(message, level) {
      toasts.push({ message, level });
    }
  });

  owner.requestManualPreview();
  assert.equal(owner.manualPreviewPending, true);

  harness.flowCanvasAdapter.selectNode('node-2');
  owner.requestManualPreview();
  assert.equal(owner.currentNodeId, 'node-2');
  assert.equal(owner.manualPreviewPending, true);
  assert.equal(harness.calls.requestPreview.length, 2);

  rejectFirst(new Error('stale executor offline'));
  await firstRequest.catch(() => {});
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(owner.currentNodeId, 'node-2');
  assert.equal(owner.manualPreviewPending, true);
  assert.deepEqual(toasts, []);

  resolveSecond();
  await secondRequest;
  owner.dispose();
});

test('PreviewPanelCapabilityOwner keeps manual preview pending when unrelated preview state arrives', async () => {
  const toasts = [];
  let rejectPreview;
  const requestPromise = new Promise((_, reject) => {
    rejectPreview = reject;
  });
  const harness = createPreviewCapabilityHarness({
    requestActivePreview() {
      return requestPromise;
    }
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter,
    showToast(message, level) {
      toasts.push({ message, level });
    }
  });

  owner.requestManualPreview();
  assert.equal(owner.currentNodeId, 'node-1');
  assert.equal(owner.manualPreviewPending, true);

  harness.emitPreview({
    ...successState({
      activeNodeId: 'node-2',
      request: {
        projectId: 'project-1',
        nodeId: 'node-2',
        flowRevision: 3,
        parameterSnapshot: [],
        requestKey: 'request-node-2'
      }
    }),
    presenter: {
      statusText: '其他节点预览完成',
      inputImageSrc: null,
      outputImageSrc: null
    }
  });

  assert.equal(owner.currentNodeId, 'node-1');
  assert.equal(owner.manualPreviewPending, true);
  assert.match(harness.container.innerHTML, /data-preview-action="manual-preview" disabled aria-disabled="true"/);
  assert.deepEqual(toasts, []);

  owner.requestManualPreview();
  assert.equal(harness.calls.requestPreview.length, 1);

  rejectPreview(new Error('current executor offline'));
  await requestPromise.catch(() => {});
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(owner.manualPreviewPending, false);
  assert.equal(toasts.length, 1);
  assert.match(toasts[0].message, /current executor offline/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner ignores manual preview rejections after a terminal preview state', async () => {
  const toasts = [];
  let rejectPreview;
  const requestPromise = new Promise((_, reject) => {
    rejectPreview = reject;
  });
  const harness = createPreviewCapabilityHarness({
    requestActivePreview() {
      return requestPromise;
    }
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter,
    showToast(message, level) {
      toasts.push({ message, level });
    }
  });

  owner.requestManualPreview();
  assert.equal(owner.manualPreviewPending, true);

  harness.emitPreview({
    ...successState(),
    presenter: {
      statusText: '预览完成',
      inputImageSrc: null,
      outputImageSrc: null
    }
  });
  assert.equal(owner.manualPreviewPending, false);

  rejectPreview(new Error('late executor offline'));
  await requestPromise.catch(() => {});
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.deepEqual(toasts, []);
  assert.match(harness.container.innerHTML, /预览完成|棰勮瀹屾垚/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner with real coordinator restores controls when input image provider fails', async () => {
  for (const mode of ['throw', 'reject']) {
    const harness = createRealPreviewOwnerHarness({
      getInputImageBase64() {
        if (mode === 'throw') {
          throw new Error('input provider throw');
        }
        return Promise.reject(new Error('input provider reject'));
      }
    });

    try {
      harness.owner.requestManualPreview();
      await waitFor(() => {
        assert.equal(harness.coordinator.getState().status, 'error');
        assert.match(harness.coordinator.getState().errorMessage, /input provider/);
      });

      assert.equal(harness.owner.manualPreviewPending, false);
      assert.match(harness.container.innerHTML, /data-preview-action="manual-preview" aria-disabled="false"/);
      assert.deepEqual(harness.toasts, []);
    } finally {
      harness.destroy();
    }
  }
});

test('PreviewPanelCapabilityOwner with real coordinator reports preview executor rejection as controlled state', async () => {
  const harness = createRealPreviewOwnerHarness({
    previewExecutor: async () => {
      throw new Error('executor rejected');
    }
  });

  try {
    harness.owner.requestManualPreview();
    await waitFor(() => {
      assert.equal(harness.coordinator.getState().status, 'error');
      assert.match(harness.coordinator.getState().errorMessage, /executor rejected/);
    });

    assert.equal(harness.owner.manualPreviewPending, false);
    assert.match(harness.container.innerHTML, /data-preview-action="manual-preview" aria-disabled="false"/);
    assert.deepEqual(harness.toasts, []);
  } finally {
    harness.destroy();
  }
});

test('PreviewPanelCapabilityOwner with real coordinator ignores canceled manual preview rejection toasts', async () => {
  const deferred = createDeferred();
  const harness = createRealPreviewOwnerHarness({
    previewExecutor: () => deferred.promise
  });

  try {
    harness.owner.requestManualPreview();
    await waitFor(() => assert.equal(harness.coordinator.getState().status, 'loading'));

    harness.owner.cancelCurrentPreview();
    await waitFor(() => assert.equal(harness.coordinator.getState().status, 'canceled'));

    deferred.reject(new Error('late canceled rejection'));
    await new Promise(resolve => setTimeout(resolve, 0));
    await new Promise(resolve => setTimeout(resolve, 0));

    assert.equal(harness.coordinator.getState().status, 'canceled');
    assert.deepEqual(harness.toasts, []);
  } finally {
    harness.destroy();
  }
});

test('PreviewPanelCapabilityOwner with real coordinator ignores stale rejection after node switch', async () => {
  const firstRequest = createDeferred();
  const node1 = {
    id: 'node-1',
    type: 'TemplateMatching',
    title: 'Template A',
    parameters: [{ name: 'Threshold', value: 128 }],
    outputs: [{ type: 'image' }]
  };
  const node2 = {
    id: 'node-2',
    type: 'TemplateMatching',
    title: 'Template B',
    parameters: [{ name: 'Threshold', value: 64 }],
    outputs: [{ type: 'image' }]
  };
  const harness = createRealPreviewOwnerHarness({
    node: node1,
    nodes: new Map([[node1.id, node1], [node2.id, node2]]),
    previewExecutor: async (nodeId, executorOptions) => {
      if (nodeId === 'node-1') {
        return firstRequest.promise;
      }
      return buildRealPreviewResponse(nodeId, executorOptions, {
        outputData: { Score: 0.75 }
      });
    }
  });

  try {
    harness.owner.requestManualPreview();
    await waitFor(() => {
      assert.equal(harness.coordinator.getState().activeNodeId, 'node-1');
      assert.equal(harness.coordinator.getState().status, 'loading');
    });

    harness.flowCanvasAdapter.selectNode('node-2');
    harness.owner.requestManualPreview();
    await waitFor(() => {
      assert.equal(harness.coordinator.getState().activeNodeId, 'node-2');
      assert.equal(harness.coordinator.getState().status, 'success');
    });

    firstRequest.reject(new Error('stale node rejection'));
    await new Promise(resolve => setTimeout(resolve, 0));
    await new Promise(resolve => setTimeout(resolve, 0));

    assert.equal(harness.coordinator.getState().activeNodeId, 'node-2');
    assert.equal(harness.coordinator.getState().status, 'success');
    assert.deepEqual(harness.coordinator.getState().outputData, { Score: 0.75 });
    assert.deepEqual(harness.toasts, []);
  } finally {
    harness.destroy();
  }
});

test('PreviewPanelCapabilityOwner with real coordinator keeps debounced auto and immediate manual versions isolated', async () => {
  const node = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }],
    outputs: [{ type: 'image' }]
  };
  const harness = createRealPreviewOwnerHarness({
    node,
    debounceMs: 100,
    previewExecutor: async (nodeId, executorOptions) => buildRealPreviewResponse(nodeId, executorOptions)
  });

  try {
    const autoPromise = harness.coordinator.requestActivePreview({
      immediate: false,
      debounceMs: 100,
      trigger: 'auto'
    });

    harness.owner.requestManualPreview();
    const autoResult = await autoPromise;
    await waitFor(() => assert.equal(harness.coordinator.getState().status, 'success'));
    await new Promise(resolve => setTimeout(resolve, 150));

    assert.equal(autoResult.status, 'superseded');
    assert.equal(harness.executorCalls.length, 1);
    assert.equal(harness.coordinator.getState().activeNodeId, 'node-1');
    assert.equal(harness.coordinator.getState().status, 'success');
  } finally {
    harness.destroy();
  }
});

test('PreviewPanelCapabilityOwner renders idle prerequisite failures with layered empty states', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  harness.emitPreview({
    ...successState({
      activeNodeId: 'node-1',
      status: 'idle',
      executionTimeMs: null,
      errorMessage: null,
      outputImageBase64: null,
      outputData: null,
      observation: null,
      artifacts: [],
      presenter: {
        statusText: '等待预览',
        inputImageSrc: null,
        outputImageSrc: null
      }
    })
  });

  assert.match(harness.container.innerHTML, /data-status="idle">等待预览/);
  assert.match(harness.container.innerHTML, /等待预览，暂无输出图像/);
  assert.doesNotMatch(harness.container.innerHTML, /预览完成，但没有返回图像输出/);

  const emitIdleError = errorMessage => harness.emitPreview({
    ...successState({
      activeNodeId: 'node-1',
      status: 'idle',
      executionTimeMs: null,
      errorMessage,
      outputImageBase64: null,
      outputData: null,
      observation: null,
      artifacts: [],
      presenter: {
        statusText: errorMessage,
        inputImageSrc: null,
        outputImageSrc: null
      }
    })
  });

  emitIdleError('请先新建/保存/打开工程后再预览');
  assert.match(harness.container.innerHTML, /data-status="idle-error">请先新建\/保存\/打开工程后再预览/);
  assert.match(harness.container.innerHTML, /请先新建\/保存\/打开工程后再预览/);
  assert.doesNotMatch(harness.container.innerHTML, /等待预览，暂无输出图像/);

  emitIdleError('请先配置文件路径');
  assert.match(harness.container.innerHTML, /data-status="idle-error">缺输入图或采集源/);
  assert.match(harness.container.innerHTML, /请先配置文件路径/);
  assert.match(harness.container.innerHTML, /缺输入图或采集源，无法生成输出图像/);
  assert.doesNotMatch(harness.container.innerHTML, /预览完成，但没有返回图像输出/);

  emitIdleError('输入图像过大，已跳过预览。请先缩小图像或执行完整检测。');
  assert.match(harness.container.innerHTML, /data-status="idle-error">输入图像过大/);
  assert.match(harness.container.innerHTML, /输入图像过大，无法生成输出图像/);
  assert.doesNotMatch(harness.container.innerHTML, /预览完成，但没有返回图像输出/);

  emitIdleError('该算子可能执行 AI、OCR、模板或特征匹配等高成本计算，请点击“手动预览”执行。');
  assert.match(harness.container.innerHTML, /data-status="idle-error">需手动预览/);
  assert.match(harness.container.innerHTML, /需手动预览后生成输出图像/);
  assert.doesNotMatch(harness.container.innerHTML, /预览完成，但没有返回图像输出/);

  emitIdleError('C:\\Users\\A\\secret\\preview.log unavailable');
  assert.match(harness.container.innerHTML, /data-status="idle-error">预览未运行/);
  assert.match(harness.container.innerHTML, /预览未运行，暂无输出图像/);
  assert.doesNotMatch(harness.container.innerHTML, /C:\\Users\\A/);
  assert.doesNotMatch(harness.container.innerHTML, /预览完成，但没有返回图像输出/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner shows image operations and routes open image through adapter', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  const imageSource = 'data:image/png;base64,TEST_IMAGE';

  harness.emitPreview({
    ...successState({
      outputData: { Score: 0.98, Width: 320 },
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: imageSource
      }
    })
  });

  assert.match(harness.container.innerHTML, /输出图像/);
  assert.match(harness.container.innerHTML, /适应窗口/);
  assert.match(harness.container.innerHTML, /原始大小/);
  assert.match(harness.container.innerHTML, /打开大图/);
  assert.match(harness.container.innerHTML, /data-role="pixel-probe-status"/);
  assert.match(harness.container.innerHTML, /移动鼠标查看像素坐标和值/);
  assert.match(harness.container.innerHTML, /data-image-mode="fit"/);
  assert.match(harness.container.innerHTML, /data-preview-action="image-fit" aria-pressed="true"/);
  assert.match(harness.container.innerHTML, /data-preview-action="image-original" aria-pressed="false"/);

  const disabledOpenEvent = previewActionEvent('open-image', { imageSource });
  disabledOpenEvent.target.disabled = true;
  owner.handleClick(disabledOpenEvent);
  assert.deepEqual(harness.calls.openImages, []);

  owner.handleClick(previewActionEvent('image-original'));
  assert.match(harness.container.innerHTML, /data-image-mode="original"/);
  assert.match(harness.container.innerHTML, /data-preview-action="image-original" aria-pressed="true"/);

  owner.handleClick(previewActionEvent('image-fit'));
  assert.match(harness.container.innerHTML, /data-image-mode="fit"/);
  assert.match(harness.container.innerHTML, /data-preview-action="image-fit" aria-pressed="true"/);

  owner.handleClick(previewActionEvent('open-image', { imageSource }));
  assert.deepEqual(harness.calls.openImages, [imageSource]);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner resets pixel probe status and cache on image mode switches', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  const imageSource = 'data:image/png;base64,TEST_IMAGE';
  let resetCalls = 0;
  let probeCalls = 0;
  const image = {};
  const stage = {
    querySelector(selector) {
      return selector === 'img' ? image : null;
    }
  };
  const pointerEvent = {
    clientX: 24,
    clientY: 12,
    target: {
      closest(selector) {
        return selector === '.preview-capability-image-stage' ? stage : null;
      }
    }
  };

  harness.emitPreview({
    ...successState({
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: imageSource
      }
    })
  });
  owner.pixelProbe = {
    reset() {
      resetCalls += 1;
    },
    probePoint(point, imageElement) {
      probeCalls += 1;
      assert.equal(point.clientX, 24);
      assert.equal(point.clientY, 12);
      assert.equal(imageElement, image);
      return {
        kind: 'pixel',
        message: `X: ${probeCalls}  Y: 2  RGB: 1,2,3  Image: 4x5  Zoom: 100%`
      };
    }
  };

  owner.handlePixelProbePointerMove(pointerEvent);
  assert.equal(owner.pixelProbeStatusKind, 'pixel');
  assert.match(owner.pixelProbeStatusText, /X: 1  Y: 2/);

  owner.handleClick(previewActionEvent('image-original'));
  assert.equal(owner.previewImageMode, 'original');
  assert.equal(resetCalls, 1);
  assert.equal(owner.pixelProbeStatusKind, 'default');
  assert.equal(owner.pixelProbeStatusText, PIXEL_PROBE_DEFAULT_MESSAGE);
  assert.doesNotMatch(harness.container.innerHTML, /X: 1  Y: 2/);

  owner.handlePixelProbePointerMove(pointerEvent);
  assert.equal(owner.pixelProbeStatusKind, 'pixel');
  assert.match(owner.pixelProbeStatusText, /X: 2  Y: 2/);

  owner.handleClick(previewActionEvent('image-fit'));
  assert.equal(owner.previewImageMode, 'fit');
  assert.equal(resetCalls, 2);
  assert.equal(owner.pixelProbeStatusKind, 'default');
  assert.equal(owner.pixelProbeStatusText, PIXEL_PROBE_DEFAULT_MESSAGE);
  assert.doesNotMatch(harness.container.innerHTML, /X: 2  Y: 2/);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner resolves a retargeted pointer path through the image stage', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  const imageSource = 'data:image/png;base64,RETARGETED_IMAGE';
  const image = {
    naturalWidth: 100,
    naturalHeight: 50,
    complete: true,
    currentSrc: imageSource,
    src: imageSource,
    getBoundingClientRect() {
      return { left: 10, top: 20, width: 200, height: 100, right: 210, bottom: 120 };
    }
  };
  const stage = {
    matches(selector) {
      return selector === '.preview-capability-image-stage';
    },
    querySelector(selector) {
      return selector === 'img' ? image : null;
    }
  };
  owner.pixelProbe = {
    reset() {},
    probePoint(point, probeImage) {
      assert.equal(probeImage, image);
      return {
        kind: 'pixel',
        message: `X: ${point.clientX}  Y: ${point.clientY}  RGB: 1,2,3`
      };
    }
  };

  harness.emitPreview({
    ...successState({
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: imageSource
      }
    })
  });

  owner.handlePixelProbePointerMove({
    clientX: 30,
    clientY: 40,
    target: {
      closest() {
        return null;
      }
    },
    composedPath() {
      return [{}, stage, harness.container];
    }
  });

  assert.equal(owner.pixelProbeStatusKind, 'pixel');
  assert.match(owner.pixelProbeStatusText, /X: 30  Y: 40/);
  owner.dispose();
});

test('PreviewPanelCapabilityOwner locks a clicked image point and clears the crosshair', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  const imageSource = 'data:image/png;base64,TEST_IMAGE';
  const crosshair = {
    hidden: true,
    style: {},
    attributes: new Map(),
    setAttribute(name, value) {
      this.attributes.set(name, value);
      if (name === 'hidden') {
        this.hidden = true;
      }
    },
    removeAttribute(name) {
      this.attributes.delete(name);
      if (name === 'hidden') {
        this.hidden = false;
      }
    }
  };
  const image = {
    naturalWidth: 100,
    naturalHeight: 50,
    complete: true,
    currentSrc: imageSource,
    src: imageSource,
    getAttribute(name) {
      return name === 'src' ? imageSource : null;
    },
    getBoundingClientRect() {
      return { left: 10, top: 20, width: 200, height: 100, right: 210, bottom: 120 };
    }
  };
  const stage = {
    scrollLeft: 0,
    scrollTop: 0,
    focus() {},
    setPointerCapture() {},
    releasePointerCapture() {},
    getBoundingClientRect() {
      return { left: 0, top: 0, width: 260, height: 160, right: 260, bottom: 160 };
    },
    querySelector(selector) {
      return selector === 'img' ? image : null;
    }
  };
  harness.container.querySelector = selector => {
    if (selector === '.preview-capability-image-stage') {
      return stage;
    }
    if (selector === '[data-role="pixel-probe-crosshair"]') {
      return crosshair;
    }
    return null;
  };
  owner.pixelProbe = {
    reset() {},
    mapPoint(point) {
      return {
        inside: true,
        x: Math.floor((point.clientX - 10) / 2),
        y: Math.floor((point.clientY - 20) / 2),
        width: 100,
        height: 50,
        scale: 2,
        scaleX: 2,
        scaleY: 2
      };
    },
    probePoint(point) {
      return {
        kind: 'pixel',
        message: `X: ${point.clientX}  Y: ${point.clientY}  RGB: 1,2,3`
      };
    },
    createLockedPoint(mapped) {
      return {
        kind: 'locked',
        message: `已锁定 X: ${mapped.x}  Y: ${mapped.y}  RGB: 1,2,3`,
        mapped,
        rgba: [1, 2, 3, 255]
      };
    }
  };
  const eventAt = (clientX, clientY) => ({
    button: 0,
    pointerId: 1,
    clientX,
    clientY,
    target: {
      closest(selector) {
        return selector === '.preview-capability-image-stage' ? stage : null;
      }
    },
    preventDefault() {}
  });

  harness.emitPreview({
    ...successState({
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: imageSource
      }
    })
  });

  owner.handlePixelProbePointerMove(eventAt(30, 40));
  assert.equal(owner.pixelProbeStatusKind, 'pixel');
  assert.match(owner.pixelProbeStatusText, /X: 30  Y: 40/);

  owner.handlePixelProbePointerDown(eventAt(110, 70));
  owner.handlePixelProbePointerUp(eventAt(110, 70));
  assert.equal(owner.pixelProbeStatusKind, 'locked');
  assert.match(owner.pixelProbeStatusText, /已锁定 X: 50  Y: 25/);
  assert.equal(owner.pixelProbeLockedPoint.mapped.x, 50);
  assert.equal(crosshair.hidden, false);
  assert.equal(crosshair.style.left, '111px');
  assert.equal(crosshair.style.top, '71px');

  owner.handlePixelProbePointerMove({
    target: {
      closest() {
        return null;
      }
    }
  });
  assert.equal(owner.pixelProbeStatusKind, 'locked');
  assert.match(owner.pixelProbeStatusText, /已锁定 X: 50  Y: 25/);

  image.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 50, right: 100, bottom: 50 });
  owner.handleClick(previewActionEvent('image-original'));
  assert.equal(owner.previewImageMode, 'original');
  assert.equal(owner.pixelProbeStatusKind, 'default');
  assert.equal(owner.pixelProbeLockedPoint.mapped.x, 50);
  assert.equal(crosshair.style.left, '50.5px');
  assert.equal(crosshair.style.top, '25.5px');

  owner.handleClick(previewActionEvent('clear-pixel-lock'));
  assert.equal(owner.pixelProbeLockedPoint, null);
  assert.equal(owner.pixelProbeStatusKind, 'default');
  assert.equal(crosshair.hidden, true);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner drags and clears an image ROI overlay', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  const imageSource = 'data:image/png;base64,TEST_IMAGE';
  const roiBox = {
    hidden: true,
    style: {},
    attributes: new Map(),
    setAttribute(name, value) {
      this.attributes.set(name, value);
      if (name === 'hidden') {
        this.hidden = true;
      }
    },
    removeAttribute(name) {
      this.attributes.delete(name);
      if (name === 'hidden') {
        this.hidden = false;
      }
    }
  };
  const image = {
    naturalWidth: 100,
    naturalHeight: 50,
    complete: true,
    currentSrc: imageSource,
    src: imageSource,
    getAttribute(name) {
      return name === 'src' ? imageSource : null;
    },
    getBoundingClientRect() {
      return { left: 10, top: 20, width: 200, height: 100, right: 210, bottom: 120 };
    }
  };
  const stage = {
    scrollLeft: 0,
    scrollTop: 0,
    focus() {},
    setPointerCapture() {},
    releasePointerCapture() {},
    getBoundingClientRect() {
      return { left: 0, top: 0, width: 260, height: 160, right: 260, bottom: 160 };
    },
    querySelector(selector) {
      return selector === 'img' ? image : null;
    }
  };
  harness.container.querySelector = selector => {
    if (selector === '.preview-capability-image-stage') {
      return stage;
    }
    if (selector === '[data-role="pixel-probe-roi"]') {
      return roiBox;
    }
    return null;
  };
  owner.pixelProbe = {
    reset() {},
    mapPoint(point) {
      return {
        inside: true,
        x: Math.floor((point.clientX - 10) / 2),
        y: Math.floor((point.clientY - 20) / 2),
        width: 100,
        height: 50,
        scale: 2,
        scaleX: 2,
        scaleY: 2
      };
    },
    probePoint() {
      return {
        kind: 'pixel',
        message: 'X: 0  Y: 0'
      };
    },
    createLockedPoint(mapped) {
      return {
        kind: 'locked',
        message: `已锁定 X: ${mapped.x}  Y: ${mapped.y}`,
        mapped
      };
    },
    createRoiSelection(roi) {
      return {
        kind: 'roi',
        message: `ROI x:${roi.x} y:${roi.y} w:${roi.width} h:${roi.height}  像素:${roi.width * roi.height}  灰度 mean:1 min:0 max:2  世界: 未配置标定/暂无世界坐标`,
        roi,
        stats: {
          ok: true,
          count: roi.width * roi.height
        }
      };
    }
  };
  const eventAt = (clientX, clientY) => ({
    button: 0,
    pointerId: 2,
    clientX,
    clientY,
    target: {
      closest(selector) {
        return selector === '.preview-capability-image-stage' ? stage : null;
      }
    },
    preventDefault() {}
  });

  harness.emitPreview({
    ...successState({
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: imageSource
      }
    })
  });

  owner.handlePixelProbePointerDown(eventAt(10, 20));
  owner.handlePixelProbePointerMove(eventAt(22, 32));
  assert.deepEqual(owner.pixelProbeRoiDraft, { x: 0, y: 0, width: 7, height: 7 });
  assert.equal(roiBox.hidden, false);
  assert.equal(roiBox.style.left, '10px');
  assert.equal(roiBox.style.top, '20px');
  assert.equal(roiBox.style.width, '14px');
  assert.equal(roiBox.style.height, '14px');

  owner.handlePixelProbePointerUp(eventAt(22, 32));
  assert.equal(owner.pixelProbeStatusKind, 'roi');
  assert.match(owner.pixelProbeStatusText, /ROI x:0 y:0 w:7 h:7/);
  assert.match(owner.pixelProbeStatusText, /未配置标定\/暂无世界坐标/);
  assert.deepEqual(owner.pixelProbeRoiSelection.roi, { x: 0, y: 0, width: 7, height: 7 });

  image.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 50, right: 100, bottom: 50 });
  owner.handleClick(previewActionEvent('image-original'));
  assert.equal(owner.previewImageMode, 'original');
  assert.equal(owner.pixelProbeStatusKind, 'default');
  assert.deepEqual(owner.pixelProbeRoiSelection.roi, { x: 0, y: 0, width: 7, height: 7 });
  assert.equal(roiBox.style.left, '0px');
  assert.equal(roiBox.style.top, '0px');
  assert.equal(roiBox.style.width, '7px');
  assert.equal(roiBox.style.height, '7px');

  owner.handleClick(previewActionEvent('clear-pixel-roi'));
  assert.equal(owner.pixelProbeRoiSelection, null);
  assert.equal(owner.pixelProbeStatusKind, 'default');
  assert.equal(roiBox.hidden, true);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner clears pixel probe selections with Escape', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  let prevented = false;

  owner.pixelProbeLockedPoint = { kind: 'locked', message: '已锁定 X: 1  Y: 2' };
  owner.pixelProbeRoiSelection = {
    kind: 'roi',
    message: 'ROI x:0 y:0 w:1 h:1',
    roi: { x: 0, y: 0, width: 1, height: 1 }
  };
  owner.handlePixelProbeKeyDown({
    key: 'Escape',
    preventDefault() {
      prevented = true;
    }
  });

  assert.equal(prevented, true);
  assert.equal(owner.pixelProbeLockedPoint, null);
  assert.equal(owner.pixelProbeRoiSelection, null);
  assert.equal(owner.pixelProbeStatusKind, 'default');

  owner.dispose();
});

test('PreviewPanelCapabilityOwner clears locked pixel state when preview identity changes', () => {
  const harness = createPreviewCapabilityHarness();
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  owner.pixelProbeLockedPoint = {
    mapped: { x: 1, y: 2, width: 10, height: 10 }
  };
  owner.pixelProbeRoiSelection = {
    roi: { x: 0, y: 0, width: 5, height: 5 }
  };
  owner.pixelProbeStatusKind = 'locked';
  owner.pixelProbeStatusText = '已锁定 X: 1  Y: 2';

  harness.emitPreview({
    ...successState({
      activeNodeId: 'node-1',
      identity: identity({ clientRequestSequence: 8 }),
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: 'data:image/png;base64,NEW_IMAGE'
      }
    })
  });

  assert.equal(owner.pixelProbeLockedPoint, null);
  assert.equal(owner.pixelProbeRoiSelection, null);
  assert.equal(owner.pixelProbeStatusKind, 'default');
  assert.equal(owner.pixelProbeStatusText, PIXEL_PROBE_DEFAULT_MESSAGE);

  owner.dispose();
});

test('PreviewPanelCapabilityOwner marks old preview results stale when input image hash changes', () => {
  const harness = createPreviewCapabilityHarness({
    inputImageBase64: 'new-input-image'
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });

  harness.emitPreview({
    ...successState({
      request: {
        projectId: 'project-1',
        nodeId: 'node-1',
        flowRevision: 3,
        parameterSnapshot: buildPreviewParameterSnapshot([{ name: 'Threshold', value: 128 }]),
        inputImageHash: buildPreviewInputImageHash('old-input-image'),
        requestKey: 'project-1:node-1:3:params:old-input'
      },
      presenter: {
        statusText: '预览完成',
        inputImageSrc: null,
        outputImageSrc: 'data:image/png;base64,OLD'
      }
    })
  });

  assert.match(harness.container.innerHTML, new RegExp(STALE_PREVIEW_MESSAGE));
  assert.match(harness.container.innerHTML, /data-status="stale"/);
  assert.match(harness.container.innerHTML, /旧输出摘要/);

  owner.dispose();
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

test('PreviewPanelCapabilityOwner ignores duplicate artifact reads while the same artifact is loading', async () => {
  let resolveRead;
  const readGate = new Promise(resolve => {
    resolveRead = resolve;
  });
  const harness = createPreviewCapabilityHarness({
    readArtifactForCurrentState: async artifactId => {
      await readGate;
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
          slice() {
            return {
              async text() {
                return JSON.stringify({ score: 1 });
              }
            };
          }
        }
      };
    }
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

  const firstRead = owner.readArtifactPreview('json-artifact');
  const secondRead = owner.readArtifactPreview('json-artifact');

  assert.equal(harness.calls.artifactReads.length, 1);
  assert.match(
    harness.container.innerHTML,
    /data-preview-action="read-artifact"[\s\S]*data-artifact-id="json-artifact"[\s\S]*disabled aria-disabled="true"/
  );

  resolveRead();
  await Promise.all([firstRead, secondRead]);

  assert.equal(harness.calls.artifactReads.length, 1);
  assert.equal(owner.artifactReadState.get('json-artifact').status, 'success');

  owner.dispose();
});

test('PreviewPanelCapabilityOwner clears previous artifact loading state when another artifact is read', async () => {
  let resolveFirstRead;
  const firstGate = new Promise(resolve => {
    resolveFirstRead = resolve;
  });
  const harness = createPreviewCapabilityHarness({
    readArtifactForCurrentState: async artifactId => {
      if (artifactId === 'artifact-a') {
        await firstGate;
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
          slice() {
            return {
              async text() {
                return JSON.stringify({ artifactId });
              }
            };
          }
        }
      };
    }
  });
  const owner = new PreviewPanelCapabilityOwner(harness.container, {
    previewAdapter: harness.adapter
  });
  harness.emitPreview({
    ...successState({
      artifacts: [
        {
          artifactId: 'artifact-a',
          kind: 'profile',
          role: 'profile-a',
          contentType: 'application/json',
          length: 20
        },
        {
          artifactId: 'artifact-b',
          kind: 'profile',
          role: 'profile-b',
          contentType: 'application/json',
          length: 20
        }
      ]
    }),
    presenter: {
      statusText: '预览完成',
      inputImageSrc: null,
      outputImageSrc: null
    }
  });

  const firstRead = owner.readArtifactPreview('artifact-a');
  assert.equal(owner.artifactReadState.get('artifact-a').status, 'loading');

  await owner.readArtifactPreview('artifact-b');

  assert.equal(harness.calls.artifactReads.length, 2);
  assert.equal(owner.artifactReadState.get('artifact-a'), undefined);
  assert.equal(owner.artifactReadState.get('artifact-b').status, 'success');
  assert.doesNotMatch(
    harness.container.innerHTML,
    /data-artifact-id="artifact-a"[\s\S]*disabled aria-disabled="true"/
  );

  resolveFirstRead();
  await firstRead;

  assert.equal(owner.artifactReadState.get('artifact-a'), undefined);
  assert.equal(owner.artifactReadState.get('artifact-b').status, 'success');

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
  assert.match(appSource, /previewResourcesEnabled:\s*!isPreviewPanelCapabilityEnabled\(\)/);
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
  assert.match(ownerSource, /模块结果/);
  assert.match(ownerSource, /端口与耗时/);
  assert.match(ownerSource, /STALE_PREVIEW_MESSAGE/);
  assert.match(ownerSource, /适应窗口/);
  assert.match(ownerSource, /原始大小/);
  assert.match(ownerSource, /打开大图/);
  assert.doesNotMatch(ownerSource, /\bfetch\s*\(/);
  assert.doesNotMatch(ownerSource, /httpClient/);
  assert.doesNotMatch(ownerSource, /localStorage|IndexedDB|InspectionHistory|Evidence/);
  assert.doesNotMatch(ownerSource, /new ImageCanvas|createElement\('canvas'|document\.createElement\('canvas'/);
  assert.doesNotMatch(ownerSource, /PropertyPanelCapabilityOwner/);
  assert.doesNotMatch(ownerSource, /operator-preview-container/);
  assert.equal((appSource.match(/new PropertyPanelCapabilityOwner\(/g) || []).length, 1);
  assert.match(appSource, /function shouldLegacyPropertyPanelOwnSidebarPreview\(\) \{[\s\S]*return !isPropertyPanelCapabilityEnabled\(\) && !isPreviewPanelCapabilityEnabled\(\);[\s\S]*\}/);
  assert.match(propertyPanelSource, /previewResourcesEnabled/);
});
