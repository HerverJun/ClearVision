import { readonly, reactive } from 'vue';
import { ApiAbortError, ApiHttpError, ApiNetworkError } from '@/platform/api';
import type { InspectionRunApiPort } from './realtimeApiAdapter';
import type { InspectionRunIdentity, InspectionRunResult, InspectionRunState, InspectionSseEvent } from './contracts';
import type { InspectionSsePort } from './sseAdapter';

export type InspectionRunPhase = 'idle' | 'hydrating' | 'starting' | 'running' | 'stopping' | 'reconnecting' | 'occupied' | 'faulted' | 'disposed';
export interface InspectionRunProjection {
  readonly projectId: string;
  readonly phase: InspectionRunPhase;
  readonly runtime: InspectionRunState | null;
  readonly latestResult: InspectionRunResult | null;
  readonly errorCode: string | null;
  readonly message: string;
  readonly reconnectAttempt: number;
  readonly connected: boolean;
}
type MutableInspectionRunProjection = { -readonly [Key in keyof InspectionRunProjection]: InspectionRunProjection[Key] };
export interface InspectionRunResources { readonly streams: number; readonly timers: number; readonly abortControllers: number; readonly subscriptions: number }
export interface InspectionRunOwner {
  readonly projectId: string;
  readonly projection: Readonly<InspectionRunProjection>;
  hydrate(): Promise<void>;
  start(identity: InspectionRunIdentity, cameraId?: string | null): Promise<boolean>;
  stop(): Promise<boolean>;
  reconcile(): Promise<void>;
  resources(): InspectionRunResources;
  settle(): Promise<void>;
  dispose(): void;
}

export function createInspectionRunOwner(options: {
  readonly projectId: string;
  readonly api: InspectionRunApiPort;
  readonly sse: InspectionSsePort;
  readonly retryDelaysMs?: readonly number[];
  readonly setTimer?: typeof globalThis.setTimeout;
  readonly clearTimer?: typeof globalThis.clearTimeout;
}): InspectionRunOwner {
  const retryDelays = options.retryDelaysMs ?? [250, 500, 1000, 2000, 5000];
  const setTimer = options.setTimer ?? globalThis.setTimeout.bind(globalThis);
  const clearTimer = options.clearTimer ?? globalThis.clearTimeout.bind(globalThis);
  const state = reactive<MutableInspectionRunProjection>({ projectId: options.projectId, phase: 'idle', runtime: null,
    latestResult: null, errorCode: null, message: '等待加载连续检测状态。', reconnectAttempt: 0, connected: false });
  let disposed = false;
  let generation = 0;
  let requestController: AbortController | null = null;
  let streamController: AbortController | null = null;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  let lastEventId: string | null = null;
  let activeIdentity: InspectionRunIdentity | null = null;
  const pending = new Set<Promise<unknown>>();

  function track<T>(promise: Promise<T>): Promise<T> {
    pending.add(promise); promise.finally(() => pending.delete(promise)).catch(() => {}); return promise;
  }
  function clearReconnect(): void { if (reconnectTimer != null) clearTimer(reconnectTimer); reconnectTimer = null; }
  function closeStream(): void { streamController?.abort(); streamController = null; state.connected = false; clearReconnect(); }
  function terminal(status: string): boolean { return status === 'Stopped' || status === 'Faulted' || status === 'Idle'; }
  function isBusy(status: InspectionRunState['status']): boolean {
    return status === 'Starting' || status === 'Running' || status === 'Stopping';
  }
  function phaseForRuntime(runtime: InspectionRunState): InspectionRunPhase {
    if (runtime.status === 'Faulted') return 'faulted';
    if (runtime.status === 'Stopping') return 'stopping';
    if (runtime.isBusy && runtime.sessionType !== 'ContinuousInspection') return 'occupied';
    return runtime.isBusy ? 'running' : 'idle';
  }
  function errorCode(error: unknown): string | null {
    if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return null;
    const payload = error.payload as Record<string, unknown>;
    const code = payload.Code ?? payload.code;
    return typeof code === 'string' && code.trim() ? code : null;
  }
  function shouldRecoverAuthority(error: unknown): boolean {
    return error instanceof ApiAbortError || error instanceof ApiNetworkError ||
      error instanceof ApiHttpError && (error.status === 409 && errorCode(error) === null || error.status >= 500);
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
      decisionConfigurationHash: current?.decisionConfigurationHash ?? activeIdentity?.expectedDecisionConfigurationHash ?? null,
      executionSource: current?.executionSource ?? (activeIdentity ? 'PersistedProject' : null),
      sessionType: event.sessionType ?? current?.sessionType ?? (activeIdentity ? 'ContinuousInspection' : null)
    });
  }
  function applyEvent(event: InspectionSseEvent): void {
    if (disposed) return;
    if (event.type === 'resultProduced' && event.result.projectId !== options.projectId) return;
    if (event.type === 'faulted' && event.projectId !== options.projectId) return;
    if (event.type === 'stateChanged' && event.state.projectId !== options.projectId) return;
    const authoritativeSessionId = state.runtime?.sessionId;
    const eventSessionId = event.type === 'stateChanged' ? event.state.sessionId :
      event.type === 'resultProduced' ? event.result.sessionId : event.type === 'faulted' ? event.sessionId : null;
    if (eventSessionId && authoritativeSessionId && eventSessionId !== authoritativeSessionId) return;
    if (event.id) lastEventId = event.id;
    state.reconnectAttempt = 0;
    if (event.type === 'resultProduced') state.latestResult = event.result;
    if (event.type === 'faulted') {
      if (state.runtime) state.runtime = Object.freeze({ ...state.runtime, status: 'Faulted', isBusy: false });
      activeIdentity = null; state.phase = 'faulted'; state.errorCode = 'INSPECTION_RUNTIME_FAULTED';
      state.message = event.errorMessage ?? '连续检测运行故障。'; closeStream();
    }
    if (event.type === 'stateChanged') {
      projectRuntime(event.state);
      state.message = `连续检测状态：${event.state.newState}`;
      if (event.state.newState === 'Running' || event.state.newState === 'Starting') state.phase = 'running';
      if (event.state.newState === 'Stopping') state.phase = 'stopping';
      if (terminal(event.state.newState)) { state.phase = event.state.newState === 'Faulted' ? 'faulted' : 'idle'; activeIdentity = null; closeStream(); }
    }
  }
  function scheduleReconnect(ownerGeneration: number): void {
    if (disposed || ownerGeneration !== generation || reconnectTimer != null || state.phase === 'idle' || state.phase === 'faulted') return;
    const delay = retryDelays[Math.min(state.reconnectAttempt, retryDelays.length - 1)] ?? 5000;
    state.phase = 'reconnecting'; state.reconnectAttempt += 1; state.message = '实时连接中断，正在恢复。';
    reconnectTimer = setTimer(() => { reconnectTimer = null; connect(ownerGeneration); }, delay);
  }
  function connect(ownerGeneration: number): void {
    if (disposed || ownerGeneration !== generation || streamController) return;
    const controller = new AbortController(); streamController = controller;
    const flight = options.sse.connect({ projectId: options.projectId, lastEventId, signal: controller.signal,
      onOpen: () => { if (!disposed && ownerGeneration === generation) { state.connected = true; if (state.phase === 'reconnecting') state.phase = state.runtime ? phaseForRuntime(state.runtime) : 'running'; } },
      onEvent: applyEvent });
    track(flight).catch(() => {
      if (!controller.signal.aborted && !disposed) { state.errorCode = 'INSPECTION_SSE_DISCONNECTED'; scheduleReconnect(ownerGeneration); }
    }).finally(() => {
      if (streamController === controller) streamController = null;
      state.connected = false;
      if (!controller.signal.aborted) scheduleReconnect(ownerGeneration);
    });
  }
  async function hydrate(): Promise<void> {
    if (disposed) return;
    const current = ++generation; closeStream(); requestController?.abort(); requestController = new AbortController();
    state.phase = 'hydrating'; state.errorCode = null;
    try {
      const runtime = await options.api.hydrate(options.projectId, { signal: requestController.signal });
      if (disposed || current !== generation) return;
      if (runtime.projectId !== options.projectId) throw new Error('Hydrated runtime project identity mismatch.');
      state.runtime = runtime; state.phase = phaseForRuntime(runtime);
      activeIdentity = runtime.isBusy && runtime.sessionType === 'ContinuousInspection' &&
        runtime.clientSnapshotId && runtime.persistenceRevision != null &&
        runtime.canonicalFlowHash && runtime.decisionConfigurationHash ? {
          projectId: options.projectId,
          clientSnapshotId: runtime.clientSnapshotId,
          expectedPersistenceRevision: runtime.persistenceRevision,
          expectedCanonicalFlowHash: runtime.canonicalFlowHash,
          expectedDecisionConfigurationHash: runtime.decisionConfigurationHash
        } : null;
      state.message = runtime.isBusy
        ? runtime.sessionType === 'ContinuousInspection'
          ? '已恢复后端连续检测运行状态。'
          : '已读取后端运行占用状态；当前会话不属于连续检测。'
        : '连续检测未运行。';
      if (runtime.isBusy && runtime.sessionType === 'ContinuousInspection') connect(current);
    } catch (error) {
      if (!disposed && current === generation && !(error instanceof ApiAbortError)) { state.phase = 'faulted'; state.errorCode = 'INSPECTION_HYDRATE_FAILED'; state.message = '无法加载连续检测状态。'; }
    } finally { if (current === generation) requestController = null; }
  }
  async function start(identity: InspectionRunIdentity, cameraId: string | null = null): Promise<boolean> {
    if (disposed || identity.projectId !== options.projectId) return false;
    const current = ++generation; closeStream(); requestController?.abort(); requestController = new AbortController();
    lastEventId = null;
    activeIdentity = identity;
    state.phase = 'starting'; state.errorCode = null; state.latestResult = null; state.message = '正在校验已保存工程并启动连续检测。';
    try {
      const result = await options.api.start(identity, cameraId, { signal: requestController.signal });
      if (disposed || current !== generation) return false;
      if (result.projectId !== identity.projectId || result.clientSnapshotId !== identity.clientSnapshotId ||
        result.persistenceRevision !== identity.expectedPersistenceRevision || result.canonicalFlowHash !== identity.expectedCanonicalFlowHash ||
        result.decisionConfigurationHash !== identity.expectedDecisionConfigurationHash) throw new Error('Start identity mismatch.');
      state.runtime = Object.freeze({ projectId: options.projectId, status: 'Starting', isBusy: true, sessionId: result.sessionId,
        startedAt: null, stoppedAt: null, clientSnapshotId: identity.clientSnapshotId,
        persistenceRevision: identity.expectedPersistenceRevision, canonicalFlowHash: identity.expectedCanonicalFlowHash,
        decisionConfigurationHash: identity.expectedDecisionConfigurationHash, executionSource: 'PersistedProject',
        sessionType: result.sessionType });
      state.phase = 'running'; state.message = '连续检测已启动。'; connect(current); return true;
    } catch (error) {
      if (!disposed && current === generation) {
        const code = errorCode(error) ?? 'INSPECTION_START_REJECTED';
        if (shouldRecoverAuthority(error)) {
          await hydrate();
          state.errorCode = code;
          if (state.runtime?.isBusy) state.message = '后端已有工程运行，会话状态已恢复。';
          else if (state.runtime) { state.phase = 'faulted'; state.message = '启动结果未知，后端未确认存在运行会话。'; }
        } else {
          activeIdentity = null; state.phase = 'faulted'; state.errorCode = code;
          state.message = '连续检测启动被后端拒绝，请保存或重新加载工程后重试。';
        }
      }
      return false;
    } finally { if (current === generation) requestController = null; }
  }
  async function stop(): Promise<boolean> {
    if (disposed || !activeIdentity) return false;
    const stoppingIdentity = activeIdentity;
    const current = ++generation; closeStream(); requestController?.abort(); requestController = new AbortController(); state.phase = 'stopping'; state.message = '正在停止连续检测。';
    try { await options.api.stop(stoppingIdentity, { signal: requestController.signal }); if (disposed || current !== generation) return false; await hydrate(); return true; }
    catch (error) {
      if (!disposed && current === generation) {
        const code = errorCode(error) ?? 'INSPECTION_STOP_FAILED';
        await hydrate();
        state.errorCode = code;
        if (state.runtime?.isBusy) state.message = '停止尚未由后端确认，已恢复当前运行状态。';
        else if (state.runtime) state.message = '后端已确认连续检测不再运行。';
      }
      return false;
    }
  }
  const owner: InspectionRunOwner = Object.freeze({ projectId: options.projectId, projection: readonly(state), hydrate, start, stop,
    reconcile: hydrate, resources: () => Object.freeze({ streams: streamController ? 1 : 0, timers: reconnectTimer == null ? 0 : 1,
      abortControllers: (requestController ? 1 : 0) + (streamController ? 1 : 0), subscriptions: streamController ? 1 : 0 }),
    settle: async () => { await Promise.allSettled([...pending]); }, dispose: () => { if (disposed) return; disposed = true; generation += 1; activeIdentity = null; closeStream(); requestController?.abort(); requestController = null; state.phase = 'disposed'; state.message = '连续检测 Owner 已释放。'; } });
  return owner;
}
