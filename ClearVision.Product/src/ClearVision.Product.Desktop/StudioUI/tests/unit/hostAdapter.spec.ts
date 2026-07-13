import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createBrowserHostFake,
  createWebView2HostAdapter,
  HostAdapterDisposedError,
  WebView2UnavailableError
} from '@/platform/host';

type FakeMessageListener = (event: { readonly data: unknown }) => void;

interface FakeWebView2Channel {
  readonly postMessage: ReturnType<typeof vi.fn>;
  readonly addEventListener: ReturnType<typeof vi.fn>;
  readonly removeEventListener: ReturnType<typeof vi.fn>;
  emit(message: unknown): void;
}

const originalChromeDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'chrome');

function installWebView2Channel(): FakeWebView2Channel {
  let listener: FakeMessageListener | undefined;
  const channel: FakeWebView2Channel = {
    postMessage: vi.fn(),
    addEventListener: vi.fn((type: string, nextListener: FakeMessageListener) => {
      expect(type).toBe('message');
      listener = nextListener;
    }),
    removeEventListener: vi.fn((type: string, currentListener: FakeMessageListener) => {
      expect(type).toBe('message');
      if (listener === currentListener) {
        listener = undefined;
      }
    }),
    emit(message: unknown): void {
      listener?.({ data: message });
    }
  };

  Object.defineProperty(globalThis, 'chrome', {
    configurable: true,
    value: {
      webview: channel
    }
  });

  return channel;
}

afterEach(() => {
  if (originalChromeDescriptor) {
    Object.defineProperty(globalThis, 'chrome', originalChromeDescriptor);
  } else {
    Reflect.deleteProperty(globalThis, 'chrome');
  }
});

describe('createWebView2HostAdapter', () => {
  it('fails fast instead of silently creating a browser fake', () => {
    Reflect.deleteProperty(globalThis, 'chrome');

    expect(() => createWebView2HostAdapter()).toThrow(WebView2UnavailableError);
  });

  it('owns one WebView2 listener for all active subscriptions', () => {
    const channel = installWebView2Channel();
    const adapter = createWebView2HostAdapter();
    const firstHandler = vi.fn();
    const secondHandler = vi.fn();

    const unsubscribeFirst = adapter.subscribe(firstHandler);
    const unsubscribeSecond = adapter.subscribe(secondHandler);

    expect(channel.addEventListener).toHaveBeenCalledTimes(1);
    expect(adapter.getDiagnostics()).toEqual({
      kind: 'desktop-webview2',
      channel: 'webview2-web-message',
      isWebView2: true,
      disposed: false,
      activeSubscriptionCount: 2
    });

    channel.emit({ type: 'host-diagnostic' });
    expect(firstHandler).toHaveBeenCalledWith({ type: 'host-diagnostic' });
    expect(secondHandler).toHaveBeenCalledWith({ type: 'host-diagnostic' });

    unsubscribeFirst();
    unsubscribeFirst();
    expect(channel.removeEventListener).not.toHaveBeenCalled();

    unsubscribeSecond();
    expect(channel.removeEventListener).toHaveBeenCalledTimes(1);
    expect(adapter.getDiagnostics().activeSubscriptionCount).toBe(0);
  });

  it('forwards messages and disposes every listener idempotently', () => {
    const channel = installWebView2Channel();
    const adapter = createWebView2HostAdapter();
    const handler = vi.fn();
    adapter.subscribe(handler);

    adapter.postMessage({ type: 'open-host-dialog' });
    expect(channel.postMessage).toHaveBeenCalledWith({ type: 'open-host-dialog' });

    adapter.dispose();
    adapter.dispose();

    expect(channel.removeEventListener).toHaveBeenCalledTimes(1);
    expect(adapter.getDiagnostics()).toMatchObject({
      disposed: true,
      activeSubscriptionCount: 0
    });

    channel.emit({ type: 'late-message' });
    expect(handler).not.toHaveBeenCalled();
    expect(() => adapter.postMessage({ type: 'late-post' })).toThrow(HostAdapterDisposedError);
    expect(() => adapter.subscribe(vi.fn())).toThrow(HostAdapterDisposedError);
  });
});

describe('createBrowserHostFake', () => {
  it('is explicit, observable, and never reports WebView2 availability', () => {
    const fake = createBrowserHostFake();
    const handler = vi.fn();
    const unsubscribe = fake.subscribe(handler);

    fake.postMessage({ type: 'browser-post' });
    fake.emitMessage({ type: 'browser-receive' });

    expect(fake.kind).toBe('browser-fake');
    expect(fake.postedMessages).toEqual([{ type: 'browser-post' }]);
    expect(handler).toHaveBeenCalledWith({ type: 'browser-receive' });
    expect(fake.getDiagnostics()).toEqual({
      kind: 'browser-fake',
      channel: 'browser-fake',
      isWebView2: false,
      disposed: false,
      activeSubscriptionCount: 1
    });

    unsubscribe();
    fake.dispose();
    fake.dispose();

    expect(fake.getDiagnostics().disposed).toBe(true);
    expect(() => fake.emitMessage({ type: 'late-message' })).toThrow(HostAdapterDisposedError);
    expect(() => fake.postMessage({ type: 'late-post' })).toThrow(HostAdapterDisposedError);
  });
});
