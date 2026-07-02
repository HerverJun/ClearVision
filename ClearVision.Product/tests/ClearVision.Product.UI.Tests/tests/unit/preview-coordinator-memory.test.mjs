import test from 'node:test';
import assert from 'node:assert/strict';
import {
  NodePreviewCoordinator,
  previewObservationMatchesRequest
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';

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
    getProjectId: () => 'project-1',
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
    coordinator.invalidateActivePreview({ immediate: true, force: true });

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
    coordinator.invalidateActivePreview({ immediate: true, force: true });

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
