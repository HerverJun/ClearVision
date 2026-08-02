import {
  computed,
  shallowRef,
  type ComputedRef,
  type DeepReadonly
} from 'vue';
import type {
  ReadQueryOwner,
  ReadQueryRefreshOptions,
  ReadQueryState
} from '@/platform/query';
import { stationPollingIntervalMs } from './stationQueries';
import { StationContractDecodeError } from './stationContracts';
import type { StationSseEvent, StationSsePort } from './stationSseAdapter';

export interface StationQuerySlot<T> {
  readonly state: ComputedRef<DeepReadonly<ReadQueryState<T>>>;
  refresh(options?: ReadQueryRefreshOptions): Promise<ReadQueryState<T>>;
  pause(): void;
  dispose(): void;
}

export function createStationQuerySlot<T>(
  factory: () => ReadQueryOwner<T>
): StationQuerySlot<T> {
  const initialOwner = factory();
  const owner = shallowRef<ReadQueryOwner<T> | undefined>(initialOwner);
  const fallback = shallowRef<DeepReadonly<ReadQueryState<T>>>(initialOwner.state.value);
  let disposed = false;

  function ensureOwner(): ReadQueryOwner<T> {
    if (disposed) throw new Error('The Station query slot has been disposed.');
    owner.value ??= factory();
    return owner.value;
  }

  const state = computed(() => owner.value?.state.value ?? fallback.value);

  return Object.freeze({
    state,
    async refresh(options?: ReadQueryRefreshOptions): Promise<ReadQueryState<T>> {
      const activeOwner = ensureOwner();
      const result = await activeOwner.refresh(options);
      fallback.value = activeOwner.state.value;
      return result;
    },
    pause(): void {
      const activeOwner = owner.value;
      if (!activeOwner) return;
      const current = activeOwner.state.value;
      if (current.phase !== 'loading' && !current.isRefreshing) return;
      fallback.value = current;
      activeOwner.dispose();
      owner.value = undefined;
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      if (owner.value) {
        fallback.value = owner.value.state.value;
        owner.value.dispose();
        owner.value = undefined;
      }
    }
  });
}

interface VisibilityPort {
  readonly hidden: boolean;
  addEventListener(type: 'visibilitychange', listener: () => void): void;
  removeEventListener(type: 'visibilitychange', listener: () => void): void;
}

interface TimerPort {
  setTimeout(callback: () => void, delayMs: number): unknown;
  clearTimeout(timer: unknown): void;
}

export interface StationPollingDiagnostics {
  readonly started: boolean;
  readonly disposed: boolean;
  readonly refreshing: boolean;
  readonly timerScheduled: boolean;
}

export interface StationPollingOwner {
  start(): void;
  refreshNow(): Promise<void>;
  getDiagnostics(): StationPollingDiagnostics;
  dispose(): void;
}

export interface StationPollingOwnerOptions {
  readonly refresh: () => Promise<unknown>;
  readonly pause: () => void;
  readonly intervalMs?: number;
  readonly visibility?: VisibilityPort;
  readonly timers?: TimerPort;
}

function defaultVisibility(): VisibilityPort {
  return document;
}

function defaultTimers(): TimerPort {
  return {
    setTimeout: (callback, delayMs) => window.setTimeout(callback, delayMs),
    clearTimeout: timer => window.clearTimeout(timer as number)
  };
}

export function createVisibleStationPollingOwner(
  options: StationPollingOwnerOptions
): StationPollingOwner {
  const visibility = options.visibility ?? defaultVisibility();
  const timers = options.timers ?? defaultTimers();
  const intervalMs = options.intervalMs ?? stationPollingIntervalMs;
  if (!Number.isFinite(intervalMs) || intervalMs < 1_000) {
    throw new RangeError('Station polling interval must be at least 1000ms.');
  }

  let timer: unknown;
  let started = false;
  let disposed = false;
  let refreshing = false;
  let generation = 0;

  function clearTimer(): void {
    if (timer === undefined) return;
    timers.clearTimeout(timer);
    timer = undefined;
  }

  function schedule(expectedGeneration: number): void {
    clearTimer();
    if (disposed || !started || visibility.hidden || expectedGeneration !== generation) return;
    timer = timers.setTimeout(() => {
      timer = undefined;
      void refreshNow();
    }, intervalMs);
  }

  async function refreshNow(): Promise<void> {
    if (disposed || !started || visibility.hidden || refreshing) return;
    const expectedGeneration = generation;
    refreshing = true;
    clearTimer();
    try {
      await options.refresh();
    } finally {
      if (expectedGeneration === generation) refreshing = false;
      schedule(expectedGeneration);
    }
  }

  function visibilityChanged(): void {
    generation += 1;
    refreshing = false;
    clearTimer();
    if (visibility.hidden) {
      options.pause();
      return;
    }
    void refreshNow();
  }

  return Object.freeze({
    start(): void {
      if (disposed) throw new Error('The Station polling owner has been disposed.');
      if (started) return;
      started = true;
      visibility.addEventListener('visibilitychange', visibilityChanged);
      if (visibility.hidden) {
        options.pause();
      } else {
        void refreshNow();
      }
    },
    refreshNow,
    getDiagnostics(): StationPollingDiagnostics {
      return Object.freeze({
        started,
        disposed,
        refreshing,
        timerScheduled: timer !== undefined
      });
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      refreshing = false;
      clearTimer();
      if (started) visibility.removeEventListener('visibilitychange', visibilityChanged);
      options.pause();
    }
  });
}

export type StationMonitoringPhase =
  | 'idle'
  | 'connecting'
  | 'live'
  | 'recovering'
  | 'recovery-polling'
  | 'paused'
  | 'unauthorized'
  | 'disposed';

export type StationAuthorityRefreshReason =
  | 'start'
  | 'manual'
  | 'initial-state'
  | 'event'
  | 'heartbeat'
  | 'cursor-gap'
  | 'decode-error'
  | 'stream-disconnected'
  | 'visibility-resume';

export interface StationAuthorityRefreshRequest {
  readonly reason: StationAuthorityRefreshReason;
  readonly events: readonly StationSseEvent[];
}

export interface StationMonitoringState {
  readonly phase: StationMonitoringPhase;
  readonly cursor: number;
  readonly reconnectAttempt: number;
  readonly duplicateEventCount: number;
  readonly gapCount: number;
  readonly lastEventAtUtc: string | null;
  readonly lastRecoveryReason: StationAuthorityRefreshReason | null;
}

export interface StationMonitoringDiagnostics {
  readonly started: boolean;
  readonly disposed: boolean;
  readonly streamCount: number;
  readonly timerCount: number;
  readonly visibilityListenerCount: number;
  readonly authorityRefreshActive: boolean;
}

export interface StationMonitoringOwner {
  readonly state: ComputedRef<DeepReadonly<StationMonitoringState>>;
  start(): void;
  refreshNow(): Promise<void>;
  getDiagnostics(): StationMonitoringDiagnostics;
  dispose(): void;
}

export interface StationMonitoringOwnerOptions {
  readonly stream?: StationSsePort | undefined;
  readonly refreshAuthority: (request: StationAuthorityRefreshRequest) => Promise<unknown>;
  readonly pauseAuthority: () => void;
  readonly recoveryIntervalMs?: number;
  readonly reconnectDelaysMs?: readonly number[];
  readonly visibility?: VisibilityPort;
  readonly timers?: TimerPort;
  readonly now?: () => Date;
}

const defaultReconnectDelaysMs = Object.freeze([1_000, 2_000, 5_000, 10_000, 15_000]);

function unauthorizedFailure(error: unknown): boolean {
  return error instanceof Error && error.name === 'ApiUnauthorizedError';
}

function monitoringState(
  phase: StationMonitoringPhase,
  cursor: number,
  reconnectAttempt: number,
  duplicateEventCount: number,
  gapCount: number,
  lastEventAtUtc: string | null,
  lastRecoveryReason: StationAuthorityRefreshReason | null
): StationMonitoringState {
  return Object.freeze({
    phase,
    cursor,
    reconnectAttempt,
    duplicateEventCount,
    gapCount,
    lastEventAtUtc,
    lastRecoveryReason
  });
}

export function createStationMonitoringOwner(
  options: StationMonitoringOwnerOptions
): StationMonitoringOwner {
  const visibility = options.visibility ?? defaultVisibility();
  const timers = options.timers ?? defaultTimers();
  const now = options.now ?? (() => new Date());
  const recoveryIntervalMs = options.recoveryIntervalMs ?? stationPollingIntervalMs;
  const reconnectDelaysMs = options.reconnectDelaysMs ?? defaultReconnectDelaysMs;
  if (!Number.isFinite(recoveryIntervalMs) || recoveryIntervalMs < 1_000) {
    throw new RangeError('Station recovery interval must be at least 1000ms.');
  }
  if (reconnectDelaysMs.length === 0 || reconnectDelaysMs.some(delay => !Number.isFinite(delay) || delay < 0)) {
    throw new RangeError('Station reconnect delays must contain non-negative finite values.');
  }

  let phase: StationMonitoringPhase = 'idle';
  let cursor = 0;
  let reconnectAttempt = 0;
  let duplicateEventCount = 0;
  let gapCount = 0;
  let lastEventAtUtc: string | null = null;
  let lastRecoveryReason: StationAuthorityRefreshReason | null = null;
  let started = false;
  let disposed = false;
  let generation = 0;
  let streamAbort: AbortController | undefined;
  let reconnectTimer: unknown;
  let recoveryTimer: unknown;
  let refreshPromise: Promise<void> | undefined;
  let queuedRefresh: StationAuthorityRefreshRequest | undefined;
  const projection = shallowRef(monitoringState(
    phase,
    cursor,
    reconnectAttempt,
    duplicateEventCount,
    gapCount,
    lastEventAtUtc,
    lastRecoveryReason
  ));

  function publish(): void {
    projection.value = monitoringState(
      phase,
      cursor,
      reconnectAttempt,
      duplicateEventCount,
      gapCount,
      lastEventAtUtc,
      lastRecoveryReason
    );
  }

  function clearReconnectTimer(): void {
    if (reconnectTimer === undefined) return;
    timers.clearTimeout(reconnectTimer);
    reconnectTimer = undefined;
  }

  function clearRecoveryTimer(): void {
    if (recoveryTimer === undefined) return;
    timers.clearTimeout(recoveryTimer);
    recoveryTimer = undefined;
  }

  function stopStream(): void {
    if (!streamAbort) return;
    streamAbort.abort();
    streamAbort = undefined;
  }

  function mergeRefresh(
    current: StationAuthorityRefreshRequest | undefined,
    incoming: StationAuthorityRefreshRequest
  ): StationAuthorityRefreshRequest {
    if (!current) return incoming;
    const events = [...current.events, ...incoming.events];
    return Object.freeze({
      reason: incoming.reason === 'event' ? current.reason : incoming.reason,
      events: Object.freeze(events)
    });
  }

  function requestRefresh(request: StationAuthorityRefreshRequest): Promise<void> {
    if (disposed || visibility.hidden || phase === 'unauthorized') return Promise.resolve();
    queuedRefresh = mergeRefresh(queuedRefresh, request);
    if (refreshPromise) return refreshPromise;
    const expectedGeneration = generation;
    refreshPromise = (async () => {
      while (queuedRefresh && !disposed && !visibility.hidden && expectedGeneration === generation) {
        const next = queuedRefresh;
        queuedRefresh = undefined;
        try {
          await options.refreshAuthority(next);
        } catch {
          lastRecoveryReason = next.reason;
          publish();
        }
      }
    })().finally(() => {
      refreshPromise = undefined;
      if (queuedRefresh && !disposed && !visibility.hidden && expectedGeneration === generation) {
        const next = queuedRefresh;
        queuedRefresh = undefined;
        void requestRefresh(next);
      }
    });
    return refreshPromise;
  }

  function refresh(reason: StationAuthorityRefreshReason, events: readonly StationSseEvent[] = []): Promise<void> {
    if (reason !== 'event' && reason !== 'heartbeat') lastRecoveryReason = reason;
    publish();
    return requestRefresh(Object.freeze({ reason, events: Object.freeze([...events]) }));
  }

  function scheduleRecoveryPoll(expectedGeneration: number): void {
    clearRecoveryTimer();
    if (disposed || visibility.hidden || expectedGeneration !== generation || phase === 'unauthorized') return;
    recoveryTimer = timers.setTimeout(() => {
      recoveryTimer = undefined;
      void refresh('stream-disconnected').finally(() => scheduleRecoveryPoll(expectedGeneration));
    }, recoveryIntervalMs);
  }

  function scheduleReconnect(expectedGeneration: number): void {
    clearReconnectTimer();
    if (!options.stream || disposed || visibility.hidden || expectedGeneration !== generation || phase === 'unauthorized') {
      return;
    }
    const delay = reconnectDelaysMs[Math.min(reconnectAttempt, reconnectDelaysMs.length - 1)]!;
    reconnectAttempt += 1;
    publish();
    reconnectTimer = timers.setTimeout(() => {
      reconnectTimer = undefined;
      void connect(expectedGeneration);
    }, delay);
  }

  function handleEvent(
    event: StationSseEvent,
    expectedGeneration: number,
    connectionSignal: AbortSignal
  ): void {
    if (disposed || visibility.hidden || connectionSignal.aborted || expectedGeneration !== generation) return;
    lastEventAtUtc = now().toISOString();

    if (event.type === 'heartbeat') {
      phase = 'live';
      publish();
      void refresh('heartbeat');
      return;
    }

    if (event.type === 'initialState') {
      const watermark = event.eventSequenceId ?? 0;
      if (cursor > 0 && watermark < cursor) gapCount += 1;
      cursor = watermark;
      publish();
      void refresh('initial-state', [event]);
      return;
    }

    if (event.id !== null) {
      if (event.id <= cursor) {
        duplicateEventCount += 1;
        publish();
        return;
      }
      if (cursor > 0 && event.id !== cursor + 1) {
        gapCount += 1;
        cursor = event.id;
        phase = 'recovering';
        lastRecoveryReason = 'cursor-gap';
        publish();
        stopStream();
        void refresh('cursor-gap');
        scheduleRecoveryPoll(expectedGeneration);
        scheduleReconnect(expectedGeneration);
        return;
      }
      cursor = event.id;
    }

    publish();
    void refresh('event', [event]);
  }

  async function connect(expectedGeneration: number): Promise<void> {
    if (!options.stream || disposed || visibility.hidden || expectedGeneration !== generation || streamAbort) return;
    phase = 'connecting';
    publish();
    const controller = new AbortController();
    streamAbort = controller;

    try {
      await options.stream.connect({
        afterSequence: cursor,
        signal: controller.signal,
        onOpen: () => {
          if (disposed || controller.signal.aborted || expectedGeneration !== generation) return;
          phase = 'live';
          reconnectAttempt = 0;
          clearReconnectTimer();
          clearRecoveryTimer();
          publish();
        },
        onEvent: event => handleEvent(event, expectedGeneration, controller.signal)
      });
    } catch (error) {
      if (controller.signal.aborted || disposed || expectedGeneration !== generation) return;
      if (unauthorizedFailure(error)) {
        phase = 'unauthorized';
        clearReconnectTimer();
        clearRecoveryTimer();
        options.pauseAuthority();
        publish();
        return;
      }
      phase = 'recovering';
      lastRecoveryReason = error instanceof SyntaxError || error instanceof StationContractDecodeError
        ? 'decode-error'
        : 'stream-disconnected';
      publish();
      void refresh(lastRecoveryReason);
    } finally {
      if (streamAbort === controller) streamAbort = undefined;
    }

    const settledPhase = projection.value.phase;
    if (disposed || visibility.hidden || expectedGeneration !== generation || settledPhase === 'unauthorized') return;
    if (settledPhase === 'live') {
      phase = 'recovering';
      lastRecoveryReason = 'stream-disconnected';
      publish();
      void refresh('stream-disconnected');
    }
    if (recoveryTimer === undefined) scheduleRecoveryPoll(expectedGeneration);
    if (reconnectTimer === undefined) scheduleReconnect(expectedGeneration);
  }

  function resume(reason: 'start' | 'visibility-resume'): void {
    generation += 1;
    const expectedGeneration = generation;
    reconnectAttempt = 0;
    phase = options.stream ? 'connecting' : 'recovery-polling';
    publish();
    void refresh(reason);
    if (options.stream) void connect(expectedGeneration);
    else scheduleRecoveryPoll(expectedGeneration);
  }

  function visibilityChanged(): void {
    clearReconnectTimer();
    clearRecoveryTimer();
    stopStream();
    queuedRefresh = undefined;
    generation += 1;
    if (visibility.hidden) {
      phase = 'paused';
      options.pauseAuthority();
      publish();
      return;
    }
    resume('visibility-resume');
  }

  return Object.freeze({
    state: computed(() => projection.value),
    start(): void {
      if (disposed) throw new Error('The Station monitoring owner has been disposed.');
      if (started) return;
      started = true;
      visibility.addEventListener('visibilitychange', visibilityChanged);
      if (visibility.hidden) {
        phase = 'paused';
        options.pauseAuthority();
        publish();
      } else {
        resume('start');
      }
    },
    refreshNow(): Promise<void> {
      return refresh('manual');
    },
    getDiagnostics(): StationMonitoringDiagnostics {
      return Object.freeze({
        started,
        disposed,
        streamCount: streamAbort && !streamAbort.signal.aborted ? 1 : 0,
        timerCount: Number(reconnectTimer !== undefined) + Number(recoveryTimer !== undefined),
        visibilityListenerCount: started && !disposed ? 1 : 0,
        authorityRefreshActive: refreshPromise !== undefined
      });
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      phase = 'disposed';
      queuedRefresh = undefined;
      clearReconnectTimer();
      clearRecoveryTimer();
      stopStream();
      if (started) visibility.removeEventListener('visibilitychange', visibilityChanged);
      options.pauseAuthority();
      publish();
    }
  });
}
