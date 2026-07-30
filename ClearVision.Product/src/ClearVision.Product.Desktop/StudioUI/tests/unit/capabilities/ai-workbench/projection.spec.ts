import { describe, expect, it } from 'vitest';
import { aiWorkbenchActionModel } from '@/capabilities/ai-workbench/actionModel';
import {
  decodeAiAgentRunEventV1,
  decodeAiBuildResultV1,
  decodeAiIntentResultV1,
  decodeAiSessionDetailV1,
  decodeAiSessionSnapshotV1
} from '@/capabilities/ai-workbench/decoder';
import { projectAiWorkbench } from '@/capabilities/ai-workbench/projection';
import {
  initialAiWorkbenchState,
  reduceAiWorkbench,
  type AiWorkbenchState
} from '@/capabilities/ai-workbench/reducer';
import {
  aiBuildOperationId,
  aiBuildRunId,
  buildResultFixture,
  buildTerminalEventFixture,
  intentFixture,
  planFixture,
  readinessFixture,
  runEventFixture,
  sessionFixture,
  snapshotFixture,
  validationFixture
} from './aiFixtures';

function readyState() {
  return reduceAiWorkbench(initialAiWorkbenchState, {
    type: 'session-ready',
    session: decodeAiSessionDetailV1(sessionFixture()),
    project: null,
    at: 1
  });
}

describe('AI Workbench reducer, projection and action model', () => {
  it('projects all required product phases with one primary action at most', () => {
    let state = reduceAiWorkbench(initialAiWorkbenchState, { type: 'session-start', mode: 'create', at: 1 });
    expect(projectAiWorkbench(state).statusLabel).toBe('正在建立会话');
    state = readyState();
    expect(aiWorkbenchActionModel(state).primary?.id).toBe('submitTask');
    state = reduceAiWorkbench(state, { type: 'intent-start', description: '检测冲压件表面缺陷', requirementMode: 'strict', at: 2 });
    expect(projectAiWorkbench(state).currentStage).toBe('任务理解');
    state = reduceAiWorkbench(state, { type: 'intent-ready', intent: decodeAiIntentResultV1(intentFixture()), at: 3 });
    state = reduceAiWorkbench(state, { type: 'plan-start', clientOperationId: '33333333-3333-4333-8333-333333333333', generation: 1, at: 4 });
    state = reduceAiWorkbench(state, { type: 'plan-attached', runId: 'run_plan_01', operation: null, at: 5 });
    state = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(1)), generation: 1, at: 6 });
    state = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(2, 'plan.completed')), generation: 1, at: 7 });
    expect(state.phase).toBe('clarifying');
    expect(aiWorkbenchActionModel(state).primary?.id).toBe('answerClarification');
  });

  it('deduplicates canonical questions and shows at most three', () => {
    const plan = planFixture({
      clarificationQuestions: [
        ...planFixture().clarificationQuestions,
        { ...planFixture().clarificationQuestions[0], id: 'duplicate' },
        { ...planFixture().clarificationQuestions[0], id: 'q_image', field: 'image_source' },
        { ...planFixture().clarificationQuestions[0], id: 'q_output', field: 'output_target' },
        { ...planFixture().clarificationQuestions[0], id: 'q_object', field: 'inspection_object' }
      ]
    });
    const completed = runEventFixture(2, 'plan.completed');
    const payload = completed.payload as Record<string, unknown>;
    let state = readyState();
    state = reduceAiWorkbench(state, { type: 'plan-start', clientOperationId: '33333333-3333-4333-8333-333333333333', generation: 1, at: 2 });
    state = reduceAiWorkbench(state, { type: 'plan-attached', runId: 'run_plan_01', operation: null, at: 3 });
    state = reduceAiWorkbench(state, {
      type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(1)), generation: 1, at: 3
    });
    state = reduceAiWorkbench(state, {
      type: 'run-event',
      event: decodeAiAgentRunEventV1({
        ...completed,
        payload: { ...payload, planResult: plan, planModeResult: plan, questionCount: 5 }
      }),
      generation: 1,
      at: 4
    });
    expect(projectAiWorkbench(state).clarificationQuestions).toHaveLength(3);
    expect(new Set(projectAiWorkbench(state).clarificationQuestions.map(item => item.field)).size).toBe(3);
  });

  it('ignores duplicate, stale and post-terminal events and detects a sequence gap', () => {
    let state = readyState();
    state = reduceAiWorkbench(state, { type: 'plan-start', clientOperationId: '33333333-3333-4333-8333-333333333333', generation: 2, at: 2 });
    state = reduceAiWorkbench(state, { type: 'plan-attached', runId: 'run_plan_01', operation: null, at: 3 });
    state = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(1)), generation: 2, at: 4 });
    const duplicate = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(1)), generation: 2, at: 5 });
    expect(duplicate).toBe(state);
    const stale = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(2)), generation: 1, at: 5 });
    expect(stale).toBe(state);
    const gap = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(3)), generation: 2, at: 6 });
    expect(gap.phase).toBe('recovering');
    expect(gap.run.replayRequired).toBe(true);

    state = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(2, 'plan.completed')), generation: 2, at: 7 });
    state = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(3, 'run.completed')), generation: 2, at: 8 });
    const late = reduceAiWorkbench(state, { type: 'run-event', event: decodeAiAgentRunEventV1(runEventFixture(4, 'plan.model.started')), generation: 2, at: 9 });
    expect(late).toBe(state);
  });

  it('exposes exactly one Build action when readiness is ready', () => {
    const state = {
      ...readyState(),
      phase: 'plan-ready' as const,
      plan: planFixture({ canBuild: true, buildReadiness: readinessFixture(true) }) as never
    };
    const model = aiWorkbenchActionModel(state);
    expect(model.primary?.id).toBe('startBuild');
    expect(model.nextStagePlaceholder).toBeNull();
    expect([model.primary, ...model.secondary].filter(action => action?.id === 'startBuild')).toHaveLength(1);
  });

  it('projects G3 Build progress, invalidation and read-only ApplyGate states', () => {
    const plan = planFixture({ canBuild: true, buildReadiness: readinessFixture(true) });
    let state: AiWorkbenchState = {
      ...readyState(), phase: 'plan-ready', plan: plan as never,
      projectBaseline: buildResultFixture().projectBaseline
    };
    state = reduceAiWorkbench(state, {
      type: 'build-start', clientOperationId: aiBuildOperationId, generation: 4, at: 2
    });
    expect(state.phase).toBe('build-starting');
    state = reduceAiWorkbench(state, { type: 'build-attached', runId: aiBuildRunId, operation: null, at: 3 });
    expect(state.phase).toBe('building');
    state = reduceAiWorkbench(state, {
      type: 'run-event', generation: 4, at: 4,
      event: decodeAiAgentRunEventV1({
        ...buildTerminalEventFixture(1), eventType: 'build.validation.started', stage: 'validation',
        status: 'running', payload: { sessionId: 'session_01', planId: plan.planId, planHash: plan.planHash }
      })
    });
    expect(state.phase).toBe('validating');
    state = reduceAiWorkbench(state, {
      type: 'run-event', generation: 4, at: 5,
      event: decodeAiAgentRunEventV1(buildTerminalEventFixture(2))
    });
    expect(state.phase).toBe('parameters-pending');
    expect(aiWorkbenchActionModel(state).primary?.id).toBe('confirmParameters');

    const changedSnapshot = decodeAiSessionSnapshotV1(snapshotFixture({
      revision: 4, answerRevision: 3, buildParameterValues: { 'threshold_1.threshold': 128 },
      buildResult: buildResultFixture(), submittedBuildFingerprint: 'd'.repeat(64)
    }));
    state = reduceAiWorkbench(state, {
      type: 'inputs-updated', snapshot: changedSnapshot, message: 'Inputs changed.', at: 6
    });
    expect(state.phase).toBe('build-blocked');
    expect(state.buildStale).toBe(true);
    expect(aiWorkbenchActionModel(state).primary?.id).toBe('recheckReadiness');

    const parameter = buildResultFixture().parameterMapping[0];
    const readyBuild = decodeAiBuildResultV1(buildResultFixture({
      parameterMapping: [{
        ...parameter, value: 128, hasExplicitValue: true, valueSummary: '128', pending: false
      }],
      validation: validationFixture(true)
    }));
    const readySnapshot = decodeAiSessionSnapshotV1(snapshotFixture({
      revision: 5, answerRevision: 3, buildParameterValues: { 'threshold_1.threshold': 128 },
      buildResult: readyBuild, submittedBuildFingerprint: 'd'.repeat(64)
    }));
    state = reduceAiWorkbench(state, { type: 'revalidation-start', at: 7 });
    expect(state.phase).toBe('revalidating');
    state = reduceAiWorkbench(state, {
      type: 'revalidation-ready', build: readyBuild, snapshot: readySnapshot, at: 8
    });
    expect(state.phase).toBe('build-ready');
    expect(state.buildStale).toBe(false);
    expect(aiWorkbenchActionModel(state).primary?.id).toBe('prepareHandoff');
    expect(aiWorkbenchActionModel(state).nextStagePlaceholder).toBeNull();
  });

  it('keeps the first terminal outcome and retains the previous candidate read-only', () => {
    const previousBuild = decodeAiBuildResultV1(buildResultFixture({ validation: validationFixture(true) }));
    let state: AiWorkbenchState = {
      ...readyState(), phase: 'build-ready', plan: planFixture() as never, build: previousBuild
    };
    state = reduceAiWorkbench(state, {
      type: 'build-start', clientOperationId: aiBuildOperationId, generation: 5, at: 2
    });
    state = reduceAiWorkbench(state, { type: 'build-attached', runId: aiBuildRunId, operation: null, at: 3 });
    state = reduceAiWorkbench(state, { type: 'cancel-start', at: 4 });
    const failed = decodeAiAgentRunEventV1({
      ...buildTerminalEventFixture(1), eventType: 'run.failed', status: 'failed',
      payload: { sessionId: 'session_01', publicMessage: 'Build failed.' }
    });
    state = reduceAiWorkbench(state, { type: 'run-event', event: failed, generation: 5, at: 5 });
    expect(state.phase).toBe('build-failed');
    expect(state.build).toBe(previousBuild);
    expect(state.buildStale).toBe(true);

    const lateCancel = decodeAiAgentRunEventV1({
      ...buildTerminalEventFixture(2), eventType: 'run.cancelled', status: 'cancelled',
      payload: { sessionId: 'session_01' }
    });
    const afterLateCancel = reduceAiWorkbench(state, {
      type: 'run-event', event: lateCancel, generation: 5, at: 6
    });
    expect(afterLateCancel).toBe(state);
  });

  it('defines one action-model owner for every Build and handoff phase without Canvas or save actions', () => {
    const expectedPrimary = new Map([
      ['build-starting', 'cancelBuild'], ['building', 'cancelBuild'], ['validating', 'cancelBuild'],
      ['parameters-pending', 'confirmParameters'], ['resources-pending', 'updateResourceDecision'],
      ['build-blocked', 'recheckReadiness'], ['build-failed', 'rebuild'],
      ['build-cancelled', 'rebuild'], ['baseline-conflict', 'reconcile'], ['unknown-outcome', 'reconcile'],
      ['build-ready', 'prepareHandoff'], ['handoff-unknown-outcome', 'reconcileHandoff']
    ]);
    for (const [phase, actionId] of expectedPrimary) {
      const model = aiWorkbenchActionModel({ ...readyState(), phase: phase as never });
      expect(model.primary?.id, phase).toBe(actionId);
      expect(model.secondary.filter(action => action.primary), phase).toHaveLength(0);
    }
    for (const phase of ['revalidating', 'build-cancelling', 'handoff-creating', 'handoff-created'] as const) {
      const model = aiWorkbenchActionModel({ ...readyState(), phase });
      expect(model.primary, phase).toBeNull();
    }
    const actionIds = [...expectedPrimary.values(), 'recheckReadiness'];
    expect(actionIds).toContain('prepareHandoff');
    expect(actionIds).not.toContain('applyToCanvas');
    expect(actionIds).not.toContain('saveProject');
  });
});
