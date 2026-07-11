import test from 'node:test';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const repoRoot = path.resolve(import.meta.dirname, '../../../../..');
const moduleUrl = pathToFileURL(path.join(
  repoRoot,
  'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelBuildPresentation.js'
)).href;

const { deriveAiBuildPresentation, aiPanelBuildPresentationTestApi } = await import(moduleUrl);

function createPanel(result, overrides = {}) {
  const panel = {
    currentResult: result,
    activeAgentRunEvents: [],
    workbenchState: result.workbenchState || 'ready_to_apply',
    pendingVisionPlan: {
      semanticExtraction: {
        imageSource: '工业相机',
        outputTarget: 'OK/NG'
      }
    },
    pendingResourceDrafts: {},
    _getAgentRunResultPayload: () => ({}),
    _getPayloadBuildResult: payload => payload?.buildResult || null,
    _getResultFlowForCanvas: payload => payload?.flow || null,
    _extractOperators: flow => flow?.operators || [],
    _extractConnections: flow => flow?.connections || [],
    _resolvePendingParametersForDraft: payload => payload?.pendingParameters || [],
    _getPendingOperatorSourceOperators: flow => flow?.operators || [],
    _collectPendingDraftGroups: pending => pending.map(item => ({
      operatorId: item.operatorId,
      fields: (item.parameterNames || []).map(parameterName => ({ parameterName }))
    })),
    _getPendingParameterConfirmationState: pending => ({
      totals: {
        total: pending.reduce((sum, item) => sum + (item.parameterNames?.length || 1), 0),
        filled: 0
      },
      isConfirmed: false
    }),
    _normalizeMissingResources: value => Array.isArray(value) ? value : [],
    _getPendingResourceDraft: item => panel.pendingResourceDrafts[item.resourceKey] || null,
    _getPayloadApplyGate: payload => payload?.applyGate || payload?.buildResult?.applyGate || null,
    _isCurrentResultAppliedToCanvas: () => false,
    _sanitizeBuildWorkspaceText: value => String(value || '').trim(),
    _sanitizeAssistantFailureText: value => String(value || '').trim(),
    ...overrides
  };
  return panel;
}

function readyResult() {
  return {
    flow: {
      operators: [
        { id: 'op_1', type: 'ImageAcquisition', name: '图像采集' },
        { id: 'op_2', type: 'Threshold', name: '二值化' }
      ],
      connections: [{ from: 'op_1', to: 'op_2' }]
    },
    buildResult: {
      operatorPipeline: [
        { operatorType: 'ImageAcquisition', displayName: '图像采集', status: 'completed' },
        { operatorType: 'Threshold', displayName: '二值化', status: 'completed' }
      ],
      workflowDiff: {
        addedNodes: ['op_1', 'op_2'],
        modifiedNodes: [],
        removedNodes: [],
        connectionChanges: ['op_1->op_2'],
        parameterChanges: ['op_2.Threshold']
      },
      applyGate: {
        canvasApplyReady: true,
        runtimeDraftReady: true,
        deploymentReady: true,
        blocked: false,
        status: 'ready'
      }
    },
    applyGate: {
      canvasApplyReady: true,
      runtimeDraftReady: true,
      deploymentReady: true,
      blocked: false,
      status: 'ready'
    },
    pendingParameters: [],
    missingResources: []
  };
}

test('Build presentation derives an apply-ready summary without mutating canonical data', () => {
  const result = readyResult();
  const before = structuredClone(result);
  const presentation = deriveAiBuildPresentation(createPanel(result));

  assert.equal(presentation.overall.key, 'ready_to_apply');
  assert.equal(presentation.nodeCount, 2);
  assert.equal(presentation.connectionCount, 1);
  assert.equal(presentation.actionItems.length, 0);
  assert.equal(presentation.gate.canvasReady, true);
  assert.deepEqual(result, before);
});

test('Build presentation groups parameter and resource blockers into the existing work areas', () => {
  const result = readyResult();
  result.applyGate = { canvasApplyReady: false, blocked: true, status: 'blocked' };
  result.buildResult.applyGate = result.applyGate;
  result.pendingParameters = [{ operatorId: 'op_2', parameterNames: ['Threshold', 'MaxValue'] }];
  result.missingResources = [{ resourceType: 'camera_binding', resourceKey: 'op_1.CameraId' }];

  const presentation = deriveAiBuildPresentation(createPanel(result));

  assert.equal(presentation.overall.key, 'needs_input');
  assert.equal(presentation.parameters.unresolved, 2);
  assert.equal(presentation.resources.unresolved, 1);
  assert.deepEqual(presentation.actionItems.map(item => item.key), ['parameters', 'resources']);
  assert.equal(presentation.actionItems[0].target, 'ai-build-parameters-section');
  assert.equal(presentation.actionItems[1].target, 'ai-build-resources-section');
});

test('Build presentation reports validation failure from structured diagnostics', () => {
  const result = readyResult();
  result.applyGate = { canvasApplyReady: false, blocked: true, status: 'blocked' };
  result.buildResult.applyGate = result.applyGate;
  result.validationPreview = {
    structuralValidation: { passed: false, status: 'failed' },
    dryRun: { status: 'pending' }
  };
  result.lastAttemptDiagnostics = [{ severity: 'error', message: '输出端口类型不兼容。' }];

  const presentation = deriveAiBuildPresentation(createPanel(result, { workbenchState: 'failed' }));

  assert.equal(presentation.overall.key, 'validation_failed');
  assert.equal(presentation.validation.overall, 'failed');
  assert.equal(presentation.actionItems[0].key, 'validation');
  assert.match(presentation.actionItems[0].summary, /输出端口类型不兼容/);
});

test('Build presentation keeps resolved resource drafts out of the unresolved count', () => {
  const result = readyResult();
  result.missingResources = [{ resourceType: 'model_resource', resourceKey: 'op_2.ModelPath' }];
  const panel = createPanel(result);
  panel.pendingResourceDrafts['op_2.ModelPath'] = { status: 'resolved', value: 'model-resource:v1' };

  const presentation = deriveAiBuildPresentation(panel);

  assert.equal(presentation.resources.total, 1);
  assert.equal(presentation.resources.resolved, 1);
  assert.equal(presentation.resources.unresolved, 0);
});

test('Build presentation distinguishes Applied from Apply ready and preserves Workflow Diff counts', () => {
  const result = readyResult();
  const presentation = deriveAiBuildPresentation(createPanel(result, {
    workbenchState: 'applied',
    _isCurrentResultAppliedToCanvas: () => true
  }));

  assert.equal(presentation.overall.key, 'applied');
  assert.equal(presentation.applied, true);
  assert.deepEqual(
    {
      added: presentation.diff.added,
      connections: presentation.diff.connections,
      parameters: presentation.diff.parameters
    },
    { added: 2, connections: 1, parameters: 1 }
  );
});

test('Build presentation helpers classify canonical check states without DOM inspection', () => {
  assert.deepEqual(aiPanelBuildPresentationTestApi.classifyCheck({ status: 'failed' }), { status: 'failed', label: '未通过' });
  assert.deepEqual(aiPanelBuildPresentationTestApi.classifyCheck({ passed: true }), { status: 'passed', label: '已通过' });
  assert.deepEqual(aiPanelBuildPresentationTestApi.classifyCheck({ status: 'running' }), { status: 'running', label: '进行中' });
});
