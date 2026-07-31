import { computed, readonly, shallowRef, type ComputedRef, type DeepReadonly, type ShallowRef } from 'vue';
import {
  ApiAbortError,
  ApiConflictError,
  ApiHttpError,
  ApiNetworkError,
  ApiServerError,
  ApiUnauthorizedError,
  type ApiTransport
} from '@/platform/api';
import type {
  AiAgentRunEventV1,
  AiHandoffArtifactIdentityV1,
  AiResourceDecisionSelectionV1,
  AiScalarValue,
  AiOperationProjectionV1,
  AiPlanAnswerV1,
  AiProjectContextV1,
  AiProjectBaselineV1,
  AiRequirementMode,
  AiSessionDetailV1,
  AiSessionSnapshotV1,
  AiSessionSummaryV1
} from './contracts';
import { decodeAiSessionSnapshotV1 } from './decoder';
import { createAiWorkbenchApi, type AiWorkbenchApi } from './apiAdapter';
import { createAgentRunStreamAdapter, type AgentRunStreamAdapter } from './agentRunStreamAdapter';
import { projectAiWorkbench, type AiWorkbenchProjection } from './projection';
import { aiWorkbenchActionModel, type AiWorkbenchActionModel } from './actionModel';
import { createAiResourceLedger, type AiResourceLedgerDiagnostics } from './resourceLedger';
import { validateBuildParameterValues } from './parameterValidation';
import {
  createAiHistoryController,
  type AiHistoryState
} from './aiHistoryController';
import {
  eventRequiresReplay,
  initialAiWorkbenchState,
  reduceAiWorkbench,
  type AiWorkbenchEvent,
  type AiWorkbenchState
} from './reducer';

export interface CreateAiSessionOwnerOptions {
  readonly api: ApiTransport;
  readonly requestedSessionId?: string | null;
  readonly projectId?: string | null;
  readonly operationIdFactory?: () => string;
  readonly now?: () => number;
}

export interface AiSessionOwner {
  readonly state: DeepReadonly<ShallowRef<AiWorkbenchState>>;
  readonly projection: ComputedRef<AiWorkbenchProjection>;
  readonly actionModel: ComputedRef<AiWorkbenchActionModel>;
  readonly history: DeepReadonly<ShallowRef<AiHistoryState>>;
  start(): Promise<void>;
  submitTask(description: string, requirementMode: AiRequirementMode): Promise<void>;
  retryIntent(): Promise<void>;
  startPlan(): Promise<void>;
  cancelPlan(): Promise<void>;
  startBuild(): Promise<void>;
  cancelBuild(): Promise<void>;
  confirmParameters(values: Readonly<Record<string, AiScalarValue>>): Promise<void>;
  updateResourceDecisions(decisions: readonly AiResourceDecisionSelectionV1[]): Promise<void>;
  recheckReadiness(): Promise<void>;
  rebuild(): Promise<void>;
  answerClarification(answers: Readonly<Record<string, string>>, acceptRecommended?: boolean): Promise<void>;
  acceptRecommendedAnswers(): Promise<void>;
  previewReadiness(): Promise<void>;
  prepareHandoff(): Promise<AiHandoffArtifactIdentityV1 | null>;
  reconcileHandoff(): Promise<AiHandoffArtifactIdentityV1 | null>;
  reconcile(): Promise<void>;
  startNewTask(): void;
  retry(): Promise<void>;
  refresh(): Promise<void>;
  loadSessionHistory(offset?: number): Promise<void>;
  loadRunHistory(offset?: number, sessionId?: string | null): Promise<void>;
  deleteSession(session: AiSessionSummaryV1): Promise<boolean>;
  reconcileSessionDelete(): Promise<boolean>;
  diagnostics(): AiResourceLedgerDiagnostics;
  dispose(): void;
}

interface PublicFailure {
  readonly phase: 'session-conflict' | 'plan-failed' | 'build-failed' | 'baseline-conflict' |
    'unknown-outcome' | 'offline-or-service-unavailable';
  readonly errorCode: string;
  readonly message: string;
}

function publicFailure(error: unknown, fallbackPhase: PublicFailure['phase'] = 'offline-or-service-unavailable'): PublicFailure {
  if (error instanceof ApiHttpError) {
    const payload = typeof error.payload === 'object' && error.payload !== null
      ? error.payload as Record<string, unknown>
      : null;
    const errorCode = typeof payload?.errorCode === 'string' && /^[a-z0-9_.:-]{1,96}$/i.test(payload.errorCode)
      ? payload.errorCode
      : `http_${error.status}`;
    const message = typeof payload?.publicMessage === 'string' && payload.publicMessage.trim()
      ? payload.publicMessage.trim()
      : error.status === 404
        ? '对象不存在或当前用户无权访问。'
        : error.status === 401
          ? '登录状态已失效，当前 AI 操作已停止。请重新登录后再继续。'
          : error.status === 409
            ? '服务端状态已经更新，请协调最新状态后继续。'
            : '本地服务未能完成请求，请稍后重试。';
    return Object.freeze({
      phase: error.status === 409 && ['project_revision_conflict', 'canonical_flow_hash_conflict',
        'candidate_fingerprint_conflict'].includes(errorCode)
        ? 'baseline-conflict'
        : error.status === 409 ? 'session-conflict' : fallbackPhase,
      errorCode,
      message
    });
  }
  return Object.freeze({
    phase: fallbackPhase,
    errorCode: fallbackPhase === 'plan-failed' ? 'plan_request_failed' :
      fallbackPhase === 'build-failed' ? 'build_request_failed' : 'service_unavailable',
    message: fallbackPhase === 'plan-failed'
      ? '规划未能完成，请检查公开诊断后重试。'
      : fallbackPhase === 'build-failed'
        ? '构建未能完成，请检查公开诊断后重试。'
      : '本地服务暂时不可用，请检查服务状态后重试。'
  });
}

function latestSnapshotFromConflict(error: unknown): AiSessionSnapshotV1 | null {
  if (!(error instanceof ApiConflictError) || typeof error.payload !== 'object' || error.payload === null) return null;
  const payload = error.payload as Record<string, unknown>;
  if (!('latestSnapshot' in payload)) return null;
  try {
    return decodeAiSessionSnapshotV1(payload.latestSnapshot, '$.latestSnapshot');
  } catch {
    return null;
  }
}

function normalizeAnswers(answers: readonly AiPlanAnswerV1[]): readonly AiPlanAnswerV1[] {
  const byField = new Map<string, AiPlanAnswerV1>();
  for (const answer of answers) {
    const field = answer.field.trim().toLowerCase();
    if (!field || !answer.value.trim()) continue;
    byField.set(field, Object.freeze({ ...answer, field }));
  }
  return Object.freeze([...byField.values()]);
}

export function createAiSessionOwner(options: CreateAiSessionOwnerOptions): AiSessionOwner {
  const api: AiWorkbenchApi = createAiWorkbenchApi(options.api);
  const state = shallowRef<AiWorkbenchState>(initialAiWorkbenchState);
  const ledger = createAiResourceLedger();
  const now = options.now ?? Date.now;
  const operationIdFactory = options.operationIdFactory ?? (() => globalThis.crypto.randomUUID());
  const requestedSessionId = options.requestedSessionId?.trim() || null;
  const projectId = options.projectId?.trim() || null;
  let currentSessionId = requestedSessionId;
  let createOperationId: string | null = null;
  let planOperationId: string | null = null;
  let buildOperationId: string | null = null;
  let handoffOperationId: string | null = null;
  let ownerGeneration = 0;
  let planGeneration = 0;
  let disposed = false;
  let authorizationFrozen = false;

  function dispatch(event: AiWorkbenchEvent): void {
    state.value = reduceAiWorkbench(state.value, event);
  }

  function freezeAuthorization(): void {
    if (authorizationFrozen) return;
    authorizationFrozen = true;
    ledger.dispose();
  }

  async function request<T>(run: (signal: AbortSignal) => Promise<T>): Promise<T> {
    if (authorizationFrozen) throw new Error('AI owner is frozen after authentication failure.');
    const controller = new AbortController();
    const release = ledger.trackRequest(controller);
    try {
      return await run(controller.signal);
    } catch (error) {
      if (error instanceof ApiUnauthorizedError) freezeAuthorization();
      throw error;
    } finally {
      release();
    }
  }

  const historyController = createAiHistoryController({
    api,
    execute: request,
    operationIdFactory
  });

  function fail(error: unknown, fallbackPhase: PublicFailure['phase'] = 'offline-or-service-unavailable'): void {
    if (disposed || error instanceof ApiAbortError) return;
    if (error instanceof ApiUnauthorizedError) freezeAuthorization();
    const failure = publicFailure(error, fallbackPhase);
    dispatch({ type: 'failed', ...failure, at: now() });
  }

  function validateSessionRoute(session: AiSessionDetailV1): void {
    if (projectId && session.snapshot.projectId !== projectId) {
      throw new ApiConflictError({
        status: 409,
        statusText: 'Conflict',
        url: '',
        payload: {
          errorCode: 'session_project_mismatch',
          publicMessage: '当前会话不属于此工程，请从正确的工程入口重新进入。'
        },
        responseBody: ''
      });
    }
    if (!projectId && session.snapshot.projectId) {
      throw new ApiConflictError({
        status: 409,
        statusText: 'Conflict',
        url: '',
        payload: {
          errorCode: 'bound_session_requires_project_route',
          publicMessage: '此会话已绑定工程，请从对应工程的 AI 入口恢复。'
        },
        responseBody: ''
      });
    }
  }

  function acceptSession(
    session: AiSessionDetailV1,
    project: AiProjectContextV1 | null,
    operation: AiOperationProjectionV1 | null = null
  ): void {
    validateSessionRoute(session);
    validateBuildSnapshot(session.snapshot);
    currentSessionId = session.sessionId;
    dispatch({ type: 'session-ready', session, project, operation, at: now() });
  }

  function validateBuildSnapshot(snapshot: AiSessionSnapshotV1): void {
    const build = snapshot.buildResult;
    if (!build) return;
    if (snapshot.buildRunId !== build.runId ||
        snapshot.buildClientOperationId !== build.clientOperationId ||
        snapshot.submittedBuildFingerprint !== build.submittedBuildFingerprint ||
        snapshot.buildTerminalSequence === null) {
      throw new Error('Build Snapshot identity mismatch.');
    }
  }

  function validateHandoffArtifact(artifact: AiHandoffArtifactIdentityV1): void {
    const session = state.value.session;
    const build = state.value.build;
    const baseline = state.value.projectBaseline;
    if (!session || !build || !baseline ||
        artifact.sessionId !== session.sessionId ||
        artifact.sessionRevision !== session.snapshot.revision ||
        artifact.planRunId !== session.snapshot.planRunId ||
        artifact.planId !== build.planId ||
        artifact.planHash !== build.planHash ||
        artifact.buildRunId !== build.runId ||
        artifact.buildClientOperationId !== build.clientOperationId ||
        artifact.buildIdentity !== build.buildIdentity ||
        artifact.candidateFlowFingerprint !== build.candidateFlowFingerprint ||
        artifact.targetKind !== baseline.targetKind ||
        artifact.projectBaseline.projectId !== baseline.projectId ||
        artifact.projectBaseline.persistenceRevision !== baseline.persistenceRevision ||
        artifact.projectBaseline.canonicalFlowHash !== baseline.canonicalFlowHash ||
        artifact.status !== 'available') {
      throw new ApiConflictError({
        status: 409,
        statusText: 'Conflict',
        url: '',
        payload: {
          errorCode: 'handoff_artifact_identity_conflict',
          publicMessage: '交接工件与当前 terminal Build 或工程基线不一致。'
        },
        responseBody: ''
      });
    }
  }

  async function loadProjectBaseline(): Promise<AiProjectBaselineV1> {
    const baseline = projectId
      ? await request(signal => api.getProjectBaseline(projectId, signal))
      : Object.freeze({
          targetKind: 'new' as const,
          projectId: null,
          persistenceRevision: null,
          canonicalFlowHash: ''
        });
    dispatch({ type: 'baseline-ready', baseline, at: now() });
    return baseline;
  }

  function acceptRunEvent(event: AiAgentRunEventV1, generation: number): 'accepted' | 'gap' | 'stale' {
    if (disposed || generation !== planGeneration || event.runId !== state.value.run.runId) return 'stale';
    if (event.sessionId && event.sessionId !== currentSessionId) return 'stale';
    if (event.plan && (event.planId !== event.plan.planId || event.planHash !== event.plan.planHash)) return 'stale';
    if (state.value.plan && event.planId && event.planId !== state.value.plan.planId) return 'stale';
    if (state.value.plan && event.planHash && event.planHash !== state.value.plan.planHash) return 'stale';
    const eventSnapshot = event.workspaceSnapshot;
    if (eventSnapshot) {
      if (!event.sessionId || event.sessionId !== currentSessionId ||
          eventSnapshot.projectId !== state.value.session?.snapshot.projectId ||
          (state.value.run.kind === 'plan' && eventSnapshot.planRunId !== event.runId) ||
          (state.value.run.kind === 'build' && eventSnapshot.buildRunId !== event.runId)) return 'stale';
    }
    if (event.build) {
      const baseline = state.value.projectBaseline;
      const snapshot = state.value.session?.snapshot;
      if (!eventSnapshot || eventSnapshot.buildResult?.buildId !== event.build.buildId ||
          eventSnapshot.buildRunId !== event.build.runId ||
          eventSnapshot.buildClientOperationId !== event.build.clientOperationId ||
          eventSnapshot.submittedBuildFingerprint !== event.build.submittedBuildFingerprint ||
          eventSnapshot.answerRevision !== event.build.answerRevision ||
          eventSnapshot.resourceRevision !== event.build.resourceRevision ||
          (state.value.plan && (event.build.planId !== state.value.plan.planId ||
            event.build.planHash !== state.value.plan.planHash)) ||
          (buildOperationId && event.build.clientOperationId !== buildOperationId) ||
          !baseline || event.build.projectBaseline.targetKind !== baseline.targetKind ||
          event.build.projectBaseline.projectId !== baseline.projectId ||
          event.build.projectBaseline.persistenceRevision !== baseline.persistenceRevision ||
          event.build.projectBaseline.canonicalFlowHash !== baseline.canonicalFlowHash ||
          (snapshot && (event.build.answerRevision !== snapshot.answerRevision ||
            event.build.resourceRevision !== snapshot.resourceRevision))) return 'stale';
    }
    if (eventRequiresReplay(state.value, event, generation)) {
      dispatch({ type: 'recovery-start', reason: '规划进度存在缺口，正在从服务端回放补齐。', at: now() });
      return 'gap';
    }
    dispatch({ type: 'run-event', event, generation, at: now() });
    return 'accepted';
  }

  const streamAdapter: AgentRunStreamAdapter = createAgentRunStreamAdapter({
    api,
    ledger,
    getAfterSequence: () => state.value.run.lastSequence,
    isTerminal: () => state.value.run.terminalSequence !== null,
    onEvent: acceptRunEvent,
    onReplay: (replay, generation) => dispatch({
      type: 'replay-observed', diagnostics: replay.diagnostics, generation, at: now()
    }),
    onTerminalReplayUnresolved: async (replay, generation) => {
      const recoverableTerminal = replay.events.find(event =>
        (event.eventType === 'run.completed' || event.eventType === 'run.failed' ||
          event.eventType === 'run.cancelled') && event.build && !event.workspaceSnapshot);
      if (disposed || generation !== planGeneration || state.value.run.kind !== 'build' ||
          state.value.run.runId !== replay.summary.runId || !currentSessionId || !recoverableTerminal) return;
      const session = await request(signal => api.getSession(currentSessionId!, signal));
      if (disposed || generation !== planGeneration || state.value.run.runId !== replay.summary.runId) return;
      validateSessionRoute(session);
      validateBuildSnapshot(session.snapshot);
      if (!session.snapshot.buildResult || session.snapshot.buildRunId !== replay.summary.runId ||
          session.snapshot.buildTerminalSequence !== replay.summary.lastSequence ||
          session.snapshot.buildRunStatus !== replay.summary.status) {
        throw new Error('Terminal Build replay is not confirmed by the canonical Session Snapshot.');
      }
      dispatch({ type: 'snapshot-ready', snapshot: session.snapshot, at: now() });
      await loadCameraBindings();
    },
    onRecovering: message => dispatch({ type: 'recovery-start', reason: message, at: now() }),
    onFailure: error => fail(error, 'offline-or-service-unavailable')
  });

  async function attachRun(
    runId: string,
    operation: AiOperationProjectionV1 | null,
    snapshot: AiSessionSnapshotV1 | null,
    initialEvents: readonly AiAgentRunEventV1[] = [],
    kind: 'plan' | 'build' = 'plan'
  ): Promise<void> {
    if (!currentSessionId) throw new Error('A confirmed Session is required before attaching a run.');
    dispatch(kind === 'build'
      ? { type: 'build-attached', runId, operation, snapshot, at: now() }
      : { type: 'plan-attached', runId, operation, snapshot, at: now() });
    for (const event of initialEvents) acceptRunEvent(event, planGeneration);
    await streamAdapter.start(runId, planGeneration);
  }

  async function reconcileCreate(operationId: string): Promise<boolean> {
    dispatch({ type: 'session-start', mode: 'hydrate', at: now() });
    const operation = await request(signal => api.getOperation(operationId, 'session_create', signal));
    if (operation.status !== 'created' || !operation.sessionId) return false;
    const session = await request(signal => api.getSession(operation.sessionId!, signal));
    const project = projectId ? await request(signal => api.getProject(projectId, signal)) : null;
    acceptSession(session, project, operation);
    return true;
  }

  async function reconcilePlanOperation(): Promise<boolean> {
    if (!planOperationId) return false;
    dispatch({ type: 'recovery-start', reason: '正在查询此前规划操作的服务端结果。', at: now() });
    const operation = await request(signal => api.getOperation(planOperationId!, 'plan_run', signal));
    if (operation.status === 'pending') return false;
    if (operation.status !== 'created' || !operation.runId || operation.sessionId !== currentSessionId) {
      throw new Error(operation.publicMessage || 'Plan operation was not created.');
    }
    await attachRun(operation.runId, operation, null);
    return true;
  }

  async function reconcileBuildOperation(): Promise<boolean> {
    if (!buildOperationId) return false;
    dispatch({ type: 'recovery-start', reason: '正在查询此前构建操作的服务端结果。', at: now() });
    const operation = await request(signal => api.getOperation(buildOperationId!, 'build_run', signal));
    if (operation.status === 'pending') {
      dispatch({ type: 'build-unknown', message: '构建创建结果尚未确认，请继续协调服务端状态。', at: now() });
      return false;
    }
    if (operation.status !== 'created' || !operation.runId || operation.sessionId !== currentSessionId) {
      throw new Error(operation.publicMessage || 'Build operation was not created.');
    }
    await attachRun(operation.runId, operation, null, [], 'build');
    return true;
  }

  async function restorePlanRun(snapshot: AiSessionSnapshotV1): Promise<void> {
    if (!snapshot.planRunId) return;
    planGeneration += 1;
    dispatch({ type: 'plan-start', clientOperationId: planOperationId ?? operationIdFactory(), generation: planGeneration, at: now() });
    await attachRun(snapshot.planRunId, null, snapshot);
  }

  async function restoreBuildRun(snapshot: AiSessionSnapshotV1): Promise<boolean> {
    if (!snapshot.buildRunId) return false;
    buildOperationId = snapshot.buildClientOperationId;
    if (snapshot.buildResult && snapshot.buildTerminalSequence !== null) {
      await loadCameraBindings();
      return true;
    }
    planGeneration += 1;
    dispatch({
      type: 'build-start',
      clientOperationId: buildOperationId ?? operationIdFactory(),
      generation: planGeneration,
      at: now()
    });
    await attachRun(snapshot.buildRunId, null, snapshot, [], 'build');
    await refreshSessionSnapshot();
    await loadCameraBindings();
    return true;
  }

  async function start(): Promise<void> {
    if (disposed || authorizationFrozen || state.value.phase === 'session-loading' || state.value.phase === 'recovering') return;
    const runGeneration = ++ownerGeneration;
    const mode = currentSessionId ? 'hydrate' : 'create';
    dispatch({ type: 'session-start', mode, at: now() });
    try {
      const project = projectId ? await request(signal => api.getProject(projectId, signal)) : null;
      if (disposed || runGeneration !== ownerGeneration) return;
      if (projectId && project?.id !== projectId) throw new Error('Canonical Project identity mismatch.');
      await loadProjectBaseline();
      if (disposed || runGeneration !== ownerGeneration) return;

      if (currentSessionId) {
        const session = await request(signal => api.getSession(currentSessionId!, signal));
        if (disposed || runGeneration !== ownerGeneration) return;
        acceptSession(session, project);
        if (await restoreBuildRun(session.snapshot)) return;
        await restorePlanRun(session.snapshot);
        return;
      }

      createOperationId ??= operationIdFactory();
      const response = await request(signal => api.createSession({
        clientOperationId: createOperationId!,
        ...(projectId ? { projectId } : {})
      }, signal));
      if (disposed || runGeneration !== ownerGeneration) return;
      if (response.session) {
        acceptSession(response.session, project, response.operation);
        await restoreBuildRun(response.session.snapshot);
        return;
      }
      if (await reconcileCreate(createOperationId)) return;
      throw new Error('Session operation is still pending.');
    } catch (error) {
      if (disposed || runGeneration !== ownerGeneration || error instanceof ApiAbortError) return;
      if (!currentSessionId && createOperationId && !(error instanceof ApiUnauthorizedError)) {
        try {
          if (await reconcileCreate(createOperationId)) return;
        } catch (reconcileError) {
          if (reconcileError instanceof ApiAbortError || disposed) return;
        }
      }
      fail(error);
    }
  }

  async function startPlan(): Promise<void> {
    if (disposed || authorizationFrozen || !currentSessionId || !state.value.taskDescription.trim()) return;
    planOperationId ??= operationIdFactory();
    planGeneration += 1;
    const generation = planGeneration;
    dispatch({ type: 'plan-start', clientOperationId: planOperationId, generation, at: now() });
    try {
      const snapshot = state.value.session?.snapshot;
      const response = await request(signal => api.createPlanRun({
        clientOperationId: planOperationId!,
        description: state.value.taskDescription,
        sessionId: currentSessionId!,
        requirementMode: state.value.requirementMode,
        confirmedPlanAnswers: snapshot?.confirmedPlanAnswers ?? [],
        resolvedPlanFields: state.value.intent?.resolvedPlanFields ?? [],
        remainingPlanFields: state.value.intent?.remainingPlanFields ?? []
      }, signal));
      if (disposed || generation !== planGeneration) return;
      if (!response.runId || response.sessionId !== currentSessionId || response.operation.kind !== 'plan_run') {
        throw new Error('Plan Run response identity mismatch.');
      }
      await attachRun(response.runId, response.operation, response.workspaceSnapshot, response.events);
    } catch (error) {
      if (disposed || generation !== planGeneration || error instanceof ApiAbortError) return;
      if (!(error instanceof ApiUnauthorizedError)) {
        try {
          if (await reconcilePlanOperation()) return;
        } catch (reconcileError) {
          if (reconcileError instanceof ApiAbortError || disposed) return;
        }
      }
      fail(error, 'plan-failed');
    }
  }

  async function startBuild(): Promise<void> {
    const session = state.value.session;
    const plan = state.value.plan;
    const baseline = state.value.projectBaseline;
    if (disposed || authorizationFrozen || !currentSessionId || !session || !plan || !baseline ||
        !(state.value.readiness?.buildReadiness.canBuild ?? plan.buildReadiness.canBuild)) return;
    buildOperationId ??= operationIdFactory();
    planGeneration += 1;
    const generation = planGeneration;
    dispatch({ type: 'build-start', clientOperationId: buildOperationId, generation, at: now() });
    try {
      const response = await request(signal => api.createBuildRun({
        clientOperationId: buildOperationId!,
        target: baseline,
        description: state.value.taskDescription || plan.originalUserPrompt || plan.goal,
        sessionId: currentSessionId!,
        requirementMode: state.value.requirementMode,
        buildFromPlan: {
          planId: plan.planId,
          planHash: plan.planHash,
          workspaceExpectedRevision: session.snapshot.revision,
          planSnapshot: plan,
          confirmedAnswers: session.snapshot.confirmedPlanAnswers,
          userSelections: session.snapshot.planQuestionSelections,
          acceptedDefaults: plan.recommendedDefaults.map(item => item.id),
          operatorCatalogVersion: plan.operatorCatalogVersion,
          stationBoundarySummary: plan.stationBoundarySummary,
          plcOutputPolicy: plan.plcOutputPolicy,
          buildIntent: baseline.targetKind === 'existing' ? 'modify' : 'new',
          originalUserPrompt: state.value.taskDescription || plan.originalUserPrompt,
          acceptedRecommendedDefaults: session.snapshot.planAcceptedRecommendedDefaults,
          answerRevision: session.snapshot.answerRevision,
          resourceRevision: session.snapshot.resourceRevision,
          parameterValues: session.snapshot.buildParameterValues,
          resourceDecisions: session.snapshot.resourceDecisions,
          metadataOnly: true
        }
      }, signal));
      if (disposed || generation !== planGeneration) return;
      if (!response.runId || response.sessionId !== currentSessionId || response.operation.kind !== 'build_run') {
        throw new Error('Build Run response identity mismatch.');
      }
      await attachRun(response.runId, response.operation, response.workspaceSnapshot, response.events, 'build');
      await refreshSessionSnapshot();
      await loadCameraBindings();
    } catch (error) {
      if (disposed || generation !== planGeneration || error instanceof ApiAbortError) return;
      if (!(error instanceof ApiUnauthorizedError) && buildOperationId) {
        try {
          if (await reconcileBuildOperation()) return;
        } catch (reconcileError) {
          if (reconcileError instanceof ApiAbortError || disposed) return;
        }
      }
      const failure = publicFailure(error, 'build-failed');
      if (failure.phase === 'baseline-conflict' || error instanceof ApiConflictError) {
        dispatch({ type: 'failed', ...failure, at: now() });
      } else {
        dispatch({
          type: 'build-unknown',
          message: '构建创建响应未能确认；请先查询 operation 状态，系统不会盲目重复创建。',
          at: now()
        });
      }
    }
  }

  async function submitTask(description: string, mode: AiRequirementMode): Promise<void> {
    if (disposed || authorizationFrozen || !currentSessionId) return;
    const normalized = description.trim();
    if (normalized.length < 8 || normalized.length > 4000) {
      dispatch({
        type: 'failed',
        phase: 'plan-blocked',
        errorCode: 'task_description_invalid',
        message: '请用 8 至 4000 个字符说明检测对象、目标或缺陷，以及期望输出。',
        at: now()
      });
      return;
    }
    planOperationId = null;
    const generation = ++ownerGeneration;
    dispatch({ type: 'intent-start', description: normalized, requirementMode: mode, at: now() });
    try {
      const snapshot = state.value.session?.snapshot;
      const intent = await request(signal => api.routeIntent({
        description: normalized,
        sessionId: currentSessionId!,
        requirementMode: mode,
        confirmedPlanAnswers: snapshot?.confirmedPlanAnswers ?? [],
        resolvedPlanFields: [],
        remainingPlanFields: []
      }, signal));
      if (disposed || generation !== ownerGeneration) return;
      dispatch({ type: 'intent-ready', intent, at: now() });
      if (intent.shouldOpenPlan) await startPlan();
    } catch (error) {
      if (disposed || generation !== ownerGeneration || error instanceof ApiAbortError) return;
      fail(error, 'plan-failed');
    }
  }

  async function retryIntent(): Promise<void> {
    if (!state.value.taskDescription) return;
    await submitTask(state.value.taskDescription, state.value.requirementMode);
  }

  async function cancelPlan(): Promise<void> {
    const runId = state.value.run.runId;
    if (disposed || authorizationFrozen || !runId || state.value.run.terminalSequence !== null) return;
    dispatch({ type: 'cancel-start', at: now() });
    try {
      await request(signal => api.cancelPlanRun(runId, signal));
      await streamAdapter.reconcile();
    } catch (error) {
      if (error instanceof ApiConflictError) {
        await streamAdapter.reconcile();
        return;
      }
      fail(error, 'plan-failed');
    }
  }

  async function cancelBuild(): Promise<void> {
    await cancelPlan();
  }

  async function persistBuildInputs(
    parameterValues: Readonly<Record<string, AiScalarValue>> | null,
    resourceDecisions: readonly AiResourceDecisionSelectionV1[] | null,
    message: string
  ): Promise<void> {
    const session = state.value.session;
    const build = state.value.build;
    if (disposed || authorizationFrozen || !session || !build) return;
    try {
      const snapshot = await request(signal => api.updateWorkspaceSnapshot(session.sessionId, {
        expectedRevision: session.snapshot.revision,
        clientMutationId: operationIdFactory(),
        projectId: session.snapshot.projectId,
        lifecycleState: 'build_inputs_changed',
        ...(parameterValues ? {
          buildParameterValues: Object.freeze({ ...session.snapshot.buildParameterValues, ...parameterValues }),
          answerRevision: session.snapshot.answerRevision + 1
        } : {}),
        ...(resourceDecisions ? {
          resourceDecisions
        } : {})
      }, signal));
      dispatch({ type: 'inputs-updated', snapshot, message, at: now() });
    } catch (error) {
      const latest = latestSnapshotFromConflict(error);
      if (latest) {
        dispatch({ type: 'snapshot-ready', snapshot: latest, at: now() });
        dispatch({
          type: 'failed',
          phase: 'session-conflict',
          errorCode: 'workspace_revision_conflict',
          message: '参数或资源已在其他位置更新；已加载最新状态，请核对后再次提交。',
          at: now()
        });
        return;
      }
      fail(error, 'build-failed');
    }
  }

  async function confirmParameters(values: Readonly<Record<string, AiScalarValue>>): Promise<void> {
    const build = state.value.build;
    const session = state.value.session;
    if (!build || !session) return;
    const allowed = new Set(build.parameterMapping
      .filter(item => item.pending && !item.resourceDependent)
      .map(item => item.canonicalKey));
    const submitted = Object.fromEntries(Object.entries(values).filter(([key]) => allowed.has(key)));
    const candidateContext = Object.fromEntries(build.parameterMapping
      .filter(item => !item.pending && !item.resourceDependent && item.hasExplicitValue)
      .map(item => [item.canonicalKey, item.value]));
    const validation = validateBuildParameterValues(
      build.parameterMapping,
      submitted,
      { ...candidateContext, ...session.snapshot.buildParameterValues },
      [...allowed]
    );
    if (Object.keys(submitted).length === 0 || !validation.valid) {
      dispatch({
        type: 'failed',
        phase: 'build-failed',
        errorCode: Object.keys(submitted).length === 0
          ? 'parameter_confirmation_empty'
          : 'parameter_confirmation_invalid',
        message: Object.values(validation.errors)[0] ?? '没有可确认的参数值，请先完成必填项和合同校验。',
        at: now()
      });
      return;
    }
    const activeKeys = new Set(validation.activeKeys);
    const activeSubmitted = Object.freeze(Object.fromEntries(
      Object.entries(submitted).filter(([key]) => activeKeys.has(key))
    ));
    await persistBuildInputs(activeSubmitted, null, '参数已确认，旧验证与就绪结论已失效。');
  }

  async function updateResourceDecisions(decisions: readonly AiResourceDecisionSelectionV1[]): Promise<void> {
    const build = state.value.build;
    if (!state.value.session || !build) return;
    const missingById = new Map(build.missingResources.map(resource => [resource.canonicalId, resource]));
    const valid = decisions.filter(decision => {
      const resource = missingById.get(decision.canonicalId);
      return resource?.resourceType === 'camera_binding' && /^[a-z0-9_.:-]{1,160}$/i.test(decision.resourceKey);
    });
    if (valid.length === 0) return;
    await persistBuildInputs(null, Object.freeze(valid), '资源决策已保存，旧验证与就绪结论已失效。');
  }

  async function recheckReadiness(): Promise<void> {
    const session = state.value.session;
    const build = state.value.build;
    if (disposed || authorizationFrozen || !session || !build) return;
    dispatch({ type: 'revalidation-start', at: now() });
    try {
      const response = await request(signal => api.revalidateBuild({
        runId: build.runId,
        sessionId: session.sessionId,
        expectedRevision: session.snapshot.revision,
        clientMutationId: operationIdFactory(),
        buildId: build.buildId,
        candidateFlowFingerprint: build.candidateFlowFingerprint,
        answerRevision: session.snapshot.answerRevision,
        resourceRevision: session.snapshot.resourceRevision
      }, signal));
      if (response.build.runId !== build.runId || response.build.buildId !== build.buildId ||
          response.build.candidateFlowFingerprint !== build.candidateFlowFingerprint) {
        throw new Error('Build revalidation response identity mismatch.');
      }
      dispatch({ type: 'revalidation-ready', build: response.build, snapshot: response.snapshot, at: now() });
    } catch (error) {
      const latest = latestSnapshotFromConflict(error);
      if (latest) dispatch({ type: 'snapshot-ready', snapshot: latest, at: now() });
      fail(error, error instanceof ApiConflictError ? 'baseline-conflict' : 'build-failed');
    }
  }

  async function rebuild(): Promise<void> {
    if (disposed || authorizationFrozen) return;
    buildOperationId = null;
    await refreshSessionSnapshot();
    if (disposed || authorizationFrozen) return;
    await startBuild();
  }

  async function loadCameraBindings(): Promise<void> {
    if (!state.value.build?.missingResources.some(resource => resource.resourceType === 'camera_binding')) return;
    try {
      const bindings = await request(signal => api.listCameraBindings(signal));
      dispatch({ type: 'camera-bindings-ready', bindings, at: now() });
    } catch (error) {
      if (error instanceof ApiAbortError || error instanceof ApiUnauthorizedError) return;
      dispatch({ type: 'camera-bindings-ready', bindings: Object.freeze([]), at: now() });
    }
  }

  async function persistAnswers(
    selections: Readonly<Record<string, string>>,
    answers: readonly AiPlanAnswerV1[],
    acceptRecommended: boolean
  ): Promise<AiSessionSnapshotV1 | null> {
    const session = state.value.session;
    if (!session) return null;
    try {
      const snapshot = await request(signal => api.updateWorkspaceSnapshot(session.sessionId, {
        expectedRevision: session.snapshot.revision,
        clientMutationId: operationIdFactory(),
        projectId: session.snapshot.projectId,
        lifecycleState: 'plan_blocked',
        planQuestionSelections: selections,
        confirmedPlanAnswers: session.snapshot.confirmedPlanAnswers,
        optimisticPlanAnswers: answers,
        answerRevision: session.snapshot.answerRevision + 1,
        requirementMode: state.value.requirementMode,
        planAcceptedRecommendedDefaults: acceptRecommended
      }, signal));
      dispatch({ type: 'snapshot-ready', snapshot, at: now() });
      return snapshot;
    } catch (error) {
      const latest = latestSnapshotFromConflict(error);
      if (latest) {
        dispatch({ type: 'snapshot-ready', snapshot: latest, at: now() });
        dispatch({
          type: 'failed',
          phase: 'session-conflict',
          errorCode: 'workspace_revision_conflict',
          message: '会话答案已在其他位置更新；已加载最新状态，请核对后再次提交。',
          at: now()
        });
        return null;
      }
      fail(error);
      return null;
    }
  }

  async function answerClarification(
    answersByField: Readonly<Record<string, string>>,
    acceptRecommended = false
  ): Promise<void> {
    const plan = state.value.plan;
    if (disposed || authorizationFrozen || !plan || !state.value.session) return;
    const normalized = new Map<string, AiPlanAnswerV1>();
    for (const question of plan.clarificationQuestions) {
      const value = answersByField[question.field]?.trim();
      if (!value) continue;
      const origin = acceptRecommended
        ? 'accepted_recommended_default'
        : question.options.some(option => option.value === value)
          ? 'explicit_user_selection'
          : 'explicit_user_text';
      normalized.set(question.field.toLowerCase(), Object.freeze({
        questionId: question.id,
        field: question.field.toLowerCase(),
        value,
        origin,
        confidence: 1,
        resolved: true
      }));
    }
    const answers = normalizeAnswers([...normalized.values()]);
    if (answers.length === 0) {
      dispatch({
        type: 'failed',
        phase: 'plan-blocked',
        errorCode: 'clarification_answer_required',
        message: '请至少回答一个待确认事项，再重新检查方案条件。',
        at: now()
      });
      return;
    }
    const selections = Object.freeze(Object.fromEntries(answers.map(answer => [answer.field, answer.value])));
    const snapshot = await persistAnswers(selections, answers, acceptRecommended);
    if (snapshot) await previewReadiness();
  }

  async function acceptRecommendedAnswers(): Promise<void> {
    const plan = state.value.plan;
    if (!plan) return;
    const answers: Record<string, string> = {};
    for (const question of plan.clarificationQuestions) {
      const recommended = question.options.find(option => option.recommended);
      const value = recommended?.value || question.defaultValue;
      if (value) answers[question.field] = value;
    }
    await answerClarification(Object.freeze(answers), true);
  }

  async function previewReadiness(): Promise<void> {
    const session = state.value.session;
    const plan = state.value.plan;
    if (disposed || authorizationFrozen || !session || !plan) return;
    dispatch({ type: 'readiness-start', at: now() });
    const sourceAnswers = session.snapshot.optimisticPlanAnswers.length > 0
      ? session.snapshot.optimisticPlanAnswers
      : session.snapshot.confirmedPlanAnswers;
    try {
      const readiness = await request(signal => api.previewReadiness({
        sessionId: session.sessionId,
        expectedRevision: session.snapshot.revision,
        planId: plan.planId,
        planHash: plan.planHash,
        planSnapshot: plan,
        requirementMode: state.value.requirementMode,
        confirmedAnswers: sourceAnswers,
        userSelections: session.snapshot.planQuestionSelections,
        acceptedDefaults: plan.recommendedDefaults.map(item => item.id),
        acceptedRecommendedDefaults: session.snapshot.planAcceptedRecommendedDefaults,
        answerRevision: session.snapshot.answerRevision,
        resourceRevision: session.snapshot.resourceRevision,
        originalUserPrompt: state.value.taskDescription || plan.originalUserPrompt,
        metadataOnly: true
      }, signal));
      if (readiness.planId !== plan.planId || readiness.planHash !== plan.planHash ||
          readiness.answerRevision !== session.snapshot.answerRevision ||
          readiness.resourceRevision !== session.snapshot.resourceRevision) {
        throw new Error('Readiness response identity mismatch.');
      }
      const latestSession = state.value.session;
      if (!latestSession || latestSession.sessionId !== session.sessionId ||
          latestSession.snapshot.revision !== session.snapshot.revision ||
          latestSession.snapshot.answerRevision !== session.snapshot.answerRevision ||
          latestSession.snapshot.resourceRevision !== session.snapshot.resourceRevision) {
        dispatch({
          type: 'failed',
          phase: 'session-conflict',
          errorCode: 'readiness_response_stale',
          message: '就绪检查返回时会话已更新；旧响应未应用，请重新检查。',
          at: now()
        });
        return;
      }
      const snapshot = await request(signal => api.updateWorkspaceSnapshot(latestSession.sessionId, {
        expectedRevision: latestSession.snapshot.revision,
        clientMutationId: operationIdFactory(),
        projectId: latestSession.snapshot.projectId,
        lifecycleState: readiness.buildReadiness.canBuild ? 'plan_ready' : 'plan_blocked',
        planQuestionSelections: latestSession.snapshot.planQuestionSelections,
        confirmedPlanAnswers: readiness.acceptedAnswers,
        optimisticPlanAnswers: [],
        answerRevision: readiness.answerRevision,
        readinessPreview: readiness,
        requirementMode: readiness.requirementMode,
        planAcceptedRecommendedDefaults: latestSession.snapshot.planAcceptedRecommendedDefaults
      }, signal));
      dispatch({ type: 'readiness-ready', readiness, snapshot, at: now() });
    } catch (error) {
      const latest = latestSnapshotFromConflict(error);
      if (latest) {
        dispatch({ type: 'snapshot-ready', snapshot: latest, at: now() });
        dispatch({
          type: 'failed',
          phase: 'session-conflict',
          errorCode: 'workspace_revision_conflict',
          message: '就绪检查期间会话已更新；已加载最新答案，请核对后重新检查。',
          at: now()
        });
        return;
      }
      fail(error, 'plan-failed');
    }
  }

  async function prepareHandoff(): Promise<AiHandoffArtifactIdentityV1 | null> {
    if (disposed || authorizationFrozen || state.value.phase !== 'build-ready') return null;
    const session = state.value.session;
    const build = state.value.build;
    const baseline = state.value.projectBaseline;
    const planRunId = session?.snapshot.planRunId;
    if (!session || !build || !baseline || !planRunId || !build.clientOperationId ||
        !build.validation.handoffEligible || build.validation.applyGate.blocked ||
        !build.validation.applyGate.canvasApplyReady || !build.validation.applyGate.runtimeDraftReady) {
      dispatch({
        type: 'failed',
        phase: 'session-conflict',
        errorCode: 'handoff_not_eligible',
        message: '当前 terminal Build 未满足服务端交接身份与 ApplyGate 条件。',
        at: now()
      });
      return null;
    }
    handoffOperationId ??= operationIdFactory();
    dispatch({ type: 'handoff-start', at: now() });
    try {
      const artifact = await request(signal => api.createHandoff({
        clientOperationId: handoffOperationId!,
        sessionId: session.sessionId,
        expectedSessionRevision: session.snapshot.revision,
        planRunId,
        planId: build.planId,
        planHash: build.planHash,
        buildRunId: build.runId,
        buildClientOperationId: build.clientOperationId,
        buildIdentity: build.buildIdentity,
        candidateFlowFingerprint: build.candidateFlowFingerprint,
        answerRevision: build.answerRevision,
        resourceRevision: build.resourceRevision,
        projectBaseline: baseline
      }, signal));
      validateHandoffArtifact(artifact);
      dispatch({ type: 'handoff-created', artifact, at: now() });
      return artifact;
    } catch (error) {
      if (disposed || error instanceof ApiAbortError) return null;
      const payload = error instanceof ApiHttpError && typeof error.payload === 'object' && error.payload !== null
        ? error.payload as Record<string, unknown>
        : null;
      if (error instanceof ApiNetworkError || error instanceof ApiServerError ||
          payload?.errorCode === 'handoff_create_unknown_outcome') {
        dispatch({
          type: 'handoff-unknown',
          message: '交接响应未能确认；请查询当前 Build 的既有工件，禁止重复创建。',
          at: now()
        });
        return null;
      }
      fail(error);
      return null;
    }
  }

  async function reconcileHandoff(): Promise<AiHandoffArtifactIdentityV1 | null> {
    if (disposed || authorizationFrozen) return null;
    const buildRunId = state.value.build?.runId;
    if (!buildRunId) return null;
    dispatch({ type: 'handoff-start', at: now() });
    try {
      const artifact = await request(signal => api.getHandoffByBuild(buildRunId, signal));
      validateHandoffArtifact(artifact);
      handoffOperationId = artifact.clientOperationId;
      dispatch({ type: 'handoff-created', artifact, at: now() });
      return artifact;
    } catch (error) {
      if (disposed || error instanceof ApiAbortError) return null;
      fail(error);
      return null;
    }
  }

  async function reconcile(): Promise<void> {
    if (disposed || authorizationFrozen) return;
    try {
      if (state.value.phase === 'handoff-unknown-outcome') {
        await reconcileHandoff();
        return;
      }
      if (projectId) await loadProjectBaseline();
      if (state.value.phase === 'baseline-conflict') {
        if (currentSessionId) {
          const session = await request(signal => api.getSession(currentSessionId!, signal));
          validateSessionRoute(session);
          dispatch({ type: 'snapshot-ready', snapshot: session.snapshot, at: now() });
        }
        return;
      }
      if (state.value.run.runId) await streamAdapter.reconcile();
      else if (buildOperationId && await reconcileBuildOperation()) return;
      else if (planOperationId && await reconcilePlanOperation()) return;
      if (currentSessionId) {
        const session = await request(signal => api.getSession(currentSessionId!, signal));
        validateSessionRoute(session);
        dispatch({ type: 'snapshot-ready', snapshot: session.snapshot, at: now() });
      }
    } catch (error) {
      fail(error);
    }
  }

  async function refreshSessionSnapshot(): Promise<void> {
    if (!currentSessionId) return;
    const session = await request(signal => api.getSession(currentSessionId!, signal));
    validateSessionRoute(session);
    validateBuildSnapshot(session.snapshot);
    dispatch({ type: 'snapshot-ready', snapshot: session.snapshot, at: now() });
  }

  function startNewTask(): void {
    if (disposed) return;
    planOperationId = null;
    buildOperationId = null;
    handoffOperationId = null;
    planGeneration += 1;
    dispatch({ type: 'new-task', at: now() });
  }

  async function retry(): Promise<void> {
    if (disposed || authorizationFrozen) return;
    if (!state.value.session) {
      dispatch({ type: 'retry', at: now() });
      await start();
      return;
    }
    if (['build-failed', 'build-cancelled', 'baseline-conflict'].includes(state.value.phase)) {
      await rebuild();
      return;
    }
    if (state.value.taskDescription) await retryIntent();
    else await reconcile();
  }

  return Object.freeze({
    state: readonly(state),
    projection: computed(() => projectAiWorkbench(state.value)),
    actionModel: computed(() => aiWorkbenchActionModel(state.value)),
    history: historyController.state,
    start,
    submitTask,
    retryIntent,
    startPlan,
    cancelPlan,
    startBuild,
    cancelBuild,
    confirmParameters,
    updateResourceDecisions,
    recheckReadiness,
    rebuild,
    answerClarification,
    acceptRecommendedAnswers,
    previewReadiness,
    prepareHandoff,
    reconcileHandoff,
    reconcile,
    startNewTask,
    retry,
    refresh: reconcile,
    loadSessionHistory: historyController.loadSessions,
    loadRunHistory: historyController.loadRuns,
    deleteSession: historyController.deleteSession,
    reconcileSessionDelete: historyController.reconcileDelete,
    diagnostics: ledger.diagnostics,
    dispose() {
      if (disposed) return;
      disposed = true;
      ownerGeneration += 1;
      planGeneration += 1;
      historyController.dispose();
      streamAdapter.dispose();
      ledger.dispose();
      dispatch({ type: 'dispose', at: now() });
    }
  });
}
