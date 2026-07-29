import { computed, readonly, shallowRef, type ComputedRef, type DeepReadonly, type ShallowRef } from 'vue';
import { ApiAbortError, ApiConflictError, ApiHttpError, ApiUnauthorizedError, type ApiTransport } from '@/platform/api';
import type {
  AiAgentRunEventV1,
  AiOperationProjectionV1,
  AiPlanAnswerV1,
  AiProjectContextV1,
  AiRequirementMode,
  AiSessionDetailV1,
  AiSessionSnapshotV1
} from './contracts';
import { decodeAiSessionSnapshotV1 } from './decoder';
import { createAiWorkbenchApi, type AiWorkbenchApi } from './apiAdapter';
import { createAgentRunStreamAdapter, type AgentRunStreamAdapter } from './agentRunStreamAdapter';
import { projectAiWorkbench, type AiWorkbenchProjection } from './projection';
import { aiWorkbenchActionModel, type AiWorkbenchActionModel } from './actionModel';
import { createAiResourceLedger, type AiResourceLedgerDiagnostics } from './resourceLedger';
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
  start(): Promise<void>;
  submitTask(description: string, requirementMode: AiRequirementMode): Promise<void>;
  retryIntent(): Promise<void>;
  startPlan(): Promise<void>;
  cancelPlan(): Promise<void>;
  answerClarification(answers: Readonly<Record<string, string>>, acceptRecommended?: boolean): Promise<void>;
  acceptRecommendedAnswers(): Promise<void>;
  previewReadiness(): Promise<void>;
  reconcile(): Promise<void>;
  startNewTask(): void;
  retry(): Promise<void>;
  refresh(): Promise<void>;
  diagnostics(): AiResourceLedgerDiagnostics;
  dispose(): void;
}

interface PublicFailure {
  readonly phase: 'session-conflict' | 'plan-failed' | 'offline-or-service-unavailable';
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
      phase: error.status === 409 ? 'session-conflict' : fallbackPhase,
      errorCode,
      message
    });
  }
  return Object.freeze({
    phase: fallbackPhase,
    errorCode: fallbackPhase === 'plan-failed' ? 'plan_request_failed' : 'service_unavailable',
    message: fallbackPhase === 'plan-failed'
      ? '规划未能完成，请检查公开诊断后重试。'
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
  let ownerGeneration = 0;
  let planGeneration = 0;
  let disposed = false;
  let authorizationFrozen = false;

  function dispatch(event: AiWorkbenchEvent): void {
    state.value = reduceAiWorkbench(state.value, event);
  }

  async function request<T>(run: (signal: AbortSignal) => Promise<T>): Promise<T> {
    if (authorizationFrozen) throw new Error('AI owner is frozen after authentication failure.');
    const controller = new AbortController();
    const release = ledger.trackRequest(controller);
    try {
      return await run(controller.signal);
    } catch (error) {
      if (error instanceof ApiUnauthorizedError) authorizationFrozen = true;
      throw error;
    } finally {
      release();
    }
  }

  function fail(error: unknown, fallbackPhase: PublicFailure['phase'] = 'offline-or-service-unavailable'): void {
    if (disposed || error instanceof ApiAbortError) return;
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
    currentSessionId = session.sessionId;
    dispatch({ type: 'session-ready', session, project, operation, at: now() });
  }

  function acceptRunEvent(event: AiAgentRunEventV1, generation: number): 'accepted' | 'gap' | 'stale' {
    if (disposed || generation !== planGeneration || event.runId !== state.value.run.runId) return 'stale';
    if (event.sessionId && event.sessionId !== currentSessionId) return 'stale';
    if (event.plan && (event.planId !== event.plan.planId || event.planHash !== event.plan.planHash)) return 'stale';
    if (state.value.plan && event.planId && event.planId !== state.value.plan.planId) return 'stale';
    if (state.value.plan && event.planHash && event.planHash !== state.value.plan.planHash) return 'stale';
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
    onRecovering: message => dispatch({ type: 'recovery-start', reason: message, at: now() }),
    onFailure: error => fail(error, 'offline-or-service-unavailable')
  });

  async function attachRun(
    runId: string,
    operation: AiOperationProjectionV1 | null,
    snapshot: AiSessionSnapshotV1 | null,
    initialEvents: readonly AiAgentRunEventV1[] = []
  ): Promise<void> {
    if (!currentSessionId) throw new Error('A confirmed Session is required before attaching a Plan Run.');
    dispatch({ type: 'plan-attached', runId, operation, snapshot, at: now() });
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

  async function restorePlanRun(snapshot: AiSessionSnapshotV1): Promise<void> {
    if (!snapshot.planRunId) return;
    planGeneration += 1;
    dispatch({ type: 'plan-start', clientOperationId: planOperationId ?? operationIdFactory(), generation: planGeneration, at: now() });
    await attachRun(snapshot.planRunId, null, snapshot);
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

      if (currentSessionId) {
        const session = await request(signal => api.getSession(currentSessionId!, signal));
        if (disposed || runGeneration !== ownerGeneration) return;
        acceptSession(session, project);
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
      normalized.set(question.field.toLowerCase(), Object.freeze({
        questionId: question.id,
        field: question.field.toLowerCase(),
        value,
        origin: 'user',
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
        planId: plan.planId,
        planHash: plan.planHash,
        planSnapshot: plan,
        requirementMode: state.value.requirementMode,
        confirmedAnswers: sourceAnswers,
        userSelections: session.snapshot.planQuestionSelections,
        acceptedDefaults: plan.recommendedDefaults.map(item => item.id),
        acceptedRecommendedDefaults: session.snapshot.planAcceptedRecommendedDefaults,
        answerRevision: session.snapshot.answerRevision,
        resourceRevision: 0,
        originalUserPrompt: state.value.taskDescription || plan.originalUserPrompt,
        metadataOnly: true
      }, signal));
      if (readiness.planId !== plan.planId || readiness.planHash !== plan.planHash) {
        throw new Error('Readiness response identity mismatch.');
      }
      const latestSession = state.value.session;
      if (!latestSession) return;
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

  async function reconcile(): Promise<void> {
    if (disposed || authorizationFrozen) return;
    try {
      if (state.value.run.runId) await streamAdapter.reconcile();
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

  function startNewTask(): void {
    if (disposed) return;
    planOperationId = null;
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
    if (state.value.taskDescription) await retryIntent();
    else await reconcile();
  }

  return Object.freeze({
    state: readonly(state),
    projection: computed(() => projectAiWorkbench(state.value)),
    actionModel: computed(() => aiWorkbenchActionModel(state.value)),
    start,
    submitTask,
    retryIntent,
    startPlan,
    cancelPlan,
    answerClarification,
    acceptRecommendedAnswers,
    previewReadiness,
    reconcile,
    startNewTask,
    retry,
    refresh: reconcile,
    diagnostics: ledger.diagnostics,
    dispose() {
      if (disposed) return;
      disposed = true;
      ownerGeneration += 1;
      planGeneration += 1;
      streamAdapter.dispose();
      ledger.dispose();
      dispatch({ type: 'dispose', at: now() });
    }
  });
}
