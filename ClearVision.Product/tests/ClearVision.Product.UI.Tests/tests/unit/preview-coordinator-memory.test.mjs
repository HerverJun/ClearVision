import test from 'node:test';
import assert from 'node:assert/strict';
import {
  NodePreviewCoordinator,
  previewObservationMatchesRequest
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';
import {
  normalizeAcquisitionSourceType
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/parameterDependencyRules.js';

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
    getFlowRevision: () => options.flowRevision ?? 1,
    getNodeById: () => node,
    getInputImageBase64: () => options.inputImageBase64 ?? 'INPUT_IMAGE',
    getOperatorMetadata: () => ({ outputPorts: [{ dataType: 'image' }] }),
    artifactClient: options.artifactClient,
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
    assert.equal(coordinator.getState().status, 'success');
    assert.deepEqual(coordinator.getState().outputData, { score: 1 });
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
