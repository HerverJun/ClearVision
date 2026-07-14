import test from 'node:test';
import assert from 'node:assert/strict';
import {
  AgentWorkspaceEventTypes,
  agentWorkspaceReducer,
  createAgentWorkspaceState,
  isPlaceholderAnswer,
  normalizeWorkspaceAnswer
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/agentWorkspaceState.js';

function plan(overrides = {}) {
  return {
    planId: 'plan-1',
    planHash: 'hash-1',
    currentPhase: 'clarification_only',
    confirmedPlanAnswers: [],
    clarificationQuestions: [
      question('q-object', 'inspection_object'),
      question('q-task', 'task_type'),
      question('q-image', 'image_source'),
      question('q-acceptance', 'acceptance_criteria')
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        blocker('hard_requirement:inspection_object', 'inspection_object', 'q-object'),
        blocker('hard_requirement:image_source', 'image_source', 'q-image')
      ],
      resolvedFields: [],
      remainingFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      primaryMessage: '需要澄清。',
      contractVersion: 'v2'
    },
    missingResources: [
      { resourceKey: 'model:detector', resourceType: 'model', parameterName: 'ModelPath', description: '模型待绑定' }
    ],
    ...overrides
  };
}

function question(id, field) {
  return {
    id,
    field,
    title: field,
    options: [
      { value: `${field}_a`, label: 'A', recommended: true, answerEffect: 'resolve_field' },
      { value: `${field}_b`, label: 'B', recommended: false, answerEffect: 'resolve_field' }
    ]
  };
}

function blocker(id, field, questionId = '') {
  return {
    id,
    category: 'hard_requirement',
    field,
    questionId,
    blocksBuild: true,
    resolutionMode: 'answer_question',
    publicLabel: field
  };
}

function reduce(state, type, payload = {}, identity = {}) {
  return agentWorkspaceReducer(state, { type, payload, ...identity });
}

test('workspace projection keeps answerable questions separate from pending resources', () => {
  const state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: plan({
    buildReadiness: {
      ...plan().buildReadiness,
      blockers: [
        ...plan().buildReadiness.blockers,
        { id: 'resource_pending:model:detector', category: 'resource_pending', field: 'model', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '模型待绑定' }
      ]
    }
  }) }, { sessionId: 's1' });

  assert.equal(state.projection.clarificationQueue.filter(item => item.field === 'inspection_object').length, 1);
  assert.equal(state.projection.clarificationQueue.filter(item => item.field === 'image_source').length, 1);
  assert.equal(state.projection.missingResources.length, 1);
  assert.equal(state.projection.clarificationQueue.some(item => item.kind === 'resource'), false);
  assert.equal(state.projection.clarificationBatch.length, 3);
  assert.deepEqual(state.clarification.batchKeys, ['inspection_object', 'task_type', 'image_source']);
});

test('canonical answers layer confirmed server values under optimistic explicit user text', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, {
    plan: plan({
      confirmedPlanAnswers: [
        { questionId: 'q-object', field: 'inspection_object', value: 'server-object', origin: 'model_inferred' }
      ]
    })
  }, { sessionId: 's1' });
  assert.equal(state.projection.answersByField.inspection_object.resolved, false);
  assert.equal(state.projection.clarificationQueue.find(item => item.field === 'inspection_object').answered, false);
  state = reduce(state, AgentWorkspaceEventTypes.ANSWER_OPTIMISTIC_SET, {
    answer: { questionId: 'q-object', field: 'inspection_object', value: '用户自定义对象', origin: 'explicit_user_text' }
  }, { planId: 'plan-1', planHash: 'hash-1' });

  assert.equal(state.projection.answersByField.inspection_object.value, '用户自定义对象');
  assert.equal(state.projection.answersByField.inspection_object.origin, 'explicit_user_text');
  assert.equal(state.projection.clarificationQueue.find(item => item.field === 'inspection_object').answered, true);
  assert.equal(state.projection.answersByField.inspection_object.resolved, true);
});

test('authoritative readiness acknowledgement confirms answers without inventing a new answer revision', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, {
    plan: plan()
  }, { sessionId: 's1' });
  state = reduce(state, AgentWorkspaceEventTypes.ANSWER_OPTIMISTIC_SET, {
    answer: { questionId: 'q-task', field: 'task_type', value: 'surface_defect', origin: 'explicit_user_selection' }
  });
  const revision = state.answers.answerRevision;
  state = reduce(state, AgentWorkspaceEventTypes.ANSWERS_CONFIRMED, {
    answers: [{ questionId: 'q-task', field: 'task_type', value: 'surface_defect', origin: 'explicit_user_selection' }],
    preserveRevision: true
  });
  assert.equal(state.answers.answerRevision, revision);
  assert.equal(state.projection.confirmedAnswers[0].value, 'surface_defect');
  assert.equal(state.projection.optimisticAnswers.length, 0);
});

test('custom and other controls are never accepted as business answers', () => {
  assert.equal(isPlaceholderAnswer('other'), true);
  assert.equal(isPlaceholderAnswer('custom_input'), true);
  assert.equal(normalizeWorkspaceAnswer({ field: 'task_type', value: 'other', origin: 'explicit_user_selection' }), null);
  assert.deepEqual(
    normalizeWorkspaceAnswer({ field: 'task_type', value: '我自己描述的分拣任务', origin: 'explicit_user_text' }),
    {
      field: 'task_type',
      questionId: '',
      value: '我自己描述的分拣任务',
      origin: 'explicit_user_text',
      confidence: 1,
      resolved: true
    }
  );
  assert.equal(
    normalizeWorkspaceAnswer({ field: 'task_type', value: 'classification' }).resolved,
    false,
    'origin-less legacy values must not masquerade as explicit user confirmation'
  );
});

test('clarification batch advances only after the whole current batch is submitted', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: plan() }, { sessionId: 's1' });
  for (const [questionId, field] of [['q-object', 'inspection_object'], ['q-task', 'task_type'], ['q-image', 'image_source']]) {
    state = reduce(state, AgentWorkspaceEventTypes.ANSWER_OPTIMISTIC_SET, {
      answer: { questionId, field, value: `${field}_a`, origin: 'explicit_user_selection' }
    }, { planId: 'plan-1', planHash: 'hash-1' });
  }

  assert.deepEqual(state.clarification.batchKeys, ['inspection_object', 'task_type', 'image_source']);
  state = reduce(state, AgentWorkspaceEventTypes.CLARIFICATION_BATCH_SUBMITTED, {
    answers: state.projection.clarificationBatch.map(item => item.answer)
  }, { planId: 'plan-1', planHash: 'hash-1' });
  assert.deepEqual(state.clarification.batchKeys, ['acceptance_criteria']);
});

test('deferred resources remain resource_pending and never make readiness buildable', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: plan() }, { sessionId: 's1' });
  state = reduce(state, AgentWorkspaceEventTypes.RESOURCE_DECISION_SET, {
    resource: { resourceKey: 'model:detector', resourceType: 'model', parameterName: 'ModelPath' },
    decision: { status: 'deferred', source: 'user_deferred' }
  }, { planId: 'plan-1', planHash: 'hash-1' });

  const resource = state.projection.missingResources.find(item => item.resourceKey === 'model:detector');
  assert.equal(resource.deferred, true);
  assert.equal(resource.answered, false);
  assert.equal(state.projection.readiness.canBuild, false);
});

test('Build result MissingResources enter only the resource projection', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, {
    plan: plan({ missingResources: [] })
  }, { sessionId: 's1' });
  state = reduce(state, AgentWorkspaceEventTypes.RESULT_RECEIVED, {
    result: {
      buildResult: {
        missingResources: [{
          resourceKey: 'camera:line-1',
          resourceType: 'camera_binding',
          parameterName: 'CameraBindingId',
          description: '相机绑定待确认'
        }]
      }
    }
  }, { planId: 'plan-1', planHash: 'hash-1' });

  const resource = state.projection.missingResources.find(item => item.resourceKey === 'camera:line-1');
  assert.ok(resource);
  assert.equal(resource.kind, 'resource');
  assert.equal(state.projection.clarificationQueue.some(item => item.resourceKey === 'camera:line-1'), false);
});

test('same canonical resource from Plan Readiness Build Result and Workspace is projected once', () => {
  const canonicalId = 'resource:v1|camera_binding|imageacquisition#1|camera_binding_id';
  const requirement = {
    canonicalId,
    resourceType: 'camera_binding',
    resourceName: '相机绑定',
    resourceKey: 'imageacquisition#1.CameraBindingId',
    operatorKey: 'imageacquisition#1',
    operatorType: 'ImageAcquisition',
    operatorIndex: 0,
    parameterName: 'CameraBindingId',
    blockingScope: 'build',
    draftPolicy: 'draft_allowed',
    resolutionTarget: 'settings:cameras'
  };
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, {
    plan: plan({
      missingResources: [{ ...requirement, source: 'plan' }],
      buildReadiness: {
        canBuild: false,
        blockers: [{ id: 'resource_pending:camera', category: 'resource_pending', blocksBuild: true, resource: { ...requirement, source: 'readiness' } }],
        missingResources: [{ ...requirement, source: 'readiness' }],
        resolvedFields: [], remainingFields: [], primaryMessage: '相机绑定必须补齐。', contractVersion: 'v2'
      }
    })
  }, { sessionId: 's1' });
  state = reduce(state, AgentWorkspaceEventTypes.RESULT_RECEIVED, {
    result: { missingResources: [{ ...requirement, resourceKey: 'op_acq.CameraBindingId', operatorId: 'op_acq', source: 'build_result' }] }
  }, { planId: 'plan-1', planHash: 'hash-1' });
  state = reduce(state, AgentWorkspaceEventTypes.RESOURCE_DECISION_SET, {
    resource: requirement,
    decision: { status: 'deferred', source: 'user_deferred' }
  }, { planId: 'plan-1', planHash: 'hash-1' });

  assert.equal(state.projection.missingResources.length, 1);
  assert.equal(state.projection.missingResources[0].canonicalId, canonicalId);
  const sources = new Set(state.projection.missingResources[0].sources);
  ['plan', 'readiness', 'build_result', 'build_readiness', 'workspace'].forEach(source => assert.equal(sources.has(source), true));
  assert.equal(state.resources.revision, 1);
  assert.equal(Object.prototype.hasOwnProperty.call(state.resources.missingByKey[canonicalId], 'raw'), false);
  assert.ok(JSON.stringify(state.resources.missingByKey[canonicalId]).length < 2000);
});

test('same resource type on different operator identities or parameters never merges', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, {
    plan: plan({
      missingResources: [
        { canonicalId: 'resource:v1|model_resource|onnxinference#1|modelpath', resourceType: 'model_resource', operatorKey: 'onnxinference#1', parameterName: 'ModelPath' },
        { canonicalId: 'resource:v1|model_resource|onnxinference#2|modelpath', resourceType: 'model_resource', operatorKey: 'onnxinference#2', parameterName: 'ModelPath' },
        { canonicalId: 'resource:v1|model_resource|onnxinference#1|labelspath', resourceType: 'model_resource', operatorKey: 'onnxinference#1', parameterName: 'LabelsPath' }
      ]
    })
  }, { sessionId: 's1' });
  assert.equal(state.projection.missingResources.length, 3);
});

test('readiness status distinguishes blocked failed timeout and active validation', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: plan() }, { sessionId: 's1' });
  assert.equal(state.readinessStatus, 'blocked');
  state = reduce(state, AgentWorkspaceEventTypes.READINESS_REQUESTED, { requestId: 'r1', startedAt: 1 }, { planId: 'plan-1', planHash: 'hash-1' });
  assert.equal(state.readinessStatus, 'validating');
  assert.equal(state.readinessRequest.requestId, 'r1');
  state = reduce(state, AgentWorkspaceEventTypes.READINESS_FAILED, { message: 'timeout', status: 'timeout' }, { planId: 'plan-1', planHash: 'hash-1' });
  assert.equal(state.readinessStatus, 'timeout');
  assert.equal(state.readinessRequest, null);
  state = reduce(state, AgentWorkspaceEventTypes.READINESS_FAILED, { message: 'failed' }, { planId: 'plan-1', planHash: 'hash-1' });
  assert.equal(state.readinessStatus, 'failed');
});

test('run reducer drops duplicate events and ignores nonterminal events after a terminal event', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.RUN_STARTED, { kind: 'build', runId: 'run-1' }, { sessionId: 's1' });
  const started = { runId: 'run-1', sequence: 1, eventType: 'run.started' };
  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, { kind: 'build', event: started }, { sessionId: 's1', runId: 'run-1' });
  const afterStarted = state;
  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, { kind: 'build', event: started }, { sessionId: 's1', runId: 'run-1' });
  assert.equal(state, afterStarted);

  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, {
    kind: 'build', event: { runId: 'run-1', sequence: 5, eventType: 'run.completed' }
  }, { sessionId: 's1', runId: 'run-1' });
  const terminal = state;
  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, {
    kind: 'build', event: { runId: 'run-1', sequence: 4, eventType: 'stage.completed' }
  }, { sessionId: 's1', runId: 'run-1' });
  assert.equal(state, terminal);
  assert.equal(state.run.build.status, 'completed');
});

test('stale session and plan events cannot overwrite the active workspace', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: plan() }, { sessionId: 's1' });
  const current = state;
  state = reduce(state, AgentWorkspaceEventTypes.READINESS_RECEIVED, {
    buildReadiness: { canBuild: true, blockers: [], resolvedFields: [], remainingFields: [], contractVersion: 'v2' }
  }, { sessionId: 's2', planId: 'plan-1', planHash: 'hash-1' });
  assert.equal(state, current);
  state = reduce(state, AgentWorkspaceEventTypes.READINESS_RECEIVED, {
    buildReadiness: { canBuild: true, blockers: [], resolvedFields: [], remainingFields: [], contractVersion: 'v2' }
  }, { sessionId: 's1', planId: 'plan-stale', planHash: 'hash-1' });
  assert.equal(state, current);
});

test('session restoration atomically restores plan, result, answers, run, and persistence identity', () => {
  const restored = reduce(createAgentWorkspaceState({ sessionId: 'old' }), AgentWorkspaceEventTypes.SESSION_RESTORED, {
    sessionId: 'restored',
    revision: 9,
    planId: 'plan-1',
    planHash: 'hash-1',
    plan: plan(),
    result: { flow: { operators: [] }, status: 'completed' },
    confirmedAnswers: [{ questionId: 'q-object', field: 'inspection_object', value: '零件', origin: 'explicit_user_text' }],
    run: {
      plan: { runId: 'plan-run', status: 'completed', events: [], eventKeys: {}, terminalSequence: 7 },
      build: { runId: 'build-run', status: 'failed', events: [], eventKeys: {}, terminalSequence: 11 }
    },
    persistence: { snapshotRevision: 9, buildRunId: 'build-run' },
    missingResources: [{ canonicalId: 'resource:v1|camera_binding|imageacquisition#1|camera_binding_id', resourceType: 'camera_binding', operatorKey: 'imageacquisition#1', parameterName: 'CameraBindingId' }],
    resourceDecisions: { 'resource:v1|camera_binding|imageacquisition#1|camera_binding_id': { status: 'deferred' } },
    resourceRevision: 4,
    ui: { workspaceMode: 'build', viewMode: 'build' }
  });

  assert.equal(restored.identity.sessionId, 'restored');
  assert.equal(restored.plan.planId, 'plan-1');
  assert.equal(restored.result.status, 'completed');
  assert.equal(restored.projection.answersByField.inspection_object.value, '零件');
  assert.equal(restored.run.build.runId, 'build-run');
  assert.equal(restored.persistence.snapshotRevision, 9);
  assert.equal(restored.ui.viewMode, 'build');
  assert.equal(restored.resources.revision, 4);
  assert.equal(restored.projection.missingResources.find(item => item.resourceType === 'camera_binding').deferred, true);
});
