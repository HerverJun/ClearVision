import { describe, expect, it } from 'vitest';
import { aiWorkbenchActionModel } from '@/capabilities/ai-workbench/actionModel';
import { decodeAiAgentRunEventV1, decodeAiIntentResultV1, decodeAiSessionDetailV1 } from '@/capabilities/ai-workbench/decoder';
import { projectAiWorkbench } from '@/capabilities/ai-workbench/projection';
import { initialAiWorkbenchState, reduceAiWorkbench } from '@/capabilities/ai-workbench/reducer';
import { intentFixture, planFixture, runEventFixture, sessionFixture } from './aiFixtures';

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

  it('never exposes a Build action when readiness is ready', () => {
    const state = { ...readyState(), phase: 'plan-ready' as const, plan: planFixture() as never };
    const model = aiWorkbenchActionModel(state);
    expect(model.primary).toBeNull();
    expect(model.nextStagePlaceholder?.label).toBe('进入下一阶段');
    expect([model.primary, ...model.secondary].filter(Boolean).map(action => action?.id))
      .not.toContain('startBuild');
  });
});
