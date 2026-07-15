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
