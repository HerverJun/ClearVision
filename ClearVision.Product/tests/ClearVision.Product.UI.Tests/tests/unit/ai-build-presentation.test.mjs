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
const { partitionPendingParameters } = await import(pathToFileURL(path.join(
  repoRoot,
  'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPendingParameterPartition.js'
)).href);

function createPanel(result, overrides = {}) {
  const panel = {
    currentResult: result,
    activeAgentRunEvents: [],
    activeAgentRunId: null,
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
    _hasPendingDraftValue: value => value !== null && value !== undefined && String(value).trim().length > 0,
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
  assert.equal(presentation.gate.status, '已就绪');
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

test('failed Build presentation keeps the quarantined artifact summary and blocker visible', () => {
  const events = [
    {
      sequence: 35,
      eventType: 'workflow.draft.updated',
      stage: 'workflow_draft',
      payload: { details: { operatorCount: 6, connectionCount: 7 } }
    },
    {
      sequence: 79,
      eventType: 'artifact.created',
      stage: 'artifact',
      payload: {
        effectiveOperators: ['ImageAcquisition', 'RoiManager', 'Thresholding', 'BlobAnalysis', 'ResultJudgment', 'ResultOutput'],
        workflowDiff: { addedNodes: ['op_cam', 'op_roi', 'op_threshold', 'op_blob', 'op_judge', 'op_out'] },
        applyGate: {
          canvasApplyReady: false,
          blocked: true,
          status: 'blocked',
          applyBlockers: ['route_missing_task_processor']
        }
      }
    },
    {
      sequence: 80,
      eventType: 'run.failed',
      stage: 'run',
      payload: { failureCode: 'route_semantics_not_satisfied' }
    }
  ];
  const panel = createPanel({}, {
    activeAgentRunEvents: events,
    workbenchState: 'failed',
    _getAgentRunResultPayload: () => events.at(-1).payload
  });

  const presentation = deriveAiBuildPresentation(panel);

  assert.equal(presentation.nodeCount, 6);
  assert.equal(presentation.connectionCount, 7);
  assert.equal(presentation.gate.blocked, true);
  assert.ok(presentation.gate.blockers.includes('route_missing_task_processor'));
  assert.ok(presentation.actionItems.some(item => item.priority === 'blocking'));
});

test('failed Build presentation prioritizes sanitized FailureSummary and first repair target', () => {
  const result = readyResult();
  result.flow = null;
  result.failureSummary = {
    category: 'workflow_artifact_admission',
    code: 'route_missing_task_processor',
    message: 'Processor is missing near C:\\factory\\secret\\flow.json.',
    repairTarget: 'Add the required processor before Apply.',
    secondaryDiagnosticCodes: ['route_semantics_not_satisfied', 'route_graph_unverified']
  };
  result.applyGate = {
    canvasApplyReady: false,
    blocked: true,
    status: 'blocked',
    applyBlockers: ['route_semantics_not_satisfied']
  };
  result.buildResult.applyGate = result.applyGate;
  const panel = createPanel(result, {
    workbenchState: 'failed',
    _sanitizeBuildWorkspaceText: (value, maxChars) => String(value || '')
      .replace(/C:\\factory\\secret\\flow\.json/gi, '[redacted-path]')
      .slice(0, maxChars)
  });

  const presentation = deriveAiBuildPresentation(panel);

  assert.equal(presentation.actionItems[0].key, 'failure');
  assert.match(presentation.actionItems[0].title, /route_missing_task_processor/);
  assert.match(presentation.actionItems[0].summary, /\[redacted-path\]/);
  assert.doesNotMatch(presentation.actionItems[0].summary, /C:\\factory/);
  assert.match(presentation.actionItems[0].impact, /required processor/);
  assert.deepEqual(presentation.failureSummary.secondaryDiagnosticCodes, [
    'route_semantics_not_satisfied',
    'route_graph_unverified'
  ]);
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
  assert.equal(aiPanelBuildPresentationTestApi.formatGateStatus(null, 'ready'), '已就绪');
  assert.equal(aiPanelBuildPresentationTestApi.formatGateStatus(null, 'blocked'), '已阻断');
  assert.equal(aiPanelBuildPresentationTestApi.formatGateStatus(null, 'unknown'), '未设置');
});

test('canonical Build stages keep structural, DryRun, and gate diagnostics in their own levels', () => {
  const panel = createPanel(readyResult());
  const derive = aiPanelBuildPresentationTestApi.deriveValidation;
  const stageGroups = {
    structural: ['validate_schema', 'schema', 'validator', 'topology', 'operator_contract'],
    dryRun: ['metadata_dry_run', 'manifest_dry_run', 'dryrun', 'preview'],
    gate: ['readiness', 'package_readiness', 'station_compatibility', 'release_review', 'apply_gate', 'deployment']
  };
  for (const [scope, stages] of Object.entries(stageGroups)) {
    for (const stage of stages) {
      const conflictingCategory = scope === 'structural'
        ? 'release'
        : scope === 'dryRun'
          ? 'contract'
          : 'connection';
      const actual = derive(panel, {
        validationPreview: {
          structuralValidation: { passed: true, status: 'passed' },
          dryRun: { succeeded: true, status: 'completed' }
        },
        lastAttemptDiagnostics: [{
          stage,
          issues: [{ severity: 'error', category: conflictingCategory, message: `${stage} failed` }]
        }]
      }, { canvasApplyReady: true, blocked: false });
      assert.equal(actual[scope].status, 'failed', stage);
      assert.equal(actual.overall, 'failed', `${stage} overall`);
      assert.equal(actual.errors[0].diagnosticStage, stage, `${stage} inherited stage`);
    }
  }
});

test('unknown canonical diagnostic remains a visible other engineering failure', () => {
  const panel = createPanel(readyResult());
  const validation = aiPanelBuildPresentationTestApi.deriveValidation(panel, {
    validationPreview: {
      structuralValidation: { passed: true, status: 'passed' },
      dryRun: { succeeded: true, status: 'completed' }
    },
    lastAttemptDiagnostics: [{
      stage: 'future_engineering_check',
      issues: [{ severity: 'error', category: 'future_category', message: '未来检查失败' }]
    }]
  }, { canvasApplyReady: true, blocked: false });

  assert.equal(validation.structural.status, 'passed');
  assert.equal(validation.dryRun.status, 'passed');
  assert.equal(validation.gate.status, 'passed');
  assert.equal(validation.other.status, 'failed');
  assert.equal(validation.overall, 'failed');
  const html = aiPanelBuildPresentationTestApi.renderValidationSummary(panel, { validation });
  assert.match(html, /其他工程检查/);
  assert.match(html, /未通过/);
});

test('parameter projection separates fill progress, confirmation, and resource-backed fields', () => {
  const derive = aiPanelBuildPresentationTestApi.deriveParameterState;
  const parameterNames = Array.from({ length: 10 }, (_, index) => `P${index + 1}`);
  const pending = [{ operatorId: 'op_1', parameterNames }];
  const makePanel = (filledCount, isConfirmed = false) => ({
    _resolvePendingParametersForDraft: () => pending,
    _getPendingOperatorSourceOperators: () => [],
    _collectPendingDraftGroups: items => items.map(item => ({
      operatorId: item.operatorId,
      fields: item.parameterNames.map((parameterName, index) => ({
        parameterName,
        dataType: 'text',
        confirmedValue: index < filledCount ? `value-${index}` : null
      }))
    })),
    _getPendingParameterConfirmationState: () => ({ isConfirmed }),
    _hasPendingDraftValue: value => value !== null && value !== undefined && String(value).length > 0
  });

  const nineFilled = derive(makePanel(9), {}, null, []);
  assert.deepEqual(
    {
      total: nineFilled.total,
      filled: nineFilled.filled,
      remainingToFill: nineFilled.remainingToFill,
      awaitingConfirmation: nineFilled.awaitingConfirmation,
      unresolved: nineFilled.unresolved
    },
    { total: 10, filled: 9, remainingToFill: 1, awaitingConfirmation: false, unresolved: 1 }
  );

  const awaiting = derive(makePanel(10), {}, null, []);
  assert.equal(awaiting.remainingToFill, 0);
  assert.equal(awaiting.awaitingConfirmation, true);
  assert.equal(awaiting.unresolved, 0);

  const confirmed = derive(makePanel(10, true), {}, null, []);
  assert.equal(confirmed.confirmed, true);
  assert.equal(confirmed.awaitingConfirmation, false);
  assert.equal(confirmed.unresolved, 0);

  const mixedPending = [{ operatorId: 'op_1', parameterNames: ['Threshold', 'ModelPath'] }];
  const mixedPanel = {
    _resolvePendingParametersForDraft: () => mixedPending,
    _getPendingOperatorSourceOperators: () => [],
    _collectPendingDraftGroups: items => items.map(item => ({
      operatorId: item.operatorId,
      fields: item.parameterNames.map(parameterName => ({ parameterName, dataType: 'text', confirmedValue: null }))
    })),
    _getPendingParameterConfirmationState: () => ({ isConfirmed: false }),
    _hasPendingDraftValue: () => false
  };
  const mixed = derive(mixedPanel, {}, null, [{
    operatorId: 'op_1',
    parameterName: 'ModelPath',
    resourceKey: 'op_1.ModelPath'
  }]);
  assert.equal(mixed.total, 1);
  assert.equal(mixed.remainingToFill, 1);
  assert.equal(mixed.resourceBackedCount, 1);
  assert.deepEqual(mixed.groups[0].fields.map(field => field.parameterName), ['Threshold']);

  const resourceOnlyPanel = {
    ...mixedPanel,
    _resolvePendingParametersForDraft: () => [{ operatorId: 'op_1', parameterNames: ['ModelPath'] }]
  };
  const resourceOnly = derive(resourceOnlyPanel, {}, null, [{
    operatorId: 'op_1',
    parameterName: 'ModelPath',
    resourceKey: 'op_1.ModelPath'
  }]);
  assert.equal(resourceOnly.total, 0);
  assert.equal(resourceOnly.remainingToFill, 0);
  assert.equal(resourceOnly.resourceBackedCount, 1);
});

test('pure pending parameter partition assigns overlapping fields only to resources', () => {
  const partition = partitionPendingParameters([{
    operatorId: 'op_threshold',
    parameterNames: ['Threshold', 'ModelPath']
  }], [{
    operatorId: 'op_threshold',
    parameterName: 'ModelPath',
    resourceKey: 'op_threshold.ModelPath'
  }]);

  assert.deepEqual(partition.ordinaryPendingParameters[0].parameterNames, ['Threshold']);
  assert.deepEqual(partition.resourceBackedPendingParameters[0].parameterNames, ['ModelPath']);
  assert.equal(partition.resourceBackedFieldCount, 1);
  assert.equal(partition.resources.length, 1);
});

test('a new running Build Run does not project the previous Apply-ready result as current', () => {
  const previous = readyResult();
  const presentation = deriveAiBuildPresentation(createPanel(previous, {
    activeAgentRunId: 'run-new',
    activeAgentRunEvents: [{
      runId: 'run-new',
      sequence: 1,
      eventType: 'run.started',
      stage: 'run',
      status: 'running'
    }],
    workbenchState: 'generating',
    _isCurrentResultAppliedToCanvas: () => true
  }));

  assert.equal(presentation.overall.key, 'building');
  assert.equal(presentation.nodeCount, 0);
  assert.equal(presentation.gate.canvasReady, false);
  assert.equal(presentation.applied, false);
});
