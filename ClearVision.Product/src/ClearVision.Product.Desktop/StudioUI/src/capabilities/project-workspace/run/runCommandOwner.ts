import { reactive, readonly, type DeepReadonly } from 'vue';
import { ApiAbortError, ApiHttpError, ApiNetworkError } from '@/platform/api';
import type {
  InspectionRunApiPort,
  InspectionRunState,
  InspectionSseEvent,
  InspectionSsePort
} from '@/capabilities/inspection-run';
import type { WorkspacePersistenceOwner } from '../persistence';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import {
  type WorkspaceRunAdmissionV1,
  type WorkspaceRunIdentityV1,
  type WorkspaceRunPort,
  type WorkspaceRunReconciliationV1,
  type WorkspaceRunResultV1
} from './runContracts';

export type WorkspaceRunPhase =
  | 'idle'
  | 'hydrating'
  | 'blocked'
  | 'admitting'
  | 'executing'
  | 'occupied'
  | 'reconnecting'
  | 'disconnected'
  | 'succeeded'
  | 'failed'
  | 'cancelled'
  | 'cancel-requested'
  | 'unknown-outcome'
  | 'disposed';

export interface WorkspaceRunProjection {
  readonly phase: WorkspaceRunPhase;
  readonly projectId: string;
  readonly clientSnapshotId: string | null;
  readonly admission: WorkspaceRunAdmissionV1 | null;
  readonly runtime: InspectionRunState | null;
  readonly result: WorkspaceRunResultV1 | null;
  readonly message: string;
  readonly errorCode: string | null;
  readonly canRun: boolean;
  readonly canStop: boolean;
  readonly canReconcile: boolean;
  readonly connected: boolean;
  readonly reconnectAttempt: number;
}

type MutableWorkspaceRunProjection = {
  -readonly [Key in keyof WorkspaceRunProjection]: WorkspaceRunProjection[Key]
};

export interface WorkspaceRunCommandOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<WorkspaceRunProjection>;
  hydrate(): Promise<void>;
  refreshAdmission(): Promise<WorkspaceRunAdmissionV1 | null>;
  run(): Promise<WorkspaceRunResultV1 | null>;
  stop(): Promise<boolean>;
  reconcile(): Promise<WorkspaceRunReconciliationV1 | null>;
  reconciliationIdentity(): WorkspaceRunIdentityV1 | null;
  prepareForLeave(reason?: string): Promise<boolean>;
  settle(): Promise<void>;
  dispose(reason?: string): void;
}

function zeroResources(): WorkspaceResourceSnapshot {
  return Object.freeze({
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    activeHostSubscriptions: 0,
    inFlightReads: 0,
    inFlightWrites: 0,
    inFlightPreview: 0,
    inFlightExecute: 0
  });
}

function errorCode(error: unknown): string | null {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return null;
  const payload = error.payload as Record<string, unknown>;
  const code = payload.code ?? payload.Code;
  return typeof code === 'string' && code.trim() ? code.trim().toUpperCase() : null;
}

function messageFor(error: unknown): string {
  return error instanceof Error && error.message.trim()
    ? error.message
    : '正式运行请求失败。';
}

function terminalMessage(result: WorkspaceRunResultV1): string {
  if (result.outcome.execution === 'Cancelled') return '正式运行已由运行时取消。';
  if (result.outcome.execution !== 'Succeeded') return result.errorMessage || '正式运行失败。';
  switch (result.outcome.decision) {
    case 'Ok': return '正式运行完成：判定 OK。';
    case 'Ng': return '正式运行完成：判定 NG。';
    case 'Undetermined': return '正式运行完成：判定未确定。';
    case 'Invalid': return '正式运行完成：判定无效。';
    default: return '正式运行完成。';
  }
}

export function createWorkspaceRunCommandOwner(options: {
  readonly projectId: string;
  readonly persistenceOwner: WorkspacePersistenceOwner;
  readonly port: WorkspaceRunPort;
  readonly runtimeApi?: Pick<InspectionRunApiPort, 'hydrate'>;
  readonly sse?: InspectionSsePort;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  readonly retryDelaysMs?: readonly number[];
  readonly setTimer?: typeof globalThis.setTimeout;
  readonly clearTimer?: typeof globalThis.clearTimeout;
}): WorkspaceRunCommandOwner {
  if (options.port.projectId !== options.projectId || options.persistenceOwner.projectId !== options.projectId) {
    throw new TypeError('正式运行需要唯一的工程标识。');
  }
  const runtimeApi: Pick<InspectionRunApiPort, 'hydrate'> = options.runtimeApi ?? Object.freeze({
    async hydrate(): Promise<InspectionRunState> {
      throw new Error('正式运行的实时状态适配器尚未配置。');
    }
  });
  const sse: InspectionSsePort = options.sse ?? Object.freeze({
    async connect(_options: Parameters<InspectionSsePort['connect']>[0]): Promise<void> {
      void _options;
      throw new Error('正式运行的事件流适配器尚未配置。');
    }
  });
  const lease: WorkspaceCapabilityDiagnosticsLease = options.diagnostics.reserveRun(options.projectId);
  const state = reactive<MutableWorkspaceRunProjection>({
    phase: 'idle',
    projectId: options.projectId,
    clientSnapshotId: null,
    admission: null,
    runtime: null,
    result: null,
    message: '正式保存的工程无未处理修改时可以开始正式运行。',
    errorCode: null,
    canRun: false,
    canStop: false,
    canReconcile: false,
    connected: false,
    reconnectAttempt: 0
  });
  const retryDelays = options.retryDelaysMs ?? [250, 500, 1000, 2000, 5000];
  const setTimer = options.setTimer ?? globalThis.setTimeout.bind(globalThis);
  const clearTimer = options.clearTimer ?? globalThis.clearTimeout.bind(globalThis);
  let disposed = false;
  let operationGeneration = 0;
  let activeController: AbortController | undefined;
  let authoritySettledLocalAbort: AbortController | undefined;
  let stopController: AbortController | undefined;
  let reconcileController: AbortController | undefined;
  let hydrateController: AbortController | undefined;
  let admissionController: AbortController | undefined;
  let streamController: AbortController | undefined;
  let reconnectTimer: ReturnType<typeof setTimeout> | undefined;
  let lastEventSequence: number | null = null;
  let runtimeLockedPersistence = false;
  let runPromise: Promise<WorkspaceRunResultV1 | null> | undefined;
  let stopPromise: Promise<boolean> | undefined;
  let reconcilePromise: Promise<WorkspaceRunReconciliationV1 | null> | undefined;
  let hydratePromise: Promise<void> | undefined;
  let admissionPromise: Promise<WorkspaceRunAdmissionV1 | null> | undefined;
  const pending = new Set<Promise<unknown>>();

  function isCurrent(generation: number, clientSnapshotId: string): boolean {
    return !disposed && generation === operationGeneration && state.projectId === options.projectId &&
      options.persistenceOwner.projectId === options.projectId && state.clientSnapshotId === clientSnapshotId;
  }

  function syncDiagnostics(inFlight = false): void {
    if (disposed) return;
    const activeAbortControllers = [
      activeController,
      stopController,
      reconcileController,
      hydrateController,
      admissionController,
      streamController
    ]
      .filter(controller => controller !== undefined).length;
    lease.update(Object.freeze({
      ...zeroResources(),
      activeSubscriptions: 1 + (streamController ? 1 : 0),
      activeTimers: reconnectTimer ? 1 : 0,
      activeAbortControllers,
      inFlightExecute: inFlight ? 1 : 0
    }));
  }

  function syncAvailability(): void {
    if (disposed) return;
    state.canRun = !runPromise && !hydratePromise && !admissionPromise &&
      state.admission?.allowed !== false && options.persistenceOwner.projection.canRun &&
      (state.phase === 'idle' || state.phase === 'blocked' || state.phase === 'succeeded' ||
        state.phase === 'failed' || state.phase === 'cancelled');
    state.canStop = Boolean(!stopPromise && state.clientSnapshotId && state.admission &&
      ((runPromise && activeController && state.phase === 'executing') ||
      (state.runtime?.sessionType === 'WorkspaceFormalRun' && state.runtime.isBusy)) &&
      (state.phase === 'executing' || state.phase === 'cancel-requested' || state.phase === 'disconnected'));
    state.canReconcile = Boolean(!stopPromise && !reconcilePromise && state.clientSnapshotId && state.admission &&
      (state.phase === 'executing' || state.phase === 'cancel-requested' ||
        state.phase === 'unknown-outcome' || state.phase === 'disconnected'));
  }

  function track<T>(promise: Promise<T>): Promise<T> {
    pending.add(promise);
    promise.finally(() => pending.delete(promise)).catch(() => {});
    return promise;
  }

  function clearReconnect(): void {
    if (reconnectTimer) clearTimer(reconnectTimer);
    reconnectTimer = undefined;
  }

  function closeStream(): void {
    streamController?.abort();
    streamController = undefined;
    state.connected = false;
    clearReconnect();
  }

  function identityFromRuntime(runtime: InspectionRunState): WorkspaceRunIdentityV1 | null {
    if (runtime.sessionType !== 'WorkspaceFormalRun' || !runtime.clientSnapshotId ||
      runtime.persistenceRevision == null || !runtime.canonicalFlowHash ||
      !runtime.decisionConfigurationHash) {
      return null;
    }
    return Object.freeze({
      projectId: options.projectId,
      clientSnapshotId: runtime.clientSnapshotId,
      expectedPersistenceRevision: runtime.persistenceRevision,
      expectedCanonicalFlowHash: runtime.canonicalFlowHash,
      expectedDecisionConfigurationHash: runtime.decisionConfigurationHash
    });
  }

  function admissionFromIdentity(identity: WorkspaceRunIdentityV1): WorkspaceRunAdmissionV1 {
    return Object.freeze({
      allowed: true,
      code: null,
      message: '已从后端运行状态恢复正式运行身份。',
      projectId: identity.projectId,
      clientSnapshotId: identity.clientSnapshotId,
      persistenceRevision: identity.expectedPersistenceRevision,
      canonicalFlowHash: identity.expectedCanonicalFlowHash,
      decisionConfigurationHash: identity.expectedDecisionConfigurationHash,
      violations: Object.freeze([])
    });
  }

  function identitiesMatch(left: WorkspaceRunIdentityV1, right: WorkspaceRunIdentityV1): boolean {
    return left.projectId === right.projectId &&
      left.clientSnapshotId === right.clientSnapshotId &&
      left.expectedPersistenceRevision === right.expectedPersistenceRevision &&
      left.expectedCanonicalFlowHash === right.expectedCanonicalFlowHash &&
      left.expectedDecisionConfigurationHash === right.expectedDecisionConfigurationHash;
  }

  function acceptsSequence(id: string | null): boolean {
    if (id == null || id === '') return true;
    const sequence = Number(id);
    if (!Number.isSafeInteger(sequence) || sequence < 0) return false;
    if (lastEventSequence != null && sequence <= lastEventSequence) return false;
    lastEventSequence = sequence;
    return true;
  }

  function applyRuntimeEvent(event: InspectionSseEvent, ownerGeneration: number): void {
    if (disposed || ownerGeneration !== operationGeneration || !acceptsSequence(event.id)) return;
    if (event.type === 'heartbeat') {
      state.reconnectAttempt = 0;
      return;
    }
    const sessionId = event.type === 'stateChanged' ? event.state.sessionId
      : event.type === 'resultProduced' ? event.result.sessionId
        : event.type === 'progressChanged' ? event.progress.sessionId
          : event.sessionId;
    if (event.type === 'stateChanged' && event.state.projectId !== options.projectId) return;
    if (event.type === 'resultProduced' && event.result.projectId !== options.projectId) return;
    if (event.type === 'progressChanged' && event.progress.projectId !== options.projectId) return;
    if (event.type === 'faulted' && event.projectId !== options.projectId) return;
    if (state.runtime?.sessionId && state.runtime.sessionId !== sessionId) return;
    state.reconnectAttempt = 0;
    if (event.type === 'stateChanged' && state.runtime) {
      const busy = event.state.newState === 'Starting' || event.state.newState === 'Running' ||
        event.state.newState === 'Stopping';
      state.runtime = Object.freeze({
        ...state.runtime,
        status: event.state.newState,
        isBusy: busy,
        startedAt: event.state.startedAt ?? state.runtime.startedAt,
        stoppedAt: event.state.stoppedAt
      });
      state.phase = event.state.newState === 'Stopping' ? 'cancel-requested'
        : busy ? 'executing' : event.state.newState === 'Faulted' ? 'unknown-outcome' : state.phase;
      state.message = '正式运行状态：' + event.state.newState;
      if (!busy) {
        closeStream();
        void reconcileCurrent();
      }
    }
    if (event.type === 'resultProduced' || event.type === 'faulted') {
      closeStream();
      void reconcileCurrent();
    }
    syncAvailability();
  }

  async function rereadAfterRetryExhausted(ownerGeneration: number): Promise<void> {
    if (disposed || ownerGeneration !== operationGeneration || hydrateController) return;
    const controller = new AbortController();
    hydrateController = controller;
    try {
      const runtime = await runtimeApi.hydrate(options.projectId, { signal: controller.signal });
      if (disposed || ownerGeneration !== operationGeneration) return;
      state.runtime = runtime;
      const identity = identityFromRuntime(runtime);
      const expectedIdentity = currentIdentity();
      if (runtime.isBusy && identity && expectedIdentity && identitiesMatch(identity, expectedIdentity)) {
        state.phase = 'disconnected';
        state.errorCode = 'RUN_SSE_RETRY_EXHAUSTED';
        state.message = '实时重连已达上限；后端仍确认正式运行中，请手动核对状态。';
      } else if (!runtime.isBusy && currentIdentity()) {
        await reconcileCurrent();
      } else if (runtime.isBusy) {
        state.phase = 'unknown-outcome';
        state.errorCode = 'RUN_RUNTIME_IDENTITY_MISMATCH';
        state.message = '服务端运行状态与当前正式运行身份不一致，工作区保持锁定。';
      }
    } catch (error) {
      if (!disposed && ownerGeneration === operationGeneration && !(error instanceof ApiAbortError)) {
        state.phase = 'disconnected';
        state.errorCode = errorCode(error) ?? 'RUN_AUTHORITY_REREAD_FAILED';
        state.message = '实时重连已达上限，且服务端运行状态重新读取失败。';
      }
    } finally {
      if (hydrateController === controller) hydrateController = undefined;
      syncDiagnostics(Boolean(activeController));
      syncAvailability();
    }
  }

  function scheduleReconnect(ownerGeneration: number): void {
    if (disposed || ownerGeneration !== operationGeneration || reconnectTimer ||
      state.phase === 'succeeded' || state.phase === 'failed' || state.phase === 'cancelled') return;
    if (state.reconnectAttempt >= retryDelays.length) {
      void track(rereadAfterRetryExhausted(ownerGeneration));
      return;
    }
    const delay = retryDelays[state.reconnectAttempt] ?? 5000;
    state.phase = 'reconnecting';
    state.reconnectAttempt += 1;
    state.message = '正式运行实时连接中断，正在恢复。';
    reconnectTimer = setTimer(() => {
      reconnectTimer = undefined;
      connect(ownerGeneration);
    }, delay);
    syncDiagnostics(Boolean(activeController));
  }

  function connect(ownerGeneration: number): void {
    if (disposed || ownerGeneration !== operationGeneration || streamController) return;
    const controller = new AbortController();
    streamController = controller;
    syncDiagnostics(Boolean(activeController));
    const flight = sse.connect({
      projectId: options.projectId,
      lastEventId: lastEventSequence == null ? null : String(lastEventSequence),
      signal: controller.signal,
      onOpen: () => {
        if (!disposed && ownerGeneration === operationGeneration && streamController === controller) {
          state.connected = true;
          if (state.phase === 'reconnecting') state.phase = 'executing';
        }
      },
      onEvent: event => applyRuntimeEvent(event, ownerGeneration)
    });
    track(flight).catch(() => {
      if (!controller.signal.aborted && !disposed && ownerGeneration === operationGeneration) {
        state.errorCode = 'RUN_SSE_DISCONNECTED';
      }
    }).finally(() => {
      if (streamController === controller) streamController = undefined;
      state.connected = false;
      syncDiagnostics(Boolean(activeController));
      if (!controller.signal.aborted) scheduleReconnect(ownerGeneration);
    });
  }

  async function performAdmissionRefresh(): Promise<WorkspaceRunAdmissionV1 | null> {
    if (disposed || state.runtime?.isBusy) return null;
    if (!options.persistenceOwner.projection.canRun) {
      state.phase = 'blocked';
      state.admission = null;
      state.errorCode = 'RUN_PERSISTENCE_GATE';
      state.message = '存在未保存参数或待协调保存状态，正式运行准入未发起。';
      syncAvailability();
      return null;
    }
    admissionController?.abort();
    const controller = new AbortController();
    admissionController = controller;
    const clientSnapshotId = globalThis.crypto.randomUUID();
    state.phase = 'admitting';
    state.clientSnapshotId = clientSnapshotId;
    state.admission = null;
    state.errorCode = null;
    state.message = '正在读取已保存工程的正式运行准入。';
    syncDiagnostics();
    syncAvailability();
    try {
      const admission = await options.port.admit({
        projectId: options.projectId,
        clientSnapshotId,
        expectedPersistenceRevision: options.persistenceOwner.projection.persistenceRevision
      }, { signal: controller.signal });
      if (disposed || admissionController !== controller) return null;
      state.admission = admission;
      state.phase = admission.allowed ? 'idle' : 'blocked';
      state.errorCode = admission.allowed ? null : admission.code;
      state.message = admission.message;
      return admission;
    } catch (error) {
      if (disposed || controller.signal.aborted || error instanceof ApiAbortError) return null;
      state.phase = 'blocked';
      state.errorCode = errorCode(error) ?? 'RUN_ADMISSION_FAILED';
      state.message = error instanceof ApiHttpError && (error.status === 401 || error.status === 403)
        ? '当前会话无权读取正式运行准入。'
        : '正式运行准入读取失败。';
      if (error instanceof ApiHttpError && error.status === 409) await performHydrate();
      return null;
    } finally {
      if (admissionController === controller) admissionController = undefined;
      syncDiagnostics();
      syncAvailability();
    }
  }

  function refreshAdmission(): Promise<WorkspaceRunAdmissionV1 | null> {
    if (admissionPromise) return admissionPromise;
    const operation = track(performAdmissionRefresh());
    const flight = operation.finally(() => {
      if (admissionPromise === flight) admissionPromise = undefined;
      syncAvailability();
    });
    admissionPromise = flight;
    syncAvailability();
    return flight;
  }

  async function performHydrate(): Promise<void> {
    if (disposed) return;
    const generation = ++operationGeneration;
    closeStream();
    hydrateController?.abort();
    const controller = new AbortController();
    hydrateController = controller;
    state.phase = 'hydrating';
    state.errorCode = null;
    state.message = '正在读取服务端运行状态。';
    syncDiagnostics();
    syncAvailability();
    try {
      const runtime = await runtimeApi.hydrate(options.projectId, { signal: controller.signal });
      if (disposed || generation !== operationGeneration) return;
      const expectedIdentity = currentIdentity();
      state.runtime = runtime;
      const identity = identityFromRuntime(runtime);
      if (identity && expectedIdentity && !identitiesMatch(identity, expectedIdentity)) {
        if (options.persistenceOwner.projection.canRun) {
          runtimeLockedPersistence = options.persistenceOwner.setRunning(
            '服务端运行身份与当前正式运行身份不一致。'
          );
        }
        state.phase = 'unknown-outcome';
        state.errorCode = 'RUN_RUNTIME_IDENTITY_MISMATCH';
        state.message = '服务端运行状态与当前正式运行身份不一致，工作区保持锁定。';
        return;
      }
      if (runtime.isBusy && identity) {
        state.clientSnapshotId = identity.clientSnapshotId;
        state.admission = admissionFromIdentity(identity);
        state.result = null;
        if (options.persistenceOwner.projection.canRun) {
          runtimeLockedPersistence = options.persistenceOwner.setRunning('后端正式运行会话已恢复。');
        }
        state.phase = runtime.status === 'Stopping' ? 'cancel-requested' : 'executing';
        state.message = '已恢复后端正式运行会话。';
        lastEventSequence = null;
        connect(generation);
        return;
      }
      if (runtime.isBusy) {
        state.clientSnapshotId = null;
        state.admission = null;
        state.result = null;
        if (options.persistenceOwner.projection.canRun) {
          runtimeLockedPersistence = options.persistenceOwner.setRunning('工程由其他运行会话占用。');
        }
        state.phase = 'occupied';
        state.errorCode = 'ADMISSION_RUNTIME_ALREADY_ACTIVE';
        state.message = '工程由' + (runtime.sessionType ?? '其他运行会话') + '占用；当前页面不会启动第二个正式运行。';
        return;
      }
      if (identity) {
        state.clientSnapshotId = identity.clientSnapshotId;
        state.admission = admissionFromIdentity(identity);
        if (options.persistenceOwner.projection.canRun) {
          runtimeLockedPersistence = options.persistenceOwner.setRunning('正在核对已结束的正式运行。');
        }
        state.phase = 'unknown-outcome';
        await reconcileCurrent();
        return;
      }
      if (runtimeLockedPersistence) {
        options.persistenceOwner.clearRunning('后端确认工程未运行。');
        runtimeLockedPersistence = false;
      }
      state.phase = 'idle';
      state.message = '后端确认工程未运行。';
      await refreshAdmission();
    } catch (error) {
      if (!disposed && generation === operationGeneration && !(error instanceof ApiAbortError)) {
        state.phase = 'blocked';
        state.errorCode = errorCode(error) ?? 'RUN_HYDRATE_FAILED';
        state.message = error instanceof ApiHttpError && (error.status === 401 || error.status === 403)
          ? '当前会话无权读取工程运行状态。'
          : '无法读取工程的服务端运行状态。';
      }
    } finally {
      if (hydrateController === controller) hydrateController = undefined;
      syncDiagnostics(Boolean(activeController));
      syncAvailability();
    }
  }

  function hydrate(): Promise<void> {
    if (hydratePromise) return hydratePromise;
    const operation = track(performHydrate());
    const flight = operation.finally(() => {
      if (hydratePromise === flight) hydratePromise = undefined;
      syncAvailability();
    });
    hydratePromise = flight;
    syncAvailability();
    return flight;
  }

  function currentIdentity(): WorkspaceRunIdentityV1 | null {
    const admission = state.admission;
    if (!state.clientSnapshotId || !admission || admission.persistenceRevision == null ||
      !admission.canonicalFlowHash || !admission.decisionConfigurationHash) {
      return null;
    }
    return {
      projectId: options.projectId,
      clientSnapshotId: state.clientSnapshotId,
      expectedPersistenceRevision: admission.persistenceRevision,
      expectedCanonicalFlowHash: admission.canonicalFlowHash,
      expectedDecisionConfigurationHash: admission.decisionConfigurationHash
    };
  }

  function reconciliationMatchesIdentity(reconciliation: WorkspaceRunReconciliationV1, identity: WorkspaceRunIdentityV1): boolean {
    return identitiesMatch({
      projectId: reconciliation.projectId,
      clientSnapshotId: reconciliation.clientSnapshotId,
      expectedPersistenceRevision: reconciliation.persistenceRevision,
      expectedCanonicalFlowHash: reconciliation.canonicalFlowHash,
      expectedDecisionConfigurationHash: reconciliation.decisionConfigurationHash
    }, identity);
  }

  function resultMatchesIdentity(result: WorkspaceRunResultV1, identity: WorkspaceRunIdentityV1): boolean {
    return result.projectId === identity.projectId &&
      result.executionSnapshotId === identity.clientSnapshotId &&
      result.persistenceRevision === identity.expectedPersistenceRevision &&
      result.flowHash === identity.expectedCanonicalFlowHash &&
      result.decisionConfigurationHash === identity.expectedDecisionConfigurationHash;
  }

  function cancelAdmission(reason: string): boolean {
    if (disposed || state.phase !== 'admitting' || !activeController) return false;
    const controller = activeController;
    operationGeneration += 1;
    activeController = undefined;
    authoritySettledLocalAbort = undefined;
    runPromise = undefined;
    state.clientSnapshotId = null;
    state.admission = null;
    state.result = null;
    state.errorCode = 'RUN_ADMISSION_CANCELLED';
    void reason;
    state.message = '已在本机取消正式运行准入；后端未创建运行会话。';
    controller.abort();
    options.persistenceOwner.clearRunning(state.message);
    state.phase = options.persistenceOwner.projection.canRun ? 'idle' : 'blocked';
    syncDiagnostics(false);
    syncAvailability();
    return true;
  }

  function markRuntimeTerminal(status: InspectionRunState['status']): void {
    if (state.runtime) {
      state.runtime = Object.freeze({
        ...state.runtime,
        status,
        isBusy: false,
        stoppedAt: state.runtime.stoppedAt ?? new Date().toISOString()
      });
    }
    runtimeLockedPersistence = false;
    closeStream();
  }

  function applyReconciliation(
    reconciliation: WorkspaceRunReconciliationV1,
    identity: WorkspaceRunIdentityV1,
    generation: number
  ): void {
    if (!isCurrent(generation, identity.clientSnapshotId)) return;
    if (!reconciliationMatchesIdentity(reconciliation, identity)) {
      state.phase = 'unknown-outcome';
      state.errorCode = 'RUN_RECONCILE_IDENTITY_MISMATCH';
      state.message = '核对响应与本次正式运行身份不一致；工作区保持锁定。';
      syncAvailability();
      return;
    }
    if (reconciliation.result && !resultMatchesIdentity(reconciliation.result, identity)) {
      state.phase = 'unknown-outcome';
      state.errorCode = 'RUN_RECONCILE_RESULT_IDENTITY_MISMATCH';
      state.message = '核对结果与本次正式运行身份不一致；工作区保持锁定。';
      syncAvailability();
      return;
    }

    switch (reconciliation.status) {
      case 'still-running':
        state.phase = state.phase === 'cancel-requested' ? 'cancel-requested' : 'executing';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        break;
      case 'cancel-requested':
        state.phase = 'cancel-requested';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        break;
      case 'cancelled':
        state.result = reconciliation.result;
        state.phase = 'cancelled';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        options.persistenceOwner.clearRunning(state.message);
        markRuntimeTerminal('Stopped');
        break;
      case 'succeeded':
        if (!reconciliation.result) {
          state.phase = 'unknown-outcome';
          state.errorCode = 'RUN_RECONCILE_RESULT_MISSING';
          state.message = '后端确认运行成功，但未返回正式结果；工作区保持锁定。';
          break;
        }
        state.result = reconciliation.result;
        state.phase = 'succeeded';
        state.errorCode = reconciliation.code;
        state.message = terminalMessage(reconciliation.result);
        options.persistenceOwner.clearRunning(state.message);
        markRuntimeTerminal('Stopped');
        break;
      case 'failed':
        state.result = reconciliation.result;
        state.phase = 'failed';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        options.persistenceOwner.clearRunning(state.message);
        markRuntimeTerminal('Faulted');
        break;
      case 'result-not-found':
      case 'identity-mismatch':
        state.phase = 'unknown-outcome';
        state.errorCode = reconciliation.code ?? `RUN_${reconciliation.status.toUpperCase().replace('-', '_')}`;
        state.message = `${reconciliation.message} 工作区保持锁定。`;
        break;
    }
    syncAvailability();
  }

  async function recoverExecuteAuthority(
    identity: WorkspaceRunIdentityV1,
    generation: number
  ): Promise<void> {
    if (!isCurrent(generation, identity.clientSnapshotId)) return;
    hydrateController?.abort();
    const controller = new AbortController();
    hydrateController = controller;
    syncDiagnostics(Boolean(activeController));
    try {
      const runtime = await runtimeApi.hydrate(options.projectId, { signal: controller.signal });
      if (!isCurrent(generation, identity.clientSnapshotId)) return;
      state.runtime = runtime;
      const runtimeIdentity = identityFromRuntime(runtime);
      if (runtime.isBusy) {
        if (!runtimeIdentity || !identitiesMatch(runtimeIdentity, identity)) {
          state.phase = 'unknown-outcome';
          state.errorCode = 'RUN_RUNTIME_IDENTITY_MISMATCH';
          state.message = '启动响应未知，且后端运行身份与本次准入不一致；工作区保持锁定。';
          return;
        }
        state.phase = runtime.status === 'Stopping' ? 'cancel-requested' : 'executing';
        state.message = '执行响应未确认，已恢复后端正式运行会话。';
        lastEventSequence = null;
        connect(generation);
        return;
      }
      await reconcileCurrent();
    } catch {
      // The prior unknown outcome remains locked until an explicit reconcile succeeds.
    } finally {
      if (hydrateController === controller) hydrateController = undefined;
      syncDiagnostics(Boolean(activeController));
      syncAvailability();
    }
  }

  async function performRun(): Promise<WorkspaceRunResultV1 | null> {
    if (disposed) return null;
    if (!options.persistenceOwner.projection.canRun) {
      state.phase = 'blocked';
      state.errorCode = 'RUN_PERSISTENCE_GATE';
      state.message = '请先保存、核对或解决当前工程状态，再开始正式运行。';
      syncAvailability();
      return null;
    }
    if (!options.persistenceOwner.setRunning('正在校验正式运行准入条件。')) {
      state.phase = 'blocked';
      state.errorCode = 'RUN_MUTATION_GATE';
      state.message = '当前工作区修改状态不允许开始正式运行。';
      syncAvailability();
      return null;
    }

    const generation = ++operationGeneration;
    const clientSnapshotId = globalThis.crypto.randomUUID();
    const controller = new AbortController();
    activeController = controller;
    state.clientSnapshotId = clientSnapshotId;
    state.admission = null;
    state.result = null;
    state.phase = 'admitting';
    state.errorCode = null;
    state.message = '正在校验用于正式运行的已保存工程快照。';
    syncDiagnostics(true);
    syncAvailability();

    try {
      const admission = await options.port.admit({
        projectId: options.projectId,
        clientSnapshotId,
        expectedPersistenceRevision: options.persistenceOwner.projection.persistenceRevision
      }, { signal: controller.signal });
      if (!isCurrent(generation, clientSnapshotId)) return null;
      state.admission = admission;
      if (!admission.allowed) {
        state.phase = 'blocked';
        state.errorCode = admission.code;
        state.message = admission.message;
        options.persistenceOwner.clearRunning('正式运行准入被拒绝。');
        return null;
      }
      if (admission.projectId !== options.projectId || admission.clientSnapshotId !== clientSnapshotId ||
        admission.persistenceRevision !== options.persistenceOwner.projection.persistenceRevision ||
        !admission.canonicalFlowHash || !admission.decisionConfigurationHash) {
        state.phase = 'blocked';
        state.errorCode = 'ADMISSION_IDENTITY_INVALID';
        state.message = '准入响应未返回当前已保存工程的完整身份。';
        options.persistenceOwner.clearRunning('正式运行准入身份校验未通过。');
        return null;
      }

      state.phase = 'executing';
      state.message = '正在执行已通过准入校验的正式工程快照。';
      syncAvailability();
      const result = await options.port.execute({
        projectId: options.projectId,
        clientSnapshotId,
        expectedPersistenceRevision: admission.persistenceRevision,
        expectedCanonicalFlowHash: admission.canonicalFlowHash,
        expectedDecisionConfigurationHash: admission.decisionConfigurationHash
      }, { signal: controller.signal });
      if (!isCurrent(generation, clientSnapshotId)) return null;
      if (result.projectId !== options.projectId || result.executionSnapshotId !== clientSnapshotId ||
        result.persistenceRevision !== admission.persistenceRevision || result.flowHash !== admission.canonicalFlowHash ||
        result.decisionConfigurationHash !== admission.decisionConfigurationHash) {
        state.phase = 'unknown-outcome';
        state.errorCode = 'RUN_RESULT_IDENTITY_MISMATCH';
        state.message = '执行响应与已通过准入校验的工程快照不一致。';
        return null;
      }
      state.result = result;
      state.phase = result.outcome.execution === 'Cancelled'
        ? 'cancelled'
        : result.outcome.execution === 'Succeeded' ? 'succeeded' : 'failed';
      state.message = terminalMessage(result);
      options.persistenceOwner.clearRunning(state.message);
      markRuntimeTerminal(
        result.outcome.execution === 'Succeeded' || result.outcome.execution === 'Cancelled'
          ? 'Stopped'
          : 'Faulted'
      );
      return result;
    } catch (error) {
      if (!isCurrent(generation, clientSnapshotId)) return null;
      if (state.phase === 'cancelled' || state.phase === 'succeeded' || state.phase === 'failed') {
        return state.result;
      }
      if (authoritySettledLocalAbort === controller) {
        authoritySettledLocalAbort = undefined;
        return null;
      }
      state.errorCode = errorCode(error) ?? (error instanceof ApiAbortError ? 'RUN_CANCELLED' : 'RUN_NETWORK_FAILURE');
      if (error instanceof ApiAbortError) {
        state.phase = 'unknown-outcome';
        state.message = '已请求取消正式运行，但服务端结果未知；工作区保持锁定。';
      } else if (error instanceof ApiNetworkError) {
        state.phase = 'unknown-outcome';
        state.message = '正式运行的网络结果未知；工作区保持锁定。';
      } else if (error instanceof ApiHttpError) {
        if (error.status === 401 || error.status === 403) {
          state.phase = 'blocked';
          state.message = '当前会话无权执行正式运行；后端未创建本次会话。';
          options.persistenceOwner.clearRunning(state.message);
        } else {
          state.phase = 'unknown-outcome';
          state.message = '正式运行响应不确定，正在读取服务端状态。';
        }
      } else {
        state.phase = 'failed';
        state.message = messageFor(error);
        options.persistenceOwner.clearRunning(state.message);
      }
      const identity = currentIdentity();
      if (identity && state.phase === 'unknown-outcome') {
        await recoverExecuteAuthority(identity, generation);
      }
      return null;
    } finally {
      if (isCurrent(generation, clientSnapshotId)) {
        activeController = undefined;
        if (authoritySettledLocalAbort === controller) authoritySettledLocalAbort = undefined;
        syncDiagnostics(false);
        syncAvailability();
      }
    }
  }

  async function stopCurrent(): Promise<boolean> {
    if (stopPromise) return stopPromise;
    if (disposed || !state.canStop) return false;

    const generation = operationGeneration;
    const clientSnapshotId = state.clientSnapshotId;
    if (!clientSnapshotId) return false;
    const identity = currentIdentity();
    if (!identity) {
      state.phase = 'unknown-outcome';
      state.errorCode = 'RUN_IDENTITY_UNAVAILABLE';
      state.message = '正式运行身份不完整；工作区保持锁定。';
      activeController?.abort();
      syncAvailability();
      return true;
    }

    state.phase = 'cancel-requested';
    state.message = '正在通过后端运行协调器停止正式运行。';
    syncAvailability();
    const controller = new AbortController();
    stopController = controller;
    syncDiagnostics(Boolean(activeController));
    const operation = track((async () => {
      try {
        const reconciliation = await options.port.stop(identity, { signal: controller.signal });
        applyReconciliation(reconciliation, identity, generation);
        if (isCurrent(generation, clientSnapshotId) && activeController) {
          // The authoritative stop has settled the server-side run. Abort only
          // the local execute response wait so the owner can release its flight.
          authoritySettledLocalAbort = activeController;
          activeController.abort();
        }
        return true;
      } catch (error) {
        if (isCurrent(generation, clientSnapshotId)) {
          state.phase = 'unknown-outcome';
          state.errorCode = errorCode(error) ?? 'RUN_STOP_NETWORK_FAILURE';
          state.message = '停止请求的结果未知；离开前请核对后端正式运行状态。';
          // The server-side run is no longer tied to this HTTP request. Abort
          // only the local response wait so dispose cannot be mistaken for stop.
          activeController?.abort();
          syncDiagnostics(false);
          syncAvailability();
          await reconcileCurrent();
        }
        return true;
      }
    })());
    stopPromise = operation.finally(() => {
      stopPromise = undefined;
      if (stopController === controller) stopController = undefined;
      syncDiagnostics(Boolean(activeController));
      syncAvailability();
    });
    syncAvailability();
    return stopPromise;
  }

  async function reconcileCurrent(): Promise<WorkspaceRunReconciliationV1 | null> {
    if (reconcilePromise) return reconcilePromise;
    if (disposed) return null;
    const identity = currentIdentity();
    const clientSnapshotId = state.clientSnapshotId;
    if (!identity || !clientSnapshotId) return null;
    const generation = operationGeneration;
    state.message = '正在根据后端运行状态与结果库核对正式运行。';
    syncAvailability();
    const controller = new AbortController();
    reconcileController = controller;
    syncDiagnostics(Boolean(activeController));
    const operation = track((async () => {
      try {
        const reconciliation = await options.port.reconcile(identity, { signal: controller.signal });
        if (!isCurrent(generation, clientSnapshotId)) return null;
        applyReconciliation(reconciliation, identity, generation);
        return reconciliation;
      } catch (error) {
        if (isCurrent(generation, clientSnapshotId)) {
          state.phase = 'unknown-outcome';
          state.errorCode = errorCode(error) ?? 'RUN_RECONCILE_NETWORK_FAILURE';
          state.message = '尚未获得后端确认时核对失败；工作区保持锁定。';
          syncAvailability();
        }
        return null;
      }
    })());
    reconcilePromise = operation.finally(() => {
      reconcilePromise = undefined;
      if (reconcileController === controller) reconcileController = undefined;
      syncDiagnostics(Boolean(activeController));
      syncAvailability();
    });
    syncAvailability();
    return reconcilePromise;
  }

  async function prepareRunForLeave(reason = 'route-leave'): Promise<boolean> {
    void reason;
    if (disposed) return true;
    if (state.phase === 'admitting') {
      if (activeController) {
        cancelAdmission(reason);
        return true;
      }
      if (admissionPromise) {
        await admissionPromise;
        return disposed || state.phase !== 'admitting';
      }
      return false;
    }
    if (state.phase === 'executing') {
      state.message = '正式运行仍由后端执行；离开页面不会隐式停止会话。';
      syncAvailability();
      return false;
    }
    if (state.phase === 'cancel-requested' || state.phase === 'unknown-outcome') {
      const reconciliation = await reconcileCurrent();
      return Boolean(reconciliation && (
        reconciliation.status === 'cancelled' ||
        reconciliation.status === 'succeeded' ||
        reconciliation.status === 'failed'
      ));
    }
    return state.phase !== 'disposed';
  }

  const owner: WorkspaceRunCommandOwner = Object.freeze({
    projectId: options.projectId,
    projection: readonly(state),
    hydrate(): Promise<void> {
      return hydrate();
    },
    refreshAdmission(): Promise<WorkspaceRunAdmissionV1 | null> {
      return refreshAdmission();
    },
    run(): Promise<WorkspaceRunResultV1 | null> {
      if (runPromise) return runPromise;
      const operation = track(performRun());
      const flight = operation.finally(() => {
        if (runPromise === flight) {
          runPromise = undefined;
          syncAvailability();
        }
      });
      runPromise = flight;
      syncAvailability();
      return runPromise;
    },
    stop(): Promise<boolean> {
      return stopCurrent();
    },
    reconcile(): Promise<WorkspaceRunReconciliationV1 | null> {
      return reconcileCurrent();
    },
    reconciliationIdentity(): WorkspaceRunIdentityV1 | null {
      return currentIdentity();
    },
    prepareForLeave(reason = 'route-leave'): Promise<boolean> {
      return prepareRunForLeave(reason);
    },
    async settle(): Promise<void> {
      await Promise.allSettled([...pending]);
    },
    dispose(reason = 'workspace-run-disposed'): void {
      if (disposed) return;
      if (state.phase === 'admitting') cancelAdmission(reason);
      disposed = true;
      operationGeneration += 1;
      clearReconnect();
      streamController?.abort();
      hydrateController?.abort();
      admissionController?.abort();
      activeController?.abort();
      stopController?.abort();
      reconcileController?.abort();
      activeController = undefined;
      stopController = undefined;
      reconcileController = undefined;
      streamController = undefined;
      hydrateController = undefined;
      admissionController = undefined;
      authoritySettledLocalAbort = undefined;
      state.phase = 'disposed';
      state.canRun = false;
      state.canStop = false;
      state.canReconcile = false;
      state.connected = false;
      lease.update(zeroResources());
      lease.dispose(reason);
    }
  });
  syncDiagnostics();
  syncAvailability();
  return owner;
}
