import {
  HostAdapterDisposedError,
  type HostMessageHandler,
  type HostUnsubscribe,
  type StudioHostAdapter
} from './types';

export interface BrowserHostFake extends StudioHostAdapter {
  readonly kind: 'browser-fake';
  readonly postedMessages: readonly unknown[];
  emitMessage(message: unknown): void;
}

export function createBrowserHostFake(): BrowserHostFake {
  const subscriptions = new Set<HostMessageHandler>();
  const messages: unknown[] = [];
  let disposed = false;

  const assertActive = (): void => {
    if (disposed) {
      throw new HostAdapterDisposedError();
    }
  };

  const fake: BrowserHostFake = {
    kind: 'browser-fake',
    get postedMessages(): readonly unknown[] {
      return Object.freeze([...messages]);
    },
    postMessage(message: unknown): void {
      assertActive();
      messages.push(message);
    },
    subscribe(handler: HostMessageHandler): HostUnsubscribe {
      assertActive();
      const subscription: HostMessageHandler = message => handler(message);
      subscriptions.add(subscription);

      let subscribed = true;
      return () => {
        if (!subscribed) {
          return;
        }

        subscribed = false;
        subscriptions.delete(subscription);
      };
    },
    emitMessage(message: unknown): void {
      assertActive();
      for (const subscription of [...subscriptions]) {
        subscription(message);
      }
    },
    getDiagnostics() {
      return Object.freeze({
        kind: 'browser-fake' as const,
        channel: 'browser-fake' as const,
        isWebView2: false,
        disposed,
        activeSubscriptionCount: subscriptions.size
      });
    },
    dispose(): void {
      if (disposed) {
        return;
      }

      disposed = true;
      subscriptions.clear();
    }
  };

  return Object.freeze(fake);
}
