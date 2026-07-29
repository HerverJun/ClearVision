export interface AiResourceLedgerDiagnostics {
  readonly requestCount: number;
  readonly streamCount: number;
  readonly timerCount: number;
  readonly subscriptionCount: number;
  readonly disposed: boolean;
}

export interface AiResourceLedger {
  trackRequest(controller: AbortController): () => void;
  trackStream(cancel: () => void): () => void;
  trackTimer(timer: ReturnType<typeof globalThis.setTimeout>): () => void;
  trackSubscription(unsubscribe: () => void): () => void;
  diagnostics(): AiResourceLedgerDiagnostics;
  dispose(): void;
}

export function createAiResourceLedger(): AiResourceLedger {
  const requests = new Set<AbortController>();
  const streams = new Set<() => void>();
  const timers = new Set<ReturnType<typeof globalThis.setTimeout>>();
  const subscriptions = new Set<() => void>();
  let disposed = false;

  function releaseFrom<T>(set: Set<T>, resource: T): () => void {
    let released = false;
    return () => {
      if (released) return;
      released = true;
      set.delete(resource);
    };
  }

  function assertActive(): void {
    if (disposed) throw new Error('AI resource ledger is disposed.');
  }

  return Object.freeze({
    trackRequest(controller: AbortController) {
      assertActive();
      requests.add(controller);
      return releaseFrom(requests, controller);
    },
    trackStream(cancel: () => void) {
      assertActive();
      streams.add(cancel);
      return releaseFrom(streams, cancel);
    },
    trackTimer(timer: ReturnType<typeof globalThis.setTimeout>) {
      assertActive();
      timers.add(timer);
      return releaseFrom(timers, timer);
    },
    trackSubscription(unsubscribe: () => void) {
      assertActive();
      subscriptions.add(unsubscribe);
      return releaseFrom(subscriptions, unsubscribe);
    },
    diagnostics() {
      return Object.freeze({
        requestCount: requests.size,
        streamCount: streams.size,
        timerCount: timers.size,
        subscriptionCount: subscriptions.size,
        disposed
      });
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const controller of requests) controller.abort('ai-session-owner-disposed');
      for (const cancel of streams) cancel();
      for (const timer of timers) globalThis.clearTimeout(timer);
      for (const unsubscribe of subscriptions) unsubscribe();
      requests.clear();
      streams.clear();
      timers.clear();
      subscriptions.clear();
    }
  });
}
