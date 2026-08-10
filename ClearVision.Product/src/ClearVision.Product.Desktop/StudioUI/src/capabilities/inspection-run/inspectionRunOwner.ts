import { readonly, reactive } from 'vue';
import { ApiAbortError, ApiHttpError, ApiNetworkError } from '@/platform/api';
import type { InspectionRunApiPort } from './realtimeApiAdapter';
import type {
  InspectionRunIdentity,
  InspectionRunProgress,
  InspectionRunResult,
  InspectionRunState,
  InspectionSseEvent
} from './contracts';
import type { InspectionSsePort } from './sseAdapter';
import {
  calculateRunConsoleStatistics,
  flattenRunDiagnostics,
  type RunConsoleResultItem,
  type RunConsoleStatistics
} from './runConsoleProjection';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner
} from '@/capabilities/project-workspace/workspaceLifecycleDiagnostics';

export type InspectionRunPhase =
  | 'idle'
  | 'hydrating'
  | 'starting'
  | 'running'
  | 'stopping'
  | 'reconnecting'
  | 'disconnected'
  | 'occupied'
  | 'faulted'
  | 'disposed';

export interface InspectionRunProjection {
  readonly projectId: string;
  readonly phase: InspectionRunPhase;
  readonly runtime: InspectionRunState | null;
  readonly latestResult: InspectionRunResult | null;
  readonly recentResults: readonly InspectionRunResult[];
  readonly statistics: RunConsoleStatistics;
  readonly progress: InspectionRunProgress | null;
  readonly errorCode: string | null;
  readonly message: string;
  readonly reconnectAttempt: number;
  readonly connected: boolean;
}

type MutableInspectionRunProjection = {
  -readonly [Key in keyof InspectionRunProjection]: InspectionRunProjection[Key]
};

export interface InspectionRunResources {
  readonly streams: number;
  readonly timers: number;
  readonly abortControllers: number;
  readonly subscriptions: number;
}

export interface InspectionRunOwner {
  readonly projectId: string;
  readonly projection: Readonly<InspectionRunProjection>;
  hydrate(): Promise<void>;
  start(identity: InspectionRunIdentity, cameraId?: string | null): Promise<boolean>;
  stop(): Promise<boolean>;
  prepareForLeave(): Promise<boolean>;
  reconcile(): Promise<void>;
  resources(): InspectionRunResources;
  settle(): Promise<void>;
  dispose(): void;
}

function consoleResult(result: InspectionRunResult): RunConsoleResultItem {
  return Object.freeze({
    id: result.resultId,
    timestamp: result.timestamp,
    outcome: result.outcome,
    defectCount: result.defectCount,
    processingTimeMs: result.processingTimeMs,
    errorMessage: result.errorMessage,
    diagnostics: Object.freeze([
      ...flattenRunDiagnostics(result.analysisData, 'analysis'),
      ...flattenRunDiagnostics(result.outputData, 'output')
    ])
  });
}

export function createInspectionRunOwner(options: {
  readonly projectId: string;
  readonly api: InspectionRunApiPort;
  readonly sse: InspectionSsePort;
  readonly retryDelaysMs?: readonly number[];
  readonly setTimer?: typeof globalThis.setTimeout;
  readonly clearTimer?: typeof globalThis.clearTimeout;
  readonly diagnostics?: WorkspaceLifecycleDiagnosticsOwner;
}): InspectionRunOwner {
  const retryDelays = options.retryDelaysMs ?? [250, 500, 1000, 2000, 5000];
  const setTimer = options.setTimer ?? globalThis.setTimeout.bind(globalThis);
  const clearTimer = options.clearTimer ?? globalThis.clearTimeout.bind(globalThis);
  const state = reactive<MutableInspectionRunProjection>({
    projectId: options.projectId,
    phase: 'idle',
    runtime: null,
    latestResult: null,
    recentResults: Object.freeze([]),
    statistics: calculateRunConsoleStatistics([]),
    progress: null,
    errorCode: null,
    message: '等待读取后端运行状态。',
    reconnectAttempt: 0,
    connected: false
  });
  let disposed = false;
  let generation = 0;
  let requestController: AbortController | null = null;
  let streamController: AbortController | null = null;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  let lastEventSequence: number | null = null;
  let activeIdentity: InspectionRunIdentity | null = null;
  let hydratePromise: Promise<void> | null = null;
  let startPromise: Promise<boolean> | null = null;
  let stopPromise: Promise<boolean> | null = null;
  let prepareForLeavePromise: Promise<boolean> | null = null;
  let preparingForLeave = false;
  const pending = new Set<Promise<unknown>>();
  const lease: WorkspaceCapabilityDiagnosticsLease | undefined = options.diagnostics?.reserveCapability(
    options.projectId,
    'inspection-run'
  );

  function syncDiagnostics(): void {
    const resources = {
      streams: streamController ? 1 : 0,
      timers: reconnectTimer == null ? 0 : 1,
      abortControllers: (requestController ? 1 : 0) + (streamController ? 1 : 0),
      subscriptions: streamController ? 1 : 0
    };
    lease?.update(Object.freeze({
      activeSubscriptions: resources.subscriptions,
      activeTimers: resources.timers,
      activeAnimationFrames: 0,
      activeObservers: 0,
      activeAbortControllers: resources.abortControllers,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: requestController ? 1 : 0,
      inFlightWrites: state.phase === 'starting' || state.phase === 'stopping' ? 1 : 0,
      inFlightPreview: 0,
      inFlightExecute: state.phase === 'running' || state.phase === 'reconnecting' ? 1 : 0
    }));
  }

  function track<T>(promise: Promise<T>): Promise<T> {
    pending.add(promise);
    promise.finally(() => pending.delete(promise)).catch(() => {});
    return promise;
  }

  function clearReconnect(): void {
    if (reconnectTimer != null) clearTimer(reconnectTimer);
    reconnectTimer = null;
  }

  function closeStream(): void {
    streamController?.abort();
    streamController = null;
    state.connected = false;
    clearReconnect();
    syncDiagnostics();
  }

  function terminal(status: string): boolean {
    return status === 'Stopped' || status === 'Faulted' || status === 'Idle';
  }

  function isBusy(status: InspectionRunState['status']): boolean {
    return status === 'Starting' || status === 'Running' || status === 'Stopping';
  }

  function phaseForRuntime(runtime: InspectionRunState): InspectionRunPhase {
    if (runtime.status === 'Faulted') return 'faulted';
    if (runtime.status === 'Stopping') return runtime.sessionType === 'ContinuousInspection' ? 'stopping' : 'occupied';
    if (runtime.isBusy && runtime.sessionType !== 'ContinuousInspection') return 'occupied';
    return runtime.isBusy ? 'running' : 'idle';
  }

  function responseCode(error: unknown): string | null {
    if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return null;
    const payload = error.payload as Record<string, unknown>;
    const code = payload.Code ?? payload.code;
    return typeof code === 'string' && code.trim() ? code.trim().toUpperCase() : null;
  }

  function shouldRecoverAuthority(error: unknown): boolean {
    return error instanceof ApiAbortError || error instanceof ApiNetworkError ||
      error instanceof ApiHttpError && (error.status === 409 || error.status >= 500);
  }

  function identityFor(runtime: InspectionRunState): InspectionRunIdentity | null {
    return runtime.isBusy && runtime.sessionType === 'ContinuousInspection' &&
      runtime.clientSnapshotId && runtime.persistenceRevision != null &&
      runtime.canonicalFlowHash && runtime.decisionConfigurationHash
      ? Object.freeze({
        projectId: options.projectId,
        clientSnapshotId: runtime.clientSnapshotId,
        expectedPersistenceRevision: runtime.persistenceRevision,
        expectedCanonicalFlowHash: runtime.canonicalFlowHash,
        expectedDecisionConfigurationHash: runtime.decisionConfigurationHash
      })
      : null;
  }

  function applyRuntime(runtime: InspectionRunState): void {
    if (runtime.projectId !== options.projectId) {
      throw new Error('Hydrated runtime project identity mismatch.');
    }
    state.runtime = runtime;
    state.phase = phaseForRuntime(runtime);
    activeIdentity = identityFor(runtime);
    state.message = runtime.isBusy
      ? runtime.sessionType === 'ContinuousInspection'
        ? '已恢复后端连续检测会话。'
        : '已读取后端运行占用状态；当前会话不属于连续检测（' +
          (runtime.sessionType ?? '未知类型') + '）。'
      : '后端确认当前工程未运行。';
  }

  function acceptsSequence(id: string | null): boolean {
    if (id == null || id === '') return true;
    const sequence = Number(id);
    if (!Number.isSafeInteger(sequence) || sequence < 0) return false;
    if (lastEventSequence != null && sequence <= lastEventSequence) return false;
    lastEventSequence = sequence;
    return true;
  }

  function projectRuntime(event: Extract<InspectionSseEvent, { type: 'stateChanged' }>['state']): void {
    const current = state.runtime;
    state.runtime = Object.freeze({
      projectId: options.projectId,
      status: event.newState,
      isBusy: isBusy(event.newState),
      sessionId: event.sessionId,
      startedAt: event.startedAt ?? current?.startedAt ?? null,
      stoppedAt: event.stoppedAt,
      clientSnapshotId: current?.clientSnapshotId ?? activeIdentity?.clientSnapshotId ?? null,
      persistenceRevision: current?.persistenceRevision ?? activeIdentity?.expectedPersistenceRevision ?? null,
      canonicalFlowHash: current?.canonicalFlowHash ?? activeIdentity?.expectedCanonicalFlowHash ?? null,
      decisionConfigurationHash: current?.decisionConfigurationHash ??
        activeIdentity?.expectedDecisionConfigurationHash ?? null,
      executionSource: current?.executionSource ?? (activeIdentity ? 'PersistedProject' : null),
      sessionType: event.sessionType ?? current?.sessionType ?? (activeIdentity ? 'ContinuousInspection' : null)
    });
  }

  function applyEvent(event: InspectionSseEvent, ownerGeneration: number): void {
    if (disposed || ownerGeneration !== generation || !acceptsSequence(event.id)) return;
    if (event.type === 'resultProduced' && event.result.projectId !== options.projectId) return;
    if (event.type === 'progressChanged' && event.progress.projectId !== options.projectId) return;
    if (event.type === 'faulted' && event.projectId !== options.projectId) return;
    if (event.type === 'stateChanged' && event.state.projectId !== options.projectId) return;
    const authoritativeSessionId = state.runtime?.sessionId;
    const eventSessionId = event.type === 'stateChanged' ? event.state.sessionId
      : event.type === 'resultProduced' ? event.result.sessionId
        : event.type === 'progressChanged' ? event.progress.sessionId
          : event.type === 'faulted' ? event.sessionId : null;
    if (eventSessionId && authoritativeSessionId && eventSessionId !== authoritativeSessionId) return;
    state.reconnectAttempt = 0;
    if (event.type === 'resultProduced') {
      const results = Object.freeze([event.result, ...state.recentResults].slice(0, 50));
      state.latestResult = event.result;
      state.recentResults = results;
      state.statistics = calculateRunConsoleStatistics(results.map(consoleResult));
    }
    if (event.type === 'progressChanged') state.progress = event.progress;
    if (event.type === 'faulted') {
      if (state.runtime) state.runtime = Object.freeze({ ...state.runtime, status: 'Faulted', isBusy: false });
      activeIdentity = null;
      state.phase = 'faulted';
      state.errorCode = 'INSPECTION_RUNTIME_FAULTED';
      state.message = event.errorMessage ?? '连续检测运行故障。';
      closeStream();
    }
    if (event.type === 'stateChanged') {
      projectRuntime(event.state);
      state.message = '连续检测状态：' + event.state.newState;
      if (event.state.newState === 'Running' || event.state.newState === 'Starting') state.phase = 'running';
      if (event.state.newState === 'Stopping') state.phase = 'stopping';
      if (terminal(event.state.newState)) {
        state.phase = event.state.newState === 'Faulted' ? 'faulted' : 'idle';
        activeIdentity = null;
        closeStream();
      }
    }
  }

  async function rereadAfterRetryExhausted(ownerGeneration: number): Promise<void> {
    if (disposed || ownerGeneration !== generation || requestController) return;
    const controller = new AbortController();
    requestController = controller;
    syncDiagnostics();
    try {
      const runtime = await options.api.hydrate(options.projectId, { signal: controller.signal });
      if (disposed || ownerGeneration !== generation) return;
      applyRuntime(runtime);
      if (runtime.isBusy && runtime.sessionType === 'ContinuousInspection') {
        state.phase = 'disconnected';
        state.errorCode = 'INSPECTION_SSE_RETRY_EXHAUSTED';
        state.message = '实时重连已达上限；后端仍确认会话运行，可手动核对后重连。';
      }
    } catch (error) {
      if (!disposed && ownerGeneration === generation && !(error instanceof ApiAbortError)) {
        state.phase = 'disconnected';
        state.errorCode = responseCode(error) ?? 'INSPECTION_AUTHORITY_REREAD_FAILED';
        state.message = '实时重连已达上限，且服务端运行状态重新读取失败。';
      }
    } finally {
      if (requestController === controller) requestController = null;
      syncDiagnostics();
    }
  }

  function scheduleReconnect(ownerGeneration: number): void {
    if (disposed || preparingForLeave || ownerGeneration !== generation || reconnectTimer != null ||
      state.phase === 'idle' || state.phase === 'faulted' || state.phase === 'occupied') return;
    if (state.reconnectAttempt >= retryDelays.length) {
      state.connected = false;
      void track(rereadAfterRetryExhausted(ownerGeneration));
      return;
    }
    const delay = retryDelays[state.reconnectAttempt] ?? 5000;
    state.phase = 'reconnecting';
    state.reconnectAttempt += 1;
    state.message = '实时连接中断，正在恢复。';
    reconnectTimer = setTimer(() => {
      reconnectTimer = null;
      connect(ownerGeneration);
    }, delay);
    syncDiagnostics();
  }

  function connect(ownerGeneration: number): void {
    if (disposed || preparingForLeave || ownerGeneration !== generation || streamController) return;
    const controller = new AbortController();
    streamController = controller;
    syncDiagnostics();
    const flight = options.sse.connect({
      projectId: options.projectId,
      lastEventId: lastEventSequence == null ? null : String(lastEventSequence),
      signal: controller.signal,
      onOpen: () => {
        if (!disposed && ownerGeneration === generation && streamController === controller) {
          state.connected = true;
          if (state.phase === 'reconnecting') {
            state.phase = state.runtime ? phaseForRuntime(state.runtime) : 'running';
          }
        }
      },
      onEvent: event => applyEvent(event, ownerGeneration)
    });
    track(flight).catch(() => {
      if (!controller.signal.aborted && !disposed && ownerGeneration === generation) {
        state.errorCode = 'INSPECTION_SSE_DISCONNECTED';
      }
    }).finally(() => {
      if (streamController === controller) streamController = null;
      state.connected = false;
      syncDiagnostics();
      if (!controller.signal.aborted) scheduleReconnect(ownerGeneration);
    });
  }

  async function performHydrate(): Promise<void> {
    if (disposed) return;
    const current = ++generation;
    closeStream();
    requestController?.abort();
    const controller = new AbortController();
    requestController = controller;
    state.phase = 'hydrating';
    state.errorCode = null;
    try {
      const runtime = await options.api.hydrate(options.projectId, { signal: controller.signal });
      if (disposed || current !== generation) return;
      applyRuntime(runtime);
      if (runtime.isBusy && runtime.sessionType === 'ContinuousInspection') connect(current);
    } catch (error) {
      if (!disposed && current === generation && !(error instanceof ApiAbortError)) {
        state.phase = 'faulted';
        state.errorCode = responseCode(error) ?? 'INSPECTION_HYDRATE_FAILED';
        state.message = error instanceof ApiHttpError && (error.status === 401 || error.status === 403)
          ? '当前会话无权读取运行状态。'
          : '无法读取后端连续检测状态。';
      }
    } finally {
      if (requestController === controller) requestController = null;
      syncDiagnostics();
    }
  }

  function hydrate(): Promise<void> {
    if (hydratePromise) return hydratePromise;
    const operation = track(performHydrate());
    const flight = operation.finally(() => {
      if (hydratePromise === flight) hydratePromise = null;
    });
    hydratePromise = flight;
    return flight;
  }

  async function performStart(identity: InspectionRunIdentity, cameraId: string | null): Promise<boolean> {
    if (disposed || identity.projectId !== options.projectId || state.runtime?.isBusy) return false;
    const current = ++generation;
    closeStream();
    requestController?.abort();
    const controller = new AbortController();
    requestController = controller;
    lastEventSequence = null;
    activeIdentity = identity;
    state.phase = 'starting';
    state.errorCode = null;
    state.latestResult = null;
    state.recentResults = Object.freeze([]);
    state.statistics = calculateRunConsoleStatistics([]);
    state.progress = null;
    state.message = '正在启动已准入的连续检测会话。';
    try {
      const result = await options.api.start(identity, cameraId, { signal: controller.signal });
      if (disposed || current !== generation) return false;
      if (result.projectId !== identity.projectId || result.clientSnapshotId !== identity.clientSnapshotId ||
        result.persistenceRevision !== identity.expectedPersistenceRevision ||
        result.canonicalFlowHash !== identity.expectedCanonicalFlowHash ||
        result.decisionConfigurationHash !== identity.expectedDecisionConfigurationHash) {
        throw new Error('Start identity mismatch.');
      }
      state.runtime = Object.freeze({
        projectId: options.projectId,
        status: 'Starting',
        isBusy: true,
        sessionId: result.sessionId,
        startedAt: null,
        stoppedAt: null,
        clientSnapshotId: identity.clientSnapshotId,
        persistenceRevision: identity.expectedPersistenceRevision,
        canonicalFlowHash: identity.expectedCanonicalFlowHash,
        decisionConfigurationHash: identity.expectedDecisionConfigurationHash,
        executionSource: 'PersistedProject',
        sessionType: result.sessionType
      });
      state.phase = 'running';
      state.message = '连续检测已启动。';
      connect(current);
      return true;
    } catch (error) {
      if (!disposed && current === generation) {
        const code = responseCode(error) ?? 'INSPECTION_START_REJECTED';
        if (shouldRecoverAuthority(error)) {
          await hydrate();
          if (disposed) return false;
          state.errorCode = code;
          if (state.runtime?.isBusy) state.message = '启动响应未确认，后端运行状态已恢复。';
          else if (state.runtime) {
            state.phase = 'faulted';
            state.message = '启动结果未知，后端未确认存在运行会话。';
          }
        } else {
          activeIdentity = null;
          state.phase = 'faulted';
          state.errorCode = code;
          state.message = error instanceof ApiHttpError && (error.status === 401 || error.status === 403)
            ? '当前会话无权启动连续检测。'
            : '连续检测启动被后端拒绝。';
        }
      }
      return false;
    } finally {
      if (requestController === controller) requestController = null;
      syncDiagnostics();
    }
  }

  function start(identity: InspectionRunIdentity, cameraId: string | null = null): Promise<boolean> {
    if (startPromise) return startPromise;
    const operation = track(performStart(identity, cameraId));
    const flight = operation.finally(() => {
      if (startPromise === flight) startPromise = null;
    });
    startPromise = flight;
    return flight;
  }

  async function performStop(): Promise<boolean> {
    if (disposed || !activeIdentity) return false;
    const stoppingIdentity = activeIdentity;
    const current = ++generation;
    closeStream();
    requestController?.abort();
    const controller = new AbortController();
    requestController = controller;
    state.phase = 'stopping';
    state.message = '正在请求停止连续检测。';
    try {
      await options.api.stop(stoppingIdentity, { signal: controller.signal });
      if (disposed || current !== generation) return false;
      await hydrate();
      if (disposed) return false;
      return true;
    } catch (error) {
      if (!disposed && current === generation) {
        const code = responseCode(error) ?? 'INSPECTION_STOP_FAILED';
        await hydrate();
        if (disposed) return false;
        state.errorCode = code;
        if (state.runtime?.isBusy) state.message = '停止结果未知，后端仍确认会话运行。';
        else if (state.runtime) state.message = '后端已确认连续检测不再运行。';
      }
      return false;
    } finally {
      if (requestController === controller) requestController = null;
      syncDiagnostics();
    }
  }

  function stop(): Promise<boolean> {
    if (stopPromise) return stopPromise;
    const operation = track(performStop());
    const flight = operation.finally(() => {
      if (stopPromise === flight) stopPromise = null;
    });
    stopPromise = flight;
    return flight;
  }

  async function settlePending(): Promise<void> {
    while (pending.size > 0) {
      await Promise.allSettled([...pending]);
    }
  }

  async function performPrepareForLeave(): Promise<boolean> {
    if (disposed) return false;
    preparingForLeave = true;
    closeStream();
    try {
      await settlePending();
      if (disposed) return false;

      const runtime = state.runtime;
      if (!runtime) {
        state.errorCode = 'INSPECTION_LEAVE_AUTHORITY_MISSING';
        state.message = '离开前未取得连续检测的服务端状态，当前禁止关闭页面。';
        return false;
      }
      if (!runtime.isBusy || runtime.sessionType !== 'ContinuousInspection') return true;
      if (!activeIdentity) {
        state.errorCode = 'INSPECTION_LEAVE_IDENTITY_MISSING';
        state.message = '连续检测仍在运行，但缺少完整执行身份；当前禁止卸载。';
        return false;
      }

      const stopped = await stop();
      if (!stopped || disposed) return false;
      await settlePending();
      const settledRuntime = state.runtime;
      if (!settledRuntime || settledRuntime.isBusy) {
        state.errorCode = 'INSPECTION_LEAVE_STILL_BUSY';
        state.message = '停止请求后后端仍确认连续检测运行，当前禁止卸载。';
        return false;
      }
      return true;
    } finally {
      preparingForLeave = false;
    }
  }

  function prepareForLeave(): Promise<boolean> {
    if (prepareForLeavePromise) return prepareForLeavePromise;
    const flight = performPrepareForLeave().finally(() => {
      if (prepareForLeavePromise === flight) prepareForLeavePromise = null;
    });
    prepareForLeavePromise = flight;
    return flight;
  }

  const owner: InspectionRunOwner = Object.freeze({
    projectId: options.projectId,
    projection: readonly(state),
    hydrate,
    start,
    stop,
    prepareForLeave,
    reconcile: hydrate,
    resources: () => Object.freeze({
      streams: streamController ? 1 : 0,
      timers: reconnectTimer == null ? 0 : 1,
      abortControllers: (requestController ? 1 : 0) + (streamController ? 1 : 0),
      subscriptions: streamController ? 1 : 0
    }),
    settle: settlePending,
    dispose: () => {
      if (disposed) return;
      disposed = true;
      generation += 1;
      activeIdentity = null;
      closeStream();
      requestController?.abort();
      requestController = null;
      hydratePromise = null;
      startPromise = null;
      stopPromise = null;
      prepareForLeavePromise = null;
      preparingForLeave = false;
      state.phase = 'disposed';
      state.message = '连续检测页面已关闭；未向服务端发送停止命令。';
      lease?.update(Object.freeze({
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
      }));
      lease?.dispose('inspection-run-owner-disposed');
    }
  });
  syncDiagnostics();
  return owner;
}
