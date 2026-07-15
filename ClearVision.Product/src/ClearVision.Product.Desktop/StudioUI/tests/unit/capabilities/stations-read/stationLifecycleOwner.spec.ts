import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiAbortError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import {
  createStationQuerySlot,
  createStationsQuery,
  createVisibleStationPollingOwner
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
});
