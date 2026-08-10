import type {
  AiAgentRunEventV1,
  AiAgentRunReplayDiagnosticsV1,
  AiBuildResultV1,
  AiCameraBindingOptionV1,
  AiHandoffArtifactIdentityV1,
  AiIntentResultV1,
  AiOperationProjectionV1,
  AiPlanV1,
  AiProjectContextV1,
  AiProjectBaselineV1,
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
  | 'build-starting'
  | 'building'
  | 'validating'
  | 'build-blocked'
  | 'parameters-pending'
  | 'resources-pending'
  | 'revalidating'
  | 'build-ready'
  | 'handoff-creating'
  | 'handoff-unknown-outcome'
  | 'handoff-created'
  | 'build-failed'
  | 'build-cancelling'
  | 'build-cancelled'
  | 'baseline-conflict'
  | 'unknown-outcome'
  | 'cancelling'
  | 'cancelled'
  | 'recovering'
  | 'session-conflict'
  | 'plan-failed'
  | 'offline-or-service-unavailable'
  | 'disposed';

export interface AiPlanRunState {
  readonly kind: 'plan' | 'build' | null;
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
  readonly projectBaseline: AiProjectBaselineV1 | null;
  readonly taskDescription: string;
  readonly requirementMode: AiRequirementMode;
  readonly intent: AiIntentResultV1 | null;
  readonly plan: AiPlanV1 | null;
  readonly readiness: AiReadinessPreviewV1 | null;
  readonly operation: AiOperationProjectionV1 | null;
  readonly build: AiBuildResultV1 | null;
  readonly handoff: AiHandoffArtifactIdentityV1 | null;
  readonly buildStale: boolean;
  readonly replayDiagnostics: AiAgentRunReplayDiagnosticsV1 | null;
  readonly cameraBindings: readonly AiCameraBindingOptionV1[];
  readonly run: AiPlanRunState;
  readonly errorCode: string | null;
  readonly message: string;
  readonly updatedAt: number;
}

export type AiWorkbenchEvent =
  | Readonly<{ type: 'session-start'; mode: 'create' | 'hydrate'; at: number }>
  | Readonly<{ type: 'session-ready'; session: AiSessionDetailV1; project: AiProjectContextV1 | null; operation?: AiOperationProjectionV1 | null; at: number }>
  | Readonly<{ type: 'baseline-ready'; baseline: AiProjectBaselineV1; at: number }>
  | Readonly<{ type: 'intent-start'; description: string; requirementMode: AiRequirementMode; at: number }>
  | Readonly<{ type: 'intent-ready'; intent: AiIntentResultV1; at: number }>
  | Readonly<{ type: 'plan-start'; clientOperationId: string; generation: number; at: number }>
  | Readonly<{ type: 'plan-attached'; runId: string; operation: AiOperationProjectionV1 | null; snapshot?: AiSessionSnapshotV1 | null; at: number }>
  | Readonly<{ type: 'build-start'; clientOperationId: string; generation: number; at: number }>
  | Readonly<{ type: 'build-attached'; runId: string; operation: AiOperationProjectionV1 | null; snapshot?: AiSessionSnapshotV1 | null; at: number }>
  | Readonly<{ type: 'build-unknown'; message: string; at: number }>
  | Readonly<{ type: 'inputs-updated'; snapshot: AiSessionSnapshotV1; message: string; at: number }>
  | Readonly<{ type: 'revalidation-start'; at: number }>
  | Readonly<{ type: 'revalidation-ready'; build: AiBuildResultV1; snapshot: AiSessionSnapshotV1; at: number }>
  | Readonly<{ type: 'handoff-start'; at: number }>
  | Readonly<{ type: 'handoff-created'; artifact: AiHandoffArtifactIdentityV1; at: number }>
  | Readonly<{ type: 'handoff-unknown'; message: string; at: number }>
  | Readonly<{ type: 'camera-bindings-ready'; bindings: readonly AiCameraBindingOptionV1[]; at: number }>
  | Readonly<{ type: 'run-event'; event: AiAgentRunEventV1; generation: number; at: number }>
  | Readonly<{ type: 'replay-observed'; diagnostics: AiAgentRunReplayDiagnosticsV1; generation: number; at: number }>
  | Readonly<{ type: 'recovery-start'; reason: string; at: number }>
  | Readonly<{ type: 'snapshot-ready'; snapshot: AiSessionSnapshotV1; at: number }>
  | Readonly<{ type: 'readiness-start'; at: number }>
  | Readonly<{ type: 'readiness-ready'; readiness: AiReadinessPreviewV1; snapshot: AiSessionSnapshotV1; at: number }>
  | Readonly<{ type: 'cancel-start'; at: number }>
  | Readonly<{ type: 'failed'; phase: Extract<AiWorkbenchPhase,
      'plan-blocked' | 'session-conflict' | 'plan-failed' | 'build-failed' | 'baseline-conflict' |
      'unknown-outcome' | 'offline-or-service-unavailable'>; errorCode: string; message: string; at: number }>
  | Readonly<{ type: 'retry'; at: number }>
  | Readonly<{ type: 'new-task'; at: number }>
  | Readonly<{ type: 'dispose'; at: number }>;

const initialRunState: AiPlanRunState = Object.freeze({
  kind: null,
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
  projectBaseline: null,
  taskDescription: '',
  requirementMode: 'strict',
  intent: null,
  plan: null,
  readiness: null,
  operation: null,
  build: null,
  handoff: null,
  buildStale: false,
  replayDiagnostics: null,
  cameraBindings: Object.freeze([]),
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

function buildPhase(build: AiBuildResultV1): AiWorkbenchPhase {
  if (build.validation.handoffEligible) return 'build-ready';
  if (build.parameterMapping.some(item => item.pending && !item.resourceDependent)) return 'parameters-pending';
  if (build.missingResources.length > 0) return 'resources-pending';
  return 'build-blocked';
}

function buildProgressPhase(event: AiAgentRunEventV1): AiWorkbenchPhase {
  const stage = event.stage.toLowerCase();
  if (stage.includes('validat') || stage.includes('dry_run') || stage.includes('dryrun') ||
      stage.includes('readiness') || stage.includes('contract') || stage.includes('release_review') ||
      stage.includes('apply_gate')) return 'validating';
  return 'building';
}

function runStatus(value: string | null): AiRunStatus | null {
  return value === 'pending' || value === 'running' || value === 'completed' || value === 'failed' ||
    value === 'cancelled' || value === 'blocked' || value === 'warning'
    ? value
    : null;
}

function sameProjectBaseline(
  left: AiProjectBaselineV1 | null,
  right: AiProjectBaselineV1 | null
): boolean {
  return left !== null && right !== null &&
    left.targetKind === right.targetKind &&
    left.projectId === right.projectId &&
    left.persistenceRevision === right.persistenceRevision &&
    left.canonicalFlowHash === right.canonicalFlowHash;
}

function buildBaselineConflicts(
  baseline: AiProjectBaselineV1 | null,
  build: AiBuildResultV1 | null
): boolean {
  return build !== null && baseline !== null && !sameProjectBaseline(baseline, build.projectBaseline);
}

function snapshotBuildStale(
  snapshot: AiSessionSnapshotV1,
  build: AiBuildResultV1 | null,
  baseline: AiProjectBaselineV1 | null = null
): boolean {
  return build !== null && (
    build.answerRevision !== snapshot.answerRevision ||
    build.resourceRevision !== snapshot.resourceRevision ||
    buildBaselineConflicts(baseline, build)
  );
}

function withSnapshot(state: AiWorkbenchState, snapshot: AiSessionSnapshotV1): AiWorkbenchState {
  if (!state.session || snapshot.revision <= state.session.snapshot.revision) return state;
  const build = snapshot.buildResult ?? state.build;
  const session = Object.freeze({ ...state.session, snapshot, updatedAtUtc: snapshot.updatedAtUtc });
  return Object.freeze({
    ...state,
    session,
    readiness: snapshot.readinessPreview ?? state.readiness,
    build,
    buildStale: snapshotBuildStale(snapshot, build, state.projectBaseline),
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
    case 'session-ready': {
      const build = event.session.snapshot.buildResult;
      const baseline = state.projectBaseline ?? event.session.snapshot.projectBaseline;
      const baselineConflict = buildBaselineConflicts(baseline, build);
      const buildStale = snapshotBuildStale(event.session.snapshot, build, baseline);
      return Object.freeze({
        ...state,
        phase: baselineConflict ? 'baseline-conflict' : buildStale ? 'build-blocked' : build ? buildPhase(build) : 'idle',
        session: event.session,
        project: event.project,
        projectBaseline: baseline,
        build,
        buildStale,
        replayDiagnostics: null,
        readiness: event.session.snapshot.readinessPreview,
        requirementMode: event.session.snapshot.requirementMode,
        operation: event.operation ?? state.operation,
        errorCode: baselineConflict ? 'project_baseline_changed' : null,
        message: baselineConflict
          ? '工程保存版本或流程内容已更新；当前候选基于旧工程保存基线，仅供查看。'
          : event.project ? '工程上下文与会话已由服务端确认。' : '会话已就绪，当前尚未绑定工程。',
        updatedAt: event.at
      });
    }
    case 'baseline-ready': {
      const baselineConflict = buildBaselineConflicts(event.baseline, state.build);
      const buildStale = state.session
        ? snapshotBuildStale(state.session.snapshot, state.build, event.baseline)
        : state.buildStale || baselineConflict;
      return Object.freeze({
        ...state,
        phase: baselineConflict
          ? 'baseline-conflict'
          : state.phase === 'baseline-conflict' && state.build
            ? buildStale ? 'build-blocked' : buildPhase(state.build)
            : state.phase,
        projectBaseline: event.baseline,
        buildStale,
        errorCode: baselineConflict
          ? 'project_baseline_changed'
          : state.errorCode === 'project_baseline_changed' ? null : state.errorCode,
        message: baselineConflict
          ? '工程保存版本或流程内容已更新；当前候选基于旧工程保存基线，仅供查看。'
          : state.message,
        updatedAt: event.at
      });
    }
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
        build: null,
        buildStale: false,
        replayDiagnostics: null,
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
        replayDiagnostics: null,
        run: Object.freeze({
          ...initialRunState,
          kind: 'plan',
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
    case 'build-start':
      return Object.freeze({
        ...state,
        phase: 'build-starting',
        operation: null,
        buildStale: state.build !== null,
        replayDiagnostics: null,
        run: Object.freeze({
          ...initialRunState,
          kind: 'build',
          clientOperationId: event.clientOperationId,
          generation: event.generation,
          status: 'pending'
        }),
        errorCode: null,
        message: '正在创建可恢复的构建任务。',
        updatedAt: event.at
      });
    case 'build-attached': {
      let next: AiWorkbenchState = Object.freeze({
        ...state,
        phase: 'building',
        operation: event.operation ?? state.operation,
        run: Object.freeze({ ...state.run, kind: 'build', runId: event.runId, status: 'running', replayRequired: false }),
        message: '构建任务已建立，正在接收公开阶段。',
        updatedAt: event.at
      });
      if (event.snapshot) next = withSnapshot(next, event.snapshot);
      return next;
    }
    case 'build-unknown':
      return Object.freeze({
        ...state,
        phase: 'unknown-outcome',
        errorCode: 'build_create_unknown_outcome',
        message: event.message,
        updatedAt: event.at
      });
    case 'inputs-updated': {
      const next = withSnapshot(Object.freeze({ ...state, buildStale: true, updatedAt: event.at }), event.snapshot);
      return Object.freeze({
        ...next,
        phase: 'build-blocked',
        buildStale: true,
        message: event.message,
        errorCode: null,
        updatedAt: event.at
      });
    }
    case 'revalidation-start':
      return Object.freeze({
        ...state,
        phase: 'revalidating',
        message: '正在使用最新参数和资源重新计算验证与就绪条件。',
        errorCode: null,
        updatedAt: event.at
      });
    case 'revalidation-ready': {
      const next = withSnapshot(Object.freeze({
        ...state,
        build: event.build,
        buildStale: false,
        updatedAt: event.at
      }), event.snapshot);
      return Object.freeze({
        ...next,
        phase: buildPhase(event.build),
        build: event.build,
        buildStale: false,
        message: event.build.validation.firstFixRecommendation,
        updatedAt: event.at
      });
    }
    case 'handoff-start':
      return Object.freeze({
        ...state,
        phase: 'handoff-creating',
        handoff: null,
        errorCode: null,
        message: '正在由服务端重新核对构建结果、工程保存基线与候选指纹。',
        updatedAt: event.at
      });
    case 'handoff-created':
      return Object.freeze({
        ...state,
        phase: 'handoff-created',
        handoff: event.artifact,
        errorCode: null,
        message: '交接候选已创建，正在安全释放 AI 工作台。',
        updatedAt: event.at
      });
    case 'handoff-unknown':
      return Object.freeze({
        ...state,
        phase: 'handoff-unknown-outcome',
        handoff: null,
        errorCode: 'handoff_create_unknown_outcome',
        message: event.message,
        updatedAt: event.at
      });
    case 'camera-bindings-ready':
      return Object.freeze({ ...state, cameraBindings: Object.freeze([...event.bindings]), updatedAt: event.at });
    case 'replay-observed':
      if (state.run.generation !== event.generation || state.run.runId !== event.diagnostics.runId) return state;
      return Object.freeze({ ...state, replayDiagnostics: event.diagnostics, updatedAt: event.at });
    case 'run-event': {
      if (state.run.runId !== event.event.runId || state.run.generation !== event.generation) return state;
      if (event.event.sequence <= state.run.lastSequence) return state;
      if (state.run.terminalSequence !== null) return state;
      if (eventRequiresReplay(state, event.event, event.generation)) {
        return Object.freeze({
          ...state,
          phase: 'recovering',
          run: Object.freeze({ ...state.run, replayRequired: true }),
          message: state.run.kind === 'build'
            ? '构建进度存在缺口，正在从服务端回放补齐。'
            : '规划进度存在缺口，正在从服务端回放补齐。',
          updatedAt: event.at
        });
      }
      const isTerminal = terminalEvents.has(event.event.eventType);
      let next: AiWorkbenchState = Object.freeze({
        ...state,
        phase: state.run.kind === 'build' ? buildProgressPhase(event.event) : 'planning',
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
      if (event.event.build) {
        const build = event.event.build;
        next = Object.freeze({
          ...next,
          build,
          buildStale: false,
          phase: buildPhase(build),
          message: build.validation.firstFixRecommendation || event.event.summary,
          updatedAt: event.at
        });
      }
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
          phase: state.run.kind === 'build' ? 'build-failed' : 'plan-failed',
          buildStale: state.run.kind === 'build' && next.build !== null,
          errorCode: state.run.kind === 'build' ? 'build_run_failed' : 'plan_run_failed',
          message: event.event.publicMessage || event.event.summary ||
            (state.run.kind === 'build' ? '构建失败，请查看公开诊断后重试。' : '规划失败，请查看公开诊断后重试。')
        });
      } else if (event.event.eventType === 'run.cancelled') {
        next = Object.freeze({
          ...next,
          phase: state.run.kind === 'build' ? 'build-cancelled' : 'cancelled',
          buildStale: state.run.kind === 'build' && next.build !== null,
          message: state.run.kind === 'build'
            ? '本次构建已取消，上一版候选仅供查看。'
            : '本次规划已取消，可修改任务后重新开始。'
        });
      } else if (event.event.eventType === 'run.completed' && next.plan) {
        next = Object.freeze({
          ...next,
          phase: state.run.kind === 'build' && next.build
            ? buildPhase(next.build)
            : planPhase(next.plan, next.readiness)
        });
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
      if (!state.session || event.snapshot.revision <= state.session.snapshot.revision) return state;
      let next = withSnapshot(Object.freeze({ ...state, updatedAt: event.at }), event.snapshot);
      const recoveredBuildTerminalSequence = next.run.kind === 'build' &&
        event.snapshot.buildRunId === next.run.runId &&
        event.snapshot.buildTerminalSequence !== null
        ? event.snapshot.buildTerminalSequence
        : null;
      if (recoveredBuildTerminalSequence !== null) {
        next = Object.freeze({
          ...next,
          run: Object.freeze({
            ...next.run,
            status: runStatus(event.snapshot.buildRunStatus) ?? next.run.status,
            lastSequence: Math.max(next.run.lastSequence, recoveredBuildTerminalSequence),
            terminalSequence: recoveredBuildTerminalSequence,
            replayRequired: false
          })
        });
      }
      const baselineConflict = buildBaselineConflicts(next.projectBaseline, next.build);
      const terminalPhase = recoveredBuildTerminalSequence !== null && event.snapshot.buildRunStatus === 'cancelled'
        ? 'build-cancelled'
        : recoveredBuildTerminalSequence !== null && event.snapshot.buildRunStatus === 'failed'
          ? 'build-failed'
          : next.run.kind === 'build' && next.run.terminalSequence !== null &&
            event.snapshot.buildRunId === next.run.runId &&
            event.snapshot.buildTerminalSequence === next.run.terminalSequence &&
            (next.phase === 'build-cancelled' || next.phase === 'build-failed')
            ? next.phase
            : null;
      return Object.freeze({
        ...next,
        phase: baselineConflict
          ? 'baseline-conflict'
          : terminalPhase
          ? terminalPhase
          : next.build && next.buildStale
          ? 'build-blocked'
          : event.snapshot.buildResult && !next.buildStale
          ? buildPhase(event.snapshot.buildResult)
          : next.plan ? planPhase(next.plan, event.snapshot.readinessPreview) : next.phase,
        message: baselineConflict
          ? '工程保存版本或流程内容已更新；当前候选基于旧工程保存基线，仅供查看。'
          : terminalPhase === 'build-cancelled'
          ? '本次构建已取消，上一版候选仅供查看。'
          : terminalPhase === 'build-failed'
          ? '构建失败，请查看公开诊断后重试。'
          : '会话状态已与服务端最新版本协调。',
        errorCode: baselineConflict
          ? 'project_baseline_changed'
          : terminalPhase === 'build-failed' ? 'build_run_failed' : null,
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
      return Object.freeze({
        ...state,
        phase: state.run.kind === 'build' ? 'build-cancelling' : 'cancelling',
        message: state.run.kind === 'build'
          ? '正在取消构建并等待服务端确认终态。'
          : '正在取消规划并等待服务端确认终态。',
        updatedAt: event.at
      });
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
        projectBaseline: state.projectBaseline,
        cameraBindings: state.cameraBindings,
        requirementMode: state.requirementMode,
        phase: state.session ? 'idle' : 'session-loading',
        updatedAt: event.at
      });
    case 'dispose':
      return Object.freeze({ ...state, phase: 'disposed', message: '', updatedAt: event.at });
  }
}
