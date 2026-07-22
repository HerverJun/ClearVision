import test from 'node:test';
import assert from 'node:assert/strict';
import {
  NodePreviewCoordinator,
  buildCameraPreviewSourceSignature,
  createCameraPreviewInputContext,
  getOperatorPreviewCostPolicy,
  previewObservationMatchesRequest,
  resolveCameraPreviewInputFrame
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';
import {
  normalizeAcquisitionSourceType
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/parameterDependencyRules.js';

const PNG_BASE64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==';

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

function createCoordinator(options = {}) {
  const node = options.node ?? {
    id: 'node-1',
    type: 'Thresholding',
    parameters: [],
    outputs: [{ type: 'image' }]
  };
  let executeCount = 0;
  const coordinator = new NodePreviewCoordinator({
    getProjectId: () => typeof options.projectId === 'function' ? options.projectId() : (options.projectId ?? 'project-1'),
    getFlowRevision: () => typeof options.flowRevision === 'function'
      ? options.flowRevision()
      : (options.flowRevision ?? 1),
    getNodeById: () => node,
    getInputImageBase64: () => options.inputImageBase64 ?? 'INPUT_IMAGE',
    getInputImageContext: options.getInputImageContext,
    getOperatorMetadata: () => ({ outputPorts: [{ dataType: 'image' }] }),
    artifactClient: options.artifactClient ?? {
      async getPreviewArtifactBlob() { return { blob: new Blob() }; },
      async deletePreviewArtifact() {}
    },
    previewExecutor: async (nodeId, executorOptions) => {
      executeCount += 1;
      options.onPreviewOptions?.(executorOptions, executeCount, nodeId);
      if (typeof options.previewExecutor === 'function') {
        return options.previewExecutor(nodeId, executorOptions, executeCount);
      }
      if (typeof options.previewResponse === 'function') {
        return options.previewResponse(executeCount, executorOptions, nodeId);
      }
      if (options.previewResponse) {
        return options.previewResponse;
      }
      return {
        success: true,
        executionTimeMs: 12,
        outputImageBase64: 'OUTPUT_IMAGE',
        outputData: { score: executeCount }
      };
    },
    debounceMs: 0,
    maxCacheEntries: options.maxCacheEntries ?? 4,
    maxCacheOutputImageBase64Chars: options.maxCacheOutputImageBase64Chars
  });

  return {
    coordinator,
    node,
    getExecuteCount: () => executeCount
  };
}

function buildObservationIdentity(nodeId, options, overrides = {}) {
  return {
    projectId: 'project-1',
    targetNodeId: nodeId,
    debugSessionId: options.debugSessionId,
    clientRequestSequence: options.clientRequestSequence,
    flowRevision: options.flowRevision,
    ...overrides
  };
}

function buildObservationResponse(nodeId, options, overrides = {}) {
  return {
    success: true,
    executionTimeMs: 12,
    outputImageBase64: 'OUTPUT_IMAGE',
    outputData: { score: 1 },
    observation: {
      schemaVersion: 'execution-observation.v1',
      identity: buildObservationIdentity(nodeId, options, overrides),
      outcome: {
        success: true,
        executionTimeMs: 12,
        executedOperatorCount: 1
      },
      summary: [],
      detail: {
        kind: 'dictionary',
        displayValue: '1/1 fields',
        children: [],
        truncated: false,
        pathHint: '$',
        addressable: false
      },
      diagnostics: [],
      limits: {},
      truncated: false
    }
  };
}

function buildArtifactPreviewResponse(nodeId, options, suffix = '1') {
  return {
    ...buildObservationResponse(nodeId, options),
    inputImageBase64: null,
    outputImageBase64: null,
    artifacts: [
      { artifactId: `input-artifact-${suffix}`, kind: 'image', role: 'inputImage', contentType: 'image/png', length: 4 },
      { artifactId: `output-artifact-${suffix}`, kind: 'image', role: 'outputImage', contentType: 'image/png', length: 4 }
    ]
  };
}

test('normalizeAcquisitionSourceType recognizes localized file and camera labels', () => {
  assert.equal(normalizeAcquisitionSourceType('文件'), 'file');
  assert.equal(normalizeAcquisitionSourceType('图像文件'), 'file');
  assert.equal(normalizeAcquisitionSourceType('File|文件'), 'file');
  assert.equal(normalizeAcquisitionSourceType('相机'), 'camera');
  assert.equal(normalizeAcquisitionSourceType('摄像头'), 'camera');
  assert.equal(normalizeAcquisitionSourceType('Camera|相机'), 'camera');
});

test('NodePreviewCoordinator accepts localized file acquisition source when file path is present', async () => {
  const fileNode = {
    id: 'file-node',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: '文件' },
      { name: 'FilePath', value: 'C:\\Images\\part.png' }
    ],
    outputs: [{ type: 'image' }]
  };
  const { coordinator, getExecuteCount } = createCoordinator({
    node: fileNode,
    inputImageBase64: null
  });

  try {
    coordinator.setActiveNode(fileNode);
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    assert.equal(getExecuteCount(), 1);
    assert.equal(coordinator.getState().errorMessage, null);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator reports missing project instead of idling silently', async () => {
  const { coordinator, node, getExecuteCount } = createCoordinator({
    projectId: () => null
  });

  try {
    coordinator.setActiveNode(node);
    await waitFor(() => assert.equal(
      coordinator.getState().errorMessage,
      '请先新建/保存/打开工程后再预览'
    ));

    assert.equal(coordinator.getState().status, 'idle');
    assert.equal(coordinator.getState().presenter.statusText, '请先新建/保存/打开工程后再预览');
    assert.equal(coordinator.getState().request.projectId, null);
    assert.equal(getExecuteCount(), 0);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator cache entries do not retain input image base64', async () => {
  const inputImageBase64 = 'A'.repeat(1024 * 1024);
  const { coordinator, node, getExecuteCount } = createCoordinator({ inputImageBase64 });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true });

    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    assert.equal(getExecuteCount(), 1);
    assert.equal(coordinator.getState().inputImageBase64, inputImageBase64);
    const cachedEntry = Array.from(coordinator.cache.values())[0];
    assert.equal(cachedEntry.inputImageBase64, null);

    coordinator.requestActivePreview({ immediate: true });
    await Promise.resolve();

    assert.equal(getExecuteCount(), 1);
    assert.equal(coordinator.getState().inputImageBase64, inputImageBase64);
    assert.equal(Array.from(coordinator.cache.values())[0].inputImageBase64, null);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator evicts cached output images by total base64 budget', () => {
  const { coordinator } = createCoordinator({
    maxCacheEntries: 8,
    maxCacheOutputImageBase64Chars: 10
  });

  try {
    coordinator.setCacheEntry('first', {
      status: 'success',
      outputImageBase64: 'A'.repeat(6)
    });
    coordinator.setCacheEntry('second', {
      status: 'success',
      outputImageBase64: 'B'.repeat(6)
    });

    assert.equal(coordinator.cache.has('first'), false);
    assert.equal(coordinator.cache.has('second'), true);
    assert.equal(coordinator.getCachedOutputImageBase64Chars(), 6);

    coordinator.setCacheEntry('metadata-only', {
      status: 'success',
      outputImageBase64: null,
      outputData: { score: 1 }
    });

    assert.equal(coordinator.cache.has('metadata-only'), true);
    assert.equal(coordinator.getCachedOutputImageBase64Chars(), 6);

    coordinator.setCacheEntry('too-large', {
      status: 'success',
      outputImageBase64: 'C'.repeat(11)
    });

    assert.equal(coordinator.cache.has('too-large'), false);
    assert.equal(coordinator.getCachedOutputImageBase64Chars(), 0);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator compacts preview outputData before retaining state and cache', async () => {
  const imagePayload = `data:image/png;base64,${'A'.repeat(200)}`;
  const longText = 'x'.repeat(1000);
  const originalOutputData = {
    Text: longText,
    PreviewImageBase64: imagePayload,
    Items: Array.from({ length: 40 }, (_, index) => ({
      label: `item-${index}`,
      nested: {
        value: index,
        fields: {
          a: 1,
          b: 2
        }
      }
    }))
  };
  const { coordinator, node } = createCoordinator({
    previewResponse: {
      success: true,
      executionTimeMs: 5,
      outputImageBase64: 'OUTPUT_IMAGE',
      outputData: originalOutputData
    }
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true });

    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    const stateOutput = coordinator.getState().outputData;
    const cachedOutput = Array.from(coordinator.cache.values())[0].outputData;

    assert.equal(stateOutput.Text.length, 515);
    assert.match(stateOutput.Text, /\.\.\.$/);
    assert.equal(stateOutput.PreviewImageBase64, undefined);
    assert.equal(stateOutput.__omittedImageFieldCount, 1);
    assert.equal(stateOutput.Items.length, 25);
    assert.equal(stateOutput.Items.at(-1), '+16 more');
    assert.deepEqual(cachedOutput, stateOutput);
    assert.equal(originalOutputData.Text, longText);
    assert.equal(originalOutputData.PreviewImageBase64, imagePayload);
    assert.equal(originalOutputData.Items.length, 40);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator keeps high-cost auto previews idle until manual request', async () => {
  const highCostNode = {
    id: 'template-node',
    type: 'TemplateMatching',
    parameters: [],
    outputs: [{ type: 'image' }]
  };
  const { coordinator, getExecuteCount } = createCoordinator({
    node: highCostNode
  });

  try {
    coordinator.setActiveNode(highCostNode);
    await waitFor(() => assert.match(coordinator.getState().errorMessage || '', /高成本计算|手动执行/));

    assert.equal(coordinator.getState().status, 'idle');
    assert.equal(getExecuteCount(), 0);

    coordinator.requestActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    assert.equal(getExecuteCount(), 1);
  } finally {
    coordinator.destroy();
  }
});

test('getOperatorPreviewCostPolicy restores auto preview for explicit light operator allowlist', () => {
  const lightOperatorTypes = [
    'BlobLabeling',
    'BoxFilter',
    'BoxNms',
    'CaliperTool',
    'DetectionSequenceJudge',
    'DualModalVoting',
    'EdgePairDefect',
    'FrequencyFilter',
    'GeometricTolerance',
    'GlcmTexture',
    'HistogramAnalysis',
    'LineMeasurement',
    'ParallelLineFind',
    'PhaseClosure',
    'QuadrilateralFind'
  ];

  for (const type of lightOperatorTypes) {
    const policy = getOperatorPreviewCostPolicy(
      { id: `${type}-node`, type, parameters: [] },
      {
        type,
        category: 'AI feature tools',
        tags: ['ai', 'feature', 'measurement'],
        keywords: ['feature analysis']
      });

    assert.equal(policy.level, 'light', type);
    assert.equal(policy.autoPreviewAllowed, true, type);
  }
});

test('getOperatorPreviewCostPolicy keeps explicit high-cost operator types manual', () => {
  const highCostOperatorTypes = [
    'DeepLearning',
    'OnnxInference',
    'SemanticSegmentation',
    'SurfaceDefectDetection',
    'AnomalyDetection',
    'OcrRecognition',
    'TemplateMatching',
    'TemplateMatch',
    'ShapeMatching',
    'PlanarMatching',
    'AkazeFeatureMatch',
    'OrbFeatureMatch',
    'LocalDeformableMatching',
    'PPFMatch',
    'RansacPlaneSegmentation',
    'PPFEstimation'
  ];

  for (const type of highCostOperatorTypes) {
    const policy = getOperatorPreviewCostPolicy({ id: `${type}-node`, type, parameters: [] });

    assert.equal(policy.level, 'high', type);
    assert.equal(policy.autoPreviewAllowed, false, type);
  }
});

test('getOperatorPreviewCostPolicy treats ImageAcquisition file and camera modes separately', () => {
  const filePolicy = getOperatorPreviewCostPolicy({
    id: 'file-acquisition',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'File' },
      { name: 'FilePath', value: 'sample.png' }
    ]
  });

  assert.equal(filePolicy.autoPreviewAllowed, true);

  const cameraPolicy = getOperatorPreviewCostPolicy({
    id: 'camera-acquisition',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: 'camera-1' }
    ]
  });

  assert.equal(cameraPolicy.autoPreviewAllowed, false);
  assert.equal(cameraPolicy.reason, '相机采集会访问真实设备，自动预览已暂停；请使用手动预览或运行流程。');

  const unconfiguredCameraPolicy = getOperatorPreviewCostPolicy({
    id: 'unconfigured-camera-acquisition',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: '' }
    ]
  });

  assert.equal(unconfiguredCameraPolicy.autoPreviewAllowed, false);
  assert.equal(unconfiguredCameraPolicy.reason, '相机采集会访问真实设备，自动预览已暂停；请使用手动预览或运行流程。');
});

test('getOperatorPreviewCostPolicy does not infer high cost from generic ai or feature metadata substrings', () => {
  const cases = [
    {
      node: { id: 'histogram-node', type: 'HistogramAnalysis', parameters: [] },
      metadata: {
        type: 'HistogramAnalysis',
        category: 'basic analysis',
        tags: ['ai', 'quality'],
        keywords: ['feature summary']
      }
    },
    {
      node: { id: 'line-node', type: 'LineMeasurement', parameters: [] },
      metadata: {
        type: 'LineMeasurement',
        category: 'geometry feature measurement',
        tags: ['feature'],
        keywords: ['straight line']
      }
    }
  ];

  for (const { node, metadata } of cases) {
    const policy = getOperatorPreviewCostPolicy(node, metadata);

    assert.equal(policy.autoPreviewAllowed, true, node.type);
    assert.equal(policy.level, 'light', node.type);
  }
});

test('getOperatorPreviewCostPolicy keeps explicit metadata tokens high cost', () => {
  const explicitTokens = ['feature-matching', 'template-matching', 'onnx', 'deep-learning'];

  for (const token of explicitTokens) {
    const policy = getOperatorPreviewCostPolicy(
      { id: `${token}-node`, type: 'CustomPreviewOperator', parameters: [] },
      {
        type: 'CustomPreviewOperator',
        tags: [token]
      });

    assert.equal(policy.autoPreviewAllowed, false, token);
    assert.equal(policy.level, 'high', token);
  }
});

test('NodePreviewCoordinator reports missing camera before live camera manual policy', async () => {
  const cameraNode = {
    id: 'camera-node',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: '' }
    ],
    outputs: [{ type: 'image' }]
  };
  const { coordinator, getExecuteCount } = createCoordinator({
    node: cameraNode,
    inputImageBase64: null
  });

  try {
    coordinator.setActiveNode(cameraNode);
    await waitFor(() => assert.equal(coordinator.getState().errorMessage, '请先选择相机'));

    assert.equal(coordinator.getState().status, 'idle');
    assert.equal(coordinator.getState().presenter.statusText, '请先选择相机');
    assert.equal(getExecuteCount(), 0);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator sends client request sequence and flow revision', async () => {
  let capturedOptions = null;
  const { coordinator, node } = createCoordinator({
    flowRevision: 7,
    onPreviewOptions: options => {
      capturedOptions = options;
    },
    previewResponse: (_count, executorOptions, nodeId) =>
      buildObservationResponse(nodeId, executorOptions)
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });

    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    assert.ok(Number.isSafeInteger(capturedOptions.clientRequestSequence));
    assert.ok(capturedOptions.clientRequestSequence > 0);
    assert.equal(capturedOptions.flowRevision, 7);
    assert.equal(capturedOptions.artifactMode, 'references');
    assert.equal(coordinator.getState().outputData.score, 1);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator drops observation responses with mismatched identity fields', async () => {
  const mismatchCases = [
    ['projectId', 'other-project'],
    ['targetNodeId', 'other-node'],
    ['debugSessionId', '00000000-0000-0000-0000-000000000000'],
    ['clientRequestSequence', 999999],
    ['flowRevision', 999999]
  ];

  for (const [field, value] of mismatchCases) {
    const { coordinator, node, getExecuteCount } = createCoordinator({
      flowRevision: 3,
      previewResponse: (_count, executorOptions, nodeId) =>
        buildObservationResponse(nodeId, executorOptions, { [field]: value })
    });

    try {
      coordinator.setActiveNode(node);
      coordinator.invalidateActivePreview({ immediate: true, force: true });

      await waitFor(() => assert.equal(getExecuteCount(), 1));
      await new Promise(resolve => setTimeout(resolve, 0));

      assert.equal(coordinator.getState().status, 'loading', `${field} mismatch should not complete preview`);
      assert.equal(coordinator.getState().outputData, null);
      assert.equal(coordinator.getState().errorMessage, null);
      assert.equal(coordinator.cache.size, 0);
    } finally {
      coordinator.destroy();
    }
  }
});

test('NodePreviewCoordinator reads artifact images and releases object URLs plus server artifacts', async () => {
  const originalCreateObjectUrl = globalThis.URL.createObjectURL;
  const originalRevokeObjectUrl = globalThis.URL.revokeObjectURL;
  const revokedUrls = [];
  const readArtifactIds = [];
  const deletedArtifactIds = [];
  let objectUrlSequence = 0;
  globalThis.URL.createObjectURL = () => `blob:preview-artifact-${++objectUrlSequence}`;
  globalThis.URL.revokeObjectURL = url => revokedUrls.push(url);

  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      readArtifactIds.push(artifactId);
      return { blob: { artifactId } };
    },
    async deletePreviewArtifact(artifactId) {
      deletedArtifactIds.push(artifactId);
    }
  };

  const { coordinator, node } = createCoordinator({
    artifactClient,
    previewResponse: (_count, executorOptions, nodeId) => ({
      ...buildObservationResponse(nodeId, executorOptions),
      inputImageBase64: null,
      outputImageBase64: null,
      artifacts: [
        { artifactId: 'input-artifact', kind: 'image', role: 'inputImage', contentType: 'image/png', length: 4 },
        { artifactId: 'output-artifact', kind: 'image', role: 'outputImage', contentType: 'image/png', length: 4 }
      ]
    })
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });

    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    assert.deepEqual(readArtifactIds, ['input-artifact', 'output-artifact']);
    assert.equal(coordinator.getState().inputImageBase64, 'blob:preview-artifact-1');
    assert.equal(coordinator.getState().outputImageBase64, 'blob:preview-artifact-2');

    coordinator.destroy();
    await Promise.resolve();

    assert.deepEqual(revokedUrls.sort(), ['blob:preview-artifact-1', 'blob:preview-artifact-2']);
    assert.deepEqual(deletedArtifactIds.sort(), ['input-artifact', 'output-artifact']);
  } finally {
    globalThis.URL.createObjectURL = originalCreateObjectUrl;
    globalThis.URL.revokeObjectURL = originalRevokeObjectUrl;
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator releases uncached artifact resources before replacing maxCacheEntries zero state', async () => {
  const originalCreateObjectUrl = globalThis.URL.createObjectURL;
  const originalRevokeObjectUrl = globalThis.URL.revokeObjectURL;
  const revokedUrls = [];
  const deletedArtifactIds = [];
  let objectUrlSequence = 0;
  globalThis.URL.createObjectURL = () => `blob:uncached-${++objectUrlSequence}`;
  globalThis.URL.revokeObjectURL = url => revokedUrls.push(url);

  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      return { blob: { artifactId } };
    },
    async deletePreviewArtifact(artifactId) {
      deletedArtifactIds.push(artifactId);
    }
  };

  const { coordinator, node } = createCoordinator({
    maxCacheEntries: 0,
    artifactClient,
    previewResponse: (count, executorOptions, nodeId) =>
      buildArtifactPreviewResponse(nodeId, executorOptions, count)
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));
    assert.equal(coordinator.getState().inputImageBase64, 'blob:uncached-1');

    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(coordinator.getState().inputImageBase64, 'blob:uncached-3'));

    assert.deepEqual(revokedUrls, ['blob:uncached-1', 'blob:uncached-2']);
    assert.deepEqual(deletedArtifactIds.sort(), ['input-artifact-1', 'output-artifact-1']);
  } finally {
    globalThis.URL.createObjectURL = originalCreateObjectUrl;
    globalThis.URL.revokeObjectURL = originalRevokeObjectUrl;
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator releases live camera artifact resources between bypass previews', async () => {
  const originalCreateObjectUrl = globalThis.URL.createObjectURL;
  const originalRevokeObjectUrl = globalThis.URL.revokeObjectURL;
  const revokedUrls = [];
  const deletedArtifactIds = [];
  let objectUrlSequence = 0;
  globalThis.URL.createObjectURL = () => `blob:camera-${++objectUrlSequence}`;
  globalThis.URL.revokeObjectURL = url => revokedUrls.push(url);

  const cameraNode = {
    id: 'camera-node',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'camera' },
      { name: 'CameraId', value: 'cam-1' }
    ],
    outputs: [{ type: 'image' }]
  };
  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      return { blob: { artifactId } };
    },
    async deletePreviewArtifact(artifactId) {
      deletedArtifactIds.push(artifactId);
    }
  };
  const { coordinator } = createCoordinator({
    node: cameraNode,
    artifactClient,
    getInputImageBase64: () => null,
    previewResponse: (count, executorOptions, nodeId) =>
      buildArtifactPreviewResponse(nodeId, executorOptions, `camera-${count}`)
  });

  try {
    coordinator.setActiveNode(cameraNode);
    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(coordinator.getState().inputImageBase64, 'blob:camera-3'));

    assert.deepEqual(revokedUrls, ['blob:camera-1', 'blob:camera-2']);
    assert.deepEqual(deletedArtifactIds.sort(), ['input-artifact-camera-1', 'output-artifact-camera-1']);
  } finally {
    globalThis.URL.createObjectURL = originalCreateObjectUrl;
    globalThis.URL.revokeObjectURL = originalRevokeObjectUrl;
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator publishes an explicitly captured camera frame without executing side-effect preview', () => {
  const cameraNode = {
    id: 'camera-capture-node',
    type: 'ImageAcquisition',
    title: '图像采集',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: 'cam-1' },
    ],
    outputs: [{ type: 'image' }],
  };
  const { coordinator, getExecuteCount } = createCoordinator({
    node: cameraNode,
    getInputImageBase64: () => null,
  });

  try {
    const state = coordinator.publishExternalFrame(cameraNode, {
      imageBase64: PNG_BASE64,
      cameraBindingId: 'cam-1',
      triggerMode: 'Software',
      width: 1920,
      height: 1080,
      capturedAtUtc: '2026-07-16T08:00:00Z',
    });

    assert.equal(getExecuteCount(), 0);
    assert.equal(state.status, 'success');
    assert.equal(state.activeNodeId, cameraNode.id);
    assert.equal(state.inputImageBase64, PNG_BASE64);
    assert.equal(state.outputImageBase64, PNG_BASE64);
    assert.equal(state.outputData.CameraBindingId, 'cam-1');
    assert.equal(state.outputData.Width, 1920);
    assert.equal(state.presenter.outputImageSrc, `data:image/png;base64,${PNG_BASE64}`);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator forwards captured camera source identity and frame id to downstream preview', async () => {
  const previewOptions = [];
  let previewInput = {
    imageBase64: PNG_BASE64,
    sourceNodeId: 'camera-source-1',
    frameId: 'frame-1'
  };
  const { coordinator, node, getExecuteCount } = createCoordinator({
    getInputImageContext: () => previewInput,
    onPreviewOptions: options => previewOptions.push(options)
  });

  try {
    coordinator.setActiveNode(node);
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));

    assert.equal(getExecuteCount(), 1);
    assert.equal(previewOptions[0].inputImageBase64, PNG_BASE64);
    assert.equal(previewOptions[0].inputImageSourceNodeId, 'camera-source-1');
    assert.equal(coordinator.getState().request.inputFrameId, 'frame-1');

    coordinator.requestActivePreview({ immediate: true });
    await Promise.resolve();
    assert.equal(getExecuteCount(), 1);

    node.parameters.push({ name: 'Threshold', value: 160 });
    coordinator.invalidateActivePreview({ immediate: true, trigger: 'parameters' });
    await waitFor(() => assert.equal(getExecuteCount(), 2));
    assert.equal(previewOptions[1].inputImageSourceNodeId, 'camera-source-1');
    assert.equal(coordinator.getState().request.inputFrameId, 'frame-1');

    previewInput = { ...previewInput, frameId: 'frame-2' };
    coordinator.requestActivePreview({ immediate: true });
    await waitFor(() => assert.equal(getExecuteCount(), 3));
    assert.equal(coordinator.getState().request.inputFrameId, 'frame-2');
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator only injects an external image into acquisition when it is bound to that node', async () => {
  const cameraNode = {
    id: 'camera-node',
    type: 'ImageAcquisition',
    title: '图像采集',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: 'cam-1' }
    ],
    outputs: [{ type: 'image' }]
  };
  let previewInput = { imageBase64: PNG_BASE64, sourceNodeId: null, frameId: null };
  const previewOptions = [];
  const { coordinator } = createCoordinator({
    node: cameraNode,
    getInputImageContext: () => previewInput,
    onPreviewOptions: options => previewOptions.push(options)
  });

  try {
    coordinator.setActiveNode(cameraNode, { autoPreview: false });
    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(previewOptions.length, 1));
    assert.equal(previewOptions[0].inputImageBase64, null);
    assert.equal(previewOptions[0].inputImageSourceNodeId, null);

    previewInput = { imageBase64: PNG_BASE64, sourceNodeId: cameraNode.id, frameId: 'frame-bound' };
    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(previewOptions.length, 2));
    assert.equal(previewOptions[1].inputImageBase64, PNG_BASE64);
    assert.equal(previewOptions[1].inputImageSourceNodeId, cameraNode.id);
  } finally {
    coordinator.destroy();
  }
});

test('captured camera source signature changes only with acquisition-affecting parameters', () => {
  const cameraNode = {
    id: 'camera-node',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: 'cam-1' },
      { name: 'TriggerMode', value: 'Software' },
      { name: 'ExposureTime', value: 5000 },
      { name: 'Gain', value: 1 },
      { name: 'UnrelatedPreviewValue', value: 10 }
    ]
  };
  const original = buildCameraPreviewSourceSignature(cameraNode);

  cameraNode.parameters.find(item => item.name === 'UnrelatedPreviewValue').value = 20;
  assert.equal(buildCameraPreviewSourceSignature(cameraNode), original);

  cameraNode.parameters.find(item => item.name === 'Gain').value = 2;
  assert.notEqual(buildCameraPreviewSourceSignature(cameraNode), original);
});

test('camera preview input context records capture identity and metadata', () => {
  const cameraNode = {
    id: 'camera-node',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: 'cam-1' },
      { name: 'TriggerMode', value: 'Software' },
      { name: 'ExposureTime', value: 5000 },
      { name: 'Gain', value: 1 }
    ]
  };

  const context = createCameraPreviewInputContext(cameraNode, {
    imageBase64: PNG_BASE64,
    projectId: 'project-1',
    frameId: 'frame-1',
    cameraBindingId: 'cam-1',
    triggerMode: 'Software',
    width: 1920,
    height: 1080,
    capturedAtUtc: '2026-07-18T08:00:00.000Z'
  });

  assert.equal(context.imageBase64, PNG_BASE64);
  assert.equal(context.sourceNodeId, cameraNode.id);
  assert.equal(context.projectId, 'project-1');
  assert.equal(context.frameId, 'frame-1');
  assert.equal(context.sourceSignature, buildCameraPreviewSourceSignature(cameraNode));
  assert.equal(context.cameraBindingId, 'cam-1');
  assert.equal(context.triggerMode, 'Software');
  assert.equal(context.width, 1920);
  assert.equal(context.height, 1080);
  assert.equal(context.capturedAtUtc, '2026-07-18T08:00:00.000Z');

  const contextWithoutDimensions = createCameraPreviewInputContext(cameraNode, {
    imageBase64: PNG_BASE64,
    frameId: 'frame-without-dimensions'
  });
  assert.equal(contextWithoutDimensions.width, null);
  assert.equal(contextWithoutDimensions.height, null);
});

test('camera preview input frame is reused only in the matching project and downstream branch', () => {
  const cameraNode = {
    id: 'camera-node',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: 'cam-1' },
      { name: 'TriggerMode', value: 'Software' },
      { name: 'ExposureTime', value: 5000 },
      { name: 'Gain', value: 1 }
    ]
  };
  const frame = createCameraPreviewInputContext(cameraNode, {
    imageBase64: PNG_BASE64,
    projectId: 'project-1',
    frameId: 'frame-1'
  });
  const connections = [
    { source: 'camera-node', target: 'equalization-node' },
    { source: 'equalization-node', target: 'blob-node' }
  ];

  const downstream = resolveCameraPreviewInputFrame({
    frame,
    currentProjectId: 'project-1',
    sourceNode: cameraNode,
    targetNodeId: 'blob-node',
    connections
  });
  assert.equal(downstream.frame, frame);
  assert.equal(downstream.shouldInvalidate, false);

  const unrelated = resolveCameraPreviewInputFrame({
    frame,
    currentProjectId: 'project-1',
    sourceNode: cameraNode,
    targetNodeId: 'unrelated-node',
    connections
  });
  assert.equal(unrelated.frame, null);
  assert.equal(unrelated.shouldInvalidate, false);

  const projectChanged = resolveCameraPreviewInputFrame({
    frame,
    currentProjectId: 'project-2',
    sourceNode: cameraNode,
    targetNodeId: 'blob-node',
    connections
  });
  assert.equal(projectChanged.frame, null);
  assert.equal(projectChanged.shouldInvalidate, true);
});

test('camera preview input frame is invalidated when the source node disappears or capture settings change', () => {
  const cameraNode = {
    id: 'camera-node',
    type: 'ImageAcquisition',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'CameraId', value: 'cam-1' },
      { name: 'TriggerMode', value: 'Software' },
      { name: 'ExposureTime', value: 5000 },
      { name: 'Gain', value: 1 }
    ]
  };
  const frame = createCameraPreviewInputContext(cameraNode, {
    imageBase64: PNG_BASE64,
    projectId: 'project-1',
    frameId: 'frame-1'
  });
  const baseRequest = {
    frame,
    currentProjectId: 'project-1',
    targetNodeId: 'camera-node'
  };

  for (const sourceNode of [null, { ...cameraNode, disabled: true }]) {
    const resolution = resolveCameraPreviewInputFrame({ ...baseRequest, sourceNode });
    assert.equal(resolution.frame, null);
    assert.equal(resolution.shouldInvalidate, true);
  }

  const changedCameraNode = {
    ...cameraNode,
    parameters: cameraNode.parameters.map(parameter =>
      parameter.name === 'ExposureTime'
        ? { ...parameter, value: 6000 }
        : parameter)
  };
  const settingsChanged = resolveCameraPreviewInputFrame({
    ...baseRequest,
    sourceNode: changedCameraNode
  });
  assert.equal(settingsChanged.frame, null);
  assert.equal(settingsChanged.shouldInvalidate, true);
  assert.match(settingsChanged.message, /采集配置已变更/);
});

test('NodePreviewCoordinator rolls back partial artifact URLs when later artifact read fails', async () => {
  const originalCreateObjectUrl = globalThis.URL.createObjectURL;
  const originalRevokeObjectUrl = globalThis.URL.revokeObjectURL;
  const revokedUrls = [];
  const deletedArtifactIds = [];
  globalThis.URL.createObjectURL = () => 'blob:partial-1';
  globalThis.URL.revokeObjectURL = url => revokedUrls.push(url);

  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      if (artifactId === 'output-artifact-1') {
        throw new Error('second artifact failed');
      }
      return { blob: { artifactId } };
    },
    async deletePreviewArtifact(artifactId) {
      deletedArtifactIds.push(artifactId);
    }
  };
  const { coordinator, node, getExecuteCount } = createCoordinator({
    artifactClient,
    previewResponse: (_count, executorOptions, nodeId) =>
      buildArtifactPreviewResponse(nodeId, executorOptions, '1')
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true });

    await waitFor(() => assert.equal(getExecuteCount(), 1));
    await waitFor(() => assert.deepEqual(deletedArtifactIds.sort(), ['input-artifact-1', 'output-artifact-1']));

    assert.deepEqual(revokedUrls, ['blob:partial-1']);
    assert.equal(coordinator.getState().status, 'loading');
    assert.equal(coordinator.cache.size, 0);
  } finally {
    globalThis.URL.createObjectURL = originalCreateObjectUrl;
    globalThis.URL.revokeObjectURL = originalRevokeObjectUrl;
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator releases artifact resources on cache replacement eviction node switch and duplicate release', () => {
  const originalRevokeObjectUrl = globalThis.URL.revokeObjectURL;
  const revokedUrls = [];
  const deletedArtifactIds = [];
  globalThis.URL.revokeObjectURL = url => revokedUrls.push(url);

  const { coordinator, node } = createCoordinator({
    maxCacheEntries: 1,
    artifactClient: {
      async getPreviewArtifactBlob() { return { blob: new Blob() }; },
      async deletePreviewArtifact(artifactId) {
        deletedArtifactIds.push(artifactId);
      }
    }
  });

  try {
    const bundle = {
      previewArtifactIds: ['duplicate-artifact', 'duplicate-artifact'],
      previewArtifactObjectUrls: ['blob:duplicate', 'blob:duplicate'],
      previewArtifactReleased: false
    };
    coordinator.releasePreviewResources(bundle);
    coordinator.releasePreviewResources(bundle);

    coordinator.setCacheEntry('same', {
      status: 'success',
      outputImageBase64: 'blob:old',
      previewArtifactIds: ['cache-old'],
      previewArtifactObjectUrls: ['blob:cache-old'],
      previewArtifactReleased: false
    });
    coordinator.setCacheEntry('same', {
      status: 'success',
      outputImageBase64: 'blob:new',
      previewArtifactIds: ['cache-new'],
      previewArtifactObjectUrls: ['blob:cache-new'],
      previewArtifactReleased: false
    });
    coordinator.setCacheEntry('evicting', {
      status: 'success',
      outputImageBase64: 'blob:evicting',
      previewArtifactIds: ['cache-evicted'],
      previewArtifactObjectUrls: ['blob:cache-evicted'],
      previewArtifactReleased: false
    });

    coordinator.state = {
      ...coordinator.getState(),
      activeNodeId: 'node-1',
      previewArtifactIds: ['state-node-switch'],
      previewArtifactObjectUrls: ['blob:state-node-switch'],
      previewArtifactReleased: false
    };
    coordinator.setActiveNode(null);

    assert.equal(coordinator.releasedObjectUrls, undefined);
    assert.equal(coordinator.deletedArtifactIds, undefined);
    assert.deepEqual(revokedUrls.sort(), [
      'blob:cache-evicted',
      'blob:cache-new',
      'blob:cache-old',
      'blob:duplicate',
      'blob:state-node-switch'
    ]);
    assert.deepEqual(deletedArtifactIds.sort(), [
      'cache-evicted',
      'cache-new',
      'cache-old',
      'duplicate-artifact',
      'state-node-switch'
    ]);
  } finally {
    globalThis.URL.revokeObjectURL = originalRevokeObjectUrl;
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator releases same-node method artifacts before debounced refresh', async () => {
  const originalCreateObjectUrl = globalThis.URL.createObjectURL;
  const originalRevokeObjectUrl = globalThis.URL.revokeObjectURL;
  const revokedUrls = [];
  const deletedArtifactIds = [];
  let objectUrlSequence = 0;
  globalThis.URL.createObjectURL = () => `blob:method-scope-${++objectUrlSequence}`;
  globalThis.URL.revokeObjectURL = url => revokedUrls.push(url);

  const node = {
    id: 'circle-node',
    type: 'CircleMeasurement',
    parameters: [{ name: 'Method', value: 'CaliperFitV2' }],
    outputs: [{ type: 'image' }]
  };
  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      return { blob: new Blob([artifactId], { type: 'image/png' }) };
    },
    async deletePreviewArtifact(artifactId) {
      deletedArtifactIds.push(artifactId);
    }
  };
  const { coordinator } = createCoordinator({
    node,
    artifactClient,
    previewResponse: (_count, executorOptions, nodeId) =>
      buildArtifactPreviewResponse(nodeId, executorOptions, 'caliper')
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));
    assert.equal(coordinator.cache.size, 1);

    node.parameters = [{ name: 'Method', value: 'HoughCircle' }];
    coordinator.requestActivePreview({ immediate: false, debounceMs: 50, trigger: 'auto' });
    await Promise.resolve();

    assert.deepEqual(revokedUrls.sort(), ['blob:method-scope-1', 'blob:method-scope-2']);
    assert.deepEqual(deletedArtifactIds.sort(), ['input-artifact-caliper', 'output-artifact-caliper']);
    assert.equal(coordinator.cache.size, 0);
    assert.equal(coordinator.getState().status, 'loading');
    assert.equal(coordinator.getState().outputData, null);
    assert.equal(coordinator.getState().outputImageBase64, null);
    assert.deepEqual(coordinator.getState().previewArtifactIds, []);
  } finally {
    globalThis.URL.createObjectURL = originalCreateObjectUrl;
    globalThis.URL.revokeObjectURL = originalRevokeObjectUrl;
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator aborts same-node project switch and deletes late artifacts', async () => {
  let projectId = 'project-1';
  let callCount = 0;
  let firstSignal = null;
  let resolveFirst = null;
  const deletedArtifactIds = [];
  const readArtifactIds = [];
  const node = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: [],
    outputs: [{ type: 'image' }]
  };
  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      readArtifactIds.push(artifactId);
      return { blob: new Blob([artifactId], { type: 'image/png' }) };
    },
    async deletePreviewArtifact(artifactId) {
      deletedArtifactIds.push(artifactId);
    }
  };

  const coordinator = new NodePreviewCoordinator({
    getProjectId: () => projectId,
    getFlowRevision: () => 1,
    getNodeById: () => node,
    getInputImageBase64: () => 'INPUT_IMAGE',
    getOperatorMetadata: () => ({ outputPorts: [{ dataType: 'image' }] }),
    artifactClient: {
      async getPreviewArtifactBlob() { return { blob: new Blob() }; },
      async deletePreviewArtifact() {}
    },
    artifactClient,
    previewExecutor: async (nodeId, options) => {
      callCount += 1;
      const callProjectId = projectId;
      if (callCount === 1) {
        firstSignal = options.signal;
        return new Promise(resolve => {
          resolveFirst = () => {
            const response = buildArtifactPreviewResponse(nodeId, options, 'late-project');
            response.observation.identity.projectId = callProjectId;
            resolve(response);
          };
        });
      }

      const response = buildArtifactPreviewResponse(nodeId, options, 'current-project');
      response.observation.identity.projectId = callProjectId;
      return response;
    },
    debounceMs: 0
  });

  try {
    coordinator.setActiveNode(node);
    await waitFor(() => assert.equal(callCount, 1));

    projectId = 'project-2';
    coordinator.requestActivePreview({ immediate: true, force: true, trigger: 'manual' });
    assert.equal(firstSignal.aborted, true);
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));
    assert.equal(coordinator.getState().observation.identity.projectId, 'project-2');

    resolveFirst();
    await waitFor(() => assert.deepEqual(deletedArtifactIds.sort(), [
      'input-artifact-late-project',
      'output-artifact-late-project'
    ]));
    assert.deepEqual(readArtifactIds, [
      'input-artifact-current-project',
      'output-artifact-current-project'
    ]);
    assert.equal(coordinator.getState().observation.identity.projectId, 'project-2');
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator deletes stale response artifacts without reading blobs', async () => {
  const readArtifactIds = [];
  const deletedArtifactIds = [];
  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      readArtifactIds.push(artifactId);
      throw new Error('stale artifacts must not be read');
    },
    async deletePreviewArtifact(artifactId) {
      deletedArtifactIds.push(artifactId);
    }
  };
  const { coordinator, node, getExecuteCount } = createCoordinator({
    artifactClient,
    previewResponse: (_count, executorOptions, nodeId) => ({
      ...buildObservationResponse(nodeId, executorOptions, { clientRequestSequence: 999999 }),
      artifacts: [
        { artifactId: 'stale-output', kind: 'image', role: 'outputImage', contentType: 'image/png', length: 4 }
      ]
    })
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true });

    await waitFor(() => assert.equal(getExecuteCount(), 1));
    await Promise.resolve();

    assert.equal(coordinator.getState().status, 'loading');
    assert.deepEqual(readArtifactIds, []);
    assert.deepEqual(deletedArtifactIds, ['stale-output']);
    assert.equal(coordinator.cache.size, 0);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator ignores late observation response without overwriting newer state', async () => {
  let callCount = 0;
  let resolveFirst = null;
  const node = {
    id: 'node-1',
    type: 'Thresholding',
    parameters: [],
    outputs: [{ type: 'image' }]
  };
  const coordinator = new NodePreviewCoordinator({
    getProjectId: () => 'project-1',
    getFlowRevision: () => 5,
    getNodeById: () => node,
    getInputImageBase64: () => 'INPUT_IMAGE',
    getOperatorMetadata: () => ({ outputPorts: [{ dataType: 'image' }] }),
    artifactClient: {
      async getPreviewArtifactBlob() { return { blob: new Blob() }; },
      async deletePreviewArtifact() {}
    },
    previewExecutor: async (nodeId, options) => {
      callCount += 1;
      if (callCount === 1) {
        return new Promise(resolve => {
          resolveFirst = () => resolve({
            ...buildObservationResponse(nodeId, options),
            outputData: { score: 'late' }
          });
        });
      }

      return {
        ...buildObservationResponse(nodeId, options),
        outputData: { score: 'current' }
      };
    },
    debounceMs: 0
  });

  try {
    coordinator.setActiveNode(node);
    await waitFor(() => assert.equal(callCount, 1));

    coordinator.invalidateActivePreview({ immediate: true, force: true });
    await waitFor(() => assert.equal(coordinator.getState().outputData?.score, 'current'));

    resolveFirst();
    await new Promise(resolve => setTimeout(resolve, 0));

    assert.equal(callCount, 2);
    assert.equal(coordinator.getState().status, 'success');
    assert.equal(coordinator.getState().outputData.score, 'current');
    assert.equal(coordinator.getState().errorMessage, null);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator retains accepted observation and reads current artifacts on demand', async () => {
  const originalCreateObjectUrl = globalThis.URL.createObjectURL;
  const createdObjectUrls = [];
  const readArtifactIds = [];
  const artifactClient = {
    async getPreviewArtifactBlob(artifactId) {
      readArtifactIds.push(artifactId);
      return {
        blob: new Blob(['payload'], { type: artifactId === 'image-artifact' ? 'image/png' : 'text/plain' }),
        headers: new Map()
      };
    },
    async deletePreviewArtifact() {}
  };
  globalThis.URL.createObjectURL = blob => {
    const url = `blob://artifact-${createdObjectUrls.length + 1}`;
    createdObjectUrls.push({ url, type: blob.type });
    return url;
  };

  const { coordinator, node } = createCoordinator({
    artifactClient,
    previewResponse: (_count, executorOptions, nodeId) => ({
      ...buildObservationResponse(nodeId, executorOptions),
      outputImageBase64: null,
      artifacts: [
        { artifactId: 'text-artifact', kind: 'profile', role: 'profile', contentType: 'text/plain', length: 7 },
        { artifactId: 'image-artifact', kind: 'image', role: 'mask', contentType: 'image/png', length: 7 }
      ]
    })
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true });

    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));
    const state = coordinator.getState();
    assert.equal(state.observation.identity.targetNodeId, node.id);
    assert.equal(state.artifacts.length, 2);

    const textRead = await coordinator.readArtifactForCurrentState(
      'text-artifact',
      state.observation.identity
    );
    assert.equal(await textRead.blob.text(), 'payload');

    const imageRead = await coordinator.readArtifactForCurrentState(
      'image-artifact',
      state.observation.identity,
      { objectUrl: true }
    );
    assert.equal(imageRead.objectUrl, 'blob://artifact-1');
    assert.deepEqual(readArtifactIds, ['text-artifact', 'image-artifact']);
    assert.equal(coordinator.getState().previewArtifactObjectUrls.includes('blob://artifact-1'), true);

    await assert.rejects(
      () => coordinator.readArtifactForCurrentState('missing-artifact', state.observation.identity),
      /stale/
    );
    await assert.rejects(
      () => coordinator.readArtifactForCurrentState('text-artifact', {
        ...state.observation.identity,
        flowRevision: 999
      }),
      /stale/
    );
  } finally {
    globalThis.URL.createObjectURL = originalCreateObjectUrl;
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator clears old image and summary before a changed parameter snapshot or flow revision resolves', async () => {
  let flowRevision = 1;
  const node = {
    id: 'blob-node',
    type: 'BlobAnalysis',
    parameters: [{ name: 'MaxArea', value: 1000 }],
    outputs: [{ type: 'image' }]
  };
  const { coordinator } = createCoordinator({
    node,
    flowRevision: () => flowRevision,
    previewResponse: (executeCount, executorOptions, nodeId) => ({
      ...buildObservationResponse(nodeId, executorOptions),
      outputImageBase64: `OUTPUT_IMAGE_${executeCount}`,
      outputData: { BlobCount: executeCount }
    })
  });

  try {
    coordinator.setActiveNode(node);
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));
    assert.equal(coordinator.getState().outputImageBase64, 'OUTPUT_IMAGE_1');
    assert.deepEqual(coordinator.getState().outputData, { BlobCount: 1 });

    node.parameters[0].value = 150;
    flowRevision = 2;
    const refreshed = coordinator.invalidateActivePreview({ immediate: false });

    assert.equal(coordinator.getState().status, 'loading');
    assert.equal(coordinator.getState().outputImageBase64, null);
    assert.equal(coordinator.getState().outputData, null);
    assert.equal(coordinator.getState().presenter.outputImageSrc, null);
    assert.equal(coordinator.getState().presenter.summaryItems.length, 0);

    await refreshed;
    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));
    assert.equal(coordinator.getState().request.flowRevision, 2);
    assert.equal(coordinator.getState().outputImageBase64, 'OUTPUT_IMAGE_2');
    assert.deepEqual(coordinator.getState().outputData, { BlobCount: 2 });
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator maps side-effect admission rejection to blocked preview state', async () => {
  const sideEffectError = new Error('node preview blocked side-effect operator');
  sideEffectError.status = 400;
  sideEffectError.payload = {
    code: 'ADMISSION_NODE_PREVIEW_SIDE_EFFECT_BLOCKED',
    error: '节点预览已安全拦截副作用算子“TcpCommunication”：预览不会执行外部动作，正式运行流程时才会执行。'
  };

  const { coordinator, node } = createCoordinator({
    previewExecutor: async () => {
      throw sideEffectError;
    }
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true });

    await waitFor(() => assert.equal(coordinator.getState().status, 'blocked'));
    assert.match(coordinator.getState().errorMessage, /节点预览已安全拦截副作用算子/);
    assert.doesNotMatch(coordinator.getState().errorMessage, /blocked side-effect/);
    assert.equal(coordinator.getState().outputImageBase64, null);
  } finally {
    coordinator.destroy();
  }
});

test('NodePreviewCoordinator maps expired artifact reads without polluting preview state', async () => {
  const artifactClient = {
    async getPreviewArtifactBlob() {
      const error = new Error('HTTP 404');
      error.status = 404;
      throw error;
    },
    async deletePreviewArtifact() {}
  };
  const { coordinator, node } = createCoordinator({
    artifactClient,
    previewResponse: (_count, executorOptions, nodeId) => ({
      ...buildObservationResponse(nodeId, executorOptions),
      artifacts: [
        { artifactId: 'expired-artifact', kind: 'profile', role: 'profile', contentType: 'application/json', length: 2 }
      ]
    })
  });

  try {
    coordinator.setActiveNode(node);
    coordinator.invalidateActivePreview({ immediate: true, force: true });

    await waitFor(() => assert.equal(coordinator.getState().status, 'success'));
    const identity = coordinator.getState().observation.identity;

    await assert.rejects(
      () => coordinator.readArtifactForCurrentState('expired-artifact', identity),
      /资源已过期或不可用/
    );
    assert.equal(coordinator.getState().status, 'success');
    assert.equal(coordinator.getState().errorMessage, null);
  } finally {
    coordinator.destroy();
  }
});

test('previewObservationMatchesRequest keeps compatibility for old responses without observation', () => {
  assert.equal(
    previewObservationMatchesRequest(
      { success: true, outputData: { score: 1 } },
      {
        projectId: 'project-1',
        targetNodeId: 'node-1',
        debugSessionId: 'debug-1',
        clientRequestSequence: 1,
        flowRevision: 1
      }
    ),
    true
  );
});
