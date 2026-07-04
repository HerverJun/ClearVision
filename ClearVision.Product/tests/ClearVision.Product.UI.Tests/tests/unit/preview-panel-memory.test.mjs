import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildPreviewParameterSnapshot
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js';
import {
  PreviewPanel
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanel.js';
import {
  MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS,
  buildOperatorResultViewModel,
  buildSafeJsonPreview
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/operatorResultViewModel.mjs';

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
