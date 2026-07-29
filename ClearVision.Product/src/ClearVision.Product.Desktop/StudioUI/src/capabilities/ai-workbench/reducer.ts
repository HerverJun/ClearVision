import type {
  AiAgentRunEventV1,
  AiIntentResultV1,
  AiOperationProjectionV1,
  AiPlanV1,
  AiProjectContextV1,
  AiReadinessPreviewV1,
  AiRequirementMode,
  AiRunStatus,
  AiSessionDetailV1,
  AiSessionSnapshotV1
} from './contracts';

export type AiWorkbenchPhase =
  | 'idle'
  | 'session-loading'
  | 'intent-routing'
  | 'planning'
  | 'clarifying'
  | 'plan-blocked'
  | 'plan-ready'
  | 'cancelling'
  | 'cancelled'
  | 'recovering'
  | 'session-conflict'
  | 'plan-failed'
  | 'offline-or-service-unavailable'
  | 'disposed';

export interface AiPlanRunState {
  readonly clientOperationId: string | null;
  readonly runId: string | null;
  readonly status: AiRunStatus | null;
  readonly generation: number;
  readonly lastSequence: number;
  readonly terminalSequence: number | null;
  readonly replayRequired: boolean;
  readonly events: readonly AiAgentRunEventV1[];
}

export interface AiWorkbenchState {
  readonly phase: AiWorkbenchPhase;
  readonly session: AiSessionDetailV1 | null;
  readonly project: AiProjectContextV1 | null;
  readonly taskDescription: string;
  readonly requirementMode: AiRequirementMode;
  readonly intent: AiIntentResultV1 | null;
  readonly plan: AiPlanV1 | null;
  readonly readiness: AiReadinessPreviewV1 | null;
  readonly operation: AiOperationProjectionV1 | null;
  readonly run: AiPlanRunState;
  readonly errorCode: string | null;
  readonly message: string;
  readonly updatedAt: number;
}

export type AiWorkbenchEvent =
  | Readonly<{ type: 'session-start'; mode: 'create' | 'hydrate'; at: number }>
  | Readonly<{ type: 'session-ready'; session: AiSessionDetailV1; project: AiProjectContextV1 | null; operation?: AiOperationProjectionV1 | null; at: number }>
  | Readonly<{ type: 'intent-start'; description: string; requirementMode: AiRequirementMode; at: number }>
  | Readonly<{ type: 'intent-ready'; intent: AiIntentResultV1; at: number }>
  | Readonly<{ type: 'plan-start'; clientOperationId: string; generation: number; at: number }>
  | Readonly<{ type: 'plan-attached'; runId: string; operation: AiOperationProjectionV1 | null; snapshot?: AiSessionSnapshotV1 | null; at: number }>
  | Readonly<{ type: 'run-event'; event: AiAgentRunEventV1; generation: number; at: number }>
  | Readonly<{ type: 'recovery-start'; reason: string; at: number }>
  | Readonly<{ type: 'snapshot-ready'; snapshot: AiSessionSnapshotV1; at: number }>
  | Readonly<{ type: 'readiness-start'; at: number }>
  | Readonly<{ type: 'readiness-ready'; readiness: AiReadinessPreviewV1; snapshot: AiSessionSnapshotV1; at: number }>
  | Readonly<{ type: 'cancel-start'; at: number }>
  | Readonly<{ type: 'failed'; phase: Extract<AiWorkbenchPhase, 'plan-blocked' | 'session-conflict' | 'plan-failed' | 'offline-or-service-unavailable'>; errorCode: string; message: string; at: number }>
  | Readonly<{ type: 'retry'; at: number }>
  | Readonly<{ type: 'new-task'; at: number }>
  | Readonly<{ type: 'dispose'; at: number }>;

const initialRunState: AiPlanRunState = Object.freeze({
  clientOperationId: null,
  runId: null,
  status: null,
  generation: 0,
  lastSequence: 0,
  terminalSequence: null,
  replayRequired: false,
  events: Object.freeze([])
});

export const initialAiWorkbenchState: AiWorkbenchState = Object.freeze({
  phase: 'idle',
  session: null,
  project: null,
  taskDescription: '',
  requirementMode: 'strict',
  intent: null,
  plan: null,
  readiness: null,
  operation: null,
  run: initialRunState,
  errorCode: null,
  message: '',
  updatedAt: 0
});

const terminalEvents = new Set(['run.completed', 'run.failed', 'run.cancelled']);

function planPhase(plan: AiPlanV1, readiness: AiReadinessPreviewV1 | null): AiWorkbenchPhase {
  const effective = readiness?.buildReadiness ?? plan.buildReadiness;
  if (effective.canBuild) return 'plan-ready';
  if (plan.clarificationQuestions.length > 0) return 'clarifying';
  return 'plan-blocked';
}

function withSnapshot(state: AiWorkbenchState, snapshot: AiSessionSnapshotV1): AiWorkbenchState {
  if (!state.session) return state;
  const session = Object.freeze({ ...state.session, snapshot, updatedAtUtc: snapshot.updatedAtUtc });
  return Object.freeze({
    ...state,
    session,
    readiness: snapshot.readinessPreview ?? state.readiness,
    requirementMode: snapshot.requirementMode,
    updatedAt: state.updatedAt
  });
}

function appendEvent(events: readonly AiAgentRunEventV1[], event: AiAgentRunEventV1): readonly AiAgentRunEventV1[] {
  const next = events.length >= 80 ? [...events.slice(events.length - 79), event] : [...events, event];
  return Object.freeze(next);
}

export function eventRequiresReplay(state: AiWorkbenchState, event: AiAgentRunEventV1, generation: number): boolean {
  return state.run.runId === event.runId &&
    state.run.generation === generation &&
    state.run.terminalSequence === null &&
    event.sequence > state.run.lastSequence + 1;
}

export function reduceAiWorkbench(state: AiWorkbenchState, event: AiWorkbenchEvent): AiWorkbenchState {
  if (state.phase === 'disposed') return state;
  switch (event.type) {
    case 'session-start':
      return Object.freeze({
        ...state,
        phase: event.mode === 'hydrate' ? 'recovering' : 'session-loading',
        errorCode: null,
        message: event.mode === 'hydrate' ? '正在恢复服务端会话与公开规划状态。' : '正在建立安全会话。',
        updatedAt: event.at
      });
    case 'session-ready':
      return Object.freeze({
        ...state,
        phase: 'idle',
        session: event.session,
        project: event.project,
        readiness: event.session.snapshot.readinessPreview,
        requirementMode: event.session.snapshot.requirementMode,
        operation: event.operation ?? state.operation,
        errorCode: null,
        message: event.project ? '工程上下文与会话已由服务端确认。' : '会话已就绪，当前尚未绑定工程。',
        updatedAt: event.at
      });
    case 'intent-start':
      return Object.freeze({
        ...state,
        phase: 'intent-routing',
        taskDescription: event.description,
        requirementMode: event.requirementMode,
        intent: null,
        plan: null,
        readiness: null,
        operation: null,
        run: initialRunState,
        errorCode: null,
        message: '正在识别检测对象、任务类型和关键条件。',
        updatedAt: event.at
      });
    case 'intent-ready':
      return Object.freeze({
        ...state,
        intent: event.intent,
        phase: event.intent.shouldOpenPlan ? 'planning' : 'plan-blocked',
        message: event.intent.publicReason || event.intent.assistantReply,
        updatedAt: event.at
      });
    case 'plan-start':
      return Object.freeze({
        ...state,
        phase: 'planning',
        operation: null,
        run: Object.freeze({
          ...initialRunState,
          clientOperationId: event.clientOperationId,
          generation: event.generation,
          status: 'pending'
        }),
        errorCode: null,
        message: '正在创建可恢复的规划任务。',
        updatedAt: event.at
      });
    case 'plan-attached': {
      let next: AiWorkbenchState = Object.freeze({
        ...state,
        phase: 'planning',
        operation: event.operation ?? state.operation,
        run: Object.freeze({ ...state.run, runId: event.runId, status: 'running', replayRequired: false }),
        message: '规划任务已建立，正在接收公开进度。',
        updatedAt: event.at
      });
      if (event.snapshot) next = withSnapshot(next, event.snapshot);
      return next;
    }
    case 'run-event': {
      if (state.run.runId !== event.event.runId || state.run.generation !== event.generation) return state;
      if (event.event.sequence <= state.run.lastSequence) return state;
      if (eventRequiresReplay(state, event.event, event.generation)) {
        return Object.freeze({
          ...state,
          phase: 'recovering',
          run: Object.freeze({ ...state.run, replayRequired: true }),
          message: '规划进度存在缺口，正在从服务端回放补齐。',
          updatedAt: event.at
        });
      }
      const isTerminal = terminalEvents.has(event.event.eventType);
      if (state.run.terminalSequence !== null && !isTerminal) return state;
      let next: AiWorkbenchState = Object.freeze({
        ...state,
        phase: 'planning',
        run: Object.freeze({
          ...state.run,
          status: event.event.status,
          lastSequence: event.event.sequence,
          terminalSequence: isTerminal ? event.event.sequence : state.run.terminalSequence,
          replayRequired: false,
          events: appendEvent(state.run.events, event.event)
        }),
        message: event.event.summary || event.event.title,
        updatedAt: event.at
      });
      if (event.event.workspaceSnapshot) next = withSnapshot(next, event.event.workspaceSnapshot);
      if (event.event.eventType === 'plan.completed' && event.event.plan) {
        const plan = event.event.plan;
        next = Object.freeze({
          ...next,
          plan,
          taskDescription: next.taskDescription || plan.originalUserPrompt,
          readiness: next.session?.snapshot.readinessPreview ?? null,
          phase: planPhase(plan, next.session?.snapshot.readinessPreview ?? null),
          message: plan.buildReadiness.primaryMessage || plan.nextAction || event.event.summary,
          updatedAt: event.at
        });
      } else if (event.event.eventType === 'run.failed') {
        next = Object.freeze({
          ...next,
          phase: 'plan-failed',
          errorCode: 'plan_run_failed',
          message: event.event.publicMessage || event.event.summary || '规划失败，请查看公开诊断后重试。'
        });
      } else if (event.event.eventType === 'run.cancelled') {
        next = Object.freeze({ ...next, phase: 'cancelled', message: '本次规划已取消，可修改任务后重新开始。' });
      } else if (event.event.eventType === 'run.completed' && next.plan) {
        next = Object.freeze({ ...next, phase: planPhase(next.plan, next.readiness) });
      }
      return next;
    }
    case 'recovery-start':
      return Object.freeze({
        ...state,
        phase: 'recovering',
        run: Object.freeze({ ...state.run, replayRequired: true }),
        message: event.reason,
        updatedAt: event.at
      });
    case 'snapshot-ready': {
      const next = withSnapshot(Object.freeze({ ...state, updatedAt: event.at }), event.snapshot);
      return Object.freeze({
        ...next,
        phase: next.plan ? planPhase(next.plan, event.snapshot.readinessPreview) : next.phase,
        message: '会话状态已与服务端最新版本协调。',
        errorCode: null,
        updatedAt: event.at
      });
    }
    case 'readiness-start':
      return Object.freeze({
        ...state,
        phase: 'clarifying',
        errorCode: null,
        message: '正在由服务端重新计算方案就绪条件。',
        updatedAt: event.at
      });
    case 'readiness-ready': {
      const next = withSnapshot(Object.freeze({ ...state, readiness: event.readiness, updatedAt: event.at }), event.snapshot);
      return Object.freeze({
        ...next,
        readiness: event.readiness,
        phase: event.readiness.buildReadiness.canBuild ? 'plan-ready' : (next.plan ? planPhase(next.plan, event.readiness) : 'plan-blocked'),
        errorCode: event.readiness.contractValid ? null : event.readiness.failureCode,
        message: event.readiness.failureMessage || event.readiness.buildReadiness.primaryMessage,
        updatedAt: event.at
      });
    }
    case 'cancel-start':
      return Object.freeze({ ...state, phase: 'cancelling', message: '正在取消规划并等待服务端确认终态。', updatedAt: event.at });
    case 'failed':
      return Object.freeze({
        ...state,
        phase: event.phase,
        errorCode: event.errorCode,
        message: event.message,
        updatedAt: event.at
      });
    case 'retry':
      return Object.freeze({ ...state, phase: state.session ? 'idle' : 'session-loading', errorCode: null, message: '', updatedAt: event.at });
    case 'new-task':
      return Object.freeze({
        ...initialAiWorkbenchState,
        session: state.session,
        project: state.project,
        requirementMode: state.requirementMode,
        phase: state.session ? 'idle' : 'session-loading',
        updatedAt: event.at
      });
    case 'dispose':
      return Object.freeze({ ...state, phase: 'disposed', message: '', updatedAt: event.at });
  }
}
