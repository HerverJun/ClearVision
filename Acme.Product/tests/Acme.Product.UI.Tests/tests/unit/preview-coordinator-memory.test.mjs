import test from 'node:test';
import assert from 'node:assert/strict';
import { NodePreviewCoordinator } from '../../../../src/Acme.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';

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
    getFlowRevision: () => 1,
    getNodeById: () => node,
    getInputImageBase64: () => options.inputImageBase64 ?? 'INPUT_IMAGE',
    getOperatorMetadata: () => ({ outputPorts: [{ dataType: 'image' }] }),
    previewExecutor: async () => {
      executeCount += 1;
      if (typeof options.previewResponse === 'function') {
        return options.previewResponse(executeCount);
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
