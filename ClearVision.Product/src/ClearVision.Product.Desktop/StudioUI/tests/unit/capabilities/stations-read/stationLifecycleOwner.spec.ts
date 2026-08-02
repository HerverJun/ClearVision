import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiAbortError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import {
  createStationQuerySlot,
  createStationMonitoringOwner,
  createStationsQuery,
  createVisibleStationPollingOwner,
  type StationSseConnectionOptions,
  type StationSseEvent,
  type StationSsePort
} from '@/capabilities/stations-read';

class FakeVisibility {
  hidden = false;
  private readonly listeners = new Set<() => void>();

  addEventListener(_type: 'visibilitychange', listener: () => void): void {
    this.listeners.add(listener);
  }

  removeEventListener(_type: 'visibilitychange', listener: () => void): void {
    this.listeners.delete(listener);
  }

  setHidden(hidden: boolean): void {
    this.hidden = hidden;
    for (const listener of this.listeners) listener();
  }

  get listenerCount(): number {
    return this.listeners.size;
  }
}

function flushMicrotasks(): Promise<void> {
  return Promise.resolve().then(() => undefined);
}

describe('visible Station polling lifecycle owner', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('polls conservatively, stops while hidden and refreshes once visibility returns', async () => {
    const visibility = new FakeVisibility();
    const refresh = vi.fn(async () => undefined);
    const pause = vi.fn();
    const owner = createVisibleStationPollingOwner({ refresh, pause, visibility });

    owner.start();
    await flushMicrotasks();
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(owner.getDiagnostics().timerScheduled).toBe(true);

    await vi.advanceTimersByTimeAsync(15_000);
    expect(refresh).toHaveBeenCalledTimes(2);

    visibility.setHidden(true);
    expect(pause).toHaveBeenCalledTimes(1);
    expect(owner.getDiagnostics().timerScheduled).toBe(false);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(refresh).toHaveBeenCalledTimes(2);

    visibility.setHidden(false);
    await flushMicrotasks();
    expect(refresh).toHaveBeenCalledTimes(3);

    owner.dispose();
    expect(visibility.listenerCount).toBe(0);
    expect(owner.getDiagnostics()).toMatchObject({ disposed: true, timerScheduled: false });
  });

  it('aborts an in-flight read query when the page becomes hidden', async () => {
    const visibility = new FakeVisibility();
    let observedSignal: AbortSignal | undefined;
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get<T = unknown>(path: string, options: ApiGetOptions = {}): Promise<T | undefined> {
        observedSignal = options.signal;
        return new Promise((_resolve, reject) => {
          options.signal?.addEventListener('abort', () => reject(new ApiAbortError(path)), { once: true });
        });
      }
    };
    const client = createReadQueryClient(api);
    const slot = createStationQuerySlot(() => createStationsQuery(client));
    const owner = createVisibleStationPollingOwner({
      visibility,
      refresh: () => slot.refresh({ force: true }),
      pause: () => slot.pause()
    });

    owner.start();
    await flushMicrotasks();
    expect(slot.state.value.phase).toBe('loading');
    expect(observedSignal?.aborted).toBe(false);

    visibility.setHidden(true);
    await flushMicrotasks();
    expect(observedSignal?.aborted).toBe(true);

    owner.dispose();
    slot.dispose();
    client.dispose();
  });

  it('disposes its timer and never refreshes again after route unmount', async () => {
    const visibility = new FakeVisibility();
    const refresh = vi.fn(async () => undefined);
    const owner = createVisibleStationPollingOwner({
      visibility,
      refresh,
      pause: vi.fn()
    });

    owner.start();
    await flushMicrotasks();
    owner.dispose();
    await vi.advanceTimersByTimeAsync(60_000);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(owner.getDiagnostics().timerScheduled).toBe(false);
  });

  it('returns visibility listeners and timers to zero across 20 mount/unmount cycles', async () => {
    const visibility = new FakeVisibility();
    for (let cycle = 0; cycle < 20; cycle += 1) {
      const owner = createVisibleStationPollingOwner({
        visibility,
        refresh: vi.fn(async () => undefined),
        pause: vi.fn()
      });
      owner.start();
      await flushMicrotasks();
      expect(visibility.listenerCount).toBe(1);
      owner.dispose();
      expect(visibility.listenerCount).toBe(0);
      expect(owner.getDiagnostics()).toMatchObject({ disposed: true, refreshing: false, timerScheduled: false });
    }
  });
});

class FakeStationStream implements StationSsePort {
  readonly connections: StationSseConnectionOptions[] = [];
  private readonly settle: Array<() => void> = [];

  connect(options: StationSseConnectionOptions): Promise<void> {
    this.connections.push(options);
    options.onOpen();
    return new Promise(resolve => {
      this.settle.push(resolve);
      options.signal.addEventListener('abort', () => resolve(), { once: true });
    });
  }

  emit(event: StationSseEvent, index = this.connections.length - 1): void {
    this.connections[index]?.onEvent(event);
  }

  disconnect(index = this.connections.length - 1): void {
    this.settle[index]?.();
  }
}

describe('Station SSE monitoring lifecycle owner', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('establishes the snapshot watermark, suppresses duplicates and recovers a sequence gap', async () => {
    const visibility = new FakeVisibility();
    const stream = new FakeStationStream();
    const refreshes: string[] = [];
    const owner = createStationMonitoringOwner({
      stream,
      visibility,
      refreshAuthority: async request => { refreshes.push(request.reason); },
      pauseAuthority: vi.fn()
    });

    owner.start();
    await flushMicrotasks();
    expect(owner.state.value.phase).toBe('live');
    expect(stream.connections).toHaveLength(1);

    stream.emit({ type: 'initialState', id: null, stationId: null, eventSequenceId: 10 });
    await flushMicrotasks();
    stream.emit({ type: 'stationUpserted', id: 11, stationId: 'station-a', eventSequenceId: null });
    await flushMicrotasks();
    stream.emit({ type: 'stationUpserted', id: 11, stationId: 'station-a', eventSequenceId: null });
    stream.emit({ type: 'summaryUpdated', id: 13, stationId: null, eventSequenceId: null });
    await flushMicrotasks();

    expect(owner.state.value).toMatchObject({
      phase: 'recovering',
      cursor: 13,
      duplicateEventCount: 1,
      gapCount: 1,
      lastRecoveryReason: 'cursor-gap'
    });
    expect(refreshes).toEqual(expect.arrayContaining(['start', 'initial-state', 'event', 'cursor-gap']));
    expect(owner.getDiagnostics().timerCount).toBe(2);

    stream.emit({ type: 'stationUpserted', id: 14, stationId: 'station-a', eventSequenceId: null }, 0);
    stream.emit({ type: 'heartbeat', id: null, stationId: null, eventSequenceId: null }, 0);
    expect(owner.state.value).toMatchObject({ phase: 'recovering', cursor: 13 });

    await vi.advanceTimersByTimeAsync(1_000);
    expect(stream.connections).toHaveLength(2);
    expect(stream.connections[1]?.afterSequence).toBe(13);
    expect(owner.state.value.phase).toBe('live');
    expect(owner.getDiagnostics().timerCount).toBe(0);
    owner.dispose();
  });

  it('uses heartbeat only to reread server authority and leaves station online truth to that projection', async () => {
    const stream = new FakeStationStream();
    const refreshes: string[] = [];
    const owner = createStationMonitoringOwner({
      stream,
      refreshAuthority: async request => { refreshes.push(request.reason); },
      pauseAuthority: vi.fn(),
      now: () => new Date('2026-08-02T03:00:00Z')
    });
    owner.start();
    await flushMicrotasks();
    stream.emit({ type: 'heartbeat', id: null, stationId: null, eventSequenceId: null });
    await flushMicrotasks();

    expect(refreshes).toContain('heartbeat');
    expect(owner.state.value.lastEventAtUtc).toBe('2026-08-02T03:00:00.000Z');
    expect(owner.state.value).not.toHaveProperty('isOnline');
    owner.dispose();
  });

  it('returns stream, timers and visibility listener to zero on hide, resume and dispose', async () => {
    const visibility = new FakeVisibility();
    const stream = new FakeStationStream();
    const pauseAuthority = vi.fn();
    const owner = createStationMonitoringOwner({
      stream,
      visibility,
      refreshAuthority: async () => undefined,
      pauseAuthority
    });
    owner.start();
    await flushMicrotasks();
    expect(owner.getDiagnostics()).toMatchObject({ streamCount: 1, visibilityListenerCount: 1 });

    visibility.setHidden(true);
    await flushMicrotasks();
    expect(owner.state.value.phase).toBe('paused');
    expect(owner.getDiagnostics()).toMatchObject({ streamCount: 0, timerCount: 0 });

    visibility.setHidden(false);
    await flushMicrotasks();
    expect(stream.connections).toHaveLength(2);
    expect(owner.state.value.phase).toBe('live');

    owner.dispose();
    expect(visibility.listenerCount).toBe(0);
    expect(owner.getDiagnostics()).toMatchObject({
      streamCount: 0,
      timerCount: 0,
      visibilityListenerCount: 0
    });
    expect(pauseAuthority).toHaveBeenCalledTimes(2);
  });

  it('stops reconnect and recovery work after authentication expires', async () => {
    const error = new Error('expired');
    error.name = 'ApiUnauthorizedError';
    const stream: StationSsePort = {
      async connect() { throw error; }
    };
    const pauseAuthority = vi.fn();
    const owner = createStationMonitoringOwner({
      stream,
      refreshAuthority: async () => undefined,
      pauseAuthority
    });

    owner.start();
    for (let index = 0; index < 4; index += 1) await flushMicrotasks();

    expect(owner.state.value.phase).toBe('unauthorized');
    expect(owner.getDiagnostics()).toMatchObject({ streamCount: 0, timerCount: 0 });
    expect(pauseAuthority).toHaveBeenCalledOnce();
    owner.dispose();
  });

  it('turns decoder failures into authority recovery with bounded reconnect resources', async () => {
    const stream: StationSsePort = {
      async connect() { throw new SyntaxError('invalid Station event'); }
    };
    const refreshes: string[] = [];
    const owner = createStationMonitoringOwner({
      stream,
      refreshAuthority: async request => { refreshes.push(request.reason); },
      pauseAuthority: vi.fn()
    });

    owner.start();
    for (let index = 0; index < 4; index += 1) await flushMicrotasks();

    expect(owner.state.value).toMatchObject({ phase: 'recovering', lastRecoveryReason: 'decode-error' });
    expect(refreshes).toContain('decode-error');
    expect(owner.getDiagnostics().timerCount).toBe(2);
    owner.dispose();
    expect(owner.getDiagnostics().timerCount).toBe(0);
  });

  it('keeps all owned resources at zero across 20 route and feature-owner cycles', async () => {
    const visibility = new FakeVisibility();
    for (let cycle = 0; cycle < 20; cycle += 1) {
      const owner = createStationMonitoringOwner({
        stream: new FakeStationStream(),
        visibility,
        refreshAuthority: async () => undefined,
        pauseAuthority: vi.fn()
      });
      owner.start();
      await flushMicrotasks();
      owner.dispose();
      expect(visibility.listenerCount).toBe(0);
      expect(owner.getDiagnostics()).toMatchObject({
        streamCount: 0,
        timerCount: 0,
        visibilityListenerCount: 0
      });
    }
  });
});
