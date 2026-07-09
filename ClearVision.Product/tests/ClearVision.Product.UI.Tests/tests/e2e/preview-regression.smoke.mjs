import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  NodePreviewCoordinator,
  getCanvasPreviewEligibility,
  getOperatorPreviewCostPolicy,
  resolvePreviewInputImageBase64,
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';
import ResultPanel from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultPanel.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '../../../..');

const PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const minimumSmokeAssertions = parseMinimum('CV_PREVIEW_SMOKE_MIN_ASSERTIONS', 35);
let smokeAssertionCount = 0;

const smokeAssert = {
  equal(...args) {
    assert.equal(...args);
    smokeAssertionCount += 1;
  },
  match(...args) {
    assert.match(...args);
    smokeAssertionCount += 1;
  },
  ok(...args) {
    assert.ok(...args);
    smokeAssertionCount += 1;
  },
};

function parseMinimum(name, fallback) {
  const raw = process.env[name];
  if (!raw) {
    return fallback;
  }

  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

async function runPreviewCoordinatorChecks() {
  smokeAssert.equal(resolvePreviewInputImageBase64({ outputImageBase64: PNG_BASE64 }), PNG_BASE64);
  smokeAssert.equal(resolvePreviewInputImageBase64({ OutputImage: `data:image/png;base64,${PNG_BASE64}` }), PNG_BASE64);
  smokeAssert.equal(
    resolvePreviewInputImageBase64({ outputData: { PreviewImage: `data:image/png;base64,${PNG_BASE64}` } }),
    PNG_BASE64
  );

  smokeAssert.equal(getCanvasPreviewEligibility({ type: 'ImageAcquisition', outputs: [] }).eligible, true);
  smokeAssert.equal(
    getCanvasPreviewEligibility({ type: 'LegacyImageNode', outputs: [{ name: 'Image', type: '0' }] }).eligible,
    true
  );
  smokeAssert.equal(
    getCanvasPreviewEligibility({ type: 'StringNode', outputs: [{ name: 'Text', type: 'String' }] }).eligible,
    false
  );

  const acquisitionCoordinator = new NodePreviewCoordinator({
    getProjectId: () => 'project-1',
    getFlowRevision: () => 1,
    getNodeById: () => ({
      id: 'acq-1',
      type: 'ImageAcquisition',
      title: '图像采集',
      parameters: [
        { name: 'SourceType', value: 'File' },
        { name: 'FilePath', value: '' },
      ],
      outputs: [{ name: 'Image', type: 'Image' }],
    }),
    getOperatorMetadata: () => null,
    getInputImageBase64: () => null,
    previewExecutor: async () => {
      throw new Error('should not execute preview');
    },
    debounceMs: 10,
  });

  acquisitionCoordinator.setActiveNode({
    id: 'acq-1',
    type: 'ImageAcquisition',
    title: '图像采集',
    parameters: [
      { name: 'SourceType', value: 'File' },
      { name: 'FilePath', value: '' },
    ],
    outputs: [{ name: 'Image', type: 'Image' }],
  });
  await sleep(30);
  smokeAssert.equal(acquisitionCoordinator.getState().status, 'idle');
  smokeAssert.equal(acquisitionCoordinator.getState().presenter.statusText, '请先配置文件路径');
  acquisitionCoordinator.destroy();

  let cameraPreviewCalls = 0;
  let cameraPreviewOptions = null;
  const cameraNode = {
    id: 'camera-acq-1',
    type: 'ImageAcquisition',
    title: '鍥惧儚閲囬泦',
    parameters: [
      { name: 'SourceType', value: 'Camera' },
      { name: 'FilePath', value: 'stale-file.png' },
      { name: 'CameraId', value: 'cam-1' },
    ],
    outputs: [{ name: 'Image', type: 'Image' }],
  };
  const cameraCoordinator = new NodePreviewCoordinator({
    getProjectId: () => 'project-1',
    getFlowRevision: () => 1,
    getNodeById: () => cameraNode,
    getOperatorMetadata: () => null,
    getInputImageBase64: () => PNG_BASE64,
    previewExecutor: async (_nodeId, options) => {
      cameraPreviewCalls += 1;
      cameraPreviewOptions = options;
      return {
        success: true,
        inputImageBase64: PNG_BASE64,
        outputImageBase64: PNG_BASE64,
        outputData: { Source: 'camera' },
      };
    },
    debounceMs: 10,
  });

  cameraCoordinator.setActiveNode(cameraNode);
  await sleep(30);
  smokeAssert.equal(cameraPreviewCalls, 0, 'camera acquisition should not auto-preview by default');
  smokeAssert.equal(
    getOperatorPreviewCostPolicy(cameraNode).autoPreviewAllowed,
    false,
    'live camera acquisition should require manual preview'
  );
  cameraCoordinator.requestActivePreview({ immediate: true, force: true, trigger: 'manual' });
  await sleep(30);
  smokeAssert.equal(cameraPreviewCalls, 1, 'manual camera preview should still execute with a selected camera');
  smokeAssert.equal(cameraPreviewOptions.inputImageBase64, null, 'camera acquisition should not receive stale external images');
  smokeAssert.equal(cameraCoordinator.getState().status, 'success');
  cameraCoordinator.destroy();

  let noProjectPreviewCalls = 0;
  const noProjectCoordinator = new NodePreviewCoordinator({
    getProjectId: () => null,
    getFlowRevision: () => 1,
    getNodeById: () => ({
      id: 'no-project-node',
      type: 'PreviewImageNode',
      title: 'Preview without project',
      parameters: [],
      outputs: [{ name: 'Image', type: 'Image' }],
    }),
    getOperatorMetadata: () => null,
    getInputImageBase64: () => PNG_BASE64,
    previewExecutor: async () => {
      noProjectPreviewCalls += 1;
      return { success: true };
    },
    debounceMs: 10,
  });

  noProjectCoordinator.setActiveNode({
    id: 'no-project-node',
    type: 'PreviewImageNode',
    title: 'Preview without project',
    parameters: [],
    outputs: [{ name: 'Image', type: 'Image' }],
  });
  await sleep(30);
  smokeAssert.equal(noProjectPreviewCalls, 0, 'missing project should not execute preview');
  smokeAssert.equal(noProjectCoordinator.getState().status, 'idle');
  smokeAssert.equal(noProjectCoordinator.getState().presenter.hasError, false);
  smokeAssert.equal(noProjectCoordinator.getState().presenter.statusText, '请先新建/保存/打开工程后再预览');
  noProjectCoordinator.destroy();

  let abortPreviewCalls = 0;
  let abortEvents = 0;
  const abortNode = {
    id: 'abort-node',
    type: 'PreviewImageNode',
    title: 'Abort preview node',
    parameters: [],
    outputs: [{ name: 'Image', type: 'Image' }],
  };
  const abortCoordinator = new NodePreviewCoordinator({
    getProjectId: () => 'project-1',
    getFlowRevision: () => 1,
    getNodeById: () => abortNode,
    getOperatorMetadata: () => null,
    getInputImageBase64: () => PNG_BASE64,
    previewExecutor: async (_nodeId, options) => {
      abortPreviewCalls += 1;
      smokeAssert.ok(options.signal, 'preview requests should receive an abort signal');

      if (abortPreviewCalls === 1) {
        return await new Promise((_resolve, reject) => {
          const abort = () => {
            abortEvents += 1;
            const error = new Error('aborted');
            error.name = 'AbortError';
            reject(error);
          };
          options.signal.addEventListener('abort', abort, { once: true });
        });
      }

      return {
        success: true,
        outputImageBase64: PNG_BASE64,
        outputData: { Score: abortPreviewCalls },
      };
    },
    debounceMs: 10,
  });

  abortCoordinator.setActiveNode(abortNode);
  await sleep(30);
  abortCoordinator.requestActivePreview({ immediate: true, force: true });
  await sleep(30);
  smokeAssert.equal(abortEvents, 1, 'superseded preview should abort the previous request');
  smokeAssert.equal(abortPreviewCalls, 2);
  smokeAssert.equal(abortCoordinator.getState().status, 'success');
  abortCoordinator.destroy();

  let previewCalls = 0;
  let node = {
    id: 'node-1',
    type: 'PreviewImageNode',
    title: '图像预览节点',
    parameters: [{ name: 'Threshold', value: 10 }],
    outputs: [{ name: 'Image', type: 'Image' }],
  };
  let flowRevision = 1;
  const debugSessionIds = [];

  const coordinator = new NodePreviewCoordinator({
    getProjectId: () => 'project-1',
    getFlowRevision: () => flowRevision,
    getNodeById: () => node,
    getOperatorMetadata: () => null,
    getInputImageBase64: () => PNG_BASE64,
    previewExecutor: async (_nodeId, options) => {
      previewCalls += 1;
      debugSessionIds.push(options.debugSessionId);
      await sleep(10);
      return {
        success: true,
        outputImageBase64: PNG_BASE64,
        outputData: { Score: previewCalls },
        executionTimeMs: 7,
      };
    },
    debounceMs: 30,
  });

  coordinator.setActiveNode(node);
  await sleep(80);
  smokeAssert.equal(previewCalls, 1);
  smokeAssert.equal(coordinator.getState().status, 'success');
  smokeAssert.equal(coordinator.getState().presenter.overlayEnabled, true);

  coordinator.requestActivePreview();
  await sleep(50);
  smokeAssert.equal(previewCalls, 1, 'same request key should reuse cache');

  node = {
    ...node,
    parameters: [{ name: 'Threshold', value: 20 }],
  };
  coordinator.invalidateActivePreview();
  await sleep(80);
  smokeAssert.equal(previewCalls, 2, 'parameter change should invalidate preview');

  flowRevision = 2;
  coordinator.handleStructureChanged();
  await sleep(80);
  smokeAssert.equal(previewCalls, 3, 'flow revision change should invalidate preview');
  smokeAssert.equal(
    new Set(debugSessionIds).size,
    1,
    'active node previews should keep one debug session so upstream debug cache can be reused'
  );

  coordinator.destroy();

  const boundedCacheCoordinator = new NodePreviewCoordinator({
    getProjectId: () => 'project-1',
    getFlowRevision: () => 1,
    getNodeById: id => ({
      id,
      type: 'PreviewImageNode',
      title: id,
      parameters: [{ name: 'Value', value: id }],
      outputs: [{ name: 'Image', type: 'Image' }],
    }),
    getOperatorMetadata: () => null,
    previewExecutor: async () => ({
      success: true,
      outputImageBase64: PNG_BASE64,
    }),
    debounceMs: 1,
    maxCacheEntries: 2,
  });

  for (const id of ['cache-node-1', 'cache-node-2', 'cache-node-3']) {
    boundedCacheCoordinator.setActiveNode({ id, type: 'PreviewImageNode', title: id, parameters: [], outputs: [{ name: 'Image', type: 'Image' }] });
    await sleep(20);
  }

  smokeAssert.equal(boundedCacheCoordinator.cache.size, 2, 'preview cache should be bounded');
  boundedCacheCoordinator.destroy();
}

function runSourceWiringChecks() {
  const appPath = path.join(
    repoRoot,
    'src',
    'ClearVision.Product.Desktop',
    'wwwroot',
    'src',
    'app.js'
  );
  const flowEditorPath = path.join(
    repoRoot,
    'src',
    'ClearVision.Product.Desktop',
    'wwwroot',
    'src',
    'features',
    'flow-editor',
    'flowEditorInteraction.js'
  );

  const appSource = fs.readFileSync(appPath, 'utf8');
  const flowEditorSource = fs.readFileSync(flowEditorPath, 'utf8');

  smokeAssert.match(appSource, /NodePreviewCoordinator/);
  smokeAssert.match(appSource, /NodePreviewOverlay/);
  smokeAssert.match(appSource, /previewCoordinator:\s*nodePreviewCoordinator/);

  const notifyMatches = flowEditorSource.match(/notifyViewStateChanged\?\.\(\)/g) || [];
  smokeAssert.match(flowEditorSource, /scheduleViewStateNotification/);
  smokeAssert.ok(
    notifyMatches.length >= 2,
    'flow editor interaction should notify view-state changes at commit points and throttle high-frequency updates'
  );
}

function runResultPanelChecks() {
  const fakeResultPanel = {
    isExportMetadataKey: ResultPanel.prototype.isExportMetadataKey,
    isTechnicalCollectionKey: ResultPanel.prototype.isTechnicalCollectionKey,
    isStructuredExportText: () => false,
  };

  smokeAssert.equal(
    ResultPanel.prototype.shouldHideOutputDetailEntry.call(fakeResultPanel, 'OriginalImage', 'hidden', {}),
    true
  );
  smokeAssert.equal(
    ResultPanel.prototype.shouldHideOutputDetailEntry.call(fakeResultPanel, 'Image', 'hidden', {}),
    true
  );
  smokeAssert.equal(
    ResultPanel.prototype.shouldHideOutputDetailEntry.call(fakeResultPanel, 'Count', 1, {}),
    false
  );
}

await runPreviewCoordinatorChecks();
runSourceWiringChecks();
runResultPanelChecks();
if (smokeAssertionCount < minimumSmokeAssertions) {
  console.error(
    `Preview regression smoke coverage regressed: assertions=${smokeAssertionCount}, ` +
      `minimumAssertions=${minimumSmokeAssertions}.`
  );
  process.exit(1);
}

console.log(`preview regression smoke passed: assertions=${smokeAssertionCount}`);
