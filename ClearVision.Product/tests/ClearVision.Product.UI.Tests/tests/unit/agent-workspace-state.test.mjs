import test from 'node:test';
import assert from 'node:assert/strict';
import {
  AgentWorkspaceEventTypes,
  agentWorkspaceReducer,
  createAgentWorkspaceState,
  isPlaceholderAnswer,
  normalizeWorkspaceApplyGate,
  normalizeWorkspaceAnswer
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/agentWorkspaceState.js';
import { mapBuildSubphaseToAiWorkbenchState } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelWorkbench.js';

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

test('workspace projection deduplicates questions, blockers, and resources into one three-item batch', () => {
  const state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: plan() }, { sessionId: 's1' });

  assert.equal(state.projection.clarificationQueue.filter(item => item.field === 'inspection_object').length, 1);
  assert.equal(state.projection.clarificationQueue.filter(item => item.field === 'image_source').length, 1);
  assert.equal(state.projection.missingResources.length, 1);
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
  state = reduce(state, AgentWorkspaceEventTypes.ANSWER_OPTIMISTIC_SET, {
    answer: { questionId: 'q-object', field: 'inspection_object', value: '用户自定义对象', origin: 'explicit_user_text' }
  }, { planId: 'plan-1', planHash: 'hash-1' });

  assert.equal(state.projection.answersByField.inspection_object.value, '用户自定义对象');
  assert.equal(state.projection.answersByField.inspection_object.origin, 'explicit_user_text');
  assert.equal(state.projection.clarificationQueue.find(item => item.field === 'inspection_object').answered, true);
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
  assert.deepEqual(state.clarification.batchKeys, ['acceptance_criteria', 'model']);
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

test('Build result MissingResources enter the same clarification projection', () => {
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
  assert.equal(state.projection.clarificationQueue.some(item => item.resourceKey === 'camera:line-1'), true);
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
    ui: { workspaceMode: 'build', viewMode: 'build' }
  });

  assert.equal(restored.identity.sessionId, 'restored');
  assert.equal(restored.plan.planId, 'plan-1');
  assert.equal(restored.result.status, 'completed');
  assert.equal(restored.projection.answersByField.inspection_object.value, '零件');
  assert.equal(restored.run.build.runId, 'build-run');
  assert.equal(restored.persistence.snapshotRevision, 9);
  assert.equal(restored.ui.viewMode, 'build');
});

test('display projection table emits business phase, build subphase, active view, and one primary action', () => {
  const readyPlan = plan({
    clarificationQuestions: [],
    missingResources: [],
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: '可以构建。',
      contractVersion: 'v2'
    }
  });
  const cases = [
    {
      name: 'idle',
      state: createAgentWorkspaceState({ sessionId: 's1' }),
      expected: ['idle', 'not_started', 'plan', null]
    },
    {
      name: 'clarifying',
      state: reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: plan() }, { sessionId: 's1' }),
      expected: ['plan', 'not_started', 'plan', 'answer_clarifications']
    },
    {
      name: 'ready to build',
      state: reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: readyPlan }, { sessionId: 's1' }),
      expected: ['plan', 'not_started', 'plan', 'start_build']
    },
    {
      name: 'building',
      state: reduce(
        reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, { plan: readyPlan }, { sessionId: 's1' }),
        AgentWorkspaceEventTypes.RUN_STARTED,
        { kind: 'build', runId: 'run-1' },
        { sessionId: 's1' }
      ),
      expected: ['build', 'generating', 'build_evidence', null]
    }
  ];

  for (const item of cases) {
    const projection = item.state.projection;
    assert.deepEqual(
      [projection.businessPhase, projection.buildSubphase, projection.activeView, projection.actionModel.primary?.id || null],
      item.expected,
      item.name
    );
    assert.ok([0, 1].includes(projection.actionModel.primary ? 1 : 0), item.name);
  }
});

test('ApplyGate combinations remain distinct and only canvas readiness controls the apply primary action', () => {
  const gateCases = [
    {
      gate: { canvasApplyReady: true, runtimeDraftReady: true, deploymentReady: false, blocked: false },
      expectedPrimary: 'apply_canvas',
      expectedStatus: /部署仍受门禁约束/
    },
    {
      gate: { canvasApplyReady: false, runtimeDraftReady: false, deploymentReady: false, blocked: true },
      expectedPrimary: null,
      expectedStatus: /未通过画布应用门禁/
    },
    {
      gate: { canvasApplyReady: true, runtimeDraftReady: true, deploymentReady: true, blocked: false },
      expectedPrimary: 'apply_canvas',
      expectedStatus: /均已就绪/
    }
  ];

  for (const [index, item] of gateCases.entries()) {
    let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.RESULT_RECEIVED, {
      result: {
        flow: { operators: [{ id: `op-${index}` }], connections: [] },
        buildResult: { applyGate: item.gate }
      }
    }, { sessionId: 's1' });
    assert.equal(state.projection.gates.canvasApplyReady, item.gate.canvasApplyReady);
    assert.equal(state.projection.gates.runtimeDraftReady, item.gate.runtimeDraftReady);
    assert.equal(state.projection.gates.deploymentReady, item.gate.deploymentReady);
    assert.equal(state.projection.actionModel.primary?.id || null, item.expectedPrimary);
    assert.match(state.projection.actionModel.statusMessage, item.expectedStatus);
  }
});

test('legacy PascalCase payload compatibility is centralized in ApplyGate normalization', () => {
  const normalized = normalizeWorkspaceApplyGate({
    BuildResult: {
      Flow: { Operators: [{ Id: 'op-1' }] },
      ApplyGate: {
        CanvasApplyReady: true,
        RuntimeDraftReady: false,
        DeploymentReady: false,
        Blocked: false,
        Status: 'canvas_apply_ready'
      }
    }
  });
  assert.deepEqual(
    [normalized.canvasApplyReady, normalized.runtimeDraftReady, normalized.deploymentReady, normalized.status],
    [true, false, false, 'canvas_apply_ready']
  );
});

test('view switching changes display view only and preserves state and admission gates', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.PLAN_RECEIVED, {
    plan: plan({ buildReadiness: { canBuild: true, blockers: [], resolvedFields: [], remainingFields: [], contractVersion: 'v2' } })
  }, { sessionId: 's1' });
  const before = {
    plan: state.plan,
    readiness: state.readiness,
    run: state.run,
    apply: state.apply,
    action: state.projection.actionModel
  };
  state = reduce(state, AgentWorkspaceEventTypes.VIEW_CHANGED, { mode: 'build' });
  assert.equal(state.projection.activeView, 'build_evidence');
  assert.equal(state.plan, before.plan);
  assert.equal(state.readiness, before.readiness);
  assert.equal(state.run, before.run);
  assert.equal(state.apply, before.apply);
  assert.deepEqual(state.projection.actionModel, before.action);
});

test('run reducer rejects out-of-order events and every event after terminal completion', () => {
  let state = reduce(createAgentWorkspaceState({ sessionId: 's1' }), AgentWorkspaceEventTypes.RUN_STARTED, { kind: 'build', runId: 'run-1' });
  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, {
    kind: 'build', event: { runId: 'run-1', sequence: 3, eventType: 'stage.completed', stage: 'validator' }
  });
  const afterSequenceThree = state;
  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, {
    kind: 'build', event: { runId: 'run-1', sequence: 2, eventType: 'stage.completed', stage: 'parse' }
  });
  assert.equal(state, afterSequenceThree);
  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, {
    kind: 'build', event: { runId: 'run-1', sequence: 4, eventType: 'run.completed' }
  });
  const terminal = state;
  state = reduce(state, AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED, {
    kind: 'build', event: { runId: 'run-1', sequence: 5, eventType: 'run.failed' }
  });
  assert.equal(state, terminal);
});

test('AiWorkbenchStates is a display-only mapping of buildSubphase', () => {
  assert.equal(mapBuildSubphaseToAiWorkbenchState('generating'), 'generating');
  assert.equal(mapBuildSubphaseToAiWorkbenchState('ready_to_apply'), 'ready_to_apply');
  assert.equal(mapBuildSubphaseToAiWorkbenchState('unknown'), 'idle');
});
