import {
  HostAdapterDisposedError,
  WebView2UnavailableError,
  type HostMessageHandler,
  type HostUnsubscribe,
  type StudioHostAdapter
} from './types';

interface WebView2MessageEvent {
  readonly data: unknown;
}

type WebView2MessageListener = (event: WebView2MessageEvent) => void;

interface WebView2MessageChannel {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: WebView2MessageListener): void;
  removeEventListener(type: 'message', listener: WebView2MessageListener): void;
}

interface WebView2Global {
  readonly chrome?: {
    readonly webview?: WebView2MessageChannel;
  };
}

function resolveWebView2Channel(): WebView2MessageChannel {
  const channel = (globalThis as typeof globalThis & WebView2Global).chrome?.webview;

  if (
    !channel ||
    typeof channel.postMessage !== 'function' ||
    typeof channel.addEventListener !== 'function' ||
    typeof channel.removeEventListener !== 'function'
  ) {
    throw new WebView2UnavailableError();
  }

  return channel;
}

export function createWebView2HostAdapter(): StudioHostAdapter {
  const channel = resolveWebView2Channel();
  const subscriptions = new Set<HostMessageHandler>();
  let listenerAttached = false;
  let disposed = false;

  const onMessage: WebView2MessageListener = event => {
    for (const subscription of [...subscriptions]) {
      subscription(event.data);
    }
  };

  const assertActive = (): void => {
    if (disposed) {
      throw new HostAdapterDisposedError();
    }
  };

  const attachListener = (): void => {
    if (listenerAttached) {
      return;
    }

    channel.addEventListener('message', onMessage);
    listenerAttached = true;
  };

  const detachListener = (): void => {
    if (!listenerAttached) {
      return;
    }

    channel.removeEventListener('message', onMessage);
    listenerAttached = false;
  };

  const adapter: StudioHostAdapter = {
    kind: 'desktop-webview2',
    postMessage(message: unknown): void {
      assertActive();
      channel.postMessage(message);
    },
    subscribe(handler: HostMessageHandler): HostUnsubscribe {
      assertActive();
      const subscription: HostMessageHandler = message => handler(message);
      subscriptions.add(subscription);
      attachListener();

      let subscribed = true;
      return () => {
        if (!subscribed) {
          return;
        }

        subscribed = false;
        subscriptions.delete(subscription);
        if (subscriptions.size === 0) {
          detachListener();
        }
      };
    },
    getDiagnostics() {
      return Object.freeze({
        kind: 'desktop-webview2' as const,
        channel: 'webview2-web-message' as const,
        isWebView2: true,
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
      detachListener();
    }
  };

  return Object.freeze(adapter);
}
